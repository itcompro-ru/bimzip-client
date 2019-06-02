using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace BimZipClient.Infrastructure
{
    public class HttpClientProgressReport
    {
        private readonly ClientConfig _config;
        private readonly TimeSpan _reportProgressInterval;
        private int _totalBytesDownloaded;

        public delegate void ProgressChangedHandler(long? totalFileSize, long totalBytesDownloaded);
        public event ProgressChangedHandler ProgressChanged;
        
        public HttpClientProgressReport(ClientConfig config, TimeSpan reportProgressInterval)
        {
            _config = config;
            _reportProgressInterval = reportProgressInterval;
        }

        public async Task DownloadAsync(string downloadUrl, string destinationFilePath, CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            _totalBytesDownloaded = 0;

            using var httpClient = new HttpClient(
                    new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                    }
                )
            {
                Timeout = TimeSpan.FromMilliseconds(_config.ForgeDownloadTimeoutMs)
            };
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.ForgeToken);

            using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead ,cancellationToken);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength;
            
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(destinationFilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 8192, true);
            using var timer = new System.Timers.Timer { Interval = _reportProgressInterval.TotalMilliseconds,  AutoReset = true,  Enabled = true };
            
            timer.Elapsed += (sender, args) =>
            {
                if (ProgressChanged == null)
                {
                    return;
                }
                ProgressChanged(totalBytes, _totalBytesDownloaded);
            };
            
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) != 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                _totalBytesDownloaded += bytesRead;
            }
        }
    }
}