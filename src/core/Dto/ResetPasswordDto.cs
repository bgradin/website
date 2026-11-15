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
    [MinLength(12, ErrorMessage = "Use at least 12 characters for your password.")]
    [JsonPropertyName("password")]
    public string Password { get; set; }
  }
}
