using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Gradinware.Dto
{
  public class CreateUserDto
  {
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
    [JsonPropertyName("email")]
    public string Email { get; set; }

    [Required]
    [MinLength(8)]
    [JsonPropertyName("password")]
    public string Password { get; set; }
  }
}
