using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Gradinware.Data;
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
                foreach (var user in db.Users.ToList().Where(user => !user.Password.IsPasswordHash()))
                {
                    user.Password = user.Password.EncryptPassword();
                }
                db.SaveChanges();
                // Initialize the node-backed content store before accepting requests.
                scope.ServiceProvider.GetRequiredService<IContentTrunk>().EnsureCreated();
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
