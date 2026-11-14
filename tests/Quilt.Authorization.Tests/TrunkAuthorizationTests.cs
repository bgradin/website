using Gradinware.Data;
using Newtonsoft.Json.Linq;
using Quilting;
using Xunit;

namespace Quilt.Authorization.Tests;

public sealed class TrunkAuthorizationTests : IDisposable
{
  private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "quilt-auth-" + Guid.NewGuid().ToString("N"));
  private readonly string? _oldDataDirectory = Environment.GetEnvironmentVariable("DATA_DIRECTORY");
  private readonly ContentTrunk _trunk;

  public TrunkAuthorizationTests()
  {
    Environment.SetEnvironmentVariable("DATA_DIRECTORY", _dataDirectory);
    _trunk = new ContentTrunk("content.db");
    _trunk.EnsureCreated();
    Seed();
  }

  [Fact]
  public void Get_allows_members_and_hides_patch_from_other_quilters()
  {
    Assert.NotNull(_trunk.Get("q:content.secret", "engineering"));
    Assert.Null(_trunk.Get("q:content.secret", "outsider"));
    Assert.Null(_trunk.Get("q:content.secret", "unknown"));
    Assert.NotNull(_trunk.Get("q:content.secret", "admin"));
  }

  [Fact]
  public void Trunk_rejects_path_segments_that_change_hierarchy_or_array_addressing()
  {
    Assert.False(_trunk.Put("q:content.bad[0]", new JObject { ["text"] = "ambiguous" }, "admin"));
    Assert.False(_trunk.Put("q:content.", new JObject { ["text"] = "empty segment" }, "admin"));
    Assert.False(_trunk.Put("q:content..bad", new JObject { ["text"] = "empty segment" }, "admin"));
    Assert.False(_trunk.CanPut("q:content.bad[0]", "admin"));
    Assert.Null(_trunk.Get("q:content.bad[0]", "admin"));
  }

  [Fact]
  public void Quilt_rejects_invalid_keys_and_ids_before_writing()
  {
    var quilt = new Quilting.Quilt(_trunk);
    var patch = new Patch(new JObject { ["text"] = "value" });

    Assert.False(quilt.CreatePatch(patch, "q:content..bad", "admin"));
    Assert.False(quilt.CreateCircle(new Circle(new JObject { ["q:name"] = "Circle" }), "bad[id]", "admin"));
    Assert.False(quilt.CreateQuilter(patch, "bad.id", "admin"));
    Assert.False(quilt.Create(new JObject(), "bad.id"));
  }

  [Fact]
  public void Put_requires_write_membership_even_when_patch_is_readable()
  {
    var replacement = Protected("engineering", "publisher");
    replacement["text"] = "updated";

    Assert.False(_trunk.Put("q:content.readonly", replacement, "engineering"));
    Assert.True(_trunk.Put("q:content.readonly", replacement, "publisher"));
  }

  [Fact]
  public void Put_requires_a_readable_parent_and_write_access_through_the_parent()
  {
    Assert.False(_trunk.Put("q:orphan", new JObject { ["text"] = "root-level" }, "engineering"));

    Assert.True(_trunk.Put("q:content.read-only-parent", Protected("publisher", "publisher"), "admin"));
    Assert.False(_trunk.Put("q:content.read-only-parent.child", new JObject { ["text"] = "child" }, "engineering"));
    Assert.True(_trunk.Put("q:content.read-only-parent.child", new JObject { ["text"] = "child" }, "publisher"));

    Assert.True(_trunk.Put("q:content.write-restricted-parent", Protected("engineering", "publisher"), "admin"));
    Assert.False(_trunk.Put("q:content.write-restricted-parent.child", new JObject { ["text"] = "child" }, "engineering"));
    Assert.True(_trunk.Put("q:content.write-restricted-parent.child", new JObject { ["text"] = "child" }, "publisher"));
  }

  [Fact]
  public void Put_rejects_collisions_with_descendants_but_not_shared_key_prefixes()
  {
    Assert.True(_trunk.Put("q:content.secret.child", new JObject { ["text"] = "child" }, "engineering"));
    Assert.False(_trunk.Put("q:content.secret", new JObject
    {
      ["child"] = new JObject { ["text"] = "overlap" },
    }, "engineering"));

    Assert.True(_trunk.Put("q:content.secretish", new JObject { ["text"] = "separate patch" }, "admin"));
    var nonCollidingReplacement = Protected("engineering", "engineering");
    nonCollidingReplacement["text"] = "no overlapping path";
    Assert.True(_trunk.Put("q:content.secret", nonCollidingReplacement, "engineering"));
  }

  [Fact]
  public void Key_enumeration_filters_hidden_patches()
  {
    Assert.Contains("q:content.secret", _trunk.GetKeys("q:content", "engineering"));
    Assert.DoesNotContain("q:content.secret", _trunk.GetKeys("q:content", "outsider"));
  }

  [Fact]
  public void Put_ignores_supplied_pinned_and_get_derives_it()
  {
    var supplied = new JObject
    {
      ["q:access"] = new JObject
      {
        ["get"] = new JArray("engineering"),
        ["put"] = new JArray("publisher"),
      },
      ["q:pinned"] = false,
      ["text"] = "private",
    };

    Assert.True(_trunk.Put("q:content.pinned", supplied, "admin"));
    Assert.True((bool)_trunk.Get("q:content.pinned", "engineering")["q:pinned"]!);
    Assert.False((bool)_trunk.Get("q:content.pinned", "publisher")["q:pinned"]!);
  }

  [Fact]
  public void Delete_requires_write_access_and_removes_descendants_atomically()
  {
    Assert.False(_trunk.Delete("q:content.secret", "outsider"));
    Assert.True(_trunk.Put("q:content.secret.child", new JObject { ["text"] = "child" }, "engineering"));
    Assert.True(_trunk.Delete("q:content.secret", "engineering"));
    Assert.Null(_trunk.Get("q:content.secret", "engineering"));
  }

  [Fact]
  public void Seam_does_not_disclose_an_inaccessible_target()
  {
    Assert.True(_trunk.Put("q:content.seamed", new JObject { ["q:motif"] = "secret" }, "admin"));

    var patch = new Quilting.Quilt(_trunk).GetPatch("q:content.seamed", "outsider");

    Assert.NotNull(patch);
    Assert.Null(patch["q:content.secret"]);
    Assert.Null(_trunk.Get("q:content.secret", "outsider"));
  }

  [Fact]
  public void Motif_resolution_searches_from_the_patch_out_through_its_ancestors()
  {
    Assert.True(_trunk.Put("q:content.branch.shared", new JObject { ["source"] = "ancestor" }, "admin"));
    Assert.True(_trunk.Put("q:content.branch.page", new JObject { ["q:motif"] = "shared" }, "admin"));

    var patch = new Quilting.Quilt(_trunk).GetPatch("q:content.branch.page", "admin");

    Assert.Equal("ancestor", (string?)patch?["shared"]?["source"]);
  }

  [Fact]
  public void Motif_resolution_prefers_the_nearest_match_and_leaves_missing_references_as_strings()
  {
    Assert.True(_trunk.Put("q:content.branch.shared", new JObject { ["source"] = "near" }, "admin"));
    Assert.True(_trunk.Put("q:content.shared", new JObject { ["source"] = "far" }, "admin"));
    Assert.True(_trunk.Put("q:content.branch.page", new JObject
    {
      ["q:motif"] = "shared",
      ["missing"] = new JObject { ["q:motif"] = "absent" },
    }, "admin"));

    var patch = new Quilting.Quilt(_trunk).GetPatch("q:content.branch.page", "admin");

    Assert.Equal("near", (string?)patch?["shared"]?["source"]);
    Assert.Equal("absent", (string?)patch?["missing"]?["q:motif"]);
  }

  [Fact]
  public void Motif_cycles_fail_the_read()
  {
    Assert.True(_trunk.Put("q:content.shared", new JObject { ["q:motif"] = "page" }, "admin"));
    Assert.True(_trunk.Put("q:content.page", new JObject { ["q:motif"] = "shared" }, "admin"));

    Assert.Null(new Quilting.Quilt(_trunk).GetPatch("q:content.page", "admin"));
  }

  [Fact]
  public void Clear_requires_lead_membership()
  {
    Assert.Throws<UnauthorizedAccessException>(() => _trunk.Clear("engineering"));
    Assert.NotNull(_trunk.Get("q:content.secret", "engineering"));
  }

  [Fact]
  public void Malformed_access_fails_closed()
  {
    Assert.True(_trunk.Put("q:content.malformed", new JObject
    {
      ["q:access"] = "engineering",
      ["text"] = "hidden",
    }, "admin"));
    Assert.Null(_trunk.Get("q:content.malformed", "engineering"));
    Assert.False(_trunk.Put("q:content.malformed", new JObject { ["text"] = "attempt" }, "engineering"));
  }

  [Fact]
  public void Child_permissions_inherit_and_cannot_expand_parent_access()
  {
    Assert.True(_trunk.Put("q:content.branch", new JObject
    {
      ["q:access"] = new JObject
      {
        ["get"] = new JArray("engineering", "publisher"),
        ["put"] = new JArray("engineering", "publisher"),
      },
    }, "admin"));
    Assert.True(_trunk.Put("q:content.branch.narrow", new JObject
    {
      ["q:access"] = new JObject
      {
        ["get"] = new JArray("engineering"),
        ["put"] = new JArray("engineering"),
      },
      ["text"] = "narrow",
    }, "admin"));

    Assert.NotNull(_trunk.Get("q:content.branch.narrow", "engineering"));
    Assert.Null(_trunk.Get("q:content.branch.narrow", "outsider"));
    Assert.False(_trunk.Put("q:content.branch.expanded", new JObject
    {
      ["q:access"] = new JObject
      {
        ["get"] = new JArray("everyone"),
        ["put"] = new JArray("engineering"),
      },
    }, "engineering"));
  }

  private void Seed()
  {
    Assert.True(_trunk.Put("q:quilters.admin", Quilter("lead"), "admin"));
    Assert.True(_trunk.Put("q:circles.lead", new JObject { ["q:name"] = "Lead" }, "admin"));
    Assert.True(_trunk.Put("q:quilters.engineering", Quilter("engineering"), "admin"));
    Assert.True(_trunk.Put("q:quilters.outsider", Quilter(), "admin"));
    Assert.True(_trunk.Put("q:quilters.publisher", Quilter("publisher"), "admin"));
    Assert.True(_trunk.Put("q:content", new JObject { ["text"] = "root" }, "admin"));
    Assert.True(_trunk.Put("q:content.secret", Protected("engineering", "engineering"), "admin"));
    Assert.True(_trunk.Put("q:content.readonly", Protected("engineering", "publisher"), "admin"));
  }

  private static JObject Protected(string reader, string writer) => new()
  {
    ["q:access"] = new JObject
    {
      ["get"] = new JArray(reader),
      ["put"] = new JArray(writer),
    },
    ["text"] = "protected",
  };

  private static JObject Quilter(params string[] circles) => new()
  {
    ["q:circleIds"] = new JArray(circles),
  };

  public void Dispose()
  {
    _trunk.Dispose();
    Environment.SetEnvironmentVariable("DATA_DIRECTORY", _oldDataDirectory);
    if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
  }
}
