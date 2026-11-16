using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Gradinware.Data;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Linq;

namespace Gradinware
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();
            var signingKey = host.Services.GetRequiredService<IConfiguration>()["OAuth:JwtKey"];
            if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
            {
                throw new System.InvalidOperationException("OAuth:JwtKey must be provided through deployment configuration and contain at least 32 bytes.");
            }
            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AccountContext>();
                db.Database.Migrate();
                scope.ServiceProvider.GetRequiredService<DndContext>().EnsureSchema();
                foreach (var user in db.Users.ToList().Where(user => !user.Password.IsPasswordHash()))
                {
                    user.Password = user.Password.EncryptPassword();
                }
                db.SaveChanges();
                // Initialize the node-backed content store before accepting requests.
                var trunk = scope.ServiceProvider.GetRequiredService<IContentTrunk>();
                trunk.EnsureCreated();
                var adminName = host.Services.GetRequiredService<IConfiguration>()["AdminUsername"];
                var adminKey = "q:quilters." + adminName;
                if (!string.IsNullOrWhiteSpace(adminName) && trunk.Get(adminKey, adminName) == null)
                {
                    // The trunk permits this only during the empty-store bootstrap.
                    // Bundled pages do not materialize content or prevent that bootstrap.
                    var leadIdentity = new JObject { ["q:circleIds"] = new JArray("lead") };
                    trunk.Put(adminKey, leadIdentity, adminName);
                }
            }
            host.Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args).ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
            });
    }
}
