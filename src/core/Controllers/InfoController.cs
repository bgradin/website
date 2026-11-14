using System;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace Gradinware.Controllers
{
    [ApiController]
    [Route("/api")]
    public class InfoController : ControllerBase
    {
        [HttpGet]
        public JsonResult Get()
        {
            return new JsonResult(new {
                name = "www.gradinware.com",
                version = Assembly.GetEntryAssembly().GetName().Version.ToString(),
                now = DateTime.UtcNow,
            });
        }
    }
}
