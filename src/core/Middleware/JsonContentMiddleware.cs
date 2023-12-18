using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Gradinware.Data;
using Gradinware.Services;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using Quilting;

namespace Gradinware
{
  public sealed class JsonContentMiddleware
  {
    private static readonly string DATA_DIRECTORY = "/data";

    private readonly RequestDelegate _next;
    private readonly IReactSsrClient _ssrClient;
    private IContentTrunk _trunk;
    private Quilt _quilt;

    public JsonContentMiddleware(RequestDelegate next, IReactSsrClient ssrClient)
    {
      _next = next;
      _ssrClient = ssrClient;
    }

    public async Task InvokeAsync(HttpContext context, IContentTrunk trunk)
    {
      _trunk = trunk;
      _quilt = new Quilt(trunk);

      // Render request path if possible
      if (await RenderJsonContent(context))
      {
        return;
      }

      // Allow other middleware
      await _next(context);

      if (context.Response.StatusCode == 404)
      {
        // Fallback to 404
        await RenderJsonContent("/404", context);
      }
    }

    private async Task<bool> RenderJsonContent(HttpContext context)
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
            context
        );
      }
      // Handle directory indices
      else if (string.IsNullOrEmpty(extension))
      {
        return await RenderJsonContent(
            Path.Combine(requestPath, "index").Substring(1),
            context
        );
      }

      return false;
    }

    private async Task<bool> RenderJsonContent(string key, HttpContext context)
    {
      try
      {
        string page = null;
        if (HtmlCache.Exists(key))
        {
          page = await HtmlCache.Load(key);
        }
        else
        {
          JToken data = GetJsonData(key);
          if (data == null)
          {
            return false;
          }

          page = await RenderJsonContent(key, data);
        }

        await context.Response.WriteAsync(page);
      }
      catch (HttpRequestException ex)
      {
        await ReportServerError(context, ex);
        return true;
      }

      return true;
    }

    private async Task<string> RenderJsonContent(string key, JToken data)
    {
      var page = await _ssrClient.Render(data);
      await HtmlCache.Store(key, page);
      return page;
    }

    private JToken GetJsonData(string key)
    {
      return _quilt.GetPatch(Quilting.Constants.ContentKey + _trunk.Delimiter + key, Startup.Configuration?["AdminUsername"]);
    }

    private static async Task ReportServerError(HttpContext context, Exception ex)
    {
      await UpdateStatusAndWriteServerError(context, async () =>
      {
        await context.Response.WriteAsync($@"<!--
    Exception: {ex.Message}
    Stacktrace: {ex.StackTrace}
-->
");

        await ReportServerError(context);
      });
    }

    private static async Task ReportServerError(HttpContext context)
    {
      await context.Response.WriteAsync($@"
  <h1>Error</h1>
  <p>Sorry, the server threw an unexpected error 😔. Try again later.</p>
");
    }

    private static async Task UpdateStatusAndWriteServerError(HttpContext context, Func<Task> writeError)
    {
      if (context.Response.StatusCode != 500)
      {
        context.Response.StatusCode = 500;
      }

      await context.Response.WriteAsync($@"<!DOCTYPE html>
  <html lang=""en"">
  <head>
    <meta charset=""utf-8"">
    <title>Error</title>
    <link rel=""stylesheet"" href=""https://cdn.simplecss.org/simple.css"">
  </head>
  <body>
");

      await writeError();

      await context.Response.WriteAsync("</body></html>");
    }
  }
}
