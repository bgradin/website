using System.Text.Json.Serialization;
using Gradinware.Models.Authentication;

namespace Gradinware.Dto
{
  public class UserDto
  {
    public UserDto(User user)
    {
      Id = user.Id;
      FirstName = user.FirstName;
      LastName = user.LastName;
      Email = user.Email;
    }

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("firstName")]
    public string FirstName { get; set; }

    [JsonPropertyName("lastName")]
    public string LastName { get; set; }

    [JsonPropertyName("email")]
    public string Email { get; set; }
  }
}
