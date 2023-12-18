using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Jose;
using Newtonsoft.Json;

namespace Gradinware
{
  public static class JwtExtensions
  {
    public static Dictionary<string, object> DeserializeAccessToken(this string token)
    {
      try
      {
        string decoded = JWT.Decode(token, Encoding.ASCII.GetBytes(Startup.Configuration["OAuth:JwtKey"]));
        return JsonConvert.DeserializeObject<Dictionary<string, object>>(decoded);
      }
      catch
      {
        return null;
      }
    }

    public static bool IsValidAccessToken(this string token)
    {
      var values = token.DeserializeAccessToken();
      return values != null && values.IsValidAccessToken();
    }

    public static bool IsValidAccessTokenWithScope(this string token, string scope)
    {
      var values = token.DeserializeAccessToken();
      return values != null
        && values.IsValidAccessToken()
        && values["scope"] != null
        && values["scope"].MatchesScope(scope);
    }

    private static bool IsValidAccessToken(this Dictionary<string, object> values)
    {
      return values != null
        && values.ContainsKey("userId")
        && values.ContainsKey("issuedAt")
        && values.ContainsKey("expiresIn")
        && values["userId"] is long
        && values["issuedAt"] is double
        && values["expiresIn"] is long
        && new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
          .AddMilliseconds((double) values["issuedAt"])
          .AddSeconds((long) values["expiresIn"]) > DateTime.UtcNow;
    }

    private static bool MatchesScope(this object value, string scope)
    {
      if (value is string)
      {
        return value.ToString() == scope || value.ToString().Split(',').Select(x => x.Trim()).MatchesScope(scope);
      }

      if (value is string[])
      {
        return (value as string[]).Any(x => x.MatchesScope(scope));
      }

      return false;
    }
  }
}
