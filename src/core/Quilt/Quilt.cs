using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Quilting
{
  public class Quilt
  {
    readonly ITrunk _trunk;
    readonly Lazy<IEnumerable<Circle>> _circles;
    readonly Lazy<IEnumerable<Patch>> _quilters;

    public IEnumerable<Circle> Circles
    {
      get
      {
        return _circles.Value;
      }
    }

    public IEnumerable<Patch> Quilters
    {
      get
      {
        return _quilters.Value;
      }
    }

    public Quilt(ITrunk trunk)
    {
      _trunk = trunk;
      _circles = new Lazy<IEnumerable<Circle>>(() => this.LoadCircles());
      _quilters = new Lazy<IEnumerable<Patch>>(() => this.LoadQuilters());
    }

    public bool Create(JToken token, string id)
    {
      JsonUtility.VerifyType(token, JTokenType.Object);

      _trunk.BeginTransaction();

      if (_trunk.GetKeys().Count() > 0)
      {
        var quilter = _trunk.Retrieve(Constants.QuiltersKey + _trunk.Delimiter + id);
        if (!Patch.TryConvert(quilter)?.CircleIds.EmptyIfNull().Contains(Constants.LeadCircleId) ?? false)
        {
          return false;
        }

        _trunk.Clear();
      }

      if (!_trunk.Stow(Constants.QuiltersKey + _trunk.Delimiter + id, new JObject
      {
        [Patch.CircleIdsKey] = JArray.FromObject(new[] { Constants.LeadCircleId }),
      }))
      {
        return false;
      }
      if (!_trunk.Stow(Constants.CirclesKey + _trunk.Delimiter + Constants.LeadCircleId, new JObject
      {
        [Circle.CircleIdsKey] = JArray.FromObject(new[] { Constants.LeadCircleId }),
        [Circle.NameKey] = JValue.FromObject(Constants.LeadCircleName),
      }))
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
          var referenceToken = patch[Constants.PatchReferenceKey];
          if (referenceToken != null && referenceToken.Type == JTokenType.String)
          {
            var reference = referenceToken.Value<string>();
            if (!string.IsNullOrEmpty(reference) && !_trunk.GetKeys().Contains(reference))
            {
              var newPatch = Patch.TryConvert(token.SelectToken(reference));
              if (newPatch != null)
              {
                if (!_trunk.Stow(reference, newPatch)
                  || !_trunk.Stow(patchKey, patch)
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

      _trunk.CommitTransaction();
      return true;
    }

    public Map GetMap(string prefix)
    {
      var keys = _trunk.GetKeys(prefix);
      var map = new Map();
      foreach (var segments in keys.Select(x => x.Split(_trunk.Delimiter)))
      {
        map.Insert(segments);
      }
      return map;
    }

    public Patch GetPatch(string key, string quilterId)
    {
      var quilter = GetQuilter(quilterId);
      if (quilter == null)
      {
        return null;
      }

      var patch = Patch.TryConvert(_trunk.Retrieve(key));
      if (patch != null && !SewPatch(patch, key, quilter))
      {
        return null;
      }

      return patch;
    }

    public bool CreatePatch(Patch patch, string key, string quilterId)
    {
      if (!CanQuilterCreatePatch(patch, key, quilterId) || !ExtricatePatch(patch, key))
      {
        return false;
      }

      if (!_trunk.Stow(key, patch))
      {
        return false;
      }

      return true;
    }

    public string[] GetCircleIdsForPatch(string key)
    {
      var circleIds = FindPatchValue(key, (patch) => patch.CircleIds) ?? Array.Empty<string>();

      // Lead circle can access anything
      return circleIds.EmptyIfNull().Union(new string[]
      {
        Constants.LeadCircleId,
      }).ToArray();
    }

    public bool CreateCircle(Circle circle, string circleId, string quilterId)
    {
      return CreatePatch(circle, Constants.CirclesKey + _trunk.Delimiter + circleId, quilterId);
    }

    public bool CreateQuilter(Patch newQuilter, string newQuilterId, string quilterId)
    {
      return CreatePatch(newQuilter, Constants.QuiltersKey + _trunk.Delimiter + newQuilterId, quilterId);
    }

    private Patch GetQuilter(string quilterId)
    {
      return Patch.TryConvert(_trunk.Retrieve(Constants.QuiltersKey + _trunk.Delimiter + quilterId));
    }

    private T FindPatchValue<T>(string key, Func<Patch, T> selector)
    {
      if (string.IsNullOrEmpty(key))
      {
        return default(T);
      }

      var patch = Patch.TryConvert(_trunk.Retrieve(key));
      if (patch == null)
      {
        return default(T);
      }

      var value = selector(patch);
      var segments = key.Split(_trunk.Delimiter);
      return value != null
        ? value
        : FindPatchValue(string.Join(_trunk.Delimiter, segments.Take(segments.Length - 1)), selector);
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

    private bool SewPatch(Patch root, string rootKey, Patch quilter)
    {
      // Forward declare to allow recursion
      Func<Patch, string, bool> stitch = null;

      stitch = (patch, patchKey) =>
      {
        var token = patch[Constants.PatchReferenceKey];
        if (token != null && token.Type == JTokenType.String)
        {
          var reference = token.Value<string>();
          if (!string.IsNullOrEmpty(reference) && !root.ContainsKey(reference))
          {
            var referencePatch = ResolvePatch(reference);
            if (referencePatch != null)
            {
              PinPatch(referencePatch, reference, quilter.CircleIds);
              root.Add(reference, referencePatch);
              if (!VisitPatches(referencePatch, reference, stitch))
              {
                return false;
              }
            }
          }
        }

        return true;
      };

      PinPatch(root, rootKey, quilter.CircleIds);
      return VisitPatches(root, rootKey, stitch);
    }

    private bool ExtricatePatch(Patch root, string rootKey)
    {
      // Forward declare to allow recursion
      Func<Patch, string, bool> removeReferences = null;

      removeReferences = (patch, patchKey) =>
      {
        var token = patch[Constants.PatchReferenceKey];
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

    private Patch ResolvePatch(string reference)
    {
      var referencePatch = Patch.TryConvert(_trunk.Retrieve(reference));
      if (referencePatch == null && !reference.StartsWith(Constants.ContentKey))
      {
        referencePatch = Patch.TryConvert(_trunk.Retrieve(Constants.ContentKey + _trunk.Delimiter + reference));
      }

      return referencePatch;
    }

    private bool CanQuilterCreatePatch(Patch patch, string key, string quilterId)
    {
      // If user is invalid
      var quilter = GetQuilter(quilterId);
      if (quilter == null)
      {
        return false;
      }

      // If user is admin
      var quilterCircleIds = quilter.CircleIds.EmptyIfNull();
      if (quilterCircleIds.Contains(Constants.LeadCircleId))
      {
        return true;
      }

      // If patch doesn't exist or has no access restrictions
      var canonicalPatch = Patch.TryConvert(_trunk.Retrieve(key));
      if (canonicalPatch == null)
      {
        return true;
      }

      // If patch exists and user has no circle IDs in common with it
      if (!GetCircleIdsForPatch(key).Intersect(quilterCircleIds).Any())
      {
        return false;
      }

      // Quilter must have access to any added/removed circle IDs
      return patch.CircleIds
        .EmptyIfNull()
        .ExclusiveUnion(canonicalPatch.CircleIds.EmptyIfNull())
        .All(x => quilterCircleIds.Contains(x));
    }

    private void PinPatch(Patch patch, string key, string[] quilterCircleIds)
    {
      var circleIds = GetCircleIdsForPatch(key);
      var quilterNotInCircles = !quilterCircleIds
        .EmptyIfNull()
        .Intersect(circleIds.EmptyIfNull().Union(patch.CircleIds.EmptyIfNull()))
        .Any();
      if (quilterNotInCircles)
      {
        patch[Constants.PinnedKey] = quilterNotInCircles;
      }
      else if (patch.ContainsKey(Constants.PinnedKey))
      {
        patch.Remove(Constants.PinnedKey);
      }
    }

    private IEnumerable<Circle> LoadCircles()
    {
      var map = GetMap(Constants.CirclesKey);
      foreach (var key in map.GetKeys())
      {
        yield return new Circle(_trunk.Retrieve(Constants.CirclesKey + _trunk.Delimiter + key));
      }
    }

    private IEnumerable<Patch> LoadQuilters()
    {
      var map = GetMap(Constants.QuiltersKey);
      foreach (var key in map.GetKeys())
      {
        yield return new Patch(_trunk.Retrieve(Constants.QuiltersKey + _trunk.Delimiter + key));
      }
    }
  }
}
