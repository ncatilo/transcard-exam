using System.Net;
using System.Text.Json;
using TransCard.Models.Responses;

namespace TransCard.Middleware;

public class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger) {

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception. TraceId: {TraceId}", context.TraceIdentifier);

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            var isDevelopment = context.RequestServices
                .GetRequiredService<IHostEnvironment>().IsDevelopment();

            var error = new ApiErrorResponse
            {
                Type = "InternalServerError",
                Title = "An unexpected error occurred.",
                Status = 500,
                Detail = isDevelopment ? ex.Message : "Please contact support if this persists.",
                TraceId = context.TraceIdentifier
            };

            var json = JsonSerializer.Serialize(error, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await context.Response.WriteAsync(json);
        }
    }
}
