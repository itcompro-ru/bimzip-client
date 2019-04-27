using Serilog;
using Serilog.Core;
using Serilog.Sinks.SystemConsole.Themes;

namespace BimZipClient.Infrastructure
{
    public static class Log
    {
        public static Logger Create(string name)
        {
            return new LoggerConfiguration()
                .WriteTo
                .Console(
                    theme: ConsoleTheme.None,
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] " + name + ": {Message:lj}{NewLine}{Exception}"
                )
                .MinimumLevel.Verbose()
                .CreateLogger();
        }
    }
}