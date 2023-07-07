// using System.Diagnostics;
// using System.IO.Compression;

// var sw = new Stopwatch();
// sw.Start();
// var resultPath = "result";
// var locationsName = "GeoLite2-Country-Locations-en.csv";
// var blocksName = "GeoLite2-Country-Blocks-IPv4.csv";
// // var zipPath = "GeoLite2-Country-CSV_20230630.zip";
// // ZipFile.ExtractToDirectory(zipPath, "result", true);

// var resultDir = Directory.EnumerateDirectories(resultPath).First();
// var locationsPath = Path.Combine(resultDir, locationsName);
// var blockPath = Path.Combine(resultDir, blocksName);

// var id = string.Empty;
// await foreach (var countryLine in File.ReadLinesAsync(locationsPath))
// {
//     //3202326,en,EU,Europe,HR,Croatia,1
//     var parts = countryLine.Split(',');
//     if (parts[4] != "HR")
//         continue;

//     id = parts[0];
//     break;
// }

// var blocks = new HashSet<string>();
// await foreach (var blockLine in File.ReadLinesAsync(blockPath))
// {
//     // network,geoname_id,registered_country_geoname_id,represented_country_geoname_id,is_anonymous_proxy,is_satellite_provider
//     // 1.0.0.0/24,2077456,2077456,,0,0
//     var parts = blockLine.Split(',');
//     if (parts[1] == id)
//         blocks.Add(parts[0]);
// }

// sw.Stop();
// System.Console.WriteLine(blocks.Count);
// System.Console.WriteLine(sw.Elapsed);

var tikAuth = Environment.GetEnvironmentVariable("TIK_AUTH");
if (string.IsNullOrWhiteSpace(tikAuth))
    throw new ArgumentNullException(nameof(tikAuth));
var tikIp = Environment.GetEnvironmentVariable("TIK_IP");
if (string.IsNullOrWhiteSpace(tikIp))
    throw new ArgumentNullException(nameof(tikIp));

using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
using var client = new HttpClient(handler);
client.BaseAddress = new Uri($"https://{tikIp}/rest/");
client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", tikAuth);

// var response = await client.GetAsync("system/resource");
// System.Console.WriteLine(await response.Content.ReadAsStringAsync());

var result = await client.DeleteAsync("ip/firewall/address-list/*1");
//{"detail":"not enough permissions (9)","error":500,"message":"Internal Server Error"}
System.Console.WriteLine(await result.Content.ReadAsStringAsync());

var response = await client.GetAsync("ip/firewall/address-list?.proplist=.id,address,list");
System.Console.WriteLine(await response.Content.ReadAsStringAsync());