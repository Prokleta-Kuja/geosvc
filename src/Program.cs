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
            //await GeoLite2.UpdateAsync();
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

// var tikAuth = Environment.GetEnvironmentVariable("TIK_AUTH");
// if (string.IsNullOrWhiteSpace(tikAuth))
//     throw new ArgumentNullException(nameof(tikAuth));
// var tikIp = Environment.GetEnvironmentVariable("TIK_IP");
// if (string.IsNullOrWhiteSpace(tikIp))
//     throw new ArgumentNullException(nameof(tikIp));

// using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
// using var client = new HttpClient(handler);
// client.BaseAddress = new Uri($"https://{tikIp}/rest/");
// client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", tikAuth);

// // var response = await client.GetAsync("system/resource");
// // System.Console.WriteLine(await response.Content.ReadAsStringAsync());

// var result = await client.DeleteAsync("ip/firewall/address-list/*1");
// //{"detail":"not enough permissions (9)","error":500,"message":"Internal Server Error"}
// System.Console.WriteLine(await result.Content.ReadAsStringAsync());

// var response = await client.GetAsync("ip/firewall/address-list?.proplist=.id,address,list");
// System.Console.WriteLine(await response.Content.ReadAsStringAsync());