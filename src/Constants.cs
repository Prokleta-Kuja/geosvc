using System.Text;

namespace geosvc;

public static class C
{
    public static readonly bool IsDebug;
    public static readonly string MaxMindLicenseKey;
    public static readonly int MaxMindMinAge;
    public static readonly HashSet<string> Countries = new();
    public static readonly string MikrotikAuth;
    public static readonly string MikrotikIp;
    public static readonly string? MikrotikComment;
    static C()
    {
        IsDebug = Environment.GetEnvironmentVariable("DEBUG") == "1";
        MaxMindLicenseKey = Environment.GetEnvironmentVariable("MAXMIND_LIC") ?? throw new ArgumentException("MAXMIND_LIC not provided");
        MaxMindMinAge = int.TryParse(Environment.GetEnvironmentVariable("MAXMIND_AGE"), out var mmMinAge) ? mmMinAge : 3;
        var countriesEnv = Environment.GetEnvironmentVariable("COUNTRIES");
        if (!string.IsNullOrWhiteSpace(countriesEnv))
        {
            var countries = countriesEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var country in countries)
                Countries.Add(country.ToUpperInvariant());
        }
        var mikrotikAuthEnv = Environment.GetEnvironmentVariable("TIK_AUTH") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(mikrotikAuthEnv))
            MikrotikAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes(mikrotikAuthEnv));
        MikrotikIp = Environment.GetEnvironmentVariable("TIK_IP") ?? string.Empty;
        MikrotikComment = Environment.GetEnvironmentVariable("TIK_COMMENT");
    }
    public static class Paths
    {
        public static string Root => IsDebug ? Path.Combine(Environment.CurrentDirectory, "data") : "/data";
        public static string RootFor(string fileName) => Path.Combine(Root, fileName);
    }
}