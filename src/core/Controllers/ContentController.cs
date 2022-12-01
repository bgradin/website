using System.Net;
using System.Net.Http;
using Gradinware.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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

    public ContentController(ILogger<ContentController> logger)
    {
      _logger = logger;

      var trunk = new SqliteBsonTrunk("content");
      HttpContext.Response.RegisterForDispose(trunk);
      _quilt = new Quilt(trunk);
    }

    [HttpPut]
    public HttpResponseMessage Put(object body)
    {
      var token = JToken.FromObject(body);
      return new HttpResponseMessage(
        _quilt.Create(token, "brian")
          ? HttpStatusCode.OK
          : HttpStatusCode.Unauthorized
      );
    }
  }
}
