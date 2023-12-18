using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Gradinware.Dto
{
  public class TokenExchangeDto
  {
    [Required]
    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; }
  }
}
