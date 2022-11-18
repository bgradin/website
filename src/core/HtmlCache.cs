using System;
using System.IO;
using System.Threading.Tasks;
using Gradinware.Utility;

namespace Gradinware
{
  public static class HtmlCache
  {
    private static readonly string CACHE_DIRECTORY = "/data/cache";

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
      return Path.Combine(CACHE_DIRECTORY, DirectoryUtility.RemoveLeadingSeparator(key) + ".html");
    }
  }
}
