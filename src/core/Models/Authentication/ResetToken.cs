using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Gradinware.Models.Authentication
{
  [Table("ResetTokens")]
  public class ResetToken
  {
    public ResetToken()
    {
    }

    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int UserId { get; set; }

    [Required]
    public string Token { get; set; }

    public DateTime ExpiresAt { get; set; }

    public bool Used { get; set; }

    [ForeignKey("UserId")]
    public virtual User User { get; set; }
  }
}
