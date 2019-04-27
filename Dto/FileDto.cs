using System.Collections.Generic;

namespace BimZipClient.Dto
{
    public class FileDto
    {
        public string Id { get; set; }
        public string Url { get; set; }
        public List<string> Path { get; set; }
    }
}