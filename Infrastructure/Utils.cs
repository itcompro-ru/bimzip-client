using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

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
    
    	public static class StreamExtensions
	{
		/// <summary>
		/// Implementation of <see cref="Stream.CopyTo(System.IO.Stream)"/> with progress reporting
		/// </summary>
		/// <param name="fromStream"></param>
		/// <param name="destination"></param>
		/// <param name="bufferSize"></param>
		/// <param name="progressInfo"></param>
		internal static void CopyToProgress(this Stream fromStream, Stream destination, int bufferSize, CopyProgressInfo progressInfo)
		{
			var buffer = new byte[bufferSize];
			int count;
			while ((count = fromStream.Read(buffer, 0, buffer.Length)) != 0)
			{
				progressInfo.BytesTransfered += count;
				destination.Write(buffer, 0, count);
			}
		}

		/// <summary>
		/// Implementation of <see cref="Stream.CopyToAsync(System.IO.Stream)"/> with progress reporting
		/// </summary>
		/// <param name="fromStream"></param>
		/// <param name="destination"></param>
		/// <param name="bufferSize"></param>
		/// <param name="progressInfo"></param>
		internal static async Task CopyToAsyncProgress(this Stream fromStream, Stream destination, int bufferSize, CopyProgressInfo progressInfo)
		{
			var buffer = new byte[bufferSize];
			int count;
			while ((count = await fromStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
			{
				progressInfo.BytesTransfered += count;
				await destination.WriteAsync(buffer, 0, count);
			}
		}

		/// <summary>
		/// Implementation of <see cref="Stream.CopyToAsync(System.IO.Stream)"/> with progress reporting
		/// </summary>
		/// <param name="fromStream"></param>
		/// <param name="destination"></param>
		/// <param name="bufferSize"></param>
		/// <param name="progressInfo"></param>
		internal static async Task CopyToAsyncProgress(this Stream fromStream, Stream destination, int bufferSize, CopyProgressInfo progressInfo, CancellationToken cancellationToken)
		{
			var buffer = new byte[bufferSize];
			int count;
			while ((count = await fromStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) != 0)
			{
				progressInfo.BytesTransfered += count;
				await destination.WriteAsync(buffer, 0, count, cancellationToken);
			}
		}
		
	
	}
	
	public class CopyProgressInfo
	{
		public long BytesTransfered { get; set; }
	}
}