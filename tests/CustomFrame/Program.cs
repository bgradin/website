using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Gradinware.Controllers;
using Gradinware.CustomFrame;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
Directory.CreateDirectory(directory);
var catalogPath = Path.Combine(directory, "catalog.json");
var secretsPath = Path.Combine(directory, "secrets.json");
var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
void Write(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, options));
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); }
var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string> {
    ["CustomFrame:CatalogPath"] = catalogPath, ["CustomFrame:SecretsPath"] = secretsPath }).Build();
var handler = new TokenHandler();
var clock = new TestClock();
var tokens = new DropboxTokenProvider(handler, clock);
var store = new FrameCatalog(configuration, tokens);
CustomFrameController Controller(string query = "", string key = null)
{
    var context = new DefaultHttpContext();
    context.Request.QueryString = new QueryString(query);
    if (key != null) context.Request.Headers["AuthKey"] = key;
    return new CustomFrameController(store) { ControllerContext = new ControllerContext { HttpContext = context } };
}
int Status(IActionResult result) => result is ObjectResult obj ? obj.StatusCode ?? 200 : result is StatusCodeResult status ? status.StatusCode : 200;
Release Release(int code) => new() { AppVersion = "display " + code, AppVersionCode = code,
    ApplicationId = "com.bgradin.customframe", MinSdk = 25, PublishedAt = DateTimeOffset.UtcNow,
    ReleaseNotes = "", Download = new() { Url = "https://example.com/" + code + ".apk", SizeBytes = 123, Sha256 = new string('a', 64) } };
