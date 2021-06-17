using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Gradinware
{
    internal class User
    {
        [Required]
        [JsonPropertyName("id")]
        public int Id { get; set; }

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
        [JsonIgnore]
        public string Password { get; set; }

        [JsonPropertyName("authToken")]
        public string AuthToken { get; set; }

        [JsonPropertyName("passwordResetToken")]
        public string PasswordResetToken { get; set; }

        [Required]
        [JsonIgnore]
        public DateTime DateCreated { get; set; }

        [JsonIgnore]
        public string DateLastLogin { get; set; }
    }
}
