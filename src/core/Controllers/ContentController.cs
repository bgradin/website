using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Gradinware.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Quilting;

namespace Gradinware.Controllers
{
  [ApiController]
  [Route("/api/v1/content")]
  public class ContentController : ControllerBase
  {
    private readonly ILogger<ContentController> _logger;
    private readonly Quilt _quilt;

    public ContentController(ILogger<ContentController> logger, IContentTrunk trunk)
    {
      _logger = logger;

      trunk.EnsureCreated();
      _quilt = new Quilt(trunk);
    }

    [HttpPut]
    public async Task<HttpResponseMessage> Put()
    {
      using (var reader = new StreamReader(Request.Body))
      {
        var body = await reader.ReadToEndAsync();
        var token = JToken.Parse(body);
        try
        {
          return new HttpResponseMessage(
            _quilt.Create(token, Startup.Configuration?["AdminUsername"])
              ? HttpStatusCode.OK
              : HttpStatusCode.Unauthorized
          );
        }
        catch (System.Exception e)
        {
          return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }
      }
    }

    [HttpGet]
    public IActionResult Get([FromQuery] string key, [FromQuery] string id)
    {
      var patch = _quilt.GetPatch(key, id);
      return patch != null
        ? new ContentResult
        {
          Content = patch.ToString(Formatting.None),
          ContentType = "application/json"
        }
        : BadRequest();
    }

    [HttpGet]
    [Route("map")]
    public IActionResult Map([FromQuery] string key, [FromQuery] string id)
    {
      var map = _quilt.GetMap(key);
      return new ContentResult
      {
        Content = map.ToString(Formatting.None),
        ContentType = "application/json"
      };
    }

    [HttpPost]
    public async Task<IActionResult> Write([FromQuery] string key, [FromQuery] string id)
    {
      using (var reader = new StreamReader(Request.Body))
      {
        var body = await reader.ReadToEndAsync();
        var patch = Patch.TryConvert(JToken.Parse(body));
        return patch != null && _quilt.CreatePatch(patch, key, id)
          ? Ok() : BadRequest();
      }
    }
  }
}
