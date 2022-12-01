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
        if (!Patch.CanConvert(quilter) || !new Patch(quilter).CircleIds.Contains(Constants.LeadCircleId))
        {
          return false;
        }

        _trunk.Clear();
      }

      _trunk.Stow(Constants.QuiltersKey + Constants.KeyDelimiter + id, new JObject
      {
        [Patch.CircleIdsKey] = JArray.FromObject(new[] { Constants.LeadCircleId }),
      });
      _trunk.Stow(Constants.CirclesKey + Constants.KeyDelimiter + id, new JObject
      {
        [Circle.CircleIdsKey] = JArray.FromObject(new[] { Constants.LeadCircleId })
        [Circle.NameKey] = Constants.LeadCircleName,
      });

      var circlesRoot = token.SelectToken(Constants.CirclesKey);
      JsonUtility.VerifyType(circlesRoot, JTokenType.Object);
      foreach (var circleToken in (circlesRoot as JObject))
      {
        if (Patch.CanConvert(circleToken.Value))
        {
          CreateCircle(new Circle(circleToken.Value), circleToken.Key, id);
        }
      }

      var quiltersRoot = token.SelectToken(Constants.QuiltersKey);
      JsonUtility.VerifyType(quiltersRoot, JTokenType.Object);
      foreach (var quilterToken in (quiltersRoot as JObject))
      {
        if (Patch.CanConvert(quilterToken.Value))
        {
          CreateQuilter(new Patch(quilterToken.Value), quilterToken.Key, id);
        }
      }

      var contentRoot = token.SelectToken(Constants.ContentKey);
      if (Patch.CanConvert(contentRoot))
      {
        // Forward declare to allow recursion
        Action<Patch> storeReferences = null;

        storeReferences = (patch) =>
        {
          var referenceToken = patch[Constants.PatchReferenceKey];
          if (referenceToken != null && referenceToken.Type == JTokenType.String)
          {
            var reference = referenceToken.Value<string>();
            if (!string.IsNullOrEmpty(reference) && !_trunk.GetKeys().Contains(reference))
            {
              var newToken = token;
              foreach (var segment in reference.Split(Constants.KeyDelimiter))
              {
                var selectedToken = newToken[segment];
                if (selectedToken == null)
                {
                  break;
                }

                newToken = selectedToken;
              }

              _trunk.Stow(reference, newToken);
            }
          }
        };

        VisitPatches(new Patch(contentRoot), storeReferences);
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
      if (quilter == null || !GetCircleIdsForPatch(key).Intersect(quilter.CircleIds).Any())
      {
        return false;
      }

      if (!IsMemberOfCirclesExclusion(patch, key, quilter.CircleIds))
      {
        return false;
      }

      ExtricatePatch(patch);

      _trunk.Stow(key, patch.ToJson());
      return true;
    }

    public string[] GetCircleIdsForPatch(string key)
    {
      var circleIds = FindPatchValue(key, (patch) => patch.CircleIds) ?? Array.Empty<string>();

      // Lead circle can access anything
      return circleIds.Union(new string[]
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

    private void VisitPatches(Patch patch, Action<Patch> visit)
    {
      visit(patch);

      foreach (var key in patch.Keys)
      {
        var token = patch[key];
        if (Patch.CanConvert(token))
        {
          VisitPatches(new Patch(token), visit);
        }
        else if (token.Type == JTokenType.Array)
        {
          foreach (var element in (JArray) token)
          {
            if (Patch.CanConvert(element))
            {
              VisitPatches(new Patch(element), visit);
            }
          }
        }
      }
    }

    private void SewPatch(Patch root, string rootKey, Patch quilter)
    {
      // Forward declare to allow recursion
      Action<Patch> stitch = null;

      stitch = (patch) =>
      {
        var token = patch[Constants.PatchReferenceKey];
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
              root[reference] = referencePatch.ToJson();
              VisitPatches(referencePatch, stitch);
            }
          }
        }
      };

      PinPatch(root, rootKey, quilter.CircleIds);
      VisitPatches(root, stitch);
    }

    private void ExtricatePatch(Patch root)
    {
      // Forward declare to allow recursion
      Action<Patch> removeReferences = null;

      removeReferences = (patch) =>
      {
        var token = patch[Constants.PatchReferenceKey];
        if (token != null && token.Type == JTokenType.String)
        {
          var reference = token.Value<string>();
          if (!string.IsNullOrEmpty(reference) && root.ContainsKey(reference))
          {
            var referenceToken = root[reference];
            if (Patch.CanConvert(referenceToken))
            {
              var referencePatch = new Patch(referenceToken);
              root[reference] = null;
              VisitPatches(referencePatch, removeReferences);
            }
          }
        }
      };

      root[Constants.PinnedKey] = null;
      VisitPatches(root, removeReferences);
    }

    private bool IsMemberOfCirclesExclusion(Patch patch, string key, string[] quilterCircleIds)
    {
      if (quilterCircleIds.Contains(Constants.LeadCircleId))
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
        .All(quilterCircleIds.Contains);
    }

    private void PinPatch(Patch patch, string key, string[] quilterCircleIds)
    {
      var circleIds = GetCircleIdsForPatch(key);
      var quilterNotInCircles = !quilterCircleIds.Intersect(circleIds.Union(patch.CircleIds.EmptyIfNull())).Any();
      patch[Constants.PinnedKey] = quilterNotInCircles;
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
