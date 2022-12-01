using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Gradinware.Data
{
  internal abstract class KvpDbContext : SqliteDbContext
  {
    public DbSet<KeyValuePair> KeyValuePairs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
      modelBuilder.Entity<KeyValuePair>().ToTable("Kvp");
    }

    public class KeyValuePair
    {
      [Key]
      public string Key { get; set; }
      public string Value { get; set; }
    }
  }
}
