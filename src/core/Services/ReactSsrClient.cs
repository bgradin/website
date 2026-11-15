using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Gradinware.Services
{
  public class ReactSsrClient : IReactSsrClient
  {
    private readonly HttpClient _client;

    public ReactSsrClient(HttpClient client)
    {
      _client = client;
      _client.BaseAddress = new Uri("http://127.0.0.1:3000");
      _client.Timeout = TimeSpan.FromSeconds(5);
    }

    public async Task<string> Render(JToken json, CancellationToken cancellationToken = default)
    {
      using var request = new HttpRequestMessage(HttpMethod.Post, "/")
      {
        Content = new StringContent(json.ToString(Formatting.None), Encoding.UTF8, "application/json")
      };
      using var response = await _client.SendAsync(request, cancellationToken);
      response.EnsureSuccessStatusCode();
      return await response.Content.ReadAsStringAsync(cancellationToken);
    }
  }
}
