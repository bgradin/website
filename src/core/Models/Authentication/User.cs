using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Gradinware.Models.Authentication
{
  [Table("Users")]
  public class User
  {
    [Required]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MinLength(2)]
    [MaxLength(35)]
    public string FirstName { get; set; }

    [Required]
    [MinLength(2)]
    [MaxLength(35)]
    public string LastName { get; set; }

    [Required]
    [MinLength(3)]
    [MaxLength(320)]
    public string Email { get; set; }

    [Required]
    [MinLength(8)]
    public string Password { get; set; }

    [Required]
    public DateTime DateCreated { get; set; }

    public DateTime DateLastLogin { get; set; }

    public virtual ICollection<RefreshToken> RefreshTokens { get; set; }

    public virtual ICollection<ResetToken> ResetTokens { get; set; }
  }
}
