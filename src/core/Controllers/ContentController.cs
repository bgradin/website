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
        return new HttpResponseMessage(
          _quilt.Create(token, "brian")
            ? HttpStatusCode.OK
            : HttpStatusCode.Unauthorized
        );
      }
    }

    [HttpGet]
    public IActionResult Get([FromQuery] string key, [FromQuery] string id)
    {
      var patch = _quilt.GetPatch(key, id);
      return patch != null
        ? new ContentResult
        {
          Content = patch.ToJson().ToString(Formatting.None),
          ContentType = "application/json"
        }
        : BadRequest();
    }

    [HttpGet]
    [Route("map")]
    public IActionResult Map([FromQuery] string key, [FromQuery] string id)
    {
      var patch = _quilt.GetPatch(key, id);
      return patch != null
        ? new ContentResult
        {
          Content = patch.ToJson().ToString(Formatting.None),
          ContentType = "application/json"
        }
        : BadRequest();
    }

    [HttpPost]
    public async Task<IActionResult> Write([FromQuery] string key, [FromQuery] string id)
    {
      using (var reader = new StreamReader(Request.Body))
      {
        var body = await reader.ReadToEndAsync();
        var token = JToken.Parse(body);
        return Patch.CanConvert(token) && _quilt.CreatePatch(new Patch(token), key, id)
          ? Ok() : BadRequest();
      }
    }
  }
}
