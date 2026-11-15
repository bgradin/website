using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gradinware;
using Gradinware.Data;
using Newtonsoft.Json.Linq;
using Quilting;
using Gradinware.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;

internal static class CacheRegression
{
  private static void Check(bool condition, string name)
  {
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name);
  }

  private sealed class Renderer : IReactSsrClient
  {
    public int Calls;
    public Task<string> Render(JToken data) { Calls++; return Task.FromResult(data.ToString()); }
  }

  public static async Task Run(string directory)
  {
    var oldConfiguration = Startup.Configuration;
    var oldDirectory = Environment.GetEnvironmentVariable("DATA_DIRECTORY");
    var oldBuild = Environment.GetEnvironmentVariable("SSR_BUILD_VERSION");
    var oldTtl = Environment.GetEnvironmentVariable("SSR_CACHE_TTL_SECONDS");
    Environment.SetEnvironmentVariable("DATA_DIRECTORY", directory);
    Environment.SetEnvironmentVariable("SSR_BUILD_VERSION", "cache-test-a");
    Environment.SetEnvironmentVariable("SSR_CACHE_TTL_SECONDS", "300");
    try
    {
      using var trunk = new ContentTrunk("cache.db", directory);
      Check(trunk.Put("q:quilters.admin", JObject.Parse("{ 'q:circleIds': ['lead'] }"), "admin"), "bootstrap fingerprint test");
      typeof(Startup).GetProperty(nameof(Startup.Configuration)).SetValue(null,
        new ConfigurationBuilder().AddInMemoryCollection(new[] { new System.Collections.Generic.KeyValuePair<string, string>("AdminUsername", "admin") }).Build());
      using var services = new ServiceCollection().AddSingleton<IContentTrunk>(trunk).BuildServiceProvider();
      var renderer = new Renderer();
      var middleware = new JsonContentMiddleware(context => { context.Response.StatusCode = 404; return Task.CompletedTask; }, renderer);
      async Task<(int Status, string Body)> Request(string path = "/index.html")
      {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = path;
        using var body = new MemoryStream();
        context.Response.Body = body;
        await middleware.InvokeAsync(context);
        body.Position = 0;
        using var reader = new StreamReader(body);
        return (context.Response.StatusCode, await reader.ReadToEndAsync());
      }
      var quilt = new Quilt(trunk);
      Check(trunk.Put("q:content", new JObject(), "admin"), "create content root");
      Check(trunk.Put("q:content.shared", JObject.Parse("{ 'text': 'first' }"), "admin"), "create shared motif");
      Check(trunk.Put("q:content.index", JObject.Parse("{ 'q:motif': 'shared' }"), "admin"), "create page");
      (string Fingerprint, JToken Data) Read() => trunk.ReadSnapshot(() =>
      {
        var data = quilt.GetPatch("q:content.index", "admin");
        return (data == null ? null : HtmlCache.Fingerprint(data), (JToken)data);
      });
      Check(HtmlCache.Fingerprint(JObject.Parse("{ 'a': 1, 'b': 2 }")) != HtmlCache.Fingerprint(JObject.Parse("{ 'b': 2, 'a': 1 }")), "fingerprint preserves renderer object order");
      var before = Read();
      Check(before.Data["shared"]["text"].Value<string>() == "first", "snapshot resolves shared motif");
      var renders = 0;
      Task<string> Render(JToken data) { Interlocked.Increment(ref renders); return Task.FromResult(data.ToString()); }
      var cacheDirectory = Path.Combine(directory, "cache", "ssr-v2");
      Directory.CreateDirectory(cacheDirectory);
      var obsolete = Path.Combine(cacheDirectory, "obsolete.html");
      var abandoned = Path.Combine(cacheDirectory, "abandoned.tmp");
      File.WriteAllText(obsolete, "old");
      File.WriteAllText(abandoned, "partial");
      File.SetLastWriteTimeUtc(obsolete, DateTime.UtcNow.AddHours(-2));
      File.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow.AddHours(-2));
      var first = await HtmlCache.GetOrCreate(before.Fingerprint, "index", () => Render(before.Data));
      Check(!File.Exists(obsolete) && !File.Exists(abandoned), "cleanup removes obsolete entries and abandoned writes");
      Check(await HtmlCache.GetOrCreate(before.Fingerprint, "/index", () => Render(before.Data)) == first && renders == 1, "cache hit and canonical leading slash");
      var initialResponse = await Request();
      Check(initialResponse.Status == 200 && initialResponse.Body.Contains("first"), "middleware serves initial SSR page");
      await Request("/");
      var callsBeforeUnrelatedEdit = renderer.Calls;
      trunk.Put("q:content.404", JObject.Parse("{ 'text': 'new 404 page' }"), "admin");
      Check(Read().Fingerprint == before.Fingerprint, "404 edit preserves homepage fingerprint");
      Check((await Request("/")).Body == first && renderer.Calls == callsBeforeUnrelatedEdit, "404 edit preserves homepage cached HTML");
      trunk.Put("q:content.other", JObject.Parse("{ 'q:motif': 'shared' }"), "admin");
      Check((await Request("/other.html")).Body.Contains("first"), "second page resolves shared motif");
      trunk.BeginTransaction();
      trunk.Put("q:content.shared", JObject.Parse("{ 'text': 'rolled back' }"), "admin");
      trunk.RollbackTransaction();
      Check(Read().Fingerprint == before.Fingerprint, "rollback preserves fingerprint");
      Check(!trunk.Put("q:content.shared", new JObject(), "unknown") && Read().Fingerprint == before.Fingerprint, "denied write preserves fingerprint");
      trunk.Put("q:content.shared", JObject.Parse("{ 'text': 'second' }"), "admin");
      var after = Read();
      Check((await Request()).Body.Contains("second"), "middleware refreshes after shared motif change");
      Check((await Request("/other.html")).Body.Contains("second"), "shared edit refreshes all dependent pages");
      Check(after.Fingerprint != before.Fingerprint, "shared edit changes fingerprint");
      Check((await HtmlCache.GetOrCreate(after.Fingerprint, "index", () => Render(after.Data))).Contains("second"), "shared edit refreshes cached page");
      using (var reopened = new ContentTrunk("cache.db", directory))
        Check(reopened.ReadSnapshot(() => HtmlCache.Fingerprint(new Quilt(reopened).GetPatch("q:content.index", "admin"))) == after.Fingerprint, "fingerprint survives reopen");

      using (var legacy = new SqliteConnection("Data Source=" + Path.Combine(directory, "sqlite", "cache.db")))
      {
        legacy.Open();
        using var command = legacy.CreateCommand();
        command.CommandText = @"CREATE TABLE ContentRevision (Slot INTEGER PRIMARY KEY, Value TEXT);
INSERT INTO ContentRevision VALUES(1,'legacy');
CREATE TRIGGER Revision_Insert AFTER INSERT ON Nodes BEGIN UPDATE ContentRevision SET Value='changed'; END;
CREATE TRIGGER Revision_Update AFTER UPDATE ON Nodes BEGIN UPDATE ContentRevision SET Value='changed'; END;
CREATE TRIGGER Revision_Delete AFTER DELETE ON Nodes BEGIN UPDATE ContentRevision SET Value='changed'; END;";
        command.ExecuteNonQuery();
        using (var upgraded = new ContentTrunk("cache.db", directory))
          Check(upgraded.ReadSnapshot(() => HtmlCache.Fingerprint(new Quilt(upgraded).GetPatch("q:content.index", "admin"))) == after.Fingerprint, "legacy revision migration preserves content");
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('ContentRevision','Revision_Insert','Revision_Update','Revision_Delete')";
        Check(Convert.ToInt32(command.ExecuteScalar()) == 0, "legacy revision table and triggers are removed");
      }

      var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var oldRender = HtmlCache.GetOrCreate(before.Fingerprint, "race", async () => { started.SetResult(); await finish.Task; return "old"; });
      await started.Task;
      trunk.Put("q:content.shared", JObject.Parse("{ 'text': 'third' }"), "admin");
      var duringRender = Read();
      finish.SetResult();
      await oldRender;
      Check(await HtmlCache.GetOrCreate(duringRender.Fingerprint, "race", () => Task.FromResult("new")) == "new", "old in-flight render cannot populate new fingerprint");

      var concurrentRenders = 0;
      var requests = Enumerable.Range(0, 12).Select(_ => HtmlCache.GetOrCreate(after.Fingerprint, "concurrent", async () =>
      {
        Interlocked.Increment(ref concurrentRenders);
        await Task.Delay(20);
        return new string('x', 100000);
      })).ToArray();
      var results = await Task.WhenAll(requests);
      Check(concurrentRenders == 1 && results.All(x => x.Length == 100000), "concurrent misses coalesce and return complete HTML");
      Environment.SetEnvironmentVariable("SSR_BUILD_VERSION", "cache-test-b");
      Check(await HtmlCache.GetOrCreate(after.Fingerprint, "index", () => Task.FromResult("new build")) == "new build", "build version invalidates HTML");
      foreach (var file in Directory.GetFiles(Path.Combine(directory, "cache", "ssr-v2"))) File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-1));
      Check(await HtmlCache.GetOrCreate(after.Fingerprint, "index", () => Task.FromResult("expired")) == "expired", "TTL expires existing HTML");
      Environment.SetEnvironmentVariable("SSR_CACHE_TTL_SECONDS", "0");
      Check(await HtmlCache.GetOrCreate(after.Fingerprint, "index", () => Task.FromResult("uncached")) == "uncached", "zero TTL disables reuse");
      Environment.SetEnvironmentVariable("SSR_CACHE_TTL_SECONDS", "300");
      var badDirectory = Path.Combine(directory, "not-a-directory");
      File.WriteAllText(badDirectory, "file");
      Environment.SetEnvironmentVariable("DATA_DIRECTORY", badDirectory);
      Check(await HtmlCache.GetOrCreate(after.Fingerprint, "io-failure", () => Task.FromResult("available")) == "available", "cache I/O failure still serves rendered HTML");
      Environment.SetEnvironmentVariable("DATA_DIRECTORY", directory);
      trunk.Put("q:content.index.shared", JObject.Parse("{ 'text': 'local motif' }"), "admin");
      Check((await Request()).Body.Contains("local motif"), "closer motif shadows previous dependency and refreshes HTML");
      trunk.Delete("q:content.index.shared", "admin");
      Check((await Request()).Body.Contains("third"), "deleting closer motif restores inherited dependency");
      trunk.Put("q:content.index", JObject.Parse("{ 'text': 'edited homepage' }"), "admin");
      Check((await Request()).Body.Contains("edited homepage"), "direct page edit refreshes HTML");
      Check(trunk.Delete("q:content.index", "admin") && Read().Data == null && Read().Fingerprint != after.Fingerprint, "deleted page is missing despite cached HTML");
      Check((await Request()).Status == 404, "middleware returns 404 after cached page deletion");
      Check(quilt.Create(JObject.Parse("{ 'q:content': { 'index': { 'q:motif': 'page' } }, 'page': { 'text': 'imported' } }"), "admin"), "import succeeds");
      Check((await Request()).Body.Contains("imported"), "import refreshes cached page");
      var clearStarted = new TaskCompletionSource();
      var releaseRender = new TaskCompletionSource();
      var pendingRender = HtmlCache.GetOrCreate("clear-test", "clear-race", async () =>
      {
        clearStarted.SetResult();
        await releaseRender.Task;
        return "old HTML";
      });
      await clearStarted.Task;
      var clearCache = HtmlCache.Clear();
      Check(!clearCache.IsCompleted, "manual cache clear waits for in-flight renders");
      releaseRender.SetResult();
      await pendingRender;
      await clearCache;
      Check(!Directory.Exists(Path.Combine(directory, "cache", "ssr-v2")), "manual cache clear removes all HTML entries");
      Check(await HtmlCache.GetOrCreate("clear-test", "clear-race", () => Task.FromResult("fresh HTML")) == "fresh HTML", "pages regenerate after manual clear");
      await BundledRegression.Run(directory);
    }
    finally
    {
      typeof(Startup).GetProperty(nameof(Startup.Configuration)).SetValue(null, oldConfiguration);
      Environment.SetEnvironmentVariable("DATA_DIRECTORY", oldDirectory);
      Environment.SetEnvironmentVariable("SSR_BUILD_VERSION", oldBuild);
      Environment.SetEnvironmentVariable("SSR_CACHE_TTL_SECONDS", oldTtl);
    }
  }
}
