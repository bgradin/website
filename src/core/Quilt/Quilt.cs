using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Quilting
{
  public class Quilt
  {
    private const int MaxMotifDepth = 32;
    readonly ITrunk _trunk;
    int _transactionDepth;

    public Quilt(ITrunk trunk)
    {
      _trunk = trunk;
    }

    public bool Create(JToken token, string id)
    {
      JsonUtility.VerifyType(token, JTokenType.Object);
      if (!KeyRules.IsValidSegment(id, _trunk.Delimiter)) return false;
      _trunk.BeginTransaction();
      _transactionDepth++;
      try
      {
        var created = CreateInTransaction(token, id);
        if (created) _trunk.CommitTransaction();
        else _trunk.RollbackTransaction();
        return created;
      }
      catch (UnauthorizedAccessException)
      {
        _trunk.RollbackTransaction();
        return false;
      }
      catch
      {
        _trunk.RollbackTransaction();
        throw;
      }
      finally
      {
        _transactionDepth--;
      }
    }

    private bool CreateInTransaction(JToken token, string id)
    {
      if (_trunk.GetKeys(id).Count() > 0)
      {
        var quilter = _trunk.Get(Constants.QuiltersKey + _trunk.Delimiter + id, id);
        if (!Patch.TryConvert(quilter)?.CircleIds.EmptyIfNull().Contains(Constants.LeadCircleId) ?? false)
        {
          return false;
        }

        _trunk.Clear(id);
      }

      if (!_trunk.Put(Constants.QuiltersKey + _trunk.Delimiter + id, new JObject
      {
        [Patch.CircleIdsKey] = JArray.FromObject(new[] { Constants.LeadCircleId }),
      }, id))
      {
        return false;
      }
      if (!_trunk.Put(Constants.CirclesKey + _trunk.Delimiter + Constants.LeadCircleId, new JObject
      {
        [Circle.CircleIdsKey] = JArray.FromObject(new[] { Constants.LeadCircleId }),
        [Circle.NameKey] = JValue.FromObject(Constants.LeadCircleName),
      }, id))
      {
        return false;
      }

      var circlesRoot = token[Constants.CirclesKey];
      if (circlesRoot != null)
      {
        JsonUtility.VerifyType(circlesRoot, JTokenType.Object);
        foreach (var circleToken in (circlesRoot as JObject))
        {
          if (!Patch.CanConvert(circleToken.Value) || !CreateCircle(new Circle(circleToken.Value), circleToken.Key, id))
          {
            return false;
          }
        }
      }

      var quiltersRoot = token[Constants.QuiltersKey];
      if (quiltersRoot != null)
      {
        JsonUtility.VerifyType(quiltersRoot, JTokenType.Object);
        foreach (var quilterToken in (quiltersRoot as JObject))
        {
          var quilterPatch = Patch.TryConvert(quilterToken.Value);
          if (quilterPatch == null || !CreateQuilter(new Patch(quilterToken.Value), quilterToken.Key, id))
          {
            return false;
          }
        }
      }

      var contentPatch = Patch.TryConvert(token[Constants.ContentKey]);
      if (contentPatch != null)
      {
        // Forward declare to allow recursion
        Func<Patch, string, bool> storeReferences = null;

        storeReferences = (patch, patchKey) =>
        {
          var motifToken = patch[Constants.MotifKey];
          if (motifToken != null && motifToken.Type == JTokenType.String)
          {
            var reference = motifToken.Value<string>();
            if (!string.IsNullOrEmpty(reference) && !_trunk.GetKeys(id).Contains(reference))
            {
              var newPatch = Patch.TryConvert(token.SelectToken(reference));
              if (newPatch != null)
              {
                if (!_trunk.Put(reference, newPatch, id)
                  || !_trunk.Put(patchKey, patch, id)
                  || !VisitPatches(newPatch, reference, storeReferences))
                {
                  return false;
                }
              }
            }
          }

          return true;
        };

        if (!VisitPatches(contentPatch, Constants.ContentKey, storeReferences))
        {
          return false;
        }
      }

      return true;
    }

    public Map GetMap(string prefix, string quilterId)
    {
      if (prefix != string.Empty && !KeyRules.IsValidPath(prefix, _trunk.Delimiter)) return new Map();
      var keys = _trunk.GetKeys(prefix, quilterId);
      return BuildMap(keys);
    }

    private Map BuildMap(IEnumerable<string> keys)
    {
      var map = new Map();
      foreach (var segments in keys.Select(x => x.Split(_trunk.Delimiter)))
      {
        map.Insert(segments);
      }
      return map;
    }

    public Patch GetPatch(string key, string quilterId)
    {
      if (!KeyRules.IsValidPath(key, _trunk.Delimiter)) return null;
      var patch = Patch.TryConvert(_trunk.Get(key, quilterId));
      if (patch == null) return null;
      if (!SewPatch(patch, key, quilterId))
      {
        return null;
      }

      return patch;
    }

    public bool CreatePatch(Patch patch, string key, string quilterId)
    {
      if (patch == null || !KeyRules.IsValidPath(key, _trunk.Delimiter)) return false;
      if (_transactionDepth > 0) return WritePatch(patch, key, quilterId);
      _trunk.BeginTransaction();
      _transactionDepth++;
      try
      {
        if (!WritePatch(patch, key, quilterId))
        {
          _trunk.RollbackTransaction();
          return false;
        }

        _trunk.CommitTransaction();
        return true;
      }
      catch
      {
        _trunk.RollbackTransaction();
        throw;
      }
      finally
      {
        _transactionDepth--;
      }
    }

    public bool DeletePatch(string key, string quilterId)
    {
      if (!KeyRules.IsValidPath(key, _trunk.Delimiter)) return false;
      return _trunk.Delete(key, quilterId);
    }

    private bool WritePatch(Patch patch, string key, string quilterId)
    {
      return ExtricatePatch(patch, key)
        && _trunk.Put(key, patch, quilterId);
    }

    public bool CreateCircle(Circle circle, string circleId, string quilterId)
    {
      if (!KeyRules.IsValidSegment(circleId, _trunk.Delimiter)) return false;
      return CreatePatch(circle, Constants.CirclesKey + _trunk.Delimiter + circleId, quilterId);
    }

    public bool CreateQuilter(Patch newQuilter, string newQuilterId, string quilterId)
    {
      if (!KeyRules.IsValidSegment(newQuilterId, _trunk.Delimiter)) return false;
      return CreatePatch(newQuilter, Constants.QuiltersKey + _trunk.Delimiter + newQuilterId, quilterId);
    }

    private bool VisitPatches(Patch patch, string patchKey, Func<Patch, string, bool> visit)
    {
      if (!visit(patch, patchKey))
      {
        return false;
      }

      var keys = patch.GetKeys();
      for (int i = 0; i < keys.Length; i++)
      {
        var token = patch[keys[i]];
        var childPatch = Patch.TryConvert(patch[keys[i]]);
        if (childPatch != null)
        {
          if (!VisitPatches(childPatch, patchKey + _trunk.Delimiter + keys[i], visit))
          {
            return false;
          }
        }
        else if (token.Type == JTokenType.Array)
        {
          var arr = token as JArray;
          for (int j = 0; j < arr.Count; j++)
          {
            if (arr[j].Type == JTokenType.Object)
            {
              var arrPatch = Patch.TryConvert(arr[j]);
              if (arrPatch == null || !VisitPatches(new Patch(arr[j]), patchKey + $"[{j}]", visit))
              {
                return false;
              }
            }
          }
        }
      }

      return true;
    }

    private bool SewPatch(Patch root, string rootKey, string quilterId)
    {
      var activeKeys = new HashSet<string> { rootKey };
      // Forward declare to allow recursion
      Func<Patch, string, bool> stitch = null;

      stitch = (patch, patchKey) =>
      {
        var addedCurrent = activeKeys.Add(patchKey);
        try
        {
          var token = patch[Constants.MotifKey];
          if (token != null && token.Type == JTokenType.String)
          {
            var reference = token.Value<string>();
            if (!string.IsNullOrEmpty(reference) && !root.ContainsKey(reference))
            {
              var resolvedKey = ResolvePatchKey(reference, patchKey, quilterId);
              var referencePatch = resolvedKey == null ? null : Patch.TryConvert(_trunk.Get(resolvedKey, quilterId));
              if (referencePatch != null)
              {
                if (activeKeys.Contains(resolvedKey) || activeKeys.Count >= MaxMotifDepth + 1) return false;
                activeKeys.Add(resolvedKey);
                root.Add(reference, referencePatch);
                if (!VisitPatches(referencePatch, resolvedKey, stitch)) return false;
                activeKeys.Remove(resolvedKey);
              }
            }
          }
          return true;
        }
        finally
        {
          if (addedCurrent) activeKeys.Remove(patchKey);
        }
      };

      return VisitPatches(root, rootKey, stitch);
    }

    private bool ExtricatePatch(Patch root, string rootKey)
    {
      // Forward declare to allow recursion
      Func<Patch, string, bool> removeReferences = null;

      removeReferences = (patch, patchKey) =>
      {
        patch.Remove(Constants.PinnedKey);
        var token = patch[Constants.MotifKey];
        if (token != null && token.Type == JTokenType.String)
        {
          var reference = token.Value<string>();
          if (!string.IsNullOrEmpty(reference) && root.ContainsKey(reference))
          {
            var referencePatch = Patch.TryConvert(root[reference]);
            if (referencePatch != null)
            {
              root.Remove(reference);
              if (!VisitPatches(referencePatch, reference, removeReferences))
              {
                return false;
              }
            }
          }
        }

        return true;
      };

      if (root.ContainsKey(Constants.PinnedKey))
      {
        root.Remove(Constants.PinnedKey);
      }

      return VisitPatches(root, rootKey, removeReferences);
    }

    private string ResolvePatchKey(string reference, string fromPatchKey, string quilterId)
    {
      if (!KeyRules.IsValidPath(reference, _trunk.Delimiter)) return null;

      // Array elements are traversed as part of their containing patch and do not
      // have independently addressable trunk keys.
      var baseKey = fromPatchKey;
      var arrayIndex = baseKey.IndexOf('[');
      if (arrayIndex >= 0) baseKey = baseKey.Substring(0, arrayIndex);
      while (true)
      {
        var candidate = string.IsNullOrEmpty(baseKey) ? reference : baseKey + _trunk.Delimiter + reference;
        if (Patch.TryConvert(_trunk.Get(candidate, quilterId)) != null) return candidate;
        if (string.IsNullOrEmpty(baseKey)) break;
        var delimiterIndex = baseKey.LastIndexOf(_trunk.Delimiter, StringComparison.Ordinal);
        baseKey = delimiterIndex < 0 ? string.Empty : baseKey.Substring(0, delimiterIndex);
      }
      return null;
    }

    public IEnumerable<Circle> GetCircles(string id)
    {
      var map = BuildMap(_trunk.GetKeys(Constants.CirclesKey, id));
      foreach (var key in map.GetKeys())
      {
        yield return new Circle(_trunk.Get(Constants.CirclesKey + _trunk.Delimiter + key, id));
      }
    }

    public IEnumerable<Patch> GetQuilters(string id)
    {
      var map = BuildMap(_trunk.GetKeys(Constants.QuiltersKey, id));
      foreach (var key in map.GetKeys())
      {
        yield return new Patch(_trunk.Get(Constants.QuiltersKey + _trunk.Delimiter + key, id));
      }
    }
  }
}
