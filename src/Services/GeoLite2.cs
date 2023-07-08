using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Serilog;

namespace geosvc.Services;

// The GeoLite2 Country, City, and ASN databases are updated twice weekly, every Tuesday and Friday. 
// https://dev.maxmind.com/geoip/geolite2-free-geolocation-data
// Every account is limited to 2,000 total direct downloads in a 24 hour period.
// https://support.maxmind.com/hc/en-us/articles/4408216129947-Download-and-Update-Databases
public static class GeoLite2
{
    const string STATUS_FILE = ".status";
    const string ID_ASN_DB = "GeoLite2-ASN";
    const string FILE_ASN_DB = "GeoLite2-ASN.mmdb";
    const string ID_COUNTRY_DB = "GeoLite2-Country";
    const string FILE_COUNTRY_DB = "GeoLite2-Country.mmdb";
    const string ID_COUNTRY_CSV = "GeoLite2-Country-CSV";
    const string FILE_LOCATIONS_CSV = "GeoLite2-Country-Locations-en.csv";
    const string FILE_BLOCKS_CSV = "GeoLite2-Country-Blocks-IPv4.csv";
    const string FILE_BLOCKS_EXTENSION = ".blocks";
    static string GetCountryBlocksFilePath(string countryCode) => C.Paths.RootFor($"{countryCode}{FILE_BLOCKS_EXTENSION}");
    static string GetSuffix(bool isDb) => isDb ? "tar.gz" : "zip";
    static string GetUrl(string id, bool isDb = true) =>
        $"https://download.maxmind.com/app/geoip_download?edition_id={id}&license_key={C.MaxMindLicenseKey}&suffix={GetSuffix(isDb)}";
    public static async Task UpdateAsync()
    {
        var statusFilePath = C.Paths.RootFor(STATUS_FILE);
        var statusInfo = new FileInfo(statusFilePath);

        var staleDate = statusInfo.CreationTimeUtc.AddDays(C.MaxMindMinAge);
        var staleDbs = !statusInfo.Exists || staleDate < DateTime.UtcNow;

        DateTime? newLastModified = null;
        if (staleDbs)
            newLastModified = GetOldestDateTime(newLastModified, await UpdateDbsAsync(newLastModified));
        else
            Log.Information("MaxMind DBs aren't stale yet ({Stale}), skipping download", staleDate);

        var statusJson = statusInfo.Exists ? File.ReadAllText(statusFilePath) : null;
        var statusCountries = string.IsNullOrWhiteSpace(statusJson) ? null : JsonSerializer.Deserialize<List<string>>(statusJson);
        if (staleDbs || statusCountries == null || C.Countries.Except(statusCountries).Count() > 0)
            newLastModified = GetOldestDateTime(newLastModified, await UpdateCountryBlocksAsync(newLastModified));
        else
            Log.Information("No change in country block or not stale yet ({Stale}), skipping download", staleDate);

        var countryBlockFiles = Directory.EnumerateFiles(C.Paths.Root, $"*{FILE_BLOCKS_EXTENSION}");
        foreach (var countryBlockFile in countryBlockFiles)
        {
            if (string.IsNullOrWhiteSpace(countryBlockFile))
                continue;

            var name = Path.GetFileNameWithoutExtension(countryBlockFile);
            if (C.Countries.Contains(name))
                continue;

            Log.Information("Blocks file no longer needed, deleting", Path.GetFileName(countryBlockFile));
            File.Delete(countryBlockFile);
        }

        if (newLastModified.HasValue)
        {
            File.WriteAllText(statusFilePath, JsonSerializer.Serialize(C.Countries));
            statusInfo.CreationTimeUtc = newLastModified.Value;
        }
    }
    public static async Task<HashSet<string>> GetBlocksFor(string countryCode)
    {
        var info = new FileInfo(GetCountryBlocksFilePath(countryCode));
        if (!info.Exists)
            return new();

        using var fileStream = File.OpenRead(info.FullName);
        using var reader = new StreamReader(fileStream);
        var blocks = new HashSet<string>();
        while (!reader.EndOfStream)
        {
            var block = await reader.ReadLineAsync();
            if (!string.IsNullOrWhiteSpace(block))
                blocks.Add(block);
        }

        return blocks;
    }
    static async Task<DateTime?> UpdateCountryBlocksAsync(DateTime? lastModified)
    {
        using var client = new HttpClient();
        var uri = GetUrl(ID_COUNTRY_CSV, false);
        var response = await client.GetAsync(uri);
        if (!response.IsSuccessStatusCode)
        {
            Log.Error("Could not download MaxMind Country CSV: {Reason}", response.ReasonPhrase);
            return null;
        }
        lastModified = GetOldestDateTime(null, response.Content.Headers.LastModified?.UtcDateTime);

        try
        {
            var countryIds = C.Countries.ToDictionary(c => c, c => (Id: string.Empty, Name: string.Empty));
            using var responseStream = await response.Content.ReadAsStreamAsync();
            var zip = new ZipArchive(responseStream, ZipArchiveMode.Read);
            ZipArchiveEntry? locationsEntry = null, blocksEntry = null;
            foreach (var zipEntry in zip.Entries)
                switch (zipEntry.Name)
                {
                    case FILE_LOCATIONS_CSV: locationsEntry = zipEntry; break;
                    case FILE_BLOCKS_CSV: blocksEntry = zipEntry; break;
                }

            if (locationsEntry == null)
            {
                Log.Error("{File} not found in CSV zip file", FILE_BLOCKS_CSV);
                return null;
            }
            if (blocksEntry == null)
            {
                Log.Error("{File} not found in CSV zip file", FILE_LOCATIONS_CSV);
                return null;
            }

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

            foreach (var countryCode in C.Countries)
                if (string.IsNullOrWhiteSpace(countryIds[countryCode].Id))
                {
                    countryIds.Remove(countryCode);
                    Log.Warning("Country {Country} not found in {File}", countryCode, FILE_LOCATIONS_CSV);
                }

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

            foreach (var countryId in countryIds)
            {
                var blocks = countryBlocks[countryId.Value.Id];
                if (blocks.Count == 0)
                    continue;

                var countryCode = countryId.Key;
                var countryName = countryId.Value.Name;
                var countryBlocksPath = GetCountryBlocksFilePath(countryCode);
                await File.WriteAllLinesAsync(countryBlocksPath, blocks);
            }

            Log.Information("Country blocks {CountryCodes} generated", string.Join(", ", countryIds.Keys));
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "Error while processing {File} response", FILE_LOCATIONS_CSV);
        }

