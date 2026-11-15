using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Gradinware.Dto
{
  public class CreateUserDto
  {
    [Required]
    [MaxLength(8192)]
    [JsonPropertyName("captchaToken")]
    public string CaptchaToken { get; set; }

    [Required]
    [MinLength(2)]
    [MaxLength(35)]
    [JsonPropertyName("firstName")]
    public string FirstName { get; set; }

    [Required]
    [MinLength(2)]
    [MaxLength(35)]
    [JsonPropertyName("lastName")]
    public string LastName { get; set; }

    [Required]
    [MinLength(3)]
    [MaxLength(320)]
    [EmailAddress]
    [JsonPropertyName("email")]
    public string Email { get; set; }

    [Required]
    [MinLength(12, ErrorMessage = "Use at least 12 characters for your password.")]
    [JsonPropertyName("password")]
    public string Password { get; set; }
  }
}
