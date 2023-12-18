using System;
using System.IO;
using Microsoft.EntityFrameworkCore;

namespace Gradinware.Data
{
  public abstract class SqliteDbContext : DbContext
  {
    protected const string DatabaseRootDirectory = "/data";

    private const string _databaseDirectory = "sqlite";

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

      string directory = Path.Combine(DatabaseRootDirectory, GetDatabaseDirectory());
      Directory.CreateDirectory(directory);

      optionBuilder.UseSqlite($"Data Source={directory}/{GetDatabaseName()}");
    }
  }
}
