using Serilog;

namespace Dressfield.API.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseDressfieldCorrelationId(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            const string header = "X-Correlation-Id";
            var correlationId = context.Request.Headers[header].FirstOrDefault()
                ?? Guid.NewGuid().ToString("N");

            context.Response.Headers[header] = correlationId;

            using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
            {
                await next();
            }
        });

        return app;
    }

    public static WebApplication UseDressfieldSecurityHeaders(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["X-XSS-Protection"] = "0";
            headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=()";

            await next();
        });

        return app;
    }
}
