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

    void Clear();
    IEnumerable<string> GetKeys();
    IEnumerable<string> GetKeys(string prefix);
    JToken Retrieve(string key);
    bool Stow(string key, JToken value);
  }
}
