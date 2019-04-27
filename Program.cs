using System;
using System.IO;
using System.Threading.Tasks;
using BimZipClient.Infrastructure;
using Serilog.Core;

namespace BimZipClient
{
    static class Program
    {
        private static Logger _log;

        private static async Task<int> Main(string[] args)
        {
            _log = Log.Create("main");
            _log.Information("BimZip client started");
            ClientConfig config;

            try
            {
                config = new ClientConfig(args[0], new DirectoryInfo(args[1]), args[2]);
            }
            catch (Exception)
            {
                _log.Fatal("Configuration error: ");
                _log.Fatal("pass args: clientId, working directory");
                if (Utils.IsWindows())
                {
                    _log.Fatal($"e.g.  client.exe YourClientKey C:\\path\\to\\backup http://server.api");
                }
                else
                {
                    _log.Fatal($"e.g.  ./client YourClientKey /path/to/backup http://server.api");    
                }
                return -1;
            }

            try
            {
                config.CreateBackupPath();
            }
            catch (Exception e)
            {
                _log.Fatal(e, $"Target path is not accessible: {config.BasePath.FullName}");
                return -1;
            }

            var manager = new Manager(config);
            return await manager.Start();
        }
    }
}