using System.Diagnostics;
using System.IO.Compression;

const string ZIP_FILE_NAME = "csv.zip";
const string LOCATIONS_FILE_NAME = "GeoLite2-Country-Locations-en.csv";
const string BLOCKS_FILE_NAME = "GeoLite2-Country-Blocks-IPv4.csv";

var countriesArg = Environment.GetEnvironmentVariable("COUNTRIES");
if (string.IsNullOrWhiteSpace(countriesArg))
    throw new ArgumentNullException(nameof(countriesArg));
var countryCodes = countriesArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var countryIds = countryCodes.ToDictionary(c => c, c => (Id: string.Empty, Name: string.Empty));

// var maxmindLicenseKey = Environment.GetEnvironmentVariable("MAXMIND_LIC");
// if (string.IsNullOrWhiteSpace(maxmindLicenseKey))
//     throw new ArgumentNullException(nameof(maxmindLicenseKey));
// var countryCsv = $"https://download.maxmind.com/app/geoip_download?edition_id=GeoLite2-Country-CSV&license_key={maxmindLicenseKey}&suffix=zip";

// var client = new HttpClient();
// using var csvStream = await client.GetStreamAsync(countryCsv);
// using var csvFileStream = File.OpenWrite(ZIP_FILE_NAME);
// await csvStream.CopyToAsync(csvFileStream);

var zip = ZipFile.Open(ZIP_FILE_NAME, ZipArchiveMode.Read);
ZipArchiveEntry? locationsEntry = null, blocksEntry = null;
foreach (var zipEntry in zip.Entries)
    switch (zipEntry.Name)
    {
        case LOCATIONS_FILE_NAME: locationsEntry = zipEntry; break;
        case BLOCKS_FILE_NAME: blocksEntry = zipEntry; break;
    }

if (locationsEntry == null)
    throw new ArgumentNullException(nameof(locationsEntry));
if (blocksEntry == null)
    throw new ArgumentNullException(nameof(blocksEntry));

using var locationsStream = locationsEntry.Open();
using var locationsReader = new StreamReader(locationsStream);
while (!locationsReader.EndOfStream)
{
    var line = await locationsReader.ReadLineAsync();
    if (string.IsNullOrWhiteSpace(line))
        break;
    var parts = line.Split(',');
    var countryId = parts[4];
    if (!countryIds.ContainsKey(countryId))
        continue;

    countryIds[countryId] = (Id: parts[0], Name: parts[5].Trim('"'));
}

// TODO: log not found country and remove it from countryIds
foreach (var countryCode in countryCodes)
    if (string.IsNullOrWhiteSpace(countryIds[countryCode].Id))
        countryIds.Remove(countryCode);

var countryBlocks = countryIds.Values.ToDictionary(c => c.Id, _ => new HashSet<string>());
using var blocksStream = blocksEntry.Open();
using var blocksReader = new StreamReader(blocksStream);
while (!blocksReader.EndOfStream)
{
    var line = await blocksReader.ReadLineAsync();
    if (string.IsNullOrWhiteSpace(line))
        break;

    var parts = line.Split(',');
    var countryId = parts[1];
    if (!countryBlocks.ContainsKey(countryId))
        continue;

    countryBlocks[countryId].Add(parts[0]);
}

var result = new Dictionary<string, (string Name, HashSet<string> Blocks)>();
foreach (var countryId in countryIds)
{
    var blocks = countryBlocks[countryId.Value.Id];
    if (blocks.Count == 0)
        continue;

    var countryCode = countryId.Key;
    var countryName = countryId.Value.Name;
    result.Add(countryCode, (Name: countryName, Blocks: blocks));
}

foreach (var item in result)
    Console.WriteLine($"{item.Value.Name} ({item.Key}): {item.Value.Blocks.Count:#,###}");

//File.Delete(ZIP_FILE_NAME);

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