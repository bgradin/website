using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Gradinware.Attributes;
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
    private readonly IContentTrunk _trunk;
    private readonly Quilt _quilt;

    public ContentController(ILogger<ContentController> logger, IContentTrunk trunk)
    {
      _logger = logger;
      _trunk = trunk;
      _trunk.EnsureCreated();
      _quilt = new Quilt(trunk);
    }

    [HttpPut]
    [AccessTokenAuthenticated]
    [RequestSizeLimit(1048576)]
    public async Task<IActionResult> Put()
    {
      if (GetQuilterId() != Startup.Configuration["AdminUsername"])
      {
        return StatusCode(403);
      }
      try
      {
        using (var reader = new StreamReader(Request.Body))
        {
          var token = JToken.Parse(await reader.ReadToEndAsync());
          if (token.Type != JTokenType.Object) return BadRequest();
          return _quilt.Create(token, GetQuilterId()) ? Ok() : Unauthorized();
        }
      }
      catch (JsonException)
      {
        return BadRequest(new { error = "Invalid JSON body." });
      }
      catch (Exception exception)
      {
        _logger.LogError(exception, "Content import failed.");
        return StatusCode(500);
      }
    }

    [HttpGet]
    [AccessTokenAuthenticated]
    public IActionResult Get([FromQuery] string key)
    {
      if (!IsContentKey(key)) return BadRequest();
      var patch = _quilt.GetPatch(key, GetQuilterId());
      return patch != null
        ? new ContentResult { Content = patch.ToString(Formatting.None), ContentType = "application/json" }
        : NotFound();
    }

    [HttpGet("map")]
    [AccessTokenAuthenticated]
    public IActionResult Map([FromQuery] string key)
    {
      if (!IsContentKey(key)) return BadRequest();
      if (GetQuilterId() != Startup.Configuration["AdminUsername"]) return StatusCode(403);
      return new ContentResult { Content = _quilt.GetMap(key, GetQuilterId()).ToString(Formatting.None), ContentType = "application/json" };
    }

    [HttpPost]
    [AccessTokenAuthenticated]
    [RequestSizeLimit(1048576)]
    public async Task<IActionResult> Write([FromQuery] string key)
    {
      if (!IsContentKey(key)) return BadRequest();
      try
      {
        using (var reader = new StreamReader(Request.Body))
        {
          var patch = Patch.TryConvert(JToken.Parse(await reader.ReadToEndAsync()));
          if (patch == null) return BadRequest();
          return _quilt.CreatePatch(patch, key, GetQuilterId()) ? Ok() : StatusCode(403);
        }
      }
      catch (JsonException)
      {
        return BadRequest(new { error = "Invalid JSON body." });
      }
      catch (Exception exception)
      {
        _logger.LogError(exception, "Content write failed for key {ContentKey}.", key);
        return StatusCode(500);
      }
    }

    [HttpDelete]
    [AccessTokenAuthenticated]
    public IActionResult Delete([FromQuery] string key)
    {
      if (!IsContentKey(key)) return BadRequest();
      return _quilt.DeletePatch(key, GetQuilterId()) ? NoContent() : NotFound();
    }

    private string GetQuilterId()
    {
      var token = Request.Headers["Authorization"].ToString().Substring(7);
      var values = token.DeserializeAccessToken();
      var userId = Convert.ToInt64(values["userId"], CultureInfo.InvariantCulture);
      var adminUserId = Startup.Configuration["Content:AdminUserId"];
      return !string.IsNullOrEmpty(adminUserId) && userId.ToString(CultureInfo.InvariantCulture) == adminUserId
        ? Startup.Configuration["AdminUsername"]
        : userId.ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsContentKey(string key)
    {
      return !string.IsNullOrWhiteSpace(key) && key.Length <= 256 && key.StartsWith("q:content.", StringComparison.Ordinal);
    }
  }
}
