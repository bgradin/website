using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Gradinware;
using Gradinware.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name);
}

var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
Directory.CreateDirectory(directory);
try
{
    foreach (var emptyFile in new[] { false, true })
    {
        var path = Path.Combine(directory, emptyFile ? "empty.db" : "missing.db");
        if (emptyFile) File.WriteAllBytes(path, Array.Empty<byte>());
        using (var trunk = new TestTrunk(path))
        {
            Check(!trunk.GetKeys().Any(), "new schema has no keys: " + emptyFile);
            Check(trunk.Retrieve("missing") == null, "missing value returns null: " + emptyFile);
            trunk.BeginTransaction();
            Check(trunk.Stow("content.test", JObject.Parse("{\"message\":\"saved\"}")), "write to initialized schema");
            trunk.CommitTransaction();
        }
        using (var trunk = new TestTrunk(path))
        {
            Check(trunk.Retrieve("content.test")["message"].Value<string>() == "saved", "reopening preserves data");
            trunk.Clear();
            Check(!trunk.GetKeys().Any(), "clear initialized schema");
        }
    }

    using (var trunk = new TestTrunk(Path.Combine(directory, "pages.db")))
    using (var services = new ServiceCollection().AddSingleton<IContentTrunk>(trunk).BuildServiceProvider())
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = "/missing-page-" + Guid.NewGuid().ToString("N");
        var middleware = new JsonContentMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 404;
            return Task.CompletedTask;
        }, null);
        await middleware.InvokeAsync(context);
        Check(context.Response.StatusCode == 404, "empty content database yields page 404 without SSR");
    }

    foreach (var path in new[] { "/api", "/api/custom-frame/v1/releases", "/api/custom-frame/v1/config", "/api/custom-frame/unknown", "/API/unknown" })
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.RequestServices = new RejectServices();
        var called = false;
        var middleware = new JsonContentMiddleware(ctx =>
        {
            called = true;
            ctx.Response.StatusCode = 404;
            return Task.CompletedTask;
        }, null);
        await middleware.InvokeAsync(context);
        Check(called && context.Response.StatusCode == 404, "API bypasses Quilt including 404 fallback: " + path);
    }
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    Directory.Delete(directory, true);
}

sealed class TestTrunk : SqliteBsonTrunk, IContentTrunk
{
    public TestTrunk(string path) : base(path) { }
    public void EnsureCreated() => Database.EnsureCreated();
    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = GetDatabaseName() }.ToString());
}

sealed class RejectServices : IServiceProvider
{
    public object GetService(Type serviceType) => throw new Exception("API attempted to resolve " + serviceType.Name);
}
