using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gradinware.CustomFrame;
using Microsoft.AspNetCore.Mvc;

namespace Gradinware.Controllers;

[ApiController]
[Route("api/custom-frame")]
public sealed class CustomFrameController : ControllerBase
{
    private readonly FrameCatalog store;
    public CustomFrameController(FrameCatalog store) { this.store = store; }
    private ObjectResult Error(int status, string code, string message) =>
        StatusCode(status, new { error = new { code, message } });

    [HttpGet("v1/releases")]
    public IActionResult Releases()
    {
        Response.Headers.CacheControl = "no-store";
        var limit = 20;
        if (Request.Query.TryGetValue("limit", out var limits)
            && (limits.Count != 1 || !int.TryParse(limits[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit)
                || limit < 1 || limit > 100))
            return Error(400, "INVALID_LIMIT", "The release limit is invalid.");
        int? before = null;
        if (Request.Query.TryGetValue("cursor", out var cursors))
        {
            if (cursors.Count != 1 || !TryCursor(cursors[0], out var code))
                return Error(400, "INVALID_CURSOR", "The release cursor is invalid.");
            before = code;
        }
        try
        {
            var catalog = store.ReadCatalog();
            var remaining = catalog.Releases.Where(r => !before.HasValue || r.AppVersionCode < before.Value).ToList();
            var page = remaining.Take(limit).ToList();
            var body = new { releases = page, nextCursor = remaining.Count > limit
                ? Cursor(page.Last().AppVersionCode) : null };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var etag = "\"" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + "\"";
            Response.Headers.CacheControl = "public, max-age=300";
            Response.Headers.ETag = etag;
            if (Request.Headers.IfNoneMatch.ToString().Split(',').Any(v => v.Trim() == "*" || v.Trim().Replace("W/", "") == etag))
                return StatusCode(304);
            return Content(Encoding.UTF8.GetString(bytes), "application/json");
        }
        catch (Exception ex) when (Unavailable(ex)) { return UnavailableResult(); }
    }

    [HttpGet("v1/config")]
    public async System.Threading.Tasks.Task<IActionResult> Config()
    {
        Response.Headers.CacheControl = "no-store";
        if (!Request.Headers.TryGetValue("AuthKey", out var keys) || keys.Count != 1 || string.IsNullOrWhiteSpace(keys[0]))
            return UnauthorizedResult();
        try
        {
            var secrets = store.ReadSecrets();
            if (!FrameCatalog.IsAuthorized(keys[0], secrets)) return UnauthorizedResult();
            return new JsonResult(await store.GetConfigAsync(store.ReadCatalog(), secrets, HttpContext.RequestAborted));
        }
        catch (Exception ex) when (Unavailable(ex)) { return UnavailableResult(); }
    }

    [Route("{**path}")]
    public IActionResult Unknown()
    {
        Response.Headers.CacheControl = "no-store";
        return Error(404, "NOT_FOUND", "The requested API route was not found.");
    }

    private IActionResult UnauthorizedResult() => Error(401, "UNAUTHORIZED", "Authorization is required.");
    private IActionResult UnavailableResult()
    {
        Response.Headers.RetryAfter = "60";
        return Error(503, "SERVICE_UNAVAILABLE", "The requested service is temporarily unavailable.");
    }
    private static bool Unavailable(Exception ex) => ex is System.IO.IOException
        || ex is System.IO.InvalidDataException
        || ex is UnauthorizedAccessException || ex is JsonException || ex is ArgumentException;
    private static string Cursor(int code) => Convert.ToBase64String(Encoding.UTF8.GetBytes("v1:" + code.ToString(CultureInfo.InvariantCulture)));
    private static bool TryCursor(string cursor, out int code)
    {
        code = 0;
        try
        {
            var value = Encoding.UTF8.GetString(Convert.FromBase64String(cursor ?? ""));
            return value.StartsWith("v1:", StringComparison.Ordinal)
                && int.TryParse(value.Substring(3), NumberStyles.None, CultureInfo.InvariantCulture, out code)
                && code > 0 && Cursor(code) == cursor;
        }
        catch (FormatException) { return false; }
    }
}
