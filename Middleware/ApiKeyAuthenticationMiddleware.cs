using System.Security.Cryptography;
using System.Text;

public sealed class ApiKeyAuthenticationMiddleware(
    RequestDelegate next,
    IConfiguration configuration,
    ILogger<ApiKeyAuthenticationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var protection = context.GetEndpoint()?.Metadata.GetMetadata<ApiKeyProtectedAttribute>();
        if (protection is null)
        {
            await next(context);
            return;
        }

        var isAdmin = protection.Access == ApiKeyAccess.Admin;
        var headerName = isAdmin ? "X-Admin-API-Key" : "X-API-Key";
        var configurationName = isAdmin ? "Authentication:AdminApiKey" : "Authentication:ApiKey";
        var environmentName = isAdmin ? "Authentication__AdminApiKey" : "Authentication__ApiKey";
        var configuredKey = configuration[configurationName];

        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            logger.LogCritical(
                "{Access} API authentication is not configured. Set {EnvironmentName}.",
                protection.Access,
                environmentName);
            await WriteError(
                context,
                StatusCodes.Status503ServiceUnavailable,
                $"{protection.Access} API authentication is not configured.");
            return;
        }

        var providedKeys = context.Request.Headers[headerName];
        if (providedKeys.Count != 1 || string.IsNullOrWhiteSpace(providedKeys[0]) ||
            !ApiKeyComparer.KeysMatch(configuredKey, providedKeys[0]!))
        {
            logger.LogWarning(
                "Rejected unauthenticated {Access} API request from {RemoteAddress}.",
                protection.Access,
                context.Connection.RemoteIpAddress);
            context.Response.Headers.Append("WWW-Authenticate", isAdmin ? "AdminApiKey" : "ApiKey");
            await WriteError(
                context,
                StatusCodes.Status401Unauthorized,
                $"A valid {headerName} header is required.");
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

public enum ApiKeyAccess
{
    Standard,
    Admin
}

public sealed class ApiKeyProtectedAttribute(ApiKeyAccess access = ApiKeyAccess.Standard) : Attribute
{
    public ApiKeyAccess Access { get; } = access;
}

public static class ApiKeyComparer
{
    public static bool KeysMatch(string expected, string provided)
    {
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
    }
}
