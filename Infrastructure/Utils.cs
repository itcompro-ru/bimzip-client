using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace BimZipClient.Infrastructure
{
    public static class Utils
    {
        public static bool IsWindows() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public static bool IsMac() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public static bool IsLinux() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux);



        public static string CreatePath(List<string> parts)
        {
            var dirs = parts.Take(parts.Count - 1)
                .Select(c => 
                    string.Join("_", 
                        c.Split(Path.GetInvalidPathChars()
                            .Concat(new []{Path.PathSeparator}).ToArray())));
            
            return Path.Combine(Path.Combine(dirs.ToArray()), 
                string.Join("_", parts.Last().Split(Path.GetInvalidFileNameChars())));
        }
    }
}