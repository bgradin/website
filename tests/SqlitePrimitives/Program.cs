using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;

// A repeatable local comparison, not a performance assertion. Numeric values in the
// typed variant use TEXT to retain JSON decimal precision, rather than lossy REAL.
const int count = 20000;
var inputs = new[] { "null", "true", "false", "12345678901234567890.123456789", "1e300", "\"a short string\"" };
var directory = Path.Combine(Path.GetTempPath(), "sqlite-primitives-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    foreach (var jsonb in new[] { true, false })
    {
        var path = Path.Combine(directory, jsonb ? "jsonb.db" : "typed.db");
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False"); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = jsonb ? "CREATE TABLE ValuesTable(Id INTEGER PRIMARY KEY, Value BLOB NOT NULL)" : "CREATE TABLE ValuesTable(Id INTEGER PRIMARY KEY, Kind INTEGER NOT NULL, TextValue TEXT, BoolValue INTEGER)";
        command.ExecuteNonQuery();
        var timer = Stopwatch.StartNew();
        using (var transaction = connection.BeginTransaction())
        {
            command.Transaction = transaction;
            command.CommandText = jsonb ? "INSERT INTO ValuesTable(Value) VALUES(jsonb($value))" : "INSERT INTO ValuesTable(Kind,TextValue,BoolValue) VALUES($kind,$text,$bool)";
            command.Parameters.Clear();
            if (jsonb) command.Parameters.Add("$value", SqliteType.Text);
            else { command.Parameters.Add("$kind", SqliteType.Integer); command.Parameters.Add("$text", SqliteType.Text); command.Parameters.Add("$bool", SqliteType.Integer); }
            command.Prepare();
            for (var i = 0; i < count; i++)
            {
                var input = inputs[i % inputs.Length];
                if (jsonb) command.Parameters[0].Value = input;
                else
                {
                    using var document = JsonDocument.Parse(input); var value = document.RootElement;
                    command.Parameters[0].Value = (int)value.ValueKind;
                    command.Parameters[1].Value = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Number ? input : DBNull.Value;
                    command.Parameters[2].Value = value.ValueKind is JsonValueKind.True or JsonValueKind.False ? (value.GetBoolean() ? 1 : 0) : DBNull.Value;
                }
                command.ExecuteNonQuery();
            }
            transaction.Commit(); command.Transaction = null;
        }
        var write = timer.Elapsed.TotalMilliseconds; timer.Restart();
        command.Parameters.Clear(); command.Parameters.Add("$id", SqliteType.Integer);
        command.CommandText = jsonb ? "SELECT json(Value) FROM ValuesTable WHERE Id=$id" : "SELECT Kind,TextValue,BoolValue FROM ValuesTable WHERE Id=$id";
        command.Prepare(); long checksum = 0;
        for (var i = 0; i < count; i++)
        {
            command.Parameters[0].Value = (i * 7919L % count) + 1;
            using var reader = command.ExecuteReader(); reader.Read();
            string json;
            if (jsonb) json = reader.GetString(0);
            else
            {
                var kind = (JsonValueKind)reader.GetInt32(0);
                json = kind switch { JsonValueKind.Null => "null", JsonValueKind.True => "true", JsonValueKind.False => "false", JsonValueKind.String => JsonSerializer.Serialize(reader.GetString(1)), _ => reader.GetString(1) };
            }
            using var parsed = JsonDocument.Parse(json); checksum += json.Length;
        }
        Console.WriteLine($"{(jsonb ? "JSONB" : "Typed")}: {count} writes {write:F1} ms; {count} indexed reads {timer.Elapsed.TotalMilliseconds:F1} ms; {new FileInfo(path).Length} bytes; checksum {checksum}");
    }
}
finally { Directory.Delete(directory, true); }
