using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Shared.Extensions
{
    public static class RequestLoggingExtensions
    {
        public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app, string serviceName)
        {
            var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(serviceName);

            return app.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value ?? string.Empty;

                if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
                {
                    await next();
                    return;
                }

                var timer = Stopwatch.StartNew();
                await next();
                timer.Stop();

                logger.LogInformation("[{Service}] {Method} {Path}{Query} -> {Status} ({Elapsed} ms)",
                    serviceName, context.Request.Method, path, context.Request.QueryString,
                    context.Response.StatusCode, timer.ElapsedMilliseconds);
            });
        }
    }
}
