using Gradinware.Models.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Gradinware.Data
{
  public class AccountContext : SqliteDbContext
  {
    public AccountContext()
    {
      Database.Migrate();
    }

    private const string _databaseName = "accounts.db";

    public DbSet<User> Users { get; set; }
    public DbSet<UserEvent> UserEvents { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<ResetToken> ResetTokens { get; set; }

    protected override string GetDatabaseName()
    {
        return _databaseName;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<RefreshToken>()
            .HasOne(x => x.User)
            .WithMany(x => x.RefreshTokens);

        modelBuilder.Entity<ResetToken>()
            .HasOne(x => x.User)
            .WithMany(x => x.ResetTokens);
    }
  }
}
