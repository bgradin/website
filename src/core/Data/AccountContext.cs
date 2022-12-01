using Gradinware.Models.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Gradinware.Data
{
    internal class AccountContext : SqliteDbContext
    {

        public DbSet<User> Users { get; set; }
        public DbSet<UserEvent> UserEvents { get; set; }

        protected override string GetDatabaseName()
        {
            return "accounts.db";
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().ToTable("Users");
            modelBuilder.Entity<UserEvent>().ToTable("UserEvents");
        }
    }
}
