using System;
using System.IO;
using Microsoft.EntityFrameworkCore;

namespace Gradinware.Data
{
    internal abstract class SqliteDbContext : DbContext
    {
        protected const string DatabaseRootDirectory = "/data";

        private const string _databaseDirectory = "sqlite";

        private static bool _created;

        public SqliteDbContext()
        {
            if (!_created)
            {
                Database.EnsureCreated();
            }
        }

        protected abstract string GetDatabaseName();

        protected virtual string GetDatabaseDirectory()
        {
            return _databaseDirectory;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionBuilder)
        {
            if (!Directory.Exists(DatabaseRootDirectory))
            {
                throw new InvalidOperationException();
            }

            string directory = string.Format("{0}/{1}", DatabaseRootDirectory, GetDatabaseDirectory());
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            optionBuilder.UseSqlite(string.Format(@"Data Source={0}/{1}", directory, GetDatabaseName()));
        }
    }
}