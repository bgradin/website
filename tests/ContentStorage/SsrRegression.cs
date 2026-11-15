using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Gradinware;
using Gradinware.Data;
using Gradinware.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

internal static class SsrRegression
{
  private static void Check(bool condition, string message)
  {
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message);
  }

  private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
  }

  // This suite exercises the renderer boundary without SQLite or network access.
  private sealed class Trunk : IContentTrunk
  {
    public string Delimiter => ".";
    public void EnsureCreated() { }
    public T ReadSnapshot<T>(Func<T> read) => read();
    public JToken Get(string key, string id) => JObject.Parse("{ 'ui:type': 'Page', 'body': 'hello' }");
    public void BeginTransaction() => throw new NotSupportedException();
    public void CommitTransaction() => throw new NotSupportedException();
    public void RollbackTransaction() => throw new NotSupportedException();
    public void Clear(string id) => throw new NotSupportedException();
    public IEnumerable<string> GetKeys(string id) => throw new NotSupportedException();
    public IEnumerable<string> GetKeys(string prefix, string id) => throw new NotSupportedException();
    public bool Put(string key, JToken value, string id) => throw new NotSupportedException();
    public bool Delete(string key, string id) => throw new NotSupportedException();
    public bool CanPut(string key, string id) => throw new NotSupportedException();
  }

  public static async Task Run(string directory)
  {
    var oldDirectory = Environment.GetEnvironmentVariable("DATA_DIRECTORY");
    var oldTtl = Environment.GetEnvironmentVariable("SSR_CACHE_TTL_SECONDS");
    Environment.SetEnvironmentVariable("DATA_DIRECTORY", directory);
    Environment.SetEnvironmentVariable("SSR_CACHE_TTL_SECONDS", "0");
    try
    {
      using var services = new ServiceCollection().AddSingleton<IContentTrunk>(new Trunk()).BuildServiceProvider();
      DefaultHttpContext Context(CancellationToken token = default)
      {
        var context = new DefaultHttpContext { RequestServices = services, RequestAborted = token };
        context.Request.Path = "/index.html";
        context.Response.Body = new MemoryStream();
        return context;
      }
      Task Invoke(HttpContext context, HttpClient http) => new JsonContentMiddleware(
        _ => throw new Exception("Page escaped SSR"), new ReactSsrClient(http)).InvokeAsync(context);
      string Body(DefaultHttpContext context) => System.Text.Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

      using (var http = new HttpClient(new Handler(async (request, token) =>
      {
        Check(request.Method == HttpMethod.Post && request.RequestUri.ToString() == "http://127.0.0.1:3000/", "renderer posts to the local SSR endpoint");
        var json = await request.Content.ReadAsStringAsync(token);
        Check(!json.Contains('\n') && JObject.Parse(json)["body"].Value<string>() == "hello", "renderer sends compact JSON");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<!DOCTYPE html><html></html>") };
      })))
      {
        var context = Context();
        await Invoke(context, http);
        Check(context.Response.StatusCode == 200 && Body(context).StartsWith("<!DOCTYPE html>"), "successful SSR reaches the browser");
      }

      using (var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        { Content = new StringContent("private renderer failure") }))))
      {
        var context = Context();
        await Invoke(context, http);
        Check(context.Response.StatusCode == 500 && !Body(context).Contains("private renderer failure") &&
          context.Response.Headers.CacheControl == "no-store", "renderer failure returns a generic uncached error");
      }

      var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      using (var http = new HttpClient(new Handler(async (_, token) =>
      {
        started.TrySetResult();
        await Task.Delay(Timeout.Infinite, token);
        throw new Exception("Unreachable");
      })))
      {
        var client = new ReactSsrClient(http);
        http.Timeout = TimeSpan.FromMilliseconds(50);
        var context = Context();
        await new JsonContentMiddleware(_ => Task.CompletedTask, client).InvokeAsync(context);
        Check(context.Response.StatusCode == 504 && context.Response.Headers.CacheControl == "no-store", "renderer timeout returns an uncached 504");
      }

      started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      using (var cancellation = new CancellationTokenSource())
      using (var http = new HttpClient(new Handler(async (_, token) =>
      {
        started.TrySetResult();
        await Task.Delay(Timeout.Infinite, token);
        throw new Exception("Unreachable");
      })))
      {
        var context = Context(cancellation.Token);
        var rendering = Invoke(context, http);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        try { await rendering; throw new Exception("Request cancellation was swallowed"); }
        catch (OperationCanceledException) { }
        Check(context.Response.Body.Length == 0 && context.Response.StatusCode != 504, "browser cancellation reaches HttpClient without producing a timeout page");
      }
    }
    finally
    {
      await HtmlCache.Clear();
      Environment.SetEnvironmentVariable("DATA_DIRECTORY", oldDirectory);
      Environment.SetEnvironmentVariable("SSR_CACHE_TTL_SECONDS", oldTtl);
    }
  }
}
