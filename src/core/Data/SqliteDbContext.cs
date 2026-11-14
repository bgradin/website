using System;
using System.IO;
using Microsoft.EntityFrameworkCore;

namespace Gradinware.Data
{
  public abstract class SqliteDbContext : DbContext
  {
    private const string _databaseDirectory = "sqlite";

    protected abstract string GetDatabaseName();

    protected virtual string GetDatabaseDirectory()
    {
      return _databaseDirectory;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionBuilder)
    {
      var configuredRoot = Environment.GetEnvironmentVariable("DATA_DIRECTORY");
      string rootDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredRoot) ? "/data" : configuredRoot);
      string directory = Path.Combine(rootDirectory, GetDatabaseDirectory());
      Directory.CreateDirectory(directory);

      optionBuilder.UseSqlite($"Data Source={directory}/{GetDatabaseName()}");
    }
  }
}
