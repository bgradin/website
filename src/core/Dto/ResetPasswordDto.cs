using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Gradinware.Dto
{
  public class ResetPasswordDto
  {
    [Required]
    [JsonPropertyName("token")]
    public string Token { get; set; }

    [Required]
    [JsonPropertyName("password")]
    public string Password { get; set; }
  }
}
