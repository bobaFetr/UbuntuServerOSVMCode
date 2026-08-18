using System.Security.Cryptography;
using System.Text;

public sealed class ApiKeyAuthenticationMiddleware(
    RequestDelegate next,
    IConfiguration configuration,
    ILogger<ApiKeyAuthenticationMiddleware> logger)
{
    private const string HeaderName = "X-API-Key";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<ApiKeyProtectedAttribute>() is null)
        {
            await next(context);
            return;
        }

        var configuredKey = configuration["Authentication:ApiKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            logger.LogCritical("API authentication is not configured. Set Authentication__ApiKey.");
            await WriteError(context, StatusCodes.Status503ServiceUnavailable,
                "API authentication is not configured.");
            return;
        }

        var providedKeys = context.Request.Headers[HeaderName];
        if (providedKeys.Count != 1 || string.IsNullOrWhiteSpace(providedKeys[0]) ||
            !ApiKeyComparer.KeysMatch(configuredKey, providedKeys[0]!))
        {
            logger.LogWarning("Rejected unauthenticated API request from {RemoteAddress}.",
                context.Connection.RemoteIpAddress);
            context.Response.Headers.Append("WWW-Authenticate", "ApiKey");
            await WriteError(context, StatusCodes.Status401Unauthorized,
                "A valid X-API-Key header is required.");
            return;
        }

        await next(context);
    }

    private static async Task WriteError(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new ServerMessage(message));
    }
}

public sealed class ApiKeyProtectedAttribute : Attribute;

public static class ApiKeyComparer
{
    public static bool KeysMatch(string expected, string provided)
    {
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
    }
}
