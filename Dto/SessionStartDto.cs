using System;

namespace BimZipClient.Dto
{
    public class SessionStartDto
    {
        public Guid SessionKey { get; set; }
        public int Concurrency { get; set; }
        public int PullTaskTimeoutMs { get; set; }
        public int PushTaskTimeoutMs { get; set; }
        public int ProcessTaskTimeoutMs { get; set; }
        public int ProcessTaskIntervalMs { get; set; }
        public int ForgeDownloadTimeoutMs { get; set; }

        public string AccessToken { get; set; }
    }
}