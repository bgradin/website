using Newtonsoft.Json.Linq;
using Quilting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Gradinware.Data
{
  // Quilt's authorization and patch addressing sit above the JSON Pointer store.
  internal sealed class ContentTrunk : IContentTrunk, IDisposable
  {
    private readonly SqliteTrunk storage;
    private bool inTransaction;
    public string Delimiter => ".";

    public ContentTrunk() : this("content.db") { }

    internal ContentTrunk(string databaseName, string dataDirectory = null)
    {
      storage = new SqliteTrunk(databaseName, dataDirectory);
      try
      {
        storage.BeginTransaction();
        var rootType = storage.RootType;
        if (rootType == null || rootType == 6) storage.Put("", new JsonObject(), storage.TreeId);
        else if (rootType != 0) throw new InvalidOperationException("Content requires an object root.");
        storage.CommitTransaction();
      }
      catch { storage.Dispose(); throw; }
    }

    public T ReadSnapshot<T>(Func<T> read)
    {
      var previous = inTransaction;
      return storage.ReadSnapshot(() =>
      {
        inTransaction = true;
        try { return read(); }
        finally { inTransaction = previous; }
      });
    }

    public void EnsureCreated() { } // Opening the node store initializes its schema.
    public void BeginTransaction() { storage.BeginTransaction(); inTransaction = true; }
    public void CommitTransaction() { try { storage.CommitTransaction(); } finally { inTransaction = false; } }
    public void RollbackTransaction() { try { storage.RollbackTransaction(); } finally { inTransaction = false; } }
    public void Dispose() => storage.Dispose();
    private static string Pointer(string key) => "/" + key.Replace("~", "~0").Replace("/", "~1");
    private IEnumerable<string> StoredKeys() => storage.GetPropertyNames("", storage.TreeId);
    private JObject ReadStored(string key)
    {
      var value = storage.Get(Pointer(key), storage.TreeId);
      return value == null ? null : JObject.Parse(value.ToJsonString());
    }

    public void Clear(string id)
    {
      if (!ValidName(id)) throw new UnauthorizedAccessException();
      AuthorizedMutation(() =>
      {
        if (!IsLead(id) && !IsBootstrap(id)) throw new UnauthorizedAccessException();
        storage.Put("", new JsonObject(), storage.TreeId);
        return true;
      });
    }

    public IEnumerable<string> GetKeys(string id)
    {
      return GetKeys(string.Empty, id);
    }

    public IEnumerable<string> GetKeys(string prefix, string id)
    {
      if ((prefix != string.Empty && !KeyRules.IsValidPath(prefix, Delimiter)) || !ValidIdentity(id)) return Array.Empty<string>();
      return AuthorizedRead(() => StoredKeys()
        .Where(x => x.StartsWith(prefix, StringComparison.Ordinal))
        .Where(key => CanGet(key, id)).ToList());
    }

    public JToken Get(string key, string id)
    {
      return AuthorizedRead(() => GetCore(key, id));
    }

    private JToken GetCore(string key, string id)
    {
      var arrayAddress = ArrayAddress.Parse(key, Delimiter);
      if (arrayAddress != null)
      {
        var container = GetCore(arrayAddress.ContainerKey, id);
        return container?.SelectToken(arrayAddress.TokenPath, errorWhenNoMatch: false)?.DeepClone();
      }
      if (!KeyRules.IsValidPath(key, Delimiter) || !ValidIdentity(id)) return null;
      var stored = ReadStored(key);
      if (stored == null)
      {
        var keys = StoredKeys()
          .Where(x => x.StartsWith(key, StringComparison.Ordinal))
          .ToList()
          .Where(x => x.Substring(key.Length).StartsWith(Delimiter, StringComparison.Ordinal))
          .Where(x => CanGet(x, id));
        var numKeys = keys.Count();
        if (numKeys == 0)
        {
          return null;
        }

        var result = new JObject();
        foreach (var matchingKey in keys)
        {
          JToken current = result;
          JToken finalToken = null;
          string finalSegment = null;
          foreach (var segment in matchingKey.Substring(key.Length + 1).Split(Delimiter))
          {
            finalSegment = segment;
            finalToken = current;
            current[segment] = current[segment] ?? new JObject();
            current = current[segment];
          }

          finalToken[finalSegment] = PrepareView(ReadStored(matchingKey), matchingKey, id);
        }

        return result;
      }

      return CanGet(key, id) ? PrepareView(stored, key, id) : null;
    }

    public bool Put(string key, JToken value, string id)
    {
      return AuthorizedMutation(() => PutCore(key, value, id));
    }

    private bool PutCore(string key, JToken value, string id)
    {
      var arrayAddress = ArrayAddress.Parse(key, Delimiter);
      if (arrayAddress != null)
      {
        if (!(value is JObject) || !CanPut(arrayAddress.ContainerKey, null, id)) return false;
        var container = GetCore(arrayAddress.ContainerKey, id) as JObject;
        if (container == null) return false;
        var target = container.SelectToken(arrayAddress.TokenPath, errorWhenNoMatch: false);
        if (!(target is JObject)) return false;
        target.Replace(value.DeepClone());
        return PutCore(arrayAddress.ContainerKey, container, id);
      }
      if (!(value is JObject) || !KeyRules.IsValidPath(key, Delimiter)) return false;
      if (!CanPut(key, value, id)) return false;
      // Detect collisions. Terminate put operation if this happens
      // rather than attempting to implement complicated merge logic
      foreach (var relativeKey in StoredKeys()
        .Where(x => x.StartsWith(key + Delimiter, StringComparison.Ordinal))
        .Select(x => x.Substring(key.Length + Delimiter.Length)))
      {
        if (ContainsPropertyPath((JObject)value, relativeKey.Split(new[] { Delimiter }, StringSplitOptions.None)))
        {
          return false;
        }
      }

      var stored = value.DeepClone();
      StripPinned(stored);
      return storage.Put(Pointer(key), JsonNode.Parse(stored.ToString(Newtonsoft.Json.Formatting.None)), storage.TreeId);
    }

    private static bool ContainsPropertyPath(JObject value, IEnumerable<string> segments)
    {
      JToken current = value;
      foreach (var segment in segments)
      {
        if (!(current is JObject obj) || !obj.TryGetValue(segment, out current)) return false;
      }
      return true;
    }

    public bool Delete(string key, string id)
    {
      return AuthorizedMutation(() => DeleteCore(key, id));
    }

    private bool DeleteCore(string key, string id)
    {
      var arrayAddress = ArrayAddress.Parse(key, Delimiter);
      if (arrayAddress != null)
      {
        if (!CanPut(arrayAddress.ContainerKey, null, id)) return false;
        var container = GetCore(arrayAddress.ContainerKey, id) as JObject;
        var target = container?.SelectToken(arrayAddress.TokenPath, errorWhenNoMatch: false);
        if (target == null) return false;
        target.Replace(JValue.CreateNull());
        return PutCore(arrayAddress.ContainerKey, container, id);
      }
      if (!KeyRules.IsValidPath(key, Delimiter) || !ValidIdentity(id) || !CanPut(key, null, id)) return false;
      var rows = StoredKeys().Where(x => x == key || x.StartsWith(key + Delimiter, StringComparison.Ordinal)).ToList();
      if (rows.Count == 0 || rows.Any(x => !CanPut(x, null, id))) return false;
      foreach (var row in rows) storage.Delete(Pointer(row), storage.TreeId);
      return true;
    }

    public bool CanPut(string key, string id)
    {
      return CanPut(key, null, id);
    }

    private bool AuthorizedMutation(Func<bool> mutation)
    {
      var ownsTransaction = !inTransaction;
      if (ownsTransaction) BeginTransaction();
      try
      {
        var result = storage.Mutate(mutation);
        if (ownsTransaction)
        {
          if (result) CommitTransaction();
          else RollbackTransaction();
        }
        return result;
      }
      catch
      {
        if (ownsTransaction) RollbackTransaction();
        throw;
      }
    }

    private T AuthorizedRead<T>(Func<T> read)
    {
      var ownsTransaction = !inTransaction;
      if (ownsTransaction) BeginTransaction();
      try
      {
        var result = read();
        if (ownsTransaction) CommitTransaction();
        return result;
      }
      catch
      {
        if (ownsTransaction) RollbackTransaction();
        throw;
      }
    }

    private bool ValidIdentity(string id)
    {
      if (!ValidName(id)) return false;
      return IsBootstrap(id) || ReadIdentity(id) != null;
    }

    private bool ValidName(string value) => KeyRules.IsValidSegment(value, Delimiter);

    private bool IsBootstrap(string id)
    {
      return !string.IsNullOrWhiteSpace(id) && !StoredKeys().Any();
    }

    private JObject ReadIdentity(string id)
    {
      var key = Constants.QuiltersKey + Delimiter + id;
      return ReadStored(key);
    }

    private string[] Memberships(string id)
    {
      var token = ReadIdentity(id)?[Patch.CircleIdsKey];
      if (token == null || token.Type != JTokenType.Array) return Array.Empty<string>();
      if ((token as JArray).Any(x => x.Type != JTokenType.String)) return Array.Empty<string>();
      return (token as JArray).Select(x => x.Value<string>()).ToArray();
    }

    private bool IsLead(string id)
    {
      return ValidIdentity(id) && Memberships(id).Contains(Constants.LeadCircleId);
    }

    private bool CanGet(string key, string id)
    {
      var arrayAddress = ArrayAddress.Parse(key, Delimiter);
      if (arrayAddress != null) return CanGet(arrayAddress.ContainerKey, id)
        && GetCore(arrayAddress.ContainerKey, id)?.SelectToken(arrayAddress.TokenPath, false) != null;
      if (!KeyRules.IsValidPath(key, Delimiter) || !ValidIdentity(id)) return false;
      if (IsLead(id)) return HasPatchOrDescendant(key);
      if (key.StartsWith(Constants.QuiltersKey + Delimiter, StringComparison.Ordinal)
        && key != Constants.QuiltersKey + Delimiter + id) return false;
      if (key.StartsWith(Constants.CirclesKey + Delimiter, StringComparison.Ordinal)) return false;
      var memberships = Memberships(id);
      var current = key;
      while (!string.IsNullOrEmpty(current))
      {
        var patch = ReadExact(current);
        if (patch != null)
        {
          var access = Permission(patch, "get");
          var put = Permission(patch, "put");
          if (access != null && !Matches(access, memberships, true) && (put == null || !Matches(put, memberships, false))) return false;
        }
        current = ParentKey(current);
      }
      return HasPatchOrDescendant(key);
    }

    private bool CanPut(string key, JToken value, string id)
    {
      var arrayAddress = ArrayAddress.Parse(key, Delimiter);
      if (arrayAddress != null) return CanPut(arrayAddress.ContainerKey, null, id)
        && GetCore(arrayAddress.ContainerKey, id)?.SelectToken(arrayAddress.TokenPath, false) is JObject;
      if (!KeyRules.IsValidPath(key, Delimiter) || !ValidIdentity(id)) return false;
      if (IsLead(id)) return true;
      if (IsBootstrap(id))
      {
        var initialMemberships = StringArray((value as JObject)?[Patch.CircleIdsKey]);
        return key == Constants.QuiltersKey + Delimiter + id && initialMemberships.Contains(Constants.LeadCircleId);
      }
      if (key.StartsWith(Constants.QuiltersKey + Delimiter, StringComparison.Ordinal)
        || key.StartsWith(Constants.CirclesKey + Delimiter, StringComparison.Ordinal)) return false;
      var existing = ReadExact(key);
      if (existing != null && !CanGet(key, id)) return false;
      var memberships = Memberships(id);
      var current = key;
      while (!string.IsNullOrEmpty(current))
      {
        var ancestor = ReadExact(current);
        if (ancestor != null)
        {
          var put = Permission(ancestor, "put");
          if (put != null && !Matches(put, memberships, false)) return false;
          if (put == null && ancestor[Patch.CircleIdsKey] != null && !MembershipsMatch(ancestor, memberships)) return false;
        }
        current = ParentKey(current);
      }
      if (existing == null)
      {
        var parentKey = ParentKey(key);
        var parent = ReadExact(parentKey);
        if (string.IsNullOrEmpty(parentKey) || parent == null || !CanGet(parentKey, id)) return false;
      }
      var proposed = value as JObject;
      if (proposed != null)
      {
        if (existing != null)
        {
          if (existing.ContainsKey(Patch.AccessKey) && !proposed.ContainsKey(Patch.AccessKey)) return false;
          if (existing[Patch.CircleIdsKey] != null && proposed[Patch.CircleIdsKey] == null) return false;
          var oldCircles = StringArray(existing[Patch.CircleIdsKey]);
          var newCircles = StringArray(proposed[Patch.CircleIdsKey]);
          if (oldCircles.Except(newCircles).Union(newCircles.Except(oldCircles)).Any(circle => !memberships.Contains(circle))) return false;
          if (existing[Patch.CircleIdsKey] != null && newCircles.Except(oldCircles).Any()) return false;
        }
        foreach (var operation in new[] { "get", "put" })
        {
          var permission = Permission(proposed, operation);
          var previous = existing == null ? Array.Empty<string>() : Permission(existing, operation) ?? Array.Empty<string>();
          if (permission != null && permission.Except(previous).Any(circle => circle == Constants.EveryoneCircleId || !memberships.Contains(circle))) return false;
          var parent = ReadExact(ParentKey(key));
          var inherited = parent == null ? null : Permission(parent, operation);
          if (permission != null && inherited != null && permission.Any(circle => !inherited.Contains(circle))) return false;
        }
      }
      return true;
    }

    private static string[] StringArray(JToken token)
    {
      if (token == null) return Array.Empty<string>();
      if (token.Type != JTokenType.Array || (token as JArray).Any(x => x.Type != JTokenType.String)) return new[] { "\0" };
      return (token as JArray).Select(x => x.Value<string>()).ToArray();
    }

    private static bool MembershipsMatch(JObject patch, string[] memberships)
    {
      var token = patch[Patch.CircleIdsKey];
      if (token == null) return true;
      if (token.Type != JTokenType.Array || (token as JArray).Any(x => x.Type != JTokenType.String)) return false;
      return (token as JArray).Any(x => memberships.Contains(x.Value<string>()));
    }

    private static string[] Permission(JObject patch, string operation)
    {
      if (!patch.ContainsKey(Patch.AccessKey)) return null;
      var access = patch[Patch.AccessKey] as JObject;
      if (access == null) return Array.Empty<string>();
      var token = access[operation];
      if (token == null && operation == "get") token = access["put"];
      if (token == null) return Array.Empty<string>();
      if (token.Type != JTokenType.Array || (token as JArray).Any(x => x.Type != JTokenType.String)) return Array.Empty<string>();
      return (token as JArray).Select(x => x.Value<string>()).ToArray();
    }

    private static bool Matches(string[] allowed, string[] memberships, bool everyone)
    {
      return allowed.Any(circle => (everyone && circle == Constants.EveryoneCircleId) || memberships.Contains(circle));
    }

    private JObject ReadExact(string key)
    {
      return ReadStored(key);
    }

    private bool HasPatchOrDescendant(string key)
    {
      return StoredKeys()
        .Any(candidate => candidate == key || candidate.StartsWith(key + Delimiter, StringComparison.Ordinal));
    }

    private string ParentKey(string key)
    {
      var index = key.LastIndexOf(Delimiter, StringComparison.Ordinal);
      return index < 0 ? string.Empty : key.Substring(0, index);
    }

    private static void StripPinned(JToken token)
    {
      if (token is JObject obj)
      {
        obj.Remove(Constants.PinnedKey);
        foreach (var child in obj.Properties().Select(x => x.Value).ToArray()) StripPinned(child);
      }
      else if (token is JArray array)
      {
        foreach (var child in array) StripPinned(child);
      }
    }

    private JToken PrepareView(JToken token, string key, string id)
    {
      if (token is JObject obj)
      {
        obj.Remove(Constants.PinnedKey);
        obj[Constants.PinnedKey] = !CanPut(key, id);
        foreach (var property in obj.Properties().Where(x => x.Name != Constants.PinnedKey && x.Name != Patch.AccessKey).ToArray())
          PrepareView(property.Value, key + Delimiter + property.Name, id);
      }
      else if (token is JArray array)
      {
        foreach (var child in array) PrepareView(child, key, id);
      }
      return token;
    }
  }
}
