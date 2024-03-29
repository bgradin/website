using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Gradinware.Models.Authentication
{
  [Table("UserEvents")]
  public class UserEvent
  {
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int UserId { get; set; }

    public UserEventCode Code { get; set; }

    public DateTime Timestamp { get; set; }
  }
}
