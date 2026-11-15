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
