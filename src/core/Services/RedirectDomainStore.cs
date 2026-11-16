using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Gradinware.Services
{
  public sealed class RedirectDomainStore
  {
    private readonly string _connectionString;
    private readonly string _environment;
    private readonly object _gate = new();

    public RedirectDomainStore(IConfiguration configuration, IHostEnvironment environment)
    {
      var root = Environment.GetEnvironmentVariable("DATA_DIRECTORY");
      var directory = Path.Combine(Path.GetFullPath(string.IsNullOrWhiteSpace(root) ? "/data" : root), "sqlite");
      Directory.CreateDirectory(directory);
      _connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "site.db") }.ToString();
      _environment = environment.EnvironmentName.ToLowerInvariant();
      using var connection = Open();
      using var command = connection.CreateCommand();
      command.CommandText = "CREATE TABLE IF NOT EXISTS RedirectDomains (Environment TEXT PRIMARY KEY, Domains TEXT NOT NULL);";
      command.ExecuteNonQuery();
      command.CommandText = "INSERT OR IGNORE INTO RedirectDomains (Environment, Domains) VALUES ($environment, $domains)";
      command.Parameters.AddWithValue("$environment", _environment);
      command.Parameters.AddWithValue("$domains", JsonSerializer.Serialize(configuration.GetSection("AccountRedirectDomains")
        .GetChildren().Select(item => Normalize(item.Value)).Where(domain => domain != null).Distinct().OrderBy(domain => domain)));
      command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
      var connection = new SqliteConnection(_connectionString);
      connection.Open();
      return connection;
    }

    public static string Normalize(string value)
    {
      var domain = value?.Trim().ToLowerInvariant();
      if (string.IsNullOrEmpty(domain) || domain.Length > 253 || domain.EndsWith('.') ||
          Uri.CheckHostName(domain) != UriHostNameType.Dns || domain.Contains('*')) return null;
      return domain;
    }

    public string[] GetDomains()
    {
      using var connection = Open();
      using var command = connection.CreateCommand();
      command.CommandText = "SELECT Domains FROM RedirectDomains WHERE Environment = $environment";
      command.Parameters.AddWithValue("$environment", _environment);
      return JsonSerializer.Deserialize<string[]>((string)command.ExecuteScalar());
    }

    public string[] Change(string domain, bool add)
    {
      domain = Normalize(domain) ?? throw new ArgumentException("Enter a domain name without a scheme, port, or path.");
      lock (_gate)
      {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Domains FROM RedirectDomains WHERE Environment = $environment";
        command.Parameters.AddWithValue("$environment", _environment);
        var domains = JsonSerializer.Deserialize<string[]>((string)command.ExecuteScalar()).ToHashSet();
        if (add) domains.Add(domain); else domains.Remove(domain);
        var result = domains.OrderBy(value => value).ToArray();
        command.CommandText = "UPDATE RedirectDomains SET Domains = $domains WHERE Environment = $environment";
        command.Parameters.AddWithValue("$domains", JsonSerializer.Serialize(result));
        command.ExecuteNonQuery();
        transaction.Commit();
        return result;
      }
    }
  }
}
