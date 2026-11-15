using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using Gradinware.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Gradinware.Services
{
  public static class SiteSession
  {
    public static ClaimsPrincipal Principal(int userId)
    {
      var claims = new[]
      {
        new Claim(ClaimTypes.NameIdentifier, userId.ToString(CultureInfo.InvariantCulture))
      };
      return new ClaimsPrincipal(new ClaimsIdentity(claims, "site"));
    }

    public static int? UserId(HttpContext context)
    {
      var authorization = context.Request.Headers.Authorization.ToString();
      if (authorization.StartsWith("Bearer ", StringComparison.Ordinal))
      {
        var token = authorization.Substring(7);
        if (!token.IsValidAccessTokenWithScope("user")) return null;
        return Convert.ToInt32(token.DeserializeAccessToken()["userId"]);
      }

      if (context.User.Identity?.IsAuthenticated != true) return null;
      var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
      return int.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    public static bool IsAdmin(HttpContext context)
    {
      var userId = UserId(context);
      if (!userId.HasValue) return false;

      var adminUserId = Startup.Configuration["Content:AdminUserId"];
      if (userId.Value.ToString(CultureInfo.InvariantCulture) != adminUserId)
      {
        return false;
      }

      var db = context.RequestServices.GetRequiredService<AccountContext>();
      return db.Users.Any(user => user.Id == userId.Value);
    }
  }
}
