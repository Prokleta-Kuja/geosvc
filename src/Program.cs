using geosvc.Services;
using Serilog;
using Serilog.Events;

namespace geosvc;
internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(C.IsDebug ? LogEventLevel.Debug : LogEventLevel.Information)
                .MinimumLevel.Override(nameof(Microsoft), LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

        try
        {
            Directory.CreateDirectory(C.Paths.Root);
            await GeoLite2.UpdateAsync();
            await Tik.UpdateAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}