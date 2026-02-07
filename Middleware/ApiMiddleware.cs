using System.Text;

namespace BatchDownloader.API.Middleware
{
    public class ApiMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string? _key;

        public ApiMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            _key = configuration["ApiKey"];
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Simple header-based check:
            if (string.IsNullOrEmpty(_key))
            {
                //await _next(context);
                return;
            }

            if (!context.Request.Headers.TryGetValue("X-Api-Key", out var provided) || provided != _key)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("API key missing or invalid.");
                return;
            }

            await _next(context);
        }
    }
}
