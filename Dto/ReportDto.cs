namespace BimZipClient.Dto
{
    public class ReportDto
    {
        public string Id { get; set; }
        public bool Success { get; set; }
        public long Size { get; set; }
        public long DownloadTimeMs { get; set; }
        public string Message { get; set; }
    }
}