using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Gradinware.CustomFrame;

public sealed class FrameCatalog
{
    private readonly IConfiguration configuration;
    private readonly DropboxTokenProvider tokens;
    public FrameCatalog(IConfiguration configuration, DropboxTokenProvider tokens)
    { this.configuration = configuration; this.tokens = tokens; }

    // Read complete snapshots on every request. Operators replace files atomically.
    private T Read<T>(string setting)
    {
        var path = configuration["CustomFrame:" + setting];
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException();
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), new JsonSerializerOptions
        { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? throw new InvalidDataException();
    }

    public Secrets ReadSecrets()
    {
        var secrets = Read<Secrets>("SecretsPath");
        if (secrets.ApprovedKeys == null || secrets.ApprovedKeys.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException();
        return secrets;
    }

    public Catalog ReadCatalog()
    {
        var catalog = Read<Catalog>("CatalogPath");
        if (catalog.Releases == null) throw new InvalidDataException();
        var codes = new HashSet<int>();
        foreach (var release in catalog.Releases)
        {
            if (release == null || release.AppVersionCode <= 0 || !codes.Add(release.AppVersionCode)
                || string.IsNullOrWhiteSpace(release.AppVersion)
                || release.ApplicationId != "com.bgradin.customframe" || release.MinSdk <= 0
                || release.PublishedAt == default || release.PublishedAt.Offset != TimeSpan.Zero
                || release.ReleaseNotes == null || release.Download == null
                || !Uri.TryCreate(release.Download.Url, UriKind.Absolute, out var url)
                || url.Scheme != "https" || !string.IsNullOrEmpty(url.UserInfo)
                || release.Download.SizeBytes <= 0 || release.Download.Sha256 == null
                || release.Download.Sha256.Length != 64
                || release.Download.Sha256.Any(c => !(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')))
                throw new InvalidDataException();
        }
        catalog.Releases = catalog.Releases.OrderByDescending(r => r.AppVersionCode).ToList();
        return catalog;
    }

    public async Task<object> GetConfigAsync(Catalog catalog, Secrets secrets, CancellationToken cancellationToken)
    {
        var release = catalog.Releases.SingleOrDefault(r => r.AppVersionCode == catalog.RecommendedVersionCode);
        if (release == null || catalog.SupportedDevices == null
            || !catalog.SupportedDevices.ContainsKey(release.AppVersionCode.ToString(System.Globalization.CultureInfo.InvariantCulture))
            || secrets.Sources == null) throw new InvalidDataException();
        foreach (var entry in catalog.SupportedDevices)
        {
            if (!int.TryParse(entry.Key, out var code) || code <= 0
                || code.ToString(System.Globalization.CultureInfo.InvariantCulture) != entry.Key
                || !catalog.Releases.Any(r => r.AppVersionCode == code) || entry.Value == null
                || entry.Value.Any(r => r == null || string.IsNullOrWhiteSpace(r.Manufacturer)
                    || string.IsNullOrWhiteSpace(r.Model) || r.MinSdk <= 0)) throw new InvalidDataException();
        }
        foreach (var source in secrets.Sources)
            if (source.Key != "dropbox" || source.Value == null || string.IsNullOrWhiteSpace(source.Value.Name)
                || string.IsNullOrWhiteSpace(source.Value.AppKey)
                || string.IsNullOrWhiteSpace(source.Value.AppSecret)
                || string.IsNullOrWhiteSpace(source.Value.RefreshToken)) throw new InvalidDataException();
        var sources = new Dictionary<string, object>();
        foreach (var source in secrets.Sources)
            sources[source.Key] = new { source.Value.Name, accessToken = await tokens.GetAsync(source.Value, cancellationToken) };
        return new { v1 = new { release.AppVersion, release.AppVersionCode,
            catalog.SupportedDevices, sources } };
    }

    public static bool IsAuthorized(string key, Secrets secrets)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var approved = false;
        foreach (var candidate in secrets.ApprovedKeys)
            approved |= CryptographicOperations.FixedTimeEquals(hash, SHA256.HashData(Encoding.UTF8.GetBytes(candidate)));
        return approved;
    }
}

public sealed class Catalog
{
    public List<Release> Releases { get; set; }
    public int RecommendedVersionCode { get; set; }
}
public sealed class Release
{
    public string AppVersion { get; set; }
    public int AppVersionCode { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public string ReleaseNotes { get; set; }
    public string ApplicationId { get; set; }
    public int MinSdk { get; set; }
    public Download Download { get; set; }
    public List<Device> SupportedDevices { get; set; }
}
public sealed class Download
{
    public string Url { get; set; }
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; }
}
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed class Device
{
    public string Manufacturer { get; set; }
    public string Model { get; set; }
    public int MinSdk { get; set; }
}
public sealed class Secrets
{
    public List<string> ApprovedKeys { get; set; }
    public Dictionary<string, Source> Sources { get; set; }
}
public sealed class Source
{
    public string Name { get; set; }
    public string AppKey { get; set; }
    public string AppSecret { get; set; }
    public string RefreshToken { get; set; }
}
