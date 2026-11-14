using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Quilting
{
  public interface ITrunk
  {
    string Delimiter
    {
      get;
    }

    void BeginTransaction();
    void CommitTransaction();
    void RollbackTransaction();

    void Clear(string id);
    IEnumerable<string> GetKeys(string id);
    IEnumerable<string> GetKeys(string prefix, string id);
    JToken Get(string key, string id);
    bool Put(string key, JToken value, string id);
    bool Delete(string key, string id);
    bool CanPut(string key, string id);
  }
}
