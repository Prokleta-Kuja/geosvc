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

        var addressList = JsonSerializer.Deserialize<List<TikAddress>>(addressListJson);

        var stari = addressList![0];
        stari.Comment = "C#";
        await UpdateAddressAsync(client, stari);
    }
    static async Task UpdateAddressAsync(HttpClient client, TikAddress address)
    {
        var json = JsonSerializer.Serialize(address);
        var content = new StringContent(json, System.Text.Encoding.UTF8, MediaTypeNames.Application.Json);
        var updateResponse = await client.PatchAsync($"ip/firewall/address-list/{address.Id}", content);
        var updateResponseJson = await updateResponse.Content.ReadAsStringAsync();
        if (updateResponse.StatusCode != HttpStatusCode.OK)
        {
            Log.Error("Could not update MikroTik address {AddressId}: {StatusCode} - {JSON}", address.Id, updateResponse.StatusCode, updateResponseJson);
            return;
        }
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