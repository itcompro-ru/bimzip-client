using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
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
        private long _totalPulls;
        private long _totalPushes;
        private readonly Stopwatch _downloadTaskStopwatch = new Stopwatch();
        private readonly object _downloadTaskLockObject = new object();
        private readonly ConcurrentQueue<FileDto> _taskQueue = new ConcurrentQueue<FileDto>();
        private readonly ConcurrentQueue<ReportDto> _reportQueue = new ConcurrentQueue<ReportDto>();

        private readonly System.Timers.Timer _timer = new System.Timers.Timer
        {
            Interval = 60000 * 15,
            Enabled = true,
            AutoReset = true,
        };

        public Manager(ClientConfig config)
        {
            _config = config;
            _log = Log.Create("manager", config.BackupPath);
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
            _config.ForgeToken = response.AccessToken;

            _timer.Elapsed += async (sender, args) =>
            {
                try
                {
                    var tokenRequest = _restClient.GetRequest($"token/{_config.ClientId}");
                    var tokenResponse = await tokenRequest.ExecuteAsync<TokenDto>();
                    _config.ForgeToken = tokenResponse.AccessToken;
                    _log.Information("Token refresh ok");
                }
                catch (Exception e)
                {
                    _log.Error(e, "Token refresh error: ");
                }
            };
            _timer.Start();

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
                    if (r.FileList.Any())
                    {
                        Interlocked.Add(ref _totalPulls, r.FileList.Count);
                        _log.Information($"puller: added tasks: {r.FileList.Count} (total: {_totalPulls})");
                    }
                    serverErrorCount = 0;
                    serverError500Count = 0;
                }
                catch (HttpException ex) when (((int) ex.StatusCode) >= 500)
                {
                    serverError500Count++;
                    _log.Error(
                        $"puller: server status: {ex.StatusCode} " +
                        $"try {serverError500Count} of 200");
                    if (serverError500Count >= 200)
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
                if (_taskQueue.TryDequeue(out var task) == false)
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
                    _taskQueue.Enqueue(task);
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
                    Id = task.Id,
                    Size = 0,
                    Success = false,
                    Message = string.Empty,
                    DownloadTimeMs = 0
                };
                var fileInfo = new FileInfo(Path.Combine(_config.BackupPath.FullName, Utils.CreatePath(task.Path)));

                try
                {
                    // ReSharper disable once PossibleNullReferenceException
                    fileInfo.Directory.Create();
                    var downloadFileInfo = new FileInfo(fileInfo.FullName + ".bimzip");
                    sDownload.Restart();
                    
                    var client = new HttpClientProgressReport(_config, TimeSpan.FromSeconds(10));
                    client.ProgressChanged += (size, downloaded) =>
                    {
                        var message = $"worker #{id}: downloading {fileInfo.Name} ";
                        if (size.HasValue)
                        {
                            var progressPercentage = Math.Round((double) downloaded / size.Value * 100, 2);
                            message += $"{progressPercentage}% ({((double) downloaded / (1024 * 1024)):F2}/{((double) size / (1024 * 1024)):F2} MB)";
                        }
                        else
                        {
                            message += $"{(downloaded / (1024 * 1024)):F2} MB";
                        }
                        _log.Information(message);
                    };
                    await client.DownloadAsync(task.Url, downloadFileInfo.FullName, _cts.Token);
                    
                    Debug.Assert(downloadFileInfo.Exists);
                    downloadFileInfo.MoveTo(fileInfo.FullName);
                    report.Size = fileInfo.Length;
                    report.Success = true;
                    report.DownloadTimeMs = sDownload.ElapsedMilliseconds;
                    _reportQueue.Enqueue(report);
                    _log.Information(
                        $"worker #{id}: {fileInfo.Name} " +
                        $"download complete {((double) report.Size / 1024 / 1024):F2}MB " +
                        $"in {sDownload.Elapsed.TotalSeconds:F} seconds");
                }
                catch (HttpException e)
                {
                    if (e.StatusCode >= (HttpStatusCode) 400)
                    {
                        _log.Error($"Autodesk server returned error {e.StatusCode} for {fileInfo.Name}, requeue");
                        _taskQueue.Enqueue(task);
                    }
                    else
                    {
                        _log.Error($"Autodesk server returned error {e.StatusCode}for {fileInfo.Name}, skipping file");
                        report.Message = e.Message;
                        _reportQueue.Enqueue(report);
                    }
                }
                catch (HttpRequestException e)
                {
                    if (e.Message == "Resource temporarily unavailable")
                    {
                        _log.Error($"Autodesk server temporarily unavailable for {fileInfo.Name}, requeue");
                        _taskQueue.Enqueue(task);
                    }
                    else
                    {
                        _log.Error($"Autodesk server returned error for {fileInfo.Name}, skipping file", e);
                        report.Message = e.Message;
                        _reportQueue.Enqueue(report);
                    }
                }
                catch (Exception e)
                {
                    _log.Error($"Error occured while downloading {fileInfo.Name}", e);
                    report.Message = e.Message;
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
                    Interlocked.Add(ref _totalPushes, list.Count);
                    _log.Information($"pusher: reports pushed: {list.Count} (total: {_totalPushes})");
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