using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Gradinware.Data;
using Gradinware.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Quilting;

namespace Gradinware
{
  public sealed class JsonContentMiddleware
  {
    private readonly RequestDelegate _next;
    private readonly IReactSsrClient _ssrClient;
    private readonly ILogger<JsonContentMiddleware> _logger;

    public JsonContentMiddleware(RequestDelegate next, IReactSsrClient ssrClient, ILogger<JsonContentMiddleware> logger = null)
    {
      _next = next;
      _ssrClient = ssrClient;
      _logger = logger ?? NullLogger<JsonContentMiddleware>.Instance;
    }

    public async Task InvokeAsync(HttpContext context)
    {
      // API responses, including unknown routes, must not depend on Quilt storage.
      if (context.Request.Path.StartsWithSegments("/api"))
      {
        await _next(context);
        return;
      }

      var trunk = context.RequestServices.GetRequiredService<IContentTrunk>();
      var quilt = new Quilt(trunk);

      // Render request path if possible
      if (await RenderJsonContent(context, trunk, quilt))
      {
        return;
      }

      // Allow other middleware
      await _next(context);

      if (context.Response.StatusCode == 404)
      {
        // Fallback to 404
        await RenderJsonContent("/404", context, trunk, quilt);
      }
    }

    private async Task<bool> RenderJsonContent(HttpContext context, IContentTrunk trunk, Quilt quilt)
    {
      if (!context.Request.Path.HasValue)
      {
        return false;
      }

      var requestPath = context.Request.Path.Value;
      var extension = Path.GetExtension(requestPath);
      var filename = Path.GetFileNameWithoutExtension(requestPath);
      if (extension == ".html")
      {
        return await RenderJsonContent(
            Path.Combine(Path.GetDirectoryName(requestPath), filename),
            context, trunk, quilt
        );
      }
      // Handle directory indices
      else if (string.IsNullOrEmpty(extension))
      {
        return await RenderJsonContent(
            Path.Combine(requestPath, "index").Substring(1),
            context, trunk, quilt
        );
      }

      return false;
    }

    private async Task<bool> RenderJsonContent(string key, HttpContext context, IContentTrunk trunk, Quilt quilt)
    {
      key = key.TrimStart('/');
      var isAdminPage = key == "admin" || key.StartsWith("admin/", StringComparison.Ordinal);
      if (isAdminPage && !SiteSession.IsAdmin(context))
      {
        context.Response.StatusCode = 403;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsync("Access denied. Sign in with an administrator account at /login/.");
        return true;
      }
      try
      {
        // Resolve the page and every motif in one database snapshot. Never hold a
        // database transaction open while waiting for the renderer or cache lock.
        var data = trunk.ReadSnapshot(() => GetJsonData(key, trunk, quilt));
        if (data == null) return false;
        var fingerprint = HtmlCache.Fingerprint(data);
        var page = await HtmlCache.GetOrCreate(fingerprint, key,
          () => _ssrClient.Render(data, context.RequestAborted), context.RequestAborted);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = isAdminPage ? "no-store" : "no-cache";

        await context.Response.WriteAsync(page);
      }
      catch (HttpRequestException ex)
      {
        _logger.LogError(ex, "SSR failed for {PageKey}", key);
        await ReportServerError(context, 500);
        return true;
      }

      catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
      {
        _logger.LogWarning("SSR timed out for {PageKey}", key);
        await ReportServerError(context, 504);
        return true;
      }

      return true;
    }

    private JToken GetJsonData(string key, IContentTrunk trunk, Quilt quilt)
    {
      return quilt.GetPatch(Quilting.Constants.ContentKey + trunk.Delimiter + key, Startup.Configuration?["AdminUsername"]);
    }

    private static async Task ReportServerError(HttpContext context, int status)
    {
      context.Response.StatusCode = status;
      context.Response.ContentType = "text/html; charset=utf-8";
      context.Response.Headers.CacheControl = "no-store";
      await context.Response.WriteAsync(@"<!DOCTYPE html>
<html lang=""en""><head><meta charset=""utf-8""><title>Error</title></head>
<body><h1>Error</h1><p>Sorry, the server could not render this page. Try again later.</p></body></html>");
    }
  }
}
