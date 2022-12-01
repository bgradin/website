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

      if (_trunk.GetKeys().Count() > 0)
      {
        var quilter = _trunk.Retrieve(Constants.QuiltersKey + Constants.KeyDelimiter + id);
        if (!Patch.CanConvert(quilter) || !new Patch(quilter).CircleIds.EmptyIfNull().Contains(Constants.LeadCircleId))
        {
          return false;
        }

        _trunk.Clear();
      }

      _trunk.Stow(Constants.QuiltersKey + Constants.KeyDelimiter + id, new JObject
      {
        [Patch.CircleIdsKey] = JArray.FromObject(new[] { Constants.LeadCircleId }),
      });
      _trunk.Stow(Constants.CirclesKey + Constants.KeyDelimiter + Constants.LeadCircleId, new JObject
      {
        [Circle.CircleIdsKey] = JArray.FromObject(new[] { Constants.LeadCircleId }),
        [Circle.NameKey] = JValue.FromObject(Constants.LeadCircleName),
      });

      var circlesRoot = token.SelectToken(Constants.CirclesKey);
      if (circlesRoot != null)
      {
        JsonUtility.VerifyType(circlesRoot, JTokenType.Object);
        foreach (var circleToken in (circlesRoot as JObject))
        {
          if (Patch.CanConvert(circleToken.Value))
          {
            CreateCircle(new Circle(circleToken.Value), circleToken.Key, id);
          }
        }
      }

      var quiltersRoot = token.SelectToken(Constants.QuiltersKey);
      if (quiltersRoot != null)
      {
        JsonUtility.VerifyType(quiltersRoot, JTokenType.Object);
        foreach (var quilterToken in (quiltersRoot as JObject))
        {
          if (Patch.CanConvert(quilterToken.Value))
          {
            CreateQuilter(new Patch(quilterToken.Value), quilterToken.Key, id);
          }
        }
      }

      var contentRoot = token.SelectToken(Constants.ContentKey);
      if (Patch.CanConvert(contentRoot))
      {
        // Forward declare to allow recursion
        Action<Patch, string> storeReferences = null;

        storeReferences = (patch, patchKey) =>
        {
          var referenceToken = patch.GetValueOrDefault(Constants.PatchReferenceKey);
          if (referenceToken != null && referenceToken.Type == JTokenType.String)
          {
            var reference = referenceToken.Value<string>();
            if (!string.IsNullOrEmpty(reference) && !_trunk.GetKeys().Contains(reference))
            {
              JToken newToken = token;
              foreach (var segment in reference.Split(Constants.KeyDelimiter))
              {
                var selectedToken = newToken[segment];
                if (selectedToken == null)
                {
                  newToken = new JObject();
                  break;
                }

                newToken = selectedToken;
              }

              if (Patch.CanConvert(newToken))
              {
                _trunk.Stow(reference, newToken);
                _trunk.Stow(patchKey, patch.ToJson());
                VisitPatches(new Patch(newToken), reference, storeReferences);
              }
            }
          }
        };

        VisitPatches(new Patch(contentRoot), Constants.ContentKey, storeReferences);
      }

      return true;
    }

    public Map GetMap(string prefix)
    {
      var keys = _trunk.GetKeys(prefix);
      var map = new Map();
      foreach (var segments in keys.Select(x => x.Split(Constants.KeyDelimiter)))
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

      var token = _trunk.Retrieve(key);
      if (!Patch.CanConvert(token))
      {
        return null;
      }

      var patch = new Patch(token);

      SewPatch(patch, key, quilter);

      return patch;
    }

    public bool CreatePatch(Patch patch, string key, string quilterId)
    {
      var quilter = GetQuilter(quilterId);
      if (quilter == null || !GetCircleIdsForPatch(key).Intersect(quilter.CircleIds.EmptyIfNull()).Any())
      {
        return false;
      }

      if (!IsMemberOfCirclesExclusion(patch, key, quilter.CircleIds))
      {
        return false;
      }

      ExtricatePatch(patch, key);

      _trunk.Stow(key, patch.ToJson());
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
      return CreatePatch(circle, Constants.CirclesKey + Constants.KeyDelimiter + circleId, quilterId);
    }

    public bool CreateQuilter(Patch newQuilter, string newQuilterId, string quilterId)
    {
      return CreatePatch(newQuilter, Constants.QuiltersKey + Constants.KeyDelimiter + newQuilterId, quilterId);
    }

    private Patch GetQuilter(string quilterId)
    {
      var token = _trunk.Retrieve(Constants.QuiltersKey + Constants.KeyDelimiter + quilterId);
      if (!Patch.CanConvert(token))
      {
        return null;
      }

      return new Patch(token);
    }

    private T FindPatchValue<T>(string key, Func<Patch, T> selector)
    {
      if (string.IsNullOrEmpty(key))
      {
        return default(T);
      }

      var token = _trunk.Retrieve(key);
      if (!Patch.CanConvert(token))
      {
        return default(T);
      }

      var patch = new Patch(token);
      var value = selector(patch);

      var segments = key.Split(Constants.KeyDelimiter);
      return value != null
        ? value
        : FindPatchValue(string.Join(Constants.KeyDelimiter, segments.Take(segments.Length - 1)), selector);
    }

    private void VisitPatches(Patch patch, string patchKey, Action<Patch, string> visit)
    {
      visit(patch, patchKey);

      var keys = patch.Keys.ToArray();
      for (int i = 0; i < keys.Length; i++)
      {
        var token = patch[keys[i]];
        if (Patch.CanConvert(token))
        {
          VisitPatches(new Patch(token), patchKey + Constants.KeyDelimiter + keys[i], visit);
        }
        else if (token.Type == JTokenType.Array)
        {
          var arr = token as JArray;
          for (int j = 0; j < arr.Count; j++)
          {
            if (Patch.CanConvert(arr[j]))
            {
              VisitPatches(new Patch(arr[j]), patchKey + $"[{j}]", visit);
            }
          }
        }
      }
    }

    private void SewPatch(Patch root, string rootKey, Patch quilter)
    {
      // Forward declare to allow recursion
      Action<Patch, string> stitch = null;

      stitch = (patch, patchKey) =>
      {
        var token = patch.GetValueOrDefault(Constants.PatchReferenceKey);
        if (token != null && token.Type == JTokenType.String)
        {
          var reference = token.Value<string>();
          if (!string.IsNullOrEmpty(reference) && !root.ContainsKey(reference))
          {
            var referenceToken = _trunk.Retrieve(reference);
            if (Patch.CanConvert(referenceToken))
            {
              var referencePatch = new Patch(referenceToken);
              PinPatch(referencePatch, reference, quilter.CircleIds);
              root.Add(reference, referencePatch.ToJson());
              VisitPatches(referencePatch, reference, stitch);
            }
          }
        }
      };

      PinPatch(root, rootKey, quilter.CircleIds);
      VisitPatches(root, rootKey, stitch);
    }

    private void ExtricatePatch(Patch root, string rootKey)
    {
      // Forward declare to allow recursion
      Action<Patch, string> removeReferences = null;

      removeReferences = (patch, patchKey) =>
      {
        var token = patch.GetValueOrDefault(Constants.PatchReferenceKey);
        if (token != null && token.Type == JTokenType.String)
        {
          var reference = token.Value<string>();
          if (!string.IsNullOrEmpty(reference) && root.ContainsKey(reference))
          {
            var referenceToken = root.GetValueOrDefault(reference);
            if (Patch.CanConvert(referenceToken))
            {
              var referencePatch = new Patch(referenceToken);
              root.Remove(reference);
              VisitPatches(referencePatch, reference, removeReferences);
            }
          }
        }
      };

      if (root.ContainsKey(Constants.PinnedKey))
      {
        root.Remove(Constants.PinnedKey);
      }

      VisitPatches(root, rootKey, removeReferences);
    }

    private bool IsMemberOfCirclesExclusion(Patch patch, string key, string[] quilterCircleIds)
    {
      if (quilterCircleIds.EmptyIfNull().Contains(Constants.LeadCircleId))
      {
        return true;
      }

      var token = _trunk.Retrieve(key);
      if (!Patch.CanConvert(token))
      {
        return false;
      }

      var canonicalPatch = new Patch(token);
      return patch.CircleIds
        .EmptyIfNull()
        .Exclusion(canonicalPatch.CircleIds.EmptyIfNull())
        .All(x => quilterCircleIds.EmptyIfNull().Contains(x));
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
        patch.SetValue(Constants.PinnedKey, quilterNotInCircles);
      }
      else if (patch.ContainsKey(Constants.PinnedKey))
      {
        patch.Remove(Constants.PinnedKey);
      }
    }

    private IEnumerable<Circle> LoadCircles()
    {
      var map = GetMap(Constants.CirclesKey);
      foreach (var key in map.Keys)
      {
        yield return new Circle(_trunk.Retrieve(Constants.CirclesKey + Constants.KeyDelimiter + key));
      }
    }

    private IEnumerable<Patch> LoadQuilters()
    {
      var map = GetMap(Constants.QuiltersKey);
      foreach (var key in map.Keys)
      {
        yield return new Patch(_trunk.Retrieve(Constants.QuiltersKey + Constants.KeyDelimiter + key));
      }
    }
  }
}