try
{
    var catalog = new Catalog { Releases = new(), SupportedDevices = new() };
    Write(catalogPath, catalog);
    Write(secretsPath, new Secrets { ApprovedKeys = new() { "approved" }, Sources = new() });
    Check(((ContentResult)Controller().Releases()).Content == "{\"releases\":[],\"nextCursor\":null}", "empty history");
    Check(Status(Controller("?limit=0").Releases()) == 400, "invalid limit");
    Check(Status(Controller("?limit=1&limit=2").Releases()) == 400, "duplicate limit");
    Check(Status(Controller("?cursor=broken").Releases()) == 400, "invalid cursor");
    Check(Status(await Controller(key: "approved").Config()) == 503, "no recommended release");
    catalog.Releases.AddRange(new[] { Release(1), Release(2), Release(3) });
    catalog.RecommendedVersionCode = 3;
    catalog.SupportedDevices["3"] = new() { new() { Manufacturer = "tested", Model = "hardware", MinSdk = 25 } };
    Write(catalogPath, catalog);
    var first = Controller("?limit=2");
    using var page = JsonDocument.Parse(((ContentResult)first.Releases()).Content);
    Check(page.RootElement.GetProperty("releases")[0].GetProperty("appVersionCode").GetInt32() == 3, "descending order");
    var cursor = page.RootElement.GetProperty("nextCursor").GetString();
    using var second = JsonDocument.Parse(((ContentResult)Controller("?cursor=" + Uri.EscapeDataString(cursor)).Releases()).Content);
    Check(second.RootElement.GetProperty("releases").GetArrayLength() == 1, "continuation");
    var conditional = Controller("?limit=2");
    conditional.Request.Headers.IfNoneMatch = first.Response.Headers.ETag;
    Check(Status(conditional.Releases()) == 304, "conditional request");
    Check(Status(await Controller().Config()) == 401, "missing key");
    Check(Status(await Controller(key: "wrong").Config()) == 401, "invalid key");
    var valid = Controller(key: "approved");
    Check(await valid.Config() is JsonResult && valid.Response.Headers.CacheControl == "no-store", "authenticated uncached config");
    var source = new Source { Name = "Dropbox", AppKey = "app", AppSecret = "secret", RefreshToken = "refresh" };
    Write(secretsPath, new Secrets { ApprovedKeys = new() { "approved" }, Sources = new() { ["dropbox"] = source } });
    Check(Status(await Controller(key: "wrong").Config()) == 401 && handler.Calls == 0, "unauthorized never refreshes");
    var configs = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Controller(key: "approved").Config()));
    Check(handler.Calls == 1 && configs.All(c => c is JsonResult), "concurrent requests share refresh");
    var serialized = JsonSerializer.Serialize(((JsonResult)configs[0]).Value, options);
    Check(serialized.Contains("accessToken") && !serialized.Contains("refreshToken") && !serialized.Contains("appSecret"), "only access token delivered");
    clock.Advance(14101);
    Check(await Controller(key: "approved").Config() is JsonResult && handler.Calls == 2, "refresh before expiry");
    source.RefreshToken = "rotated";
    Write(secretsPath, new Secrets { ApprovedKeys = new() { "approved" }, Sources = new() { ["dropbox"] = source } });
    Check(await Controller(key: "approved").Config() is JsonResult && handler.Calls == 3, "credential rotation invalidates cache");
    clock.Advance(14101);
    handler.Payload = "upstream secret error";
    handler.Status = HttpStatusCode.BadRequest;
    var upstreamFailure = Controller(key: "approved");
    var upstreamResult = await upstreamFailure.Config();
    Check(Status(upstreamResult) == 503 && upstreamFailure.Response.Headers.CacheControl == "no-store"
        && upstreamFailure.Response.Headers.RetryAfter == "60" && !JsonSerializer.Serialize(((ObjectResult)upstreamResult).Value).Contains("upstream"), "safe refresh failure");
    await Controller(key: "approved").Config();
    Check(handler.Calls == 4, "refresh failure backoff");
    clock.Advance(61);
    handler.Status = HttpStatusCode.OK;
    handler.Payload = "{\"access_token\":\"new-token\",\"expires_in\":14400}";
    Check(await Controller(key: "approved").Config() is JsonResult && handler.Calls == 5, "retry after backoff");
    clock.Advance(14101);
    handler.Payload = "{\"access_token\":\"bad\",\"expires_in\":0}";
    Check(Status(await Controller(key: "approved").Config()) == 503, "reject invalid lifetime");
    clock.Advance(61);
    handler.Payload = "not json";
    Check(Status(await Controller(key: "approved").Config()) == 503, "malformed token response");
    clock.Advance(61);
    handler.Timeout = true;
    Check(Status(await Controller(key: "approved").Config()) == 503, "upstream timeout");
    handler.Timeout = false;
    Write(secretsPath, new Secrets { ApprovedKeys = new() { "approved" }, Sources = new() });
    var removed = await Controller(key: "approved").Config();
    Check(removed is JsonResult && !JsonSerializer.Serialize(((JsonResult)removed).Value, options).Contains("accessToken"), "source removal bypasses cached token");
    Write(secretsPath, new Secrets { ApprovedKeys = new(), Sources = new() });
    Check(Status(await Controller(key: "approved").Config()) == 401, "immediate revocation reload");
    catalog.Releases[0].Download.Sha256 = "invalid";
    Write(catalogPath, catalog);
    Check(Status(Controller().Releases()) == 503, "invalid artifact metadata");
    File.WriteAllText(catalogPath, "malformed secret-looking content");
    var failure = (ObjectResult)Controller().Releases();
    Check(failure.StatusCode == 503 && !JsonSerializer.Serialize(failure.Value).Contains("secret-looking"), "secret-free error");
    Check(Status(Controller().Unknown()) == 404, "unknown contract");
}
finally { Directory.Delete(directory, true); }

sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(int seconds) => now = now.AddSeconds(seconds);
}
sealed class TokenHandler : HttpMessageHandler, IHttpClientFactory
{
    public int Calls;
    public bool Timeout;
    public HttpStatusCode Status = HttpStatusCode.OK;
    public string Payload = "{\"access_token\":\"generated-token\",\"expires_in\":14400}";
    public HttpClient CreateClient(string name) => new(this, false);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        if (Timeout) throw new TaskCanceledException();
        if (request.RequestUri.AbsoluteUri != "https://api.dropboxapi.com/oauth2/token" || request.Method != HttpMethod.Post)
            throw new Exception("Unexpected token request");
        var body = await request.Content.ReadAsStringAsync(cancellationToken);
        if (!body.Contains("grant_type=refresh_token") || !body.Contains("client_secret=secret")) throw new Exception("Invalid OAuth form");
        await Task.Delay(10, cancellationToken);
        return new HttpResponseMessage(Status) { Content = new StringContent(Payload) };
    }
}
