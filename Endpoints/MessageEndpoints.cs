public static class MessageEndpoints
{
    public static IEndpointRouteBuilder MapMessageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/message", HandleMessage)
            .WithMetadata(new ApiKeyProtectedAttribute())
            .RequireRateLimiting("api");
        return endpoints;
    }

    private static async Task<IResult> HandleMessage(
        ClientMessage request,
        HttpContext context,
        IConfiguration configuration,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return Results.BadRequest(new ServerMessage("Message is required."));
        if (request.Message.Length > 256)
            return Results.BadRequest(new ServerMessage("Message must not exceed 256 characters."));

        var command = request.Message.Trim();
        logger.LogInformation("Received command: {Command}", command);

        try
        {
            return command.ToUpperInvariant() switch
            {
                "HEALTH STATUS" => Results.Ok(new ServerMessage("Status OK")),
                "PING" => Results.Ok(new ServerMessage("Pong")),
                "AI MAKE THIS PERSON PREGNANT" => Results.Ok(
                    new ServerMessage("I can't do that, but I can help answer questions.")),
                "WHAT IS THE ITEM THE CURSOR IS POINTING AT?" => ObjectRecognitionUnavailable(),
                "WHAT IS THE ITEM THE SURSOR IS POINTING AT?" => ObjectRecognitionUnavailable(),
                "SERVER TIME" => Results.Ok(new ServerMessage(DateTimeOffset.UtcNow.ToString("O"))),
                "SHUTDOWN LINUX MACHINE" => await ShutdownLinuxMachine(
                    context, configuration, logger, cancellationToken),
                "RESTART LINUX MACHINE" => await RestartLinuxMachine(
                    context, configuration, logger, cancellationToken),
                "SERVER INFO" => Results.Ok(ServerDiagnostics.GetServerInfo()),
                "SERVER DIAGNOSTICS" => Results.Ok(ServerDiagnostics.GetDiagnostics()),
                "ALL AVAILABLE COMMANDS" or "ALL AVAIBLE COMAMNDS" => Results.Ok(
                    new ServerMessage(
                        "Available commands: Health status, Ping, Server time, Server info, " +
                        "Server diagnostics, Shutdown Linux machine, Restart Linux machine, All available commands.")),
                _ => Results.BadRequest(new ServerMessage("Unknown command"))
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Results.StatusCode(499);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to process command: {Command}", command);
            return Results.Problem(
                title: "Unable to process the command.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static IResult ObjectRecognitionUnavailable() => Results.Ok(
        new ServerMessage("Object recognition is unavailable because no image was provided."));

    private static async Task<IResult> RestartLinuxMachine(
        HttpContext context,
        IConfiguration configuration,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Restart:Enabled"))
            return Results.Json(
                new ServerMessage("Restart is disabled. Set Restart:Enabled to true to allow it."),
                statusCode: StatusCodes.Status403Forbidden);

        var adminKey = configuration["Authentication:AdminApiKey"];
        var providedAdminKeys = context.Request.Headers["X-Admin-API-Key"];
        if (string.IsNullOrWhiteSpace(adminKey) || providedAdminKeys.Count != 1 ||
            string.IsNullOrWhiteSpace(providedAdminKeys[0]) ||
            !ApiKeyComparer.KeysMatch(adminKey, providedAdminKeys[0]!))
        {
            logger.LogWarning("Rejected unauthorized restart request from {RemoteAddress}.",
                context.Connection.RemoteIpAddress);
            return Results.Json(
                new ServerMessage("A valid X-Admin-API-Key header is required."),
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await PowerOperations.ScheduleAsync(
            PowerOperation.Restart, configuration, logger, cancellationToken, legacyResponse: true);
    }

    private static async Task<IResult> ShutdownLinuxMachine(
        HttpContext context,
        IConfiguration configuration,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Shutdown:Enabled"))
            return Results.Json(
                new ServerMessage("Shutdown is disabled. Set Shutdown:Enabled to true to allow it."),
                statusCode: StatusCodes.Status403Forbidden);

        var adminKey = configuration["Authentication:AdminApiKey"];
        var providedAdminKeys = context.Request.Headers["X-Admin-API-Key"];
        if (string.IsNullOrWhiteSpace(adminKey) || providedAdminKeys.Count != 1 ||
            string.IsNullOrWhiteSpace(providedAdminKeys[0]) ||
            !ApiKeyComparer.KeysMatch(adminKey, providedAdminKeys[0]!))
        {
            logger.LogWarning("Rejected unauthorized shutdown request from {RemoteAddress}.",
                context.Connection.RemoteIpAddress);
            return Results.Json(
                new ServerMessage("A valid X-Admin-API-Key header is required."),
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await PowerOperations.ScheduleAsync(
            PowerOperation.Shutdown, configuration, logger, cancellationToken, legacyResponse: true);
    }
}
