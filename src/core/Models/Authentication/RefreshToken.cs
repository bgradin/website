using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Gradinware.Models.Authentication
{
  [Table("RefreshTokens")]
  public class RefreshToken
  {
    public RefreshToken()
    {
      IssuedAt = DateTime.Now;
    }

    public RefreshToken(OAuthPasswordGrant grant)
      : this()
    {
      Token = grant.RefreshToken;
    }

    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public string Token { get; set; }

    public DateTime IssuedAt { get; set; }

    public int UserId { get; set; }

    [ForeignKey("UserId")]
    public virtual User User { get; set; }
  }
}
