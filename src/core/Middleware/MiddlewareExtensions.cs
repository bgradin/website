using Microsoft.AspNetCore.Builder;

namespace Gradinware
{
    public static class MiddlewareExtensions
    {
        public static IApplicationBuilder UseJsonContent(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<JsonContentMiddleware>();
        }
    }
}
