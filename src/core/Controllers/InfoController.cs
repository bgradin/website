using System;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Gradinware.Controllers
{
    [ApiController]
    [Route("/api")]
    public class InfoController : ControllerBase
    {
        private readonly ILogger<InfoController> _logger;

        public InfoController(ILogger<InfoController> logger)
        {
            _logger = logger;
        }

        [HttpGet]
        public JsonResult Get()
        {
            return new JsonResult(new {
                name = "www.gradinware.com",
                version = Assembly.GetEntryAssembly().GetName().Version.ToString(),
                now = DateTime.Now,
            });
        }
    }
}
