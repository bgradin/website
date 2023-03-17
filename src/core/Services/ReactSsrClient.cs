using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Gradinware.Services
{
  public class ReactSsrClient : IReactSsrClient
  {
    private readonly HttpClient _client;

    public ReactSsrClient(HttpClient client)
    {
      _client = client;
      _client.BaseAddress = new Uri("http://localhost:3000");
      _client.Timeout = new TimeSpan(0, 0, 5);
      _client.DefaultRequestHeaders.Clear();
    }

    public async Task<string> Render(JToken json)
    {
      var request = new HttpRequestMessage(HttpMethod.Get, "/");
      request.Content = new StringContent(json.ToString(), Encoding.UTF8, "application/json");

      using (var response = await _client.SendAsync(request))
      {
        try
        {
          var body = await response.Content.ReadAsStringAsync();
          if (response.StatusCode != HttpStatusCode.OK)
          {
            throw new HttpRequestException(body);
          }

          return body;
        }
        catch (HttpRequestException)
        {
          throw;
        }
      }
    }
  }
}