        return lastModified;
    }
    static async Task<DateTime?> UpdateDbsAsync(DateTime? lastModified)
    {
        using var client = new HttpClient();
        var asnUri = GetUrl(ID_ASN_DB);
        var asnResponse = await client.GetAsync(asnUri);
        if (!asnResponse.IsSuccessStatusCode)
        {
            Log.Error("Could not download MaxMind ASN DB: {Reason}", asnResponse.ReasonPhrase);
            return null;
        }
        lastModified = GetOldestDateTime(null, asnResponse.Content.Headers.LastModified?.UtcDateTime);
        await ProcessTarGzDb(asnResponse.Content, FILE_ASN_DB);

        var countryUri = GetUrl(ID_COUNTRY_DB);
        var countryResponse = await client.GetAsync(countryUri);
        if (!countryResponse.IsSuccessStatusCode)
        {
            Log.Error("Could not download MaxMind Country DB: {Reason}", countryResponse.ReasonPhrase);
            return null;
        }
        lastModified = GetOldestDateTime(lastModified, countryResponse.Content.Headers.LastModified?.UtcDateTime);
        await ProcessTarGzDb(countryResponse.Content, FILE_COUNTRY_DB);
        return lastModified;
    }
    static async Task ProcessTarGzDb(HttpContent httpContent, string fileName)
    {
        try
        {
            var dbInfo = new FileInfo(C.Paths.RootFor(fileName));
            using var input = await httpContent.ReadAsStreamAsync();
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            await gzip.CopyToAsync(decompressed);
            decompressed.Seek(0, SeekOrigin.Begin);

            var buffer = new byte[100];
            while (true)
            {
                decompressed.Read(buffer, 0, 100);
                var name = Encoding.ASCII.GetString(buffer).Trim('\0');

                if (string.IsNullOrWhiteSpace(name)) // End of file
                    break;

                if (Path.GetFileName(name) != fileName)
                {
                    decompressed.Seek(24, SeekOrigin.Current);
                    decompressed.Read(buffer, 0, 12);
                    var size = Convert.ToInt64(Encoding.UTF8.GetString(buffer, 0, 12).Trim('\0').Trim(), 8);
                    decompressed.Seek(376L + size, SeekOrigin.Current);
                    var pos = decompressed.Position;
                    var offset = 512 - (pos % 512);
                    if (offset == 512)
                        offset = 0;

                    decompressed.Seek(offset, SeekOrigin.Current);
                }
                else
                {
                    decompressed.Seek(24, SeekOrigin.Current);
                    decompressed.Read(buffer, 0, 12);
                    var size = Convert.ToInt64(Encoding.UTF8.GetString(buffer, 0, 12).Trim('\0').Trim(), 8);

                    decompressed.Seek(376L, SeekOrigin.Current);

                    dbInfo.Delete();
                    using var dbStream = dbInfo.Create();
                    var buf = new byte[size];
                    decompressed.Read(buf, 0, buf.Length);
                    dbStream.Write(buf, 0, buf.Length);

                    Log.Information("{File} downloaded", fileName);

                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while processing {File} response", fileName);
        }
    }
    static DateTime? GetOldestDateTime(DateTime? first, DateTime? second)
    {
        if (!first.HasValue)
            return second;
        if (!second.HasValue)
            return first;

        if (first.Value < second.Value)
            return first;

        return second;
    }
}