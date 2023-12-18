using System;
using System.Collections.Generic;
using System.Text;
using Jose;

namespace Gradinware.Models.Authentication
{
  public class OAuthPasswordGrant
  {
    public OAuthPasswordGrant(int userId)
      : this("user", userId)
    {
    }

    public OAuthPasswordGrant(int userId, string refreshToken)
      : this("user", userId, refreshToken)
    {
    }

    public OAuthPasswordGrant(string scope, int userId)
      : this(scope, userId, TokenGenerator.Generate(int.Parse(Startup.Configuration["OAuth:RefreshTokenLength"])))
    {
    }

    public OAuthPasswordGrant(string scope, int userId, int expiresIn)
      : this(
        GenerateAccessToken(scope, userId, DateTime.UtcNow, expiresIn),
        TokenGenerator.Generate(int.Parse(Startup.Configuration["OAuth:RefreshTokenLength"])),
        expiresIn,
        scope
      )
    {
    }

    public OAuthPasswordGrant(string scope, int userId, string refreshToken)
      : this(
        GenerateAccessToken(scope, userId, DateTime.UtcNow, int.Parse(Startup.Configuration["OAuth:UserAccessTokenValidDuration"])),
        refreshToken,
        int.Parse(Startup.Configuration["OAuth:UserAccessTokenValidDuration"]),
        scope
      )
    {
    }

    public string AccessToken { get; private set; }

    public string RefreshToken { get; private set; }

    public int ExpiresIn { get; private set; }

    public string Scope { get; private set; }

    private OAuthPasswordGrant(string accessToken, string refreshToken, int expiresIn, string scope)
    {
      AccessToken = accessToken;
      RefreshToken = refreshToken;
      ExpiresIn = expiresIn;
      Scope = scope;
    }

    private static string GenerateAccessToken(string scope, int userId, DateTime issuedAt, int expiresIn)
    {
      return JWT.Encode(
        new Dictionary<string, object>()
        {
          { "userId", userId },
          { "issuedAt", issuedAt.ToJsDate() },
          { "expiresIn", expiresIn },
          { "scope", scope },
        },
        Encoding.ASCII.GetBytes(Startup.Configuration["OAuth:JwtKey"]),
        JwsAlgorithm.HS256
      );
    }
  }
}
