using System;
using System.IO;
using Gradinware.Data;
using Gradinware.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Text;

namespace Gradinware
{
  public class Startup
  {
    public static IConfiguration Configuration
    {
      get; private set;
    }

    public void ConfigureServices(IServiceCollection services)
    {
      services.AddLogging();
      services.AddSingleton<Gradinware.CustomFrame.FrameCatalog>();
      services.AddSingleton(TimeProvider.System);
      services.AddSingleton<Gradinware.CustomFrame.DropboxTokenProvider>();
      services.AddHttpClient("CustomFrameDropbox")
        .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false })
        .RemoveAllLoggers();
      services.AddScoped<IContentTrunk, ContentTrunk>();
      services.AddAuthorization();
      services.AddControllers();
      services.AddHttpClient<IReactSsrClient, ReactSsrClient>();
      services.AddDbContext<AccountContext>();
      services.AddSingleton<IEmailSender, SmtpEmailSender>();
      services.AddRateLimiter(options => options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
          context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
          _ => new FixedWindowRateLimiterOptions
          {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
          })));
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
      var builder = new ConfigurationBuilder()
        .SetBasePath(env.ContentRootPath)
        .AddJsonFile("appsettings.json", false, true)
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json", true, true)
        .AddEnvironmentVariables();
      Configuration = builder.Build();

      var jwtKey = Configuration["OAuth:JwtKey"];
      if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
      {
        throw new InvalidOperationException("OAuth:JwtKey must be provided through deployment configuration and contain at least 32 bytes.");
      }

      if (env.IsDevelopment())
      {
        app.UseDeveloperExceptionPage();
      }

      app.UseRouting();
      app.UseRateLimiter();
      app.UseAuthorization();

      app.UseEndpoints(endpoints =>
      {
        endpoints.MapControllers();
      });

      var contentTypeProvider = new FileExtensionContentTypeProvider();
      contentTypeProvider.Mappings[".apk"] = "application/vnd.android.package-archive";

      app.UseFileServer(new FileServerOptions
      {
        FileProvider = new PhysicalFileProvider(Path.Combine(env.ContentRootPath, "/app/ui/public")),
        StaticFileOptions =
        {
          ContentTypeProvider = contentTypeProvider,
          ServeUnknownFileTypes = true,
          DefaultContentType = "application/octet-stream",
        },
      });

      app.UseJsonContent();
    }
  }
}
