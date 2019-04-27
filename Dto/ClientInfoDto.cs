using System;

namespace BimZipClient.Dto
{
    public class ClientInfoDto
    {
        public string ClientId { get; set; }
        public string OsVersion { get; set; }
        public string FrameworkVersion { get; set; }
        public int StorageFreeMb { get; set; }
        public DateTime StartDateTime { get; set; }
    }
}