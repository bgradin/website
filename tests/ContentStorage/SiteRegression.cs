using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Gradinware;
using Gradinware.Controllers;
using Gradinware.Data;
using Gradinware.Dto;
using Gradinware.Models.Authentication;
using Gradinware.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

internal static class SiteRegression
{
  private static void Check(bool condition, string message)
  {
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS " + message);
  }

  private static void CheckPasswordRequirements()
  {
    foreach (var dto in new object[] { new CreateUserDto(), new ResetPasswordDto(), new UpdateUserDto() })
    {
      var property = dto.GetType().GetProperty("Password");
      foreach (var password in new[] { "12345678", "elevenchars", "twelve chars", "several ordinary words", new string('a', 64) })
      {
        property.SetValue(dto, password);
        var context = new ValidationContext(dto) { MemberName = "Password" };
        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateProperty(password, context, results);
        Check(valid == (password.Length >= 12),
          dto.GetType().Name + " enforces password length for " + password.Length + " characters");
      }
    }

    var update = new UpdateUserDto();
    Check(Validator.TryValidateProperty(null, new ValidationContext(update) { MemberName = "Password" }, null),
      "profile updates can omit the password");
    var login = new LogInDto { Password = "oldpass" };
    Check(Validator.TryValidateProperty(login.Password, new ValidationContext(login) { MemberName = "Password" }, null),
      "existing shorter passwords remain valid for login");
  }

  private sealed class Renderer : IReactSsrClient
  {
    public Task<string> Render(JToken data, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(data.ToString());
  }

  private sealed class CaptchaFactory : IHttpClientFactory
  {
    public static string Response = "{\"success\":false}";

    public HttpClient CreateClient(string name) => new(new Handler());

    private sealed class Handler : HttpMessageHandler
    {
      protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
      {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
          Content = new StringContent(Response)
        };
        return Task.FromResult(response);
      }
    }
  }

  private static async Task CheckLocalLogin(AccountContext db, User admin, string directory)
  {
    var services = new ServiceCollection().AddLogging();
    services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(directory, "session-keys")));
    services.AddAuthentication("site").AddCookie("site");
    using var provider = services.BuildServiceProvider();
    using var loginScope = provider.CreateScope();
    var loginContext = new DefaultHttpContext { RequestServices = loginScope.ServiceProvider };
    loginContext.Request.Scheme = "https";
    var controller = new UsersController(db, new CaptchaFactory())
    {
      ControllerContext = new ControllerContext { HttpContext = loginContext }
    };

    Check(await controller.LogIn(new LogInDto { Email = admin.Email, Password = "wrong-password" }) is BadRequestResult,
      "local account login rejects incorrect passwords");
    Check(!loginContext.Response.Headers.ContainsKey("Set-Cookie"),
      "failed local login does not establish a browser session");

    var result = await controller.LogIn(new LogInDto { Email = admin.Email, Password = "local-test-password" });
    var grant = (result as JsonResult)?.Value as OAuthPasswordGrantDto;
    Check(grant != null && grant.AccessToken.IsValidAccessTokenWithScope("user"),
      "local account login returns the existing API token grant");
    var cookie = loginContext.Response.Headers.SetCookie.ToString().Split(';')[0];
    Check(!string.IsNullOrEmpty(cookie), "local account login issues a site session cookie");

    using var sessionScope = provider.CreateScope();
    var sessionContext = new DefaultHttpContext { RequestServices = sessionScope.ServiceProvider };
    sessionContext.Request.Scheme = "https";
    sessionContext.Request.Headers.Cookie = cookie;
    var authentication = await sessionContext.AuthenticateAsync("site");
    Check(authentication.Succeeded, "site cookie authenticates a subsequent request");
    sessionContext.User = authentication.Principal;
    Check(SiteSession.UserId(sessionContext) == admin.Id,
      "browser session uses the account authenticated by UsersController");

    sessionContext.Request.Headers.Authorization = "Bearer " + grant.AccessToken;
    Check(SiteSession.UserId(sessionContext) == admin.Id,
      "existing API bearer tokens identify the same local account");
  }

  private static void CheckRedirectDomains(IConfiguration configuration, AccountContext db, int adminId)
  {
    configuration["AccountRedirectDomains:0"] = "dnd.gradinware.com";
    var environment = new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Production" };
    var store = new RedirectDomainStore(configuration, environment);
    Check(store.GetDomains().SequenceEqual(new[] { "dnd.gradinware.com" }), "production domain list is seeded");
    CheckCors(store);
    using var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
    var context = new DefaultHttpContext { RequestServices = services };
    var controller = new AdminController(db) { ControllerContext = new ControllerContext { HttpContext = context } };
    var request = new AdminController.RedirectDomainRequest { Domain = " APP.Example.Test " };
    Check((controller.RedirectDomains(store) as StatusCodeResult)?.StatusCode == 403 &&
      (controller.AddRedirectDomain(request, store) as StatusCodeResult)?.StatusCode == 403 &&
      (controller.RemoveRedirectDomain(request, store) as StatusCodeResult)?.StatusCode == 403,
      "anonymous cannot read or modify redirect domains");
    context.User = SiteSession.Principal(adminId + 100);
    context.Request.Headers["X-Site-Action"] = "1";
    Check((controller.RedirectDomains(store) as StatusCodeResult)?.StatusCode == 403 &&
      (controller.AddRedirectDomain(request, store) as StatusCodeResult)?.StatusCode == 403 &&
      (controller.RemoveRedirectDomain(request, store) as StatusCodeResult)?.StatusCode == 403,
      "non-admin cannot read or modify redirect domains even with the action header");
    context.Request.Headers.Remove("X-Site-Action");
    context.User = SiteSession.Principal(adminId);
    Check((controller.AddRedirectDomain(request, store) as StatusCodeResult)?.StatusCode == 403,
      "redirect domain changes require the action header");
    context.Request.Headers["X-Site-Action"] = "1";
    Check(controller.AddRedirectDomain(request, store) is OkObjectResult && store.GetDomains().Contains("app.example.test"),
      "administrator can add a normalized domain");
    controller.AddRedirectDomain(request, store);
    Check(store.GetDomains().Length == 2, "duplicate domains are idempotent");
    foreach (var invalid in new[] { "https://example.test", "example.test/path", "localhost:3000", "*.example.test", "", "example.test." })
      Check(controller.AddRedirectDomain(new() { Domain = invalid }, store) is BadRequestObjectResult,
        "invalid redirect domain rejected: " + invalid);
    var reopened = new RedirectDomainStore(configuration, environment);
    Check(reopened.GetDomains().Contains("app.example.test"), "domain changes persist in site.db");
    Check(controller.RemoveRedirectDomain(request, reopened) is OkObjectResult && !store.GetDomains().Contains("app.example.test"),
      "administrator can remove a domain");
    controller.RemoveRedirectDomain(new() { Domain = "dnd.gradinware.com" }, store);
    Check(new RedirectDomainStore(configuration, environment).GetDomains().Length == 0, "empty list persists without reseeding");
    configuration["AccountRedirectDomains:0"] = "localhost";
    var development = new RedirectDomainStore(configuration,
      new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { EnvironmentName = "Development" });
    Check(development.GetDomains().SequenceEqual(new[] { "localhost" }), "development allow list is separate from production");
    Check(File.Exists(Path.Combine(Environment.GetEnvironmentVariable("DATA_DIRECTORY"), "sqlite", "site.db")), "domain data uses site.db");
  }

  private static void CheckCors(RedirectDomainStore store)
  {
    var services = new ServiceCollection();
    new Startup().ConfigureServices(services);
    services.AddSingleton(store);
    using var provider = services.BuildServiceProvider();
    var app = new ApplicationBuilder(provider);
    app.UseCors();
    app.Run(context => { context.Response.StatusCode = 401; return Task.CompletedTask; });
    var pipeline = app.Build();

    DefaultHttpContext Request(string origin)
    {
      var context = new DefaultHttpContext { RequestServices = provider };
      context.Request.Headers.Origin = origin;
      context.Request.Method = "OPTIONS";
      context.Request.Headers["Access-Control-Request-Method"] = "PUT";
      context.Request.Headers["Access-Control-Request-Headers"] = "authorization,content-type,x-site-action";
      pipeline(context).GetAwaiter().GetResult();
      return context;
    }

    var allowed = Request("https://dnd.gradinware.com");
    Check(allowed.Response.StatusCode == 204 &&
      allowed.Response.Headers.AccessControlAllowOrigin == "https://dnd.gradinware.com" &&
      allowed.Response.Headers.AccessControlAllowCredentials == "true" &&
      allowed.Response.Headers.AccessControlAllowMethods.ToString().Contains("PUT") &&
      allowed.Response.Headers.AccessControlAllowHeaders.ToString().Contains("x-site-action"),
      "CORS preflight allows configured domains, credentials, methods, and API headers");
    foreach (var origin in new[] { "https://untrusted.test", "https://dnd.gradinware.com.evil.test",
      "https://sub.dnd.gradinware.com", "http://dnd.gradinware.com", "null", "https://dnd.gradinware.com/path" })
      Check(!Request(origin).Response.Headers.ContainsKey("Access-Control-Allow-Origin"), "CORS rejects " + origin);
    store.Change("www.gradinware.com", true);
    Check(Request("https://www.gradinware.com").Response.Headers.AccessControlAllowOrigin == "https://www.gradinware.com",
      "CORS accepts newly added redirect domains without restart");
    store.Change("www.gradinware.com", false);
    Check(!Request("https://www.gradinware.com").Response.Headers.ContainsKey("Access-Control-Allow-Origin"),
      "CORS rejects removed redirect domains without restart");
    store.Change("localhost", true);
    Check(Request("http://localhost:3000").Response.Headers.AccessControlAllowOrigin == "http://localhost:3000",
      "CORS permits HTTP localhost with any port when configured");
    store.Change("localhost", false);
  }

  public static async Task Run(string directory)
  {
    CheckPasswordRequirements();
    var oldConfiguration = Startup.Configuration;
    var oldDirectory = Environment.GetEnvironmentVariable("DATA_DIRECTORY");
    try
    {
      Environment.SetEnvironmentVariable("DATA_DIRECTORY", directory);
      using var db = new AccountContext();
      await db.Database.MigrateAsync();
      var admin = new User
      {
        Email = "admin@example.test",
        FirstName = "Site",
        LastName = "Admin",
        Password = "local-test-password".EncryptPassword(),
        DateCreated = DateTime.UtcNow
      };
      var otherUser = new User
      {
        Email = "user@example.test",
        FirstName = "Test",
        LastName = "User",
        Password = "another private hash",
        DateCreated = DateTime.UtcNow
      };
      db.Users.AddRange(admin, otherUser);
      await db.SaveChangesAsync();

      var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string>
        {
          ["Content:AdminUserId"] = admin.Id.ToString(),
          ["OAuth:JwtKey"] = "local-login-regression-signing-key-32-bytes",
          ["OAuth:RefreshTokenLength"] = "75",
          ["OAuth:UserAccessTokenValidDuration"] = "300"
        })
        .Build();
      typeof(Startup).GetProperty(nameof(Startup.Configuration)).SetValue(null, configuration);

      CheckRedirectDomains(configuration, db, admin.Id);
      await CheckLocalLogin(db, admin, directory);

      var registration = new UsersController(db, new CaptchaFactory())
      {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
      };
      var registrationData = new CreateUserDto
      {
        FirstName = "Bot",
        LastName = "Account",
        Email = "bot@example.test",
        Password = "password123",
        CaptchaToken = "fake"
      };
      Check((await registration.Post(registrationData) as ObjectResult)?.StatusCode == 503,
        "registration fails closed without CAPTCHA configuration");

      Startup.Configuration["Recaptcha:SecretKey"] = "test-secret";
      Startup.Configuration["Recaptcha:Hostname"] = "example.test";
      Check(await registration.Post(registrationData) is BadRequestObjectResult &&
        !db.Users.Any(user => user.Email == registrationData.Email),
        "failed CAPTCHA prevents account creation");

      CaptchaFactory.Response = "{\"success\":true,\"hostname\":\"attacker.test\"}";
      Check(await registration.Post(registrationData) is BadRequestObjectResult &&
        !db.Users.Any(user => user.Email == registrationData.Email),
        "CAPTCHA hostname mismatch prevents account creation");

      using var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
      var context = new DefaultHttpContext { RequestServices = services };
      var adminController = new AdminController(db)
      {
        ControllerContext = new ControllerContext { HttpContext = context }
      };
      Check((await adminController.Users() as StatusCodeResult)?.StatusCode == 403,
        "anonymous cannot list users");

      context.User = SiteSession.Principal(otherUser.Id);
      Check((await adminController.Delete(admin.Id) as StatusCodeResult)?.StatusCode == 403,
        "non-admin cannot delete users");
      Check((await adminController.ClearCache() as StatusCodeResult)?.StatusCode == 403,
        "non-admin cannot clear cache");

      var profile = new UsersController(db, new CaptchaFactory())
      {
        ControllerContext = new ControllerContext { HttpContext = context }
      };
      var update = new UpdateUserDto { FirstName = "Updated", LastName = "User", Email = otherUser.Email };
      var originalFirstName = otherUser.FirstName;
      Check((profile.UpdateUser(update) as ObjectResult)?.StatusCode == 403 &&
        otherUser.FirstName == originalFirstName,
        "profile cookie updates require the action header");
      context.Request.Headers["X-Site-Action"] = "1";
      update.Email = admin.Email;
      Check(profile.UpdateUser(update) is ConflictObjectResult, "profile rejects another user's email");
      update.Email = "updated@example.test";
      Check(profile.UpdateUser(update) is OkResult && otherUser.FirstName == "Updated" && admin.FirstName == "Site",
        "profile cookie update modifies only the signed-in user");
      context.Request.Headers.Remove("X-Site-Action");

      context.User = SiteSession.Principal(admin.Id);
      var listing = (OkObjectResult)await adminController.Users();
      var json = JsonSerializer.Serialize(listing.Value);
      Check(json.Contains("admin@example.test") && !json.Contains("Password") &&
        !json.Contains("private hash") && !json.Contains("Tokens"),
        "admin user projection excludes secrets");
      Check((await adminController.Delete(otherUser.Id) as StatusCodeResult)?.StatusCode == 403,
        "cookie mutations require CSRF header");

      context.Request.Headers["X-Site-Action"] = "1";
      Check(await adminController.Delete(admin.Id) is ConflictObjectResult,
        "administrator cannot delete own account");
      Check(await adminController.Delete(otherUser.Id) is NoContentResult &&
        !await db.Users.AnyAsync(user => user.Id == otherUser.Id),
        "administrator can delete another user");

      context.User = SiteSession.Principal(otherUser.Id);
      var siteController = new SiteController(db)
      {
        ControllerContext = new ControllerContext { HttpContext = context }
      };
      Check(siteController.Session() is UnauthorizedResult, "deleted user session is rejected");

      var bundles = BundledPages.Load();
      var routes = new[] { "login", "register", "admin", "account" };
      Check(routes.All(route => bundles.ContainsKey("q:content." + route + "/index")),
        "account pages embedded with directory routes");
      var adminReadCircles = bundles["q:content.admin/index"]["q:access"]["get"].Values<string>();
      Check(adminReadCircles.SequenceEqual(new[] { "lead" }),
        "bundled admin patch restricts Quilt reads to lead");

      context.User = new ClaimsPrincipal();
      using var trunk = new ContentTrunk("admin-route.db", directory);
      using var routeServices = new ServiceCollection()
        .AddSingleton<IContentTrunk>(trunk)
        .BuildServiceProvider();
      context.RequestServices = routeServices;
      context.Request.Path = "/admin/";
      context.Response.Body = new MemoryStream();
      var anonymousMiddleware = new JsonContentMiddleware(
        _ => throw new Exception("Admin route escaped guard"), null);
      await anonymousMiddleware.InvokeAsync(context);
      Check(context.Response.StatusCode == 403, "anonymous admin page is blocked before rendering");

      Startup.Configuration["AdminUsername"] = "admin";
      var leadIdentity = JObject.Parse("{ 'q:circleIds': ['lead'] }");
      Check(trunk.Put("q:quilters.admin", leadIdentity, "admin"),
        "empty content store bootstraps lead with bundled pages");

      using var adminServices = new ServiceCollection()
        .AddSingleton<IContentTrunk>(trunk)
        .AddSingleton(db)
        .BuildServiceProvider();
      context = new DefaultHttpContext
      {
        RequestServices = adminServices,
        User = SiteSession.Principal(admin.Id)
      };
      context.Request.Path = "/admin/";
      context.Response.Body = new MemoryStream();
      var adminMiddleware = new JsonContentMiddleware(
        _ => throw new Exception("Admin page did not render"), new Renderer());
      await adminMiddleware.InvokeAsync(context);
      Check(context.Response.StatusCode == 200 && context.Response.Body.Length > 0 &&
        context.Response.Headers.CacheControl == "no-store",
        "authenticated administrator can render bundled page without caching in browser");
    }
    finally
    {
      typeof(Startup).GetProperty(nameof(Startup.Configuration)).SetValue(null, oldConfiguration);
      Environment.SetEnvironmentVariable("DATA_DIRECTORY", oldDirectory);
    }
  }
}
