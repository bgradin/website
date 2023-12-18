using System.Text.Json.Serialization;
using Gradinware.Models.Authentication;

namespace Gradinware.Dto
{
  public class OAuthPasswordGrantDto
  {
    public OAuthPasswordGrantDto(OAuthPasswordGrant grant)
    {
      AccessToken = grant.AccessToken;
      RefreshToken = grant.RefreshToken;
      ExpiresIn = grant.ExpiresIn;
      Scope = grant.Scope;
    }

    [JsonPropertyName("access_token")]
    public string AccessToken { get; private set; }

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; private set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; private set; }

    [JsonPropertyName("scope")]
    public string Scope { get; private set; }
  }
}
