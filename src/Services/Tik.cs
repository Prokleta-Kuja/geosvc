using System.Net;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace geosvc.Services;

public static class Tik
{
    const string EMPTY_JSON = "{}";
    public static async Task UpdateAsync()
    {
        if (string.IsNullOrWhiteSpace(C.MikrotikAuth))
        {
            Log.Information("TIK_AUTH not specified, skipping update");
            return;
        }
        if (string.IsNullOrWhiteSpace(C.MikrotikIp))
        {
            Log.Information("TIK_IP not specified, skipping update");
            return;
        }
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
        using var client = new HttpClient(handler);
        client.BaseAddress = new Uri($"https://{C.MikrotikIp}/rest/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", C.MikrotikAuth);


        var addressListResponse = await client.GetAsync("ip/firewall/address-list?.proplist=.id,address,list,comment");
        var addressListJson = await addressListResponse.Content.ReadAsStringAsync();
        if (addressListResponse.StatusCode != HttpStatusCode.OK || addressListJson == EMPTY_JSON)
        {
            Log.Error("Could not get MikroTik address list: {StatusCode} - {JSON}", addressListResponse.StatusCode, addressListJson);
            return;
        }

        var tikCountryList = new Dictionary<string, Dictionary<string, string>>(); // List(Address(Id))
        var addressList = JsonSerializer.Deserialize<List<TikAddress>>(addressListJson) ?? new(0);
        foreach (var item in addressList)
        {
            if (!tikCountryList.ContainsKey(item.List))
                tikCountryList.Add(item.List, new());

            if (!item.Address.Contains('/'))
                item.Address = $"{item.Address}/32";
            tikCountryList[item.List].TryAdd(item.Address, item.Id);
        }

        foreach (var countryCode in C.Countries)
        {
            var countryBlocks = await GeoLite2.GetBlocksForAsync(countryCode);
            if (countryBlocks.Count == 0)
            {
                Log.Error("No blocklist found for country code {CountryCode}", countryCode);
                continue;
            }
            var del = tikCountryList.TryGetValue(countryCode, out var tik) ? tik : new();

            var add = new HashSet<string>();
            foreach (var address in countryBlocks)
                if (del.ContainsKey(address))
                    del.Remove(address);
                else
                    add.Add(address);

            Log.Information("Country list {CountryCode}: {AddCount} additions, {DelCount} removals", countryCode, add.Count, del.Count);

            var idx = 0;
            foreach (var range in del)
            {
                await DeleteAddressAsync(client, range.Value);
                Log.Debug("Adding {CountryCode}-{Range} ({Idx} / {Total})", countryCode, range.Key, idx, add.Count);
                idx++;
            }

            idx = 0;
            foreach (var range in add)
            {
                await CreateAddressAsync(client, countryCode, range);
                Log.Debug("Adding {CountryCode}-{Range} ({Idx} / {Total})", countryCode, range, idx, add.Count);
                idx++;
            }

            Log.Information("Country list {CountryCode} on MikroTik updated");
        }
    }
    static async Task CreateAddressAsync(HttpClient client, string countryCode, string range)
    {
        var json = JsonSerializer.Serialize(new { address = range, list = countryCode, comment = C.MikrotikComment });
        var content = new StringContent(json, System.Text.Encoding.UTF8, MediaTypeNames.Application.Json);
        var createResponse = await client.PutAsync($"ip/firewall/address-list", content);
        var createResponseJson = await createResponse.Content.ReadAsStringAsync();
        if (createResponse.StatusCode != HttpStatusCode.Created)
            Log.Error("Could not add MikroTik address {CountryCode}-{Range}: {StatusCode} - {JSON}", countryCode, range, createResponse.StatusCode, createResponseJson);
    }
    static async Task UpdateAddressAsync(HttpClient client, TikAddress address)
    {
        var json = JsonSerializer.Serialize(address);
        var content = new StringContent(json, System.Text.Encoding.UTF8, MediaTypeNames.Application.Json);
        var updateResponse = await client.PatchAsync($"ip/firewall/address-list/{address.Id}", content);
        var updateResponseJson = await updateResponse.Content.ReadAsStringAsync();
        if (updateResponse.StatusCode != HttpStatusCode.OK)
            Log.Error("Could not update MikroTik address {AddressId}: {StatusCode} - {JSON}", address.Id, updateResponse.StatusCode, updateResponseJson);
    }
    static async Task DeleteAddressAsync(HttpClient client, string id)
    {
        var deleteResponse = await client.DeleteAsync($"ip/firewall/address-list/{id}");
        var deleteResponseJson = await deleteResponse.Content.ReadAsStringAsync();
        if (deleteResponse.StatusCode != HttpStatusCode.NoContent)
            Log.Error("Could not delete MikroTik addressId {AddressId}: {StatusCode} - {JSON}", id, deleteResponse.StatusCode, deleteResponseJson);
    }
}

public class TikAddress
{
    [JsonPropertyName(".id")]
    public string Id { get; set; } = null!;
    [JsonPropertyName("address")]
    public string Address { get; set; } = null!;
    [JsonPropertyName("list")]
    public string List { get; set; } = null!;
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }
}