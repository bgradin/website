using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Gradinware.Dto
{
  public class SendPasswordResetEmailDto
  {
    [Required]
    [JsonPropertyName("email")]
    public string Email { get; set; }
  }
}
