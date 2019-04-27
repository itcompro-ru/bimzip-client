using System;
using System.IO;
using System.Runtime.InteropServices;
using BimZipClient.Dto;

namespace BimZipClient.Infrastructure
{
    public class ClientConfig
    {
        public readonly string BaseEndpoint;
        public readonly string ClientId;
        public readonly DirectoryInfo BasePath;
        public DirectoryInfo BackupPath { get; private set; }
        public readonly DateTime StartDateTime = DateTime.Now;
        public volatile string ForgeToken;
        public Guid SessionKey { get; private set; }
        public int PullTaskTimeoutMs { get; private set; }
        public int PushTaskTimeoutMs { get; private set; }
        public int ProcessTaskTimeoutMs { get; private set; }
        public  int ProcessTaskIntervalMs { get; private set; }
        public int ForgeDownloadTimeoutMs { get; private set; }
        public int Concurrency { get; private set; }
        
        public ClientConfig(string clientId, DirectoryInfo basePath, string baseEndpoint)
        {
            BaseEndpoint = baseEndpoint;
            ClientId = clientId;
            BasePath = basePath;
        }

        public void CreateBackupPath()
        {
            BackupPath = BasePath.CreateSubdirectory(StartDateTime.ToString("yy-MM-dd_hh-mm-ss"));
        }

        public ClientInfoDto GetClientInfo()
        {
            var clientInfo =  new ClientInfoDto
            {
                StartDateTime = StartDateTime,
                ClientId = ClientId,
                OsVersion = RuntimeInformation.OSDescription,
                FrameworkVersion = RuntimeInformation.FrameworkDescription,
            };

            try
            {
                var drive = new DriveInfo(BasePath.FullName);
                clientInfo.StorageFreeMb = (int) (drive.TotalFreeSpace / 1024 / 1024);
            }
            catch (Exception)
            {
                // ignored
            }

            return clientInfo;
        }

        public void SetSession(SessionStartDto data)
        {
            SessionKey = data.SessionKey;
            Concurrency = Math.Max(1, data.Concurrency);
            ProcessTaskTimeoutMs = Math.Max(0, data.ProcessTaskTimeoutMs);
            PullTaskTimeoutMs = Math.Max(1, data.PullTaskTimeoutMs);
            PushTaskTimeoutMs = Math.Max(1, data.PushTaskTimeoutMs);
            ProcessTaskIntervalMs = Math.Max(0, data.ProcessTaskIntervalMs);
            ForgeDownloadTimeoutMs = Math.Max(5000, data.ForgeDownloadTimeoutMs);
        }
    }
}