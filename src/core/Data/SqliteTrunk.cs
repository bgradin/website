using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
using Quilting;

namespace Gradinware.Data
{
  /// <summary>A single JSON tree backed by indexed SQLite node records. Not an authorization store.</summary>
  public sealed class SqliteTrunk : ITrunk, IDisposable
  {
    private readonly SqliteConnection connection;
    private SqliteTransaction transaction;
    public string TreeId { get; }
    public string Delimiter => "/";

    public SqliteTrunk(string databaseName, string dataDirectory = null, string treeId = null)
    {
      if (string.IsNullOrWhiteSpace(databaseName) || Path.GetFileName(databaseName) != databaseName)
        throw new ArgumentException("A database filename is required.", nameof(databaseName));
      TreeId = treeId ?? databaseName;
      if (string.IsNullOrEmpty(TreeId)) throw new ArgumentException("A tree ID is required.", nameof(treeId));
      var root = dataDirectory ?? Environment.GetEnvironmentVariable("DATA_DIRECTORY");
      var directory = Path.Combine(Path.GetFullPath(string.IsNullOrWhiteSpace(root) ? "/data" : root), "sqlite");
      Directory.CreateDirectory(directory);
      connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, databaseName), ForeignKeys = true, Pooling = false }.ToString());
      try
      {
        connection.Open();
        var version = Convert.ToInt32(Scalar("PRAGMA user_version;"));
        if (version > 1) throw new InvalidOperationException("Unsupported SQLite trunk schema version.");
        if (version == 0 && Convert.ToInt64(Scalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")) != 0)
          throw new InvalidOperationException("This database is not a supported node store. Use a converted content database.");
        Execute("PRAGMA journal_mode=WAL;");
        Mutate(() =>
        {
          Execute(@"CREATE TABLE IF NOT EXISTS Tree (Slot INTEGER PRIMARY KEY CHECK(Slot=1), Id TEXT NOT NULL UNIQUE);
CREATE TABLE IF NOT EXISTS Nodes (
 Id INTEGER PRIMARY KEY AUTOINCREMENT,
 ParentId INTEGER REFERENCES Nodes(Id), Name TEXT COLLATE BINARY, Position INTEGER,
 Type INTEGER NOT NULL CHECK(Type BETWEEN 0 AND 6), Value BLOB,
 CHECK ((Type IN (0,1) AND Value IS NULL) OR (Type NOT IN (0,1) AND Value IS NOT NULL)),
 CHECK ((ParentId IS NULL AND Name IS NULL AND Position IS NULL) OR
        (ParentId IS NOT NULL AND ((Name IS NOT NULL AND Position IS NULL) OR (Name IS NULL AND Position IS NOT NULL))))
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_Nodes_Root ON Nodes((1)) WHERE ParentId IS NULL;
CREATE UNIQUE INDEX IF NOT EXISTS IX_Nodes_Name ON Nodes(ParentId,Name) WHERE Name IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS IX_Nodes_Position ON Nodes(ParentId,Position) WHERE Position IS NOT NULL;
CREATE INDEX IF NOT EXISTS IX_Nodes_Parent ON Nodes(ParentId);
CREATE TRIGGER IF NOT EXISTS Nodes_Parent_Insert BEFORE INSERT ON Nodes WHEN NEW.ParentId IS NOT NULL BEGIN
 SELECT CASE WHEN NOT EXISTS (SELECT 1 FROM Nodes WHERE Id=NEW.ParentId AND
 ((Type=0 AND NEW.Name IS NOT NULL AND NEW.Position IS NULL) OR (Type=1 AND NEW.Name IS NULL AND NEW.Position IS NOT NULL))) THEN RAISE(ABORT,'Invalid parent') END;
END;
CREATE TRIGGER IF NOT EXISTS Nodes_Parent_Update BEFORE UPDATE OF ParentId,Name,Position ON Nodes WHEN NEW.ParentId IS NOT NULL BEGIN
 SELECT CASE WHEN NOT EXISTS (SELECT 1 FROM Nodes WHERE Id=NEW.ParentId AND
 ((Type=0 AND NEW.Name IS NOT NULL AND NEW.Position IS NULL) OR (Type=1 AND NEW.Name IS NULL AND NEW.Position IS NOT NULL))) THEN RAISE(ABORT,'Invalid parent') END;
 SELECT CASE WHEN EXISTS (WITH RECURSIVE ancestors(Id,ParentId) AS
 (SELECT Id,ParentId FROM Nodes WHERE Id=NEW.ParentId UNION ALL SELECT n.Id,n.ParentId FROM Nodes n JOIN ancestors a ON n.Id=a.ParentId)
 SELECT 1 FROM ancestors WHERE Id=NEW.Id) THEN RAISE(ABORT,'Cycle') END;
END;
CREATE TRIGGER IF NOT EXISTS Nodes_Type_Update BEFORE UPDATE OF Type ON Nodes WHEN NEW.Type<>OLD.Type BEGIN
 SELECT CASE WHEN EXISTS(SELECT 1 FROM Nodes WHERE ParentId=NEW.Id) THEN RAISE(ABORT,'Remove children before changing type') END;
END;");
          // Retire the global SSR revision metadata from earlier deployments.
          Execute(@"DROP TRIGGER IF EXISTS Revision_Insert;
DROP TRIGGER IF EXISTS Revision_Update;
DROP TRIGGER IF EXISTS Revision_Delete;
DROP TABLE IF EXISTS ContentRevision;");
          var storedId = Scalar("SELECT Id FROM Tree LIMIT 1;") as string;
          if (storedId != null && storedId != TreeId) throw new InvalidOperationException("This database belongs to another tree.");
          if (storedId == null)
          {
            Execute("INSERT INTO Tree(Slot,Id) VALUES(1,$id);", ("$id", TreeId));
            Insert(null, null, null, null);
          }
          Execute("PRAGMA user_version=1;");
          return true;
        });
      }
      catch { connection.Dispose(); throw; }
    }

    private SqliteCommand Command(string sql, params (string, object)[] args)
    {
      var command = connection.CreateCommand();
      command.Transaction = transaction;
      command.CommandText = sql;
      foreach (var (name, value) in args) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
      return command;
    }
    private int Execute(string sql, params (string, object)[] args) { using var c = Command(sql, args); return c.ExecuteNonQuery(); }
    private object Scalar(string sql, params (string, object)[] args) { using var c = Command(sql, args); return c.ExecuteScalar(); }
    private sealed record Node(long Id, long? Parent, string Name, long? Position, int Type, string Json);
    private static Node Read(SqliteDataReader r) => new Node(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt64(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetInt64(3), r.GetInt32(4), r.IsDBNull(5) ? null : r.GetString(5));
    private Node Find(string where, params (string, object)[] args)
    {
      using var c = Command("SELECT Id,ParentId,Name,Position,Type,json(Value) FROM Nodes WHERE " + where, args);
      using var r = c.ExecuteReader();
      return r.Read() ? Read(r) : null;
    }
    private Node Root() => Find("ParentId IS NULL");
    internal int? RootType => ReadSnapshot(() => Root()?.Type);
    private static string[] Tokens(string pointer)
    {
      ArgumentNullException.ThrowIfNull(pointer);
      if (pointer == "") return Array.Empty<string>();
      if (!pointer.StartsWith('/')) throw new ArgumentException("JSON Pointer must be empty or start with '/'.", nameof(pointer));
      var tokens = pointer.Substring(1).Split('/');
      foreach (var token in tokens)
        for (var i = 0; i < token.Length; i++)
          if (token[i] == '~' && (++i == token.Length || (token[i] != '0' && token[i] != '1')))
            throw new ArgumentException("Invalid JSON Pointer escape.", nameof(pointer));
      return tokens.Select(t => t.Replace("~1", "/").Replace("~0", "~")).ToArray();
    }
    private static bool Index(string token, out long index)
    {
      index = 0;
      return token.Length > 0 && (token.Length == 1 || token[0] != '0') && token.All(c => c >= '0' && c <= '9')
        && long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }
    private Node Resolve(IEnumerable<string> tokens)
    {
      var current = Root();
      foreach (var token in tokens)
      {
        if (current == null) return null;
        if (current.Type == 0) current = Find("ParentId=$p AND Name=$n", ("$p", current.Id), ("$n", token));
        else if (current.Type == 1 && Index(token, out var index)) current = Find("ParentId=$p AND Position=$i", ("$p", current.Id), ("$i", index));
        else return null;
      }
      return current;
    }
    private void CheckTree(string id) { if (id != TreeId) throw new ArgumentException("Unknown tree ID.", nameof(id)); }
    internal T ReadSnapshot<T>(Func<T> action)
    {
      if (transaction != null) return action();
      using var snapshot = connection.BeginTransaction(deferred: true);
      transaction = snapshot;
      try { var result = action(); snapshot.Commit(); return result; }
      finally { transaction = null; }
    }
    public JsonNode Get(string pointer, string treeId)
    {
      CheckTree(treeId);
      var tokens = Tokens(pointer);
      return ReadSnapshot(() =>
      {
        var node = Resolve(tokens);
        if (node == null) return null;
        var rows = new List<Node>();
        using (var c = Command(@"WITH RECURSIVE subtree(Id) AS (SELECT $id UNION ALL SELECT n.Id FROM Nodes n JOIN subtree s ON n.ParentId=s.Id)
SELECT n.Id,n.ParentId,n.Name,n.Position,n.Type,json(n.Value) FROM Nodes n JOIN subtree s ON n.Id=s.Id;", ("$id", node.Id)))
        using (var r = c.ExecuteReader()) while (r.Read()) rows.Add(Read(r));
        var values = rows.ToDictionary(n => n.Id, n => n.Type == 0 ? (JsonNode)new JsonObject() : n.Type == 1 ? new JsonArray() : JsonNode.Parse(n.Json));
        foreach (var group in rows.Where(n => n.Id != node.Id).GroupBy(n => n.Parent.Value))
        {
          if (values[group.Key] is JsonArray array)
          {
            foreach (var child in group) array.Add((JsonNode)null);
            foreach (var child in group) array[checked((int)child.Position.Value)] = values[child.Id];
          }
          else
            foreach (var child in group) ((JsonObject)values[group.Key]).Add(child.Name, values[child.Id]);
        }
        return values[node.Id];
      });
    }
    private static int Type(JsonNode value) => value == null ? 6 : value.GetValueKind() switch
    {
      System.Text.Json.JsonValueKind.Object => 0, System.Text.Json.JsonValueKind.Array => 1,
      System.Text.Json.JsonValueKind.String => 2, System.Text.Json.JsonValueKind.Number => 3,
      System.Text.Json.JsonValueKind.True => 4, System.Text.Json.JsonValueKind.False => 5, _ => 6
    };
    private long Insert(long? parent, string name, long? position, JsonNode value)
    {
      var type = Type(value);
      Execute("INSERT INTO Nodes(ParentId,Name,Position,Type,Value) VALUES($p,$n,$i,$t,jsonb($v));", ("$p", parent), ("$n", name), ("$i", position), ("$t", type), ("$v", type <= 1 ? null : value?.ToJsonString() ?? "null"));
      var id = (long)Scalar("SELECT last_insert_rowid();");
      AddChildren(id, value);
      return id;
    }
    private void AddChildren(long id, JsonNode value)
    {
      // Iterative insertion avoids imposing the CLR call-stack limit on tree depth.
      var pending = new Stack<(long, JsonNode)>();
      pending.Push((id, value));
      while (pending.Count > 0)
      {
        var (parent, container) = pending.Pop();
        IEnumerable<(string Name, long? Position, JsonNode Value)> children = container is JsonObject obj
          ? obj.Select(p => (p.Key, (long?)null, p.Value))
          : container is JsonArray array ? array.Select((v, i) => ((string)null, (long?)i, v)) : Enumerable.Empty<(string, long?, JsonNode)>();
        foreach (var child in children)
        {
          var type = Type(child.Value);
          Execute("INSERT INTO Nodes(ParentId,Name,Position,Type,Value) VALUES($p,$n,$i,$t,jsonb($v));", ("$p", parent), ("$n", child.Name), ("$i", child.Position), ("$t", type), ("$v", type <= 1 ? null : child.Value?.ToJsonString() ?? "null"));
          if (type <= 1) pending.Push(((long)Scalar("SELECT last_insert_rowid();"), child.Value));
        }
      }
    }
    private void Remove(long id, bool includeSelf)
    {
      Execute(@"WITH RECURSIVE subtree(Id) AS (SELECT Id FROM Nodes WHERE ParentId=$id UNION ALL SELECT n.Id FROM Nodes n JOIN subtree s ON n.ParentId=s.Id)
DELETE FROM Nodes WHERE Id IN (SELECT Id FROM subtree);", ("$id", id));
      if (includeSelf) Execute("DELETE FROM Nodes WHERE Id=$id", ("$id", id));
    }
    private long Count(long parent) => Convert.ToInt64(Scalar("SELECT COALESCE(MAX(Position)+1,0) FROM Nodes WHERE ParentId=$p AND Position>=0", ("$p", parent)));
    private bool Destination(string[] tokens, out Node parent, out string name, out long? position, bool insertion)
    {
      parent = tokens.Length == 0 ? null : Resolve(tokens.Take(tokens.Length - 1));
      name = null; position = null;
      if (parent == null) return false;
      if (parent.Type == 0) { name = tokens.Last(); return true; }
      if (parent.Type != 1) return false;
      var count = Count(parent.Id);
      if (tokens.Last() == "-" && insertion) { position = count; return true; }
      if (!Index(tokens.Last(), out var index) || index > count) return false;
      position = index; return true;
    }
    public bool Put(string pointer, JsonNode value, string treeId)
    {
      CheckTree(treeId); var tokens = Tokens(pointer);
      return Mutate(() =>
      {
        var existing = Resolve(tokens);
        if (existing != null)
        {
          Remove(existing.Id, false);
          var type = Type(value);
          Execute("UPDATE Nodes SET Type=$t,Value=jsonb($v) WHERE Id=$id", ("$t", type), ("$v", type <= 1 ? null : value?.ToJsonString() ?? "null"), ("$id", existing.Id));
          AddChildren(existing.Id, value); return true;
        }
        if (tokens.Length == 0) { Insert(null, null, null, value); return true; }
        if (!Destination(tokens, out var parent, out var name, out var position, true)) return false;
        Insert(parent.Id, name, position, value); return true;
      });
    }
    private void Shift(long parent, long start, int delta)
    {
      // Two phases avoid transient unique-index collisions in SQLite's row update order.
      Execute("UPDATE Nodes SET Position=-Position-1 WHERE ParentId=$p AND Position >= $s", ("$p", parent), ("$s", start));
      Execute("UPDATE Nodes SET Position=-Position-1+$d WHERE ParentId=$p AND Position<0", ("$p", parent), ("$d", delta));
    }
    public bool Delete(string pointer, string treeId)
    {
      CheckTree(treeId); var tokens = Tokens(pointer);
      if (tokens.Length == 0) return false;
      return Mutate(() =>
      {
        var node = Resolve(tokens); if (node == null) return false;
        Remove(node.Id, true);
        if (node.Position.HasValue) Shift(node.Parent.Value, node.Position.Value + 1, -1);
        return true;
      });
    }
    public bool Move(string treeId, string sourcePointer, string destinationPointer)
    {
      CheckTree(treeId); var sourceTokens = Tokens(sourcePointer); var destinationTokens = Tokens(destinationPointer);
      if (sourceTokens.Length == 0 || destinationTokens.Length == 0) return false;
      return Mutate(() =>
      {
        var source = Resolve(sourceTokens); if (source == null) return false;
        if (sourceTokens.SequenceEqual(destinationTokens)) return true;
        if (destinationTokens.Length > sourceTokens.Length && destinationTokens.Take(sourceTokens.Length).SequenceEqual(sourceTokens)) return false;
        // Validate against the original tree before detaching. Destination is resolved again after removal.
        if (!Destination(destinationTokens, out _, out _, out _, true)) return false;
        // Park the source in a temporary slot on its current parent while resolving
        // the destination against the tree after source removal.
        if (source.Position.HasValue)
        {
          Execute("UPDATE Nodes SET Position=$i WHERE Id=$id", ("$i", long.MinValue), ("$id", source.Id));
          // Shift only real slots; Shift's second phase must not include the temporary slot.
          Execute("UPDATE Nodes SET Position=-Position-1 WHERE ParentId=$p AND Position>$s", ("$p", source.Parent), ("$s", source.Position));
          Execute("UPDATE Nodes SET Position=-Position-2 WHERE ParentId=$p AND Position<0 AND Id<>$id", ("$p", source.Parent), ("$id", source.Id));
        }
        else Execute("UPDATE Nodes SET Name=$n WHERE Id=$id", ("$n", "__move_" + Guid.NewGuid().ToString("N")), ("$id", source.Id));
        if (!Destination(destinationTokens, out var parent, out var name, out var position, true)) return false;
        if (position.HasValue)
        {
          Execute("UPDATE Nodes SET Position=-Position-1 WHERE ParentId=$p AND Position >= $s AND Id<>$id", ("$p", parent.Id), ("$s", position), ("$id", source.Id));
          Execute("UPDATE Nodes SET Position=-Position WHERE ParentId=$p AND Position<0 AND Id<>$id", ("$p", parent.Id), ("$id", source.Id));
        }
        else
        {
          var target = Find("ParentId=$p AND Name=$n", ("$p", parent.Id), ("$n", name));
          if (target != null)
          {
            Execute("UPDATE Nodes SET ParentId=$p,Name=$n,Position=NULL WHERE Id=$id", ("$p", parent.Id), ("$n", "__move_" + Guid.NewGuid().ToString("N")), ("$id", source.Id));
            Remove(target.Id, true);
          }
        }
        Execute("UPDATE Nodes SET ParentId=$p,Name=$n,Position=$i WHERE Id=$id", ("$p", parent.Id), ("$n", name), ("$i", position), ("$id", source.Id));
        return true;
      });
    }
    public void DeleteTree(string treeId)
    {
      CheckTree(treeId);
      Mutate(() => { var root = Root(); if (root != null) Remove(root.Id, true); return true; });
    }
    public void Clear(string id) { CheckTree(id); Put("", null, id); }
    public bool CanPut(string pointer, string id)
    {
      CheckTree(id); var tokens = Tokens(pointer);
      return ReadSnapshot(() => tokens.Length == 0 || Resolve(tokens) != null || Destination(tokens, out _, out _, out _, true));
    }
    // Enumerate object members without reading their values or descendant nodes.
    public IEnumerable<string> GetPropertyNames(string pointer, string treeId)
    {
      CheckTree(treeId); var tokens = Tokens(pointer);
      return ReadSnapshot(() =>
      {
        var node = Resolve(tokens); var names = new List<string>();
        if (node == null || node.Type != 0) return names;
        using var command = Command("SELECT Name FROM Nodes WHERE ParentId=$p AND Name IS NOT NULL ORDER BY Name COLLATE BINARY", ("$p", node.Id));
        using var reader = command.ExecuteReader();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
      });
    }
    public IEnumerable<string> GetKeys(string id) => GetKeys("", id);
    public IEnumerable<string> GetKeys(string prefix, string id)
    {
      CheckTree(id); var tokens = Tokens(prefix);
      return ReadSnapshot(() =>
      {
        var node = Resolve(tokens); var keys = new List<string>(); if (node == null) return keys;
        var pending = new Stack<(long Id, string Pointer)>(); pending.Push((node.Id, prefix));
        while (pending.Count > 0)
        {
          var current = pending.Pop(); keys.Add(current.Pointer);
          using var c = Command("SELECT Id,Name,Position FROM Nodes WHERE ParentId=$p ORDER BY Position,Name", ("$p", current.Id));
          using var r = c.ExecuteReader();
          while (r.Read()) pending.Push((r.GetInt64(0), current.Pointer + "/" + (r.IsDBNull(1) ? r.GetInt64(2).ToString(CultureInfo.InvariantCulture) : r.GetString(1).Replace("~", "~0").Replace("/", "~1"))));
        }
        return keys;
      });
    }
    JToken ITrunk.Get(string key, string id) { var value = Get(key, id); return value == null ? null : JToken.Parse(value.ToJsonString()); }
    bool ITrunk.Put(string key, JToken value, string id) => Put(key, value == null ? null : JsonNode.Parse(value.ToString(Newtonsoft.Json.Formatting.None)), id);
    public void BeginTransaction()
    {
      if (transaction != null) throw new InvalidOperationException("A transaction is already active.");
      transaction = connection.BeginTransaction();
    }
    public void CommitTransaction() { if (transaction == null) return; try { transaction.Commit(); } finally { transaction.Dispose(); transaction = null; } }
    public void RollbackTransaction() { if (transaction == null) return; try { transaction.Rollback(); } finally { transaction.Dispose(); transaction = null; } }
    internal bool Mutate(Func<bool> action)
    {
      var owns = transaction == null;
      if (owns) BeginTransaction();
      Execute("SAVEPOINT mutation;");
      try
      {
        var result = action();
        if (!result) Execute("ROLLBACK TO mutation;");
        Execute("RELEASE mutation;");
        if (owns) CommitTransaction();
        return result;
      }
      catch
      {
        try { Execute("ROLLBACK TO mutation; RELEASE mutation;"); }
        finally { if (owns) RollbackTransaction(); }
        throw;
      }
    }
    public void Dispose() { try { RollbackTransaction(); } finally { connection.Dispose(); } }
  }
}
