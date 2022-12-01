using Newtonsoft.Json.Bson;
using Newtonsoft.Json.Linq;
using Quilting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Gradinware.Data
{
  internal sealed class SqliteBsonTrunk : KvpDbContext, ITrunk
  {
    private readonly string _databaseName;

    public SqliteBsonTrunk(string databaseName)
    {
      _databaseName = databaseName;
    }

    protected override string GetDatabaseName()
    {
      return _databaseName;
    }

    public void Clear()
    {
      KeyValuePairs.RemoveRange(KeyValuePairs);
      SaveChanges();
    }

    public IEnumerable<string> GetKeys()
    {
      return GetKeys(string.Empty);
    }

    public IEnumerable<string> GetKeys(string prefix)
    {
      return KeyValuePairs.Select(x => x.Key).Where(x => x.StartsWith(prefix));
    }

    public JToken Retrieve(string key)
    {
      var kvp = KeyValuePairs.First(x => x.Key == key);
      var data = Convert.FromBase64String(kvp.Value);
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

    public void Stow(string key, JToken value)
    {
      using (var stream = new MemoryStream())
      using (var writer = new BsonDataWriter(stream))
      {
        var kvp = KeyValuePairs.First(x => x.Key == key);
        value.WriteTo(writer);
        kvp.Value = Convert.ToBase64String(stream.ToArray());
        SaveChanges();
      }
    }
  }
}
