using Gradinware.Models.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Gradinware.Data
{
    internal class AccountContext : SqliteDbContext
    {
        private const string _databaseName = "accounts.db";

        public DbSet<User> Users { get; set; }
        public DbSet<UserEvent> UserEvents { get; set; }

        protected override string GetDatabaseName()
        {
            return _databaseName;
        }
    }
}
