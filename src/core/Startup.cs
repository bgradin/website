using System.IO;
using System.Text;
using System.Threading.RateLimiting;
using System;
using Gradinware.Data;
using Gradinware.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

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
      services.AddSingleton<RedirectDomainStore>();
      services.AddCors();
      services.AddOptions<CorsOptions>().Configure<RedirectDomainStore>((options, domains) =>
        options.AddDefaultPolicy(policy => policy
          .SetIsOriginAllowed(domains.IsAllowedOrigin)
          .AllowAnyMethod()
          .AllowAnyHeader()
          .AllowCredentials()));
      services.AddHttpClient("captcha", client => client.Timeout = TimeSpan.FromSeconds(10));
      services.AddAuthentication("site").AddCookie("site", options =>
      {
        options.Cookie.Name = "__Host-gradinware";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
        options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
      });
      services.AddSingleton<Gradinware.CustomFrame.FrameCatalog>();
      services.AddSingleton(TimeProvider.System);
      services.AddSingleton<Gradinware.CustomFrame.DropboxTokenProvider>();
      services.AddHttpClient("CustomFrameDropbox")
        .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false })
        .RemoveAllLoggers();
      services.AddScoped<IContentTrunk>(provider =>
      {
        var environment = provider.GetRequiredService<IWebHostEnvironment>();
        var pages = environment.IsDevelopment()
          ? BundledPages.ReadDirectory(Path.Combine(environment.ContentRootPath, "pages"))
          : BundledPages.Load();
        return new ContentTrunk("content.db", bundledPages: pages);
      });
      services.AddAuthorization();
      services.AddControllers();
      services.AddHttpClient<IReactSsrClient, ReactSsrClient>();
      services.AddDbContext<AccountContext>();
      services.AddDbContext<DndContext>();
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
      app.UseCors();
      app.UseRateLimiter();
      app.UseAuthentication();
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
