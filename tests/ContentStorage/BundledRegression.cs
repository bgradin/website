using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gradinware;
using Gradinware.Data;
using Gradinware.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Quilting;

internal static class BundledRegression
{
  private static void Check(bool condition, string name)
  {
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name);
  }

  private sealed class Renderer : IReactSsrClient
  {
    public Task<string> Render(JToken data) => Task.FromResult(data.ToString());
  }

  public static async Task Run(string directory)
  {
    await DevelopmentPages(directory);
    var embedded = BundledPages.Read(typeof(BundledRegression).Assembly);
    Check(embedded["q:content.docs/index"]["title"].Value<string>() == "embedded docs", "embedded JSON resources preserve nested routes");
    var pages = new Dictionary<string, JObject>
    {
      ["q:content.index"] = JObject.Parse("{ 'q:motif': 'shared', 'items': [{ 'text': 'original' }], 'title': 'bundled' }"),
      ["q:content.shared"] = JObject.Parse("{ 'text': 'bundled motif' }"),
      ["q:content.docs/index"] = JObject.Parse("{ 'title': 'docs' }"),
      ["q:content.private"] = JObject.Parse("{ 'q:access': { 'get': ['lead'], 'put': ['lead'] }, 'title': 'secret' }")
    };
    using var trunk = new ContentTrunk("bundled.db", directory, pages);
    var quilt = new Quilt(trunk);
    Check(quilt.Create(new JObject(), "admin"), "bundled pages do not prevent initial import/bootstrap");
    using var services = new ServiceCollection().AddSingleton<IContentTrunk>(trunk).BuildServiceProvider();
    var middleware = new JsonContentMiddleware(ctx => { ctx.Response.StatusCode = 404; return Task.CompletedTask; }, new Renderer());
    async Task<(int Status, string Body)> Request(string path)
    {
      var context = new DefaultHttpContext { RequestServices = services };
      context.Request.Path = path;
      using var body = new MemoryStream();
      context.Response.Body = body;
      await middleware.InvokeAsync(context);
      body.Position = 0;
      return (context.Response.StatusCode, await new StreamReader(body).ReadToEndAsync());
    }
    bool Persisted(string key)
    {
      using var raw = new SqliteTrunk("bundled.db", directory);
      return raw.Get("/" + key.Replace("~", "~0").Replace("/", "~1"), raw.TreeId) != null;
    }
    Check((await Request("/")).Body.Contains("bundled motif"), "bundled page renders with bundled motif");
    Check((await Request("/docs/index.html")).Body.Contains("docs") && (await Request("/docs")).Status == 200, "nested bundled routes render");
    Check((await Request("/missing.html")).Status == 404, "unknown bundled route remains 404");
    Check(!Persisted("q:content.index"), "reading bundled page does not populate database");
    Check(trunk.GetKeys("admin").Contains("q:content.index"), "bundled pages appear in content map");
    trunk.Put("q:quilters.author", JObject.Parse("{ 'q:circleIds': ['authors'] }"), "admin");
    Check(trunk.Get("q:content.private", "author") == null, "bundled read permissions apply");
    Check(!trunk.Put("q:content.private", new JObject(), "author") && !Persisted("q:content.private"), "denied edit cannot materialize bundled page");
    Check(!trunk.Put("q:content.index", new JObject(), "unknown") && !Persisted("q:content.index"), "unknown identity cannot edit bundled page");
    var edit = quilt.GetPatch("q:content.index", "admin");
    edit["items"][0]["text"] = "edited item";
    Check(quilt.CreatePatch(edit, "q:content.index", "admin"), "editing bundled page through Quilt materializes database copy");
    Check(Persisted("q:content.index") && trunk.Get("q:content.index", "admin")["title"].Value<string>() == "bundled", "copy preserves unedited page fields");
    Check((await Request("/")).Body.Contains("edited item"), "bundled edit refreshes rendered HTML");
    trunk.Delete("q:content.index", "admin");
    Check(!Persisted("q:content.index") && (await Request("/")).Body.Contains("original"), "delete restores bundled page despite cached override");
    trunk.BeginTransaction();
    trunk.Put("q:content.index", JObject.Parse("{ 'title': 'rollback' }"), "admin");
    trunk.RollbackTransaction();
    Check(!Persisted("q:content.index"), "rollback removes materialized copy");
    Check(trunk.Put("q:content.index", JObject.Parse("{ 'title': 'database override' }"), "admin"), "whole page edit overrides bundle");
    Check((await Request("/")).Body.Contains("database override"), "database takes precedence over bundled page");
    using (var reopened = new ContentTrunk("bundled.db", directory, pages))
      Check(reopened.Get("q:content.index", "admin")["title"].Value<string>() == "database override", "override survives reopening database");
    trunk.Clear("admin");
    trunk.Put("q:quilters.admin", JObject.Parse("{ 'q:circleIds': ['lead'] }"), "admin");
    Check((await Request("/")).Body.Contains("original"), "clearing database preserves bundled pages");
    Check(pages["q:content.index"]["items"][0]["text"].Value<string>() == "original", "bundle objects remain immutable across reads and edits");
  }

  private static async Task DevelopmentPages(string directory)
  {
    var pagesDirectory = Path.Combine(directory, "development-pages");
    Directory.CreateDirectory(Path.Combine(pagesDirectory, "docs"));
    var page = Path.Combine(pagesDirectory, "docs", "index.json");
    var motif = Path.Combine(pagesDirectory, "shared.json");
    File.WriteAllText(page, "{ 'q:motif': 'shared', 'title': 'initial development page' }");
    File.WriteAllText(motif, "{ 'text': 'initial development motif' }");
    using var services = new ServiceCollection()
      .AddScoped<IContentTrunk>(_ => new ContentTrunk("development-pages.db", directory, BundledPages.ReadDirectory(pagesDirectory)))
      .BuildServiceProvider();
    using (var scope = services.CreateScope())
      new Quilt(scope.ServiceProvider.GetRequiredService<IContentTrunk>()).Create(new JObject(), "admin");
    var middleware = new JsonContentMiddleware(ctx => { ctx.Response.StatusCode = 404; return Task.CompletedTask; }, new Renderer());
    async Task<(int Status, string Body)> Request()
    {
      using var scope = services.CreateScope();
      var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
      context.Request.Path = "/docs/";
      using var body = new MemoryStream();
      context.Response.Body = body;
      await middleware.InvokeAsync(context);
      body.Position = 0;
      using var reader = new StreamReader(body);
      return (context.Response.StatusCode, await reader.ReadToEndAsync());
    }
    Check((await Request()).Body.Contains("initial development page"), "development files render nested routes");
    File.WriteAllText(page, "{ 'q:motif': 'shared', 'title': 'updated development page' }");
    File.WriteAllText(motif, "{ 'text': 'updated development motif' }");
    var updated = await Request();
    Check(updated.Body.Contains("updated development page") && updated.Body.Contains("updated development motif"), "development file edits invalidate cached HTML on next request");
    using (var scope = services.CreateScope())
      scope.ServiceProvider.GetRequiredService<IContentTrunk>().Put("q:content.docs/index", JObject.Parse("{ 'title': 'saved override' }"), "admin");
    Check((await Request()).Body.Contains("saved override"), "database overrides development files");
    using (var scope = services.CreateScope())
      scope.ServiceProvider.GetRequiredService<IContentTrunk>().Delete("q:content.docs/index", "admin");
    Check((await Request()).Body.Contains("updated development page"), "removing override restores current development file");
    File.Delete(page);
    Check((await Request()).Status == 404, "deleted development files stop serving cached HTML");
    File.WriteAllText(page, "{ 'title': 'new development page' }");
    Check((await Request()).Body.Contains("new development page"), "new development files render without rebuilding");
  }
}
