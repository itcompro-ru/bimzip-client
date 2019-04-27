using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using BimZipClient.Dto;
using Serilog.Core;
using Tiny.RestClient;

namespace BimZipClient.Infrastructure
{
    public class Manager
    {
        private readonly ClientConfig _config;
        private readonly Logger _log;
        private readonly TinyRestClient _restClient;
        private readonly List<Task> _tasks = new List<Task>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private volatile bool _running = true;
        private long _workerCounter;
        private readonly Stopwatch _downloadTaskStopwatch = new Stopwatch();
        private readonly object _downloadTaskLockObject = new object();
        private readonly ConcurrentQueue<FileDto> _taskQueue = new ConcurrentQueue<FileDto>();
        private readonly ConcurrentQueue<ReportDto> _reportQueue = new ConcurrentQueue<ReportDto>();

        public Manager(ClientConfig config)
        {
            _config = config;
            _log = Log.Create("manager");
            _restClient = new TinyRestClient(new HttpClient(), _config.BaseEndpoint);
            _restClient.Settings.Formatters.OfType<JsonFormatter>().First().UseCamelCase();
            _restClient.Settings.DefaultTimeout = TimeSpan.FromSeconds(120);
            _downloadTaskStopwatch.Restart();
        }

        private bool CanStartDownloadTask()
        {
            lock (_downloadTaskLockObject)
            {
                if (_downloadTaskStopwatch.Elapsed.TotalMilliseconds > _config.ProcessTaskIntervalMs)
                {
                    _downloadTaskStopwatch.Restart();
                    return true;
                }
                return false;
            }
        }

        public async Task<int> Start()
        {
            _log.Information("Starting backup session");
            var request = _restClient.PostRequest("start", _config.GetClientInfo());
            SessionStartDto response;
            try
            {
                response = await request.ExecuteAsync<SessionStartDto>();
            }
            catch (Exception e)
            {
                _log.Fatal(e, "Session start error: ");
                return -1;
            }

            _config.SetSession(response);

            _tasks.Add(Task.Run(async () => await PullData())
                .ContinueWith(x => _log.Information("Puller exit")));
            
            _tasks.Add(Task.Run(async () => await PushData())
                .ContinueWith(x => _log.Information("Pusher exit")));
            
            _tasks.AddRange(Enumerable.Range(0, _config.Concurrency)
                .Select(i => ProcessData(i + 1)
                    .ContinueWith(x => _log.Information($"Worker {i + 1} exit"))));
            
            _log.Information("Session started, workers: " + _config.Concurrency);
            Task.WaitAll(_tasks.ToArray());
            _log.Information("BimZip BimZipClient shutdown");
            return 0;
        }

        private async Task PullData()
        {
            var st = new Stopwatch();
            var serverError500Count = 0;
            var serverErrorCount = 0;
            while (_running)
            {
                st.Restart();
                var request = _restClient.GetRequest($"tasks/{_config.ClientId}/{_config.SessionKey}");
                try
                {
                    var r = await request.ExecuteAsync<TaskDto>();
                    _config.ForgeToken = r.AccessToken;

                    if (r.FileList.Any())
                    {
                        _log.Information($"puller: added tasks: {r.FileList.Count}");
                    }

                    if (r.CommandCode > 0)
                    {
                        _log.Information($"puller: new command: {r.Command}");
                    }

                    switch (r.CommandCode)
                    {
                        case 0:
                            r.FileList.ForEach(dto => _taskQueue.Enqueue(dto));
                            break;
                        case 1:
                            r.FileList.ForEach(dto => _taskQueue.Enqueue(dto));
                            _running = false;
                            return;
                        case 2:
                            _taskQueue.Clear();
                            _reportQueue.Clear();
                            _running = false;
                            return;
                        case 3:
                            _running = false;
                            _cts.Cancel();
                            return;
                        default:
                            throw new Exception("Unknown server command");
                    }
                    serverErrorCount = 0;
                    serverError500Count = 0;
                }
                catch (HttpException ex) when (((int) ex.StatusCode) >= 500)
                {
                    serverError500Count++;
                    _log.Error(
                        $"puller: server status: {ex.StatusCode} " +
                        $"try {serverError500Count} of 20");
                    if (serverError500Count >= 20)
                    {
                        _log.Error("puller: server is dead, shutting down");
                        _running = false;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    serverErrorCount++;
                    _log.Error(ex,
                        $"puller : error.  Try {serverErrorCount} of 3");
                    if (serverErrorCount >= 3)
                    {
                        _log.Error("puller: server is dead, shutting down");
                        _running = false;
                        return;
                    }
                }
                await Task.Delay(
                    TimeSpan.FromMilliseconds(Math.Max(0, _config.PullTaskTimeoutMs - st.Elapsed.TotalMilliseconds)));
            }
        }

        private async Task ProcessData(int id)
        {
            Interlocked.Increment(ref _workerCounter);
            var sTotal = new Stopwatch();
            var sDownload = new Stopwatch();

            while (_running || _taskQueue.Any())
            {
                sTotal.Restart();
                if (_taskQueue.TryDequeue(out var fd) == false)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(500), _cts.Token);
                        continue;
                    }
                    catch
                    {
                        break;
                    }
                }

