using System;
using System.IO;
using Gradinware.Data;
using Gradinware.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
      services.AddScoped<IContentTrunk, ContentTrunk>();
      services.AddAuthorization();
      services.AddControllers();
      services.AddHttpClient<IReactSsrClient, ReactSsrClient>();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
      var builder = new ConfigurationBuilder()
        .SetBasePath(env.ContentRootPath)
        .AddJsonFile("appsettings.json", false, true)
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")}.json")
        .AddEnvironmentVariables();
      Configuration = builder.Build();

      if (env.IsDevelopment())
      {
        app.UseDeveloperExceptionPage();
      }

      app.UseRouting();
      app.UseAuthorization();

      app.UseEndpoints(endpoints =>
      {
        endpoints.MapControllers();
      });

      app.UseFileServer(new FileServerOptions
      {
        FileProvider = new PhysicalFileProvider(Path.Combine(env.ContentRootPath, "/app/ui/public")),
      });

      app.UseJsonContent();
    }
  }
}
