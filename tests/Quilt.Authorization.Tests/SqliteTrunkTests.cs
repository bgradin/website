using System.Text.Json.Nodes;
using Gradinware.Data;
using Microsoft.Data.Sqlite;
using Quilting;
using Xunit;

namespace Quilt.Authorization.Tests;

public sealed class SqliteTrunkTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "sqlite-trunk-" + Guid.NewGuid().ToString("N"));
    private SqliteTrunk Open() => new("tree.db", directory);
    private static JsonNode? Json(string json) => JsonNode.Parse(json);
    private static void Equal(string expected, JsonNode? actual) => Assert.True(JsonNode.DeepEquals(Json(expected), actual), actual?.ToJsonString());
    private SqliteConnection Inspect()
    {
        var c = new SqliteConnection($"Data Source={Path.Combine(directory, "sqlite", "tree.db")};Pooling=False");
        c.Open(); return c;
    }
    private Dictionary<long, string> Rows()
    {
        using var c = Inspect(); using var command = c.CreateCommand();
        command.CommandText = "SELECT Id, COALESCE(Name,'') || ':' || COALESCE(json(Value),'container') FROM Nodes";
        using var reader = command.ExecuteReader(); var rows = new Dictionary<long, string>();
        while (reader.Read()) rows.Add(reader.GetInt64(0), reader.GetString(1));
        return rows;
    }
    private void Valid()
    {
        using var c = Inspect(); using var command = c.CreateCommand();
        command.CommandText = "PRAGMA integrity_check"; Assert.Equal("ok", command.ExecuteScalar());
        command.CommandText = "PRAGMA foreign_key_check"; Assert.Null(command.ExecuteScalar());
        command.CommandText = "SELECT COUNT(*) FROM (SELECT ParentId FROM Nodes WHERE Position IS NOT NULL GROUP BY ParentId HAVING MIN(Position)<>0 OR MAX(Position)<>COUNT(*)-1)";
        Assert.Equal(0L, command.ExecuteScalar());
    }
    [Fact]
    public void PointerTypesEscapesAndPersistence()
    {
        using (var trunk = Open())
        {
            Assert.True(trunk.Put("", Json("""{"":{"a/b":{"m~n": [null,true,false,1.234567890123456789, "日本語"]}},"~1":7,"A":8,"a":9}"""), trunk.TreeId));
            Equal("7", trunk.Get("/~01", trunk.TreeId));
            Equal("8", trunk.Get("/A", trunk.TreeId)); Equal("9", trunk.Get("/a", trunk.TreeId));
            Equal("\"日本語\"", trunk.Get("//a~1b/m~0n/4", trunk.TreeId));
            Equal("1.234567890123456789", trunk.Get("//a~1b/m~0n/3", trunk.TreeId));
            Assert.Null(trunk.Get("//a~1b/m~0n/0", trunk.TreeId));
            foreach (var index in new[] { "01", "-1", "-", "5", "999999999999999999999999", "+1" })
                Assert.Null(trunk.Get("//a~1b/m~0n/" + index, trunk.TreeId));
            foreach (var pointer in new[] { "x", "/~", "/~2", "#/a" })
                Assert.Throws<ArgumentException>(() => trunk.Get(pointer, trunk.TreeId));
            Assert.Contains("//a~1b/m~0n/4", trunk.GetKeys(trunk.TreeId));
            Assert.Throws<ArgumentException>(() => trunk.Get("", "other"));
        }
        using var reopened = Open(); Equal("7", reopened.Get("/~01", reopened.TreeId)); Valid();
    }
    [Fact]
    public void ReplacementPreservesIdAndRemovesDescendants()
    {
        using var trunk = Open(); var id = trunk.TreeId;
        trunk.Put("", Json("""{"value":{"child":[1,2]},"array":[]}"""), id);
        var before = Rows(); var nodeId = before.Single(x => x.Value.StartsWith("value:")).Key;
        foreach (var json in new[] { "12", "null", "true", "\"text\"", "[3,4]", "{\"new\":5}" })
        {
            Assert.True(trunk.Put("/value", Json(json), id)); Equal(json, trunk.Get("/value", id));
            Assert.Contains(nodeId, Rows().Keys);
        }
        Assert.False(trunk.Put("/missing/child", Json("1"), id));
        Assert.False(trunk.Put("/value/new/child", Json("1"), id));
        Assert.True(trunk.Put("/array/-", Json("1"), id));
        Assert.True(trunk.Put("/array/1", Json("2"), id));
        Assert.True(trunk.Put("/array/0", Json("3"), id));
        Assert.False(trunk.Put("/array/3", Json("4"), id));
        Equal("[3,2]", trunk.Get("/array", id));
        Assert.True(trunk.Delete("/array/0", id)); Equal("[2]", trunk.Get("/array", id));
        Assert.False(trunk.Delete("", id)); Assert.False(trunk.Delete("/absent", id));
        Valid();
    }
    [Fact]
    public void MovePreservesSubtreeIdsAndReindexesArrays()
    {
        using var trunk = Open(); var id = trunk.TreeId;
        trunk.Put("", Json("""{"a":[{"child":1},2,3],"b":[],"target":0}"""), id);
        var ids = Rows().Keys.Order().ToArray();
        Assert.True(trunk.Move(id, "/a/0", "/b/-"));
        Assert.Equal(ids, Rows().Keys.Order().ToArray());
        Equal("[2,3]", trunk.Get("/a", id)); Equal("[{\"child\":1}]", trunk.Get("/b", id));
        Assert.True(trunk.Move(id, "/a/0", "/a/1")); Equal("[3,2]", trunk.Get("/a", id));
        Assert.True(trunk.Move(id, "/b/0", "/target")); Equal("{\"child\":1}", trunk.Get("/target", id));
        Equal("[]", trunk.Get("/b", id));
        Assert.True(trunk.Move(id, "/target", "/b/0"));
        Assert.True(trunk.Move(id, "/b/0", "/b/-")); Equal("[{\"child\":1}]", trunk.Get("/b", id));
        var before = trunk.Get("", id)!.ToJsonString();
        Assert.False(trunk.Move(id, "/b", "/b/0/nested"));
        Assert.False(trunk.Move(id, "", "/a/root"));
        Assert.False(trunk.Move(id, "/a/0", "/absent/nested"));
        Equal(before, trunk.Get("", id)); Valid();
    }
    [Fact]
    public void MoveCanReplaceAncestorAndFailedMoveRollsBackInsideOuterTransaction()
    {
        using var trunk = Open(); var id = trunk.TreeId;
        trunk.Put("", Json("""{"a":{"b":{"child":1}},"items":[1,{}]}"""), id);
        Assert.True(trunk.Move(id, "/a/b", "/a")); Equal("{\"child\":1}", trunk.Get("/a", id));
        trunk.BeginTransaction();
        Assert.True(trunk.Put("/kept", Json("2"), id));
        var before = trunk.Get("", id)!.ToJsonString();
        Assert.False(trunk.Move(id, "/items/0", "/items/1/x"));
        Equal(before, trunk.Get("", id)); trunk.CommitTransaction();
        Equal("2", trunk.Get("/kept", id)); Valid();
    }
    [Fact]
    public void TransactionsDeletionAdapterAndDeepTrees()
    {
        using var trunk = Open(); var id = trunk.TreeId;
        var root = new JsonObject(); var current = root;
        for (int i = 0; i < 200; i++) { var child = new JsonObject(); current["child"] = child; current = child; }
        current["value"] = 42;
        trunk.Put("", root, id);
        Equal("42", trunk.Get(string.Concat(Enumerable.Repeat("/child", 200)) + "/value", id));
        trunk.BeginTransaction(); trunk.Put("", Json("[1,2]"), id); trunk.RollbackTransaction();
        Equal("42", trunk.Get(string.Concat(Enumerable.Repeat("/child", 200)) + "/value", id));
        ITrunk adapter = trunk;
        Assert.True(adapter.Put("/legacy", new Newtonsoft.Json.Linq.JValue("ok"), id));
        Assert.Equal("ok", adapter.Get("/legacy", id)!.ToObject<string>());
        Assert.True(trunk.Delete("/child", id)); Assert.Equal(2, Rows().Count);
        trunk.DeleteTree(id); Assert.Empty(Rows());
        Assert.True(trunk.Put("", Json("[]"), id)); Equal("[]", trunk.Get("", id)); Valid();
    }
    [Fact]
    public void StorageFailureRollsBackReplacementAndKeepsOuterTransactionUsable()
    {
        using var trunk = Open(); var id = trunk.TreeId;
        trunk.Put("", Json("{\"existing\":[1,2]}"), id);
        using (var connection = Inspect())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TRIGGER FailInsert BEFORE INSERT ON Nodes WHEN NEW.Name='fail' BEGIN SELECT RAISE(ABORT,'Injected failure'); END";
            command.ExecuteNonQuery();
        }
        trunk.BeginTransaction();
        Assert.Throws<SqliteException>(() => trunk.Put("", Json("{\"ok\":3,\"fail\":4}"), id));
        Equal("{\"existing\":[1,2]}", trunk.Get("", id));
        Assert.True(trunk.Put("/kept", Json("5"), id));
        trunk.CommitTransaction(); Valid();
    }
    [Fact]
    public void DeletedTreeStaysDeletedAndUnsupportedSchemasAreRejected()
    {
        using (var trunk = Open()) trunk.DeleteTree(trunk.TreeId);
        using (var trunk = Open())
        {
            Assert.Null(trunk.Get("", trunk.TreeId)); Assert.Empty(Rows());
            Assert.True(trunk.CanPut("", trunk.TreeId));
            trunk.Clear(trunk.TreeId); Assert.Single(Rows());
        }
        using (var connection = Inspect())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version=999"; command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidOperationException>(() => Open());
    }
    [Fact]
    public void MovesMatchArrayInsertionForEverySourceAndDestination()
    {
        using var trunk = Open(); var id = trunk.TreeId;
        for (var source = 0; source < 4; source++)
        for (var destination = 0; destination < 4; destination++)
        {
            trunk.Put("", Json("{\"items\":[0,1,2,3],\"other\":[4,5]}"), id);
            var expected = new List<int> { 0, 1, 2, 3 };
            expected.RemoveAt(source); expected.Insert(destination, source);
            Assert.True(trunk.Move(id, $"/items/{source}", $"/items/{destination}"));
            Equal(System.Text.Json.JsonSerializer.Serialize(expected), trunk.Get("/items", id));
            Assert.True(trunk.Move(id, $"/items/{destination}", "/other/1"));
            Equal($"[4,{source},5]", trunk.Get("/other", id));
            Valid();
        }
    }
    [Fact]
    public void OpeningAnUnconvertedDatabaseRejectsItWithoutChangingItsData()
    {
        Directory.CreateDirectory(Path.Combine(directory, "sqlite"));
        using (var connection = Inspect())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE Kvp(Key TEXT PRIMARY KEY,Value TEXT); INSERT INTO Kvp VALUES('existing','untouched')";
            command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidOperationException>(() => Open());
        using var reopened = Inspect(); using var check = reopened.CreateCommand();
        check.CommandText = "SELECT Value FROM Kvp WHERE Key='existing'";
        Assert.Equal("untouched", check.ExecuteScalar());
        check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('Nodes','Tree')";
        Assert.Equal(0L, check.ExecuteScalar());
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
