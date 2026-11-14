using System;
using System.IO;
using System.Threading.Tasks;
using Gradinware.Utility;

namespace Gradinware
{
  public static class HtmlCache
  {
    private static string CacheDirectory => Path.Combine(
      Path.GetFullPath(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DATA_DIRECTORY"))
        ? "/data"
        : Environment.GetEnvironmentVariable("DATA_DIRECTORY")),
      "cache");

    public static async Task Store(string key, string html)
    {
      var path = GetPath(key);

      DirectoryUtility.EnsureDirectoryExists(path);

      using (var file = new StreamWriter(path))
      {
        await file.WriteAsync(html);
      }
    }

    public static async Task<string> Load(string key)
    {
      var path = GetPath(key);

      if (!File.Exists(path))
      {
        throw new InvalidOperationException();
      }

      using (var file = new StreamReader(path))
      {
        return await file.ReadToEndAsync();
      }
    }

    public static bool Exists(string key) {
      return File.Exists(GetPath(key));
    }

    private static string GetPath(string key)
    {
      if (string.IsNullOrWhiteSpace(key) || key.IndexOf('\0') >= 0)
      {
        throw new ArgumentException("A valid cache key is required.", nameof(key));
      }

      var root = Path.GetFullPath(CacheDirectory);
      var relative = DirectoryUtility.RemoveLeadingSeparator(key) + ".html";
      var path = Path.GetFullPath(Path.Combine(root, relative));
      var relativeToRoot = Path.GetRelativePath(root, path);
      if (relativeToRoot == ".." || relativeToRoot.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relativeToRoot))
      {
        throw new ArgumentException("The cache key points outside the cache directory.", nameof(key));
      }
      return path;
    }
  }
}
