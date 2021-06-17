using System;
using System.IO;
using System.Threading.Tasks;
using JavaScriptEngineSwitcher.Core;
using Microsoft.AspNetCore.Http;

namespace Gradinware
{
    public sealed class JsonContentMiddleware
    {
        private const string HTML_TEMPLATE_PATH = "/public/index.html";
        private const string JS_BUNDLE_PATH = "wwwroot/ssr.js";

        private readonly RequestDelegate _next;
        private readonly IJsEngine _js;
        private readonly string _template;
        private readonly string _bundle;
        private readonly bool _valid;

        public JsonContentMiddleware(RequestDelegate next)
        {
            _next = next;
            _js = JsEngineSwitcher.Current.CreateDefaultEngine();

            if (File.Exists(HTML_TEMPLATE_PATH))
            {
                _template = File.ReadAllText(HTML_TEMPLATE_PATH);
            }

            if (File.Exists(JS_BUNDLE_PATH))
            {
                _bundle = File.ReadAllText(JS_BUNDLE_PATH);
                _js.Evaluate(_bundle);
                _js.Evaluate("var render = function(str) { return app.render(JSON.parse(str)).html; };");
            }

            _js.Evaluate("var renderType = typeof render");
            _valid = _js.GetVariableValue<string>("renderType") == "function";
        }

        public async Task Invoke(HttpContext context)
        {
            string path = context.Request.Path;
            if (Path.GetExtension(path) == ".html")
            {
                string filePath = "/data" + Path.ChangeExtension(path, ".json");
                if (File.Exists(filePath))
                {
                    context.Response.ContentType = "text/html; charset=utf-8";

                    string json = File.ReadAllText(filePath);
                    try
                    {
                        if (!_valid) {
                            context.Response.StatusCode = 500;
                            await context.Response.WriteAsync("<!-- Error: Invalid/missing render function -->");
                            await ReportServerError(context);
                        } else {
                            string html = _js.CallFunction<string>("render", json);
                            string result = _template.Replace("</body>", html + "\n</body>");
                            await context.Response.WriteAsync(result);
                        }
                    }
                    catch (Exception ex)
                    {
                        context.Response.StatusCode = 500;
                        await ReportServerError(context, ex);
                    }
                }
                else
                {
                    await _next(context);
                }
            }
            else
            {
                await _next(context);
            }
        }

        private static async Task ReportServerError(HttpContext context, Exception ex)
        {
            await UpdateStatusAndWriteServerError(context, async () => {
                await context.Response.WriteAsync($@"<!--
    Exception: {ex.Message}
    Stacktrace: {ex.StackTrace}
-->
");
                await ReportServerError(context);
            });
        }

        private static async Task ReportServerError(HttpContext context)
        {
            await UpdateStatusAndWriteServerError(context, async () => {
                await context.Response.WriteAsync($@"
<h1>Error</h1>
<p>Sorry, the server threw an unexpected error 😔. Try again later.</p>
");
            });
        }

        private static async Task UpdateStatusAndWriteServerError(HttpContext context, Func<Task> writeError)
        {
            if (context.Response.StatusCode != 500)
            {
                context.Response.StatusCode = 500;
            }

            await writeError();
        }
    }
}
