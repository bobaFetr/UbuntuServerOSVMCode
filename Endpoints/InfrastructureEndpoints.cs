public static class InfrastructureEndpoints
{
    public static IEndpointRouteBuilder MapInfrastructureEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var standard = endpoints.MapGroup("/api/v1")
            .WithMetadata(new ApiKeyProtectedAttribute())
            .RequireRateLimiting("api");

        standard.MapGet("/health/live", () => Results.Ok(new
        {
            status = "Healthy",
            checkedAtUtc = DateTimeOffset.UtcNow
        }));
        standard.MapGet("/health/ready", CheckReadiness);
        standard.MapGet("/server/info", () => Results.Ok(ServerDiagnostics.GetServerInfo()));
        standard.MapGet("/server/diagnostics", () => Results.Ok(ServerDiagnostics.GetDiagnostics()));

        var admin = endpoints.MapGroup("/api/v1/power")
            .WithMetadata(new ApiKeyProtectedAttribute(ApiKeyAccess.Admin))
            .RequireRateLimiting("power");

        admin.MapPost("/restart", (
            IConfiguration configuration,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
            PowerOperations.ScheduleAsync(
                PowerOperation.Restart, configuration, logger, cancellationToken));

        admin.MapPost("/shutdown", (
            IConfiguration configuration,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
            PowerOperations.ScheduleAsync(
                PowerOperation.Shutdown, configuration, logger, cancellationToken));

        return endpoints;
    }

    private static IResult CheckReadiness(IConfiguration configuration)
    {
        var standardKeyMissing = string.IsNullOrWhiteSpace(
            configuration["Authentication:ApiKey"]);
        var powerEnabled = PowerOperations.IsEnabled(PowerOperation.Restart, configuration) ||
            PowerOperations.IsEnabled(PowerOperation.Shutdown, configuration);
        var requiredAdminKeyMissing = powerEnabled && string.IsNullOrWhiteSpace(
            configuration["Authentication:AdminApiKey"]);

        if (standardKeyMissing || requiredAdminKeyMissing)
        {
            return Results.Json(
                new { status = "Unhealthy", reason = "Required API keys are not configured." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(new
        {
            status = "Healthy",
            checkedAtUtc = DateTimeOffset.UtcNow
        });
    }
}
