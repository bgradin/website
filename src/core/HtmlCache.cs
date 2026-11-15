using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Gradinware
{
  public static class HtmlCache
  {
    // Bounded striped locks avoid retaining a lock for every requested URL.
    private static readonly SemaphoreSlim[] Gates = CreateGates();
    private static long nextCleanup;
    private static readonly string ProcessVersion = Guid.NewGuid().ToString("N");
    private static string Root => Path.Combine(
      string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DATA_DIRECTORY"))
        ? "/data" : Environment.GetEnvironmentVariable("DATA_DIRECTORY"), "cache", "ssr-v2");
    private static string BuildVersion => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SSR_BUILD_VERSION")) ? ProcessVersion : Environment.GetEnvironmentVariable("SSR_BUILD_VERSION");
    private static TimeSpan Lifetime => TimeSpan.FromSeconds(
      double.TryParse(Environment.GetEnvironmentVariable("SSR_CACHE_TTL_SECONDS"), out var seconds)
      && double.IsFinite(seconds) && seconds >= 0 && seconds <= 2592000 ? seconds : 300);

    private static SemaphoreSlim[] CreateGates()
    {
      var gates = new SemaphoreSlim[64];
      for (var i = 0; i < gates.Length; i++) gates[i] = new SemaphoreSlim(1, 1);
      return gates;
    }

    // Preserve object and array order: the renderer uses object order for children.
    internal static string Fingerprint(JToken data) => Convert.ToHexString(
      SHA256.HashData(Encoding.UTF8.GetBytes(data.ToString(Formatting.None))));

    public static async Task<string> GetOrCreate(string fingerprint, string key, Func<Task<string>> render, CancellationToken cancellationToken = default)
    {
      var digest = SHA256.HashData(Encoding.UTF8.GetBytes("resolved-json-v1\0" + BuildVersion + "\0" + fingerprint + "\0" + key.TrimStart('/')));
      var path = Path.Combine(Root, Convert.ToHexString(digest) + ".html");
      var gate = Gates[digest[0] % Gates.Length];
      await gate.WaitAsync(cancellationToken);
      try
      {
        Cleanup();
        try
        {
          if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < Lifetime)
            return await File.ReadAllTextAsync(path, cancellationToken);
        }
        catch (IOException) { } // A cleanup or another process may remove the entry.
        catch (UnauthorizedAccessException) { }

        cancellationToken.ThrowIfCancellationRequested();
        var html = await render();
        if (html == null || Lifetime == TimeSpan.Zero) return html;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
          Directory.CreateDirectory(Root);
          await File.WriteAllTextAsync(temporary, html, cancellationToken);
          File.Move(temporary, path, overwrite: true);
        }
        catch (IOException) { } // Cache failures must not prevent serving a rendered page.
        catch (UnauthorizedAccessException) { }
        finally
        {
          try { File.Delete(temporary); }
          catch (IOException) { }
          catch (UnauthorizedAccessException) { }
        }
        return html;
      }
      finally { gate.Release(); }
    }

    public static async Task Clear()
    {
      // Drain every in-flight render before deleting, so it cannot repopulate stale entries.
      var acquiredGateCount = 0;
      try
      {
        foreach (var gate in Gates)
        {
          await gate.WaitAsync();
          acquiredGateCount++;
        }

        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        Interlocked.Exchange(ref nextCleanup, 0);
      }
      finally
      {
        for (var i = acquiredGateCount - 1; i >= 0; i--)
        {
          Gates[i].Release();
        }
      }
    }

    private static void Cleanup()
    {
      var now = DateTime.UtcNow;
      var scheduled = Interlocked.Read(ref nextCleanup);
      if (now.Ticks < scheduled || Interlocked.CompareExchange(ref nextCleanup, now.AddMinutes(1).Ticks, scheduled) != scheduled) return;
      try
      {
        if (!Directory.Exists(Root)) return;
        // Obsolete builds/fingerprints expire too; keep active temporary writes for an hour.
        foreach (var path in Directory.EnumerateFiles(Root))
        {
          try
          {
            var retention = path.EndsWith(".tmp", StringComparison.Ordinal) ? TimeSpan.FromHours(1) : Lifetime;
            if (now - File.GetLastWriteTimeUtc(path) >= retention) File.Delete(path);
          }
          catch (IOException) { }
          catch (UnauthorizedAccessException) { }
        }
      }
      catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }
}