                if (CanStartDownloadTask() == false)
                {
                    //_log.Verbose("CanStartDownloadTask = false");
                    _taskQueue.Enqueue(fd);
                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(500), _cts.Token);
                        continue;
                    }
                    catch
                    {
                        break;
                    }
                }

                var report = new ReportDto
                {
                    Id = fd.Id,
                    Size = 0,
                    Success = false,
                    Message = string.Empty,
                    DownloadTimeMs = 0
                };

                try
                {
                    var fileInfo = new FileInfo(Path.Combine(_config.BackupPath.FullName, Utils.CreatePath(fd.Path)));
                    // ReSharper disable once PossibleNullReferenceException
                    fileInfo.Directory.Create();
                    var downloadPath = fileInfo.FullName + ".bimzip";
                    using (var fileStream = File.Open(downloadPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
                    using (var httpClient = GetClient())
                    {
                        sDownload.Restart();
                        using (var stream = await httpClient.GetStreamAsync(new Uri(fd.Url)))
                        {
                            await stream.CopyToAsync(fileStream, _cts.Token);
                        }
                    }
                    File.Move(downloadPath, fileInfo.FullName);

                    report.Size = fileInfo.Length;
                    report.Success = true;
                    report.DownloadTimeMs = sDownload.ElapsedMilliseconds;
                    _log.Information(
                        $"worker #{id}: {fileInfo.Name} " +
                        $"downloaded {((double) report.Size / 1024 / 1024):F2}MB " +
                        $"in {sDownload.Elapsed.TotalSeconds:F} seconds");
                }
                catch (Exception e)
                {
                    report.Message = e.Message;
                }
                finally
                {
                    _reportQueue.Enqueue(report);
                }

                try
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(
                            Math.Max(0, _config.ProcessTaskTimeoutMs - sTotal.Elapsed.TotalMilliseconds)), _cts.Token);
                }
                catch
                {
                    break;
                }
            }
            Interlocked.Decrement(ref _workerCounter);
        }

        private HttpClient GetClient()
        {
            var httpClient = new HttpClient(new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            })
            {
                Timeout = TimeSpan.FromMilliseconds(_config.ForgeDownloadTimeoutMs)
            };
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _config.ForgeToken);
            return httpClient;
        }

        private async Task PushData()
        {
            var st = new Stopwatch();
            while (_running || _reportQueue.Any() || Interlocked.Read(ref _workerCounter) > 0)
            {
                st.Restart();
                var list = new List<ReportDto>();
                while (_reportQueue.TryDequeue(out var reportItem))
                {
                    list.Add(reportItem);
                }

                if (list.Any() == false)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(500), _cts.Token);
                        continue;
                    }
                    catch
                    {
                        return;
                    }
                }
                try
                {
                    var request = _restClient.PostRequest(
                        $"reports/{_config.ClientId}/{_config.SessionKey}", list);
                    await request.ExecuteAsync<TaskDto>();
                    _log.Information($"pusher: reports pushed: {list.Count} ");
                }
                catch (Exception e)
                {
                    _log.Error(e, "pusher: server reporting error");
                }
                try
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(
                            Math.Max(0, _config.PushTaskTimeoutMs - st.Elapsed.TotalMilliseconds)), _cts.Token);
                }
                catch
                {
                    return;
                }
            }
        }
    }
}