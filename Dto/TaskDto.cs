using System.Collections.Generic;
using System.Linq;

namespace BimZipClient.Dto
{
    public class TaskDto
    {
        public List<FileDto> FileList { get; set; }
        public string Command { get; set; }
        public int CommandCode => int.Parse(string.Concat(Command.Where(char.IsDigit)));
    }
}