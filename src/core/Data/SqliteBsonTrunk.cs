using Newtonsoft.Json.Bson;
using Newtonsoft.Json.Linq;
using Quilting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gradinware.Data
{
  internal class SqliteBsonTrunk : KvpDbContext, ITrunk
  {
    private readonly string _databaseName;
    private IDbContextTransaction _transaction;

    public string Delimiter
    {
      get
      {
        return KeyDelimiter;
      }
    }

    public SqliteBsonTrunk(string databaseName)
    {
      _databaseName = databaseName;
      Database.EnsureCreated();
    }

    protected override string GetDatabaseName()
    {
      return _databaseName;
    }

    public void BeginTransaction()
    {
      _transaction = Database.BeginTransaction();
    }

    public void CommitTransaction()
    {
      _transaction?.Commit();
      _transaction?.Dispose();
      _transaction = null;
    }

    public void RollbackTransaction()
    {
      _transaction?.Rollback();
      _transaction?.Dispose();
      _transaction = null;
    }

    public void Clear(string id)
    {
      if (!ValidName(id)) throw new UnauthorizedAccessException();
      AuthorizedMutation(() =>
      {
        if (!IsLead(id) && !IsBootstrap(id)) throw new UnauthorizedAccessException();
        KeyValuePairs.RemoveRange(KeyValuePairs);
        SaveChanges();
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
      return AuthorizedRead(() => KeyValuePairs.Select(x => x.Key).ToList()
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
      var kvp = KeyValuePairs.FirstOrDefault(x => x.Key == key);
      if (kvp == null)
      {
        var keys = KeyValuePairs
          .Select(x => x.Key).ToList()
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

          finalToken[finalSegment] = PrepareView(Deserialize(KeyValuePairs.First(x => x.Key == matchingKey).Value), matchingKey, id);
        }

        return result;
      }

      return CanGet(key, id) ? PrepareView(Deserialize(kvp.Value), key, id) : null;
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
      foreach (var relativeKey in KeyValuePairs
        .Select(x => x.Key).ToList()
        .Where(x => x.StartsWith(key + Delimiter, StringComparison.Ordinal))
        .Select(x => x.Substring(key.Length + Delimiter.Length)))
      {
        if (ContainsPropertyPath((JObject)value, relativeKey.Split(new[] { Delimiter }, StringSplitOptions.None)))
        {
          return false;
        }
      }

      using (var stream = new MemoryStream())
      using (var writer = new BsonDataWriter(stream))
      {
        var stored = value.DeepClone();
        StripPinned(stored);
        stored.WriteTo(writer);
        var encodedValue = Convert.ToBase64String(stream.ToArray());

        var kvp = KeyValuePairs.FirstOrDefault(x => x.Key == key);
        if (kvp != null)
        {
          kvp.Value = encodedValue;
        }
        else
        {
          KeyValuePairs.Add(new KeyValuePair { Key = key, Value = encodedValue });
        }
      }

      SaveChanges();
      return true;
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
      var rows = KeyValuePairs.ToList().Where(x => x.Key == key || x.Key.StartsWith(key + Delimiter, StringComparison.Ordinal)).ToList();
      if (rows.Count == 0 || rows.Any(x => !CanPut(x.Key, null, id))) return false;
      KeyValuePairs.RemoveRange(rows);
      SaveChanges();
      return true;
    }

    public bool CanPut(string key, string id)
    {
      return CanPut(key, null, id);
    }

    private bool AuthorizedMutation(Func<bool> mutation)
    {
      var ownsTransaction = _transaction == null;
      if (ownsTransaction) BeginTransaction();
      try
      {
        var result = mutation();
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
      var ownsTransaction = _transaction == null;
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

    private JToken Deserialize(string value)
    {
      var data = Convert.FromBase64String(value);
      using (var stream = new MemoryStream(data))
      using (var reader = new BsonDataReader(stream))
      {
        var token = JToken.ReadFrom(reader);
        if (token.Type != JTokenType.Object)
        {
          throw new InvalidOperationException();
        }

        return token;
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
      return !string.IsNullOrWhiteSpace(id) && !KeyValuePairs.Any();
    }

    private JObject ReadIdentity(string id)
    {
      var key = Constants.QuiltersKey + Delimiter + id;
      var row = KeyValuePairs.FirstOrDefault(x => x.Key == key);
      return row == null ? null : Deserialize(row.Value) as JObject;
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
      var row = KeyValuePairs.FirstOrDefault(x => x.Key == key);
      return row == null ? null : Deserialize(row.Value) as JObject;
    }

    private bool HasPatchOrDescendant(string key)
    {
      return KeyValuePairs.Select(x => x.Key).ToList()
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
