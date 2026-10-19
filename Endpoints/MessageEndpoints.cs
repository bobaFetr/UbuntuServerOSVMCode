public static class MessageEndpoints
{
    private sealed record CommandDefinition(
        string Name,
        Func<HttpContext, IConfiguration, ILogger<Program>, CancellationToken, Task<IResult>> Execute,
        params string[] Aliases);

    // Execution and discovery share this registry: register new commands only here.
    private static readonly CommandDefinition[] Commands =
    [
        Simple("Health status", () => Results.Ok(new ServerMessage("Status OK"))),
        Simple("TaskM", () => new TaskMonitor()),
        Simple("Ping", () => Results.Ok(new ServerMessage("Pong"))),
        Simple("AI make this person pregnant", () => Results.Ok(
            new ServerMessage("I can't do that, but I can help answer questions."))),
        Simple("What is the item the cursor is pointing at?", ObjectRecognitionUnavailable,
            "What is the item the sursor is pointing at?"),
        Simple("Server time", () => Results.Ok(new ServerMessage(DateTimeOffset.UtcNow.ToString("O")))),
        new("Shutdown Linux machine", ShutdownLinuxMachine),
        new("Restart Linux machine", RestartLinuxMachine),
        Simple("Server info", () => Results.Ok(ServerDiagnostics.GetServerInfo())),
        Simple("Server diagnostics", () => Results.Ok(ServerDiagnostics.GetDiagnostics())),
        Simple("Kill AI", () => Results.Ok(new ServerMessage("AI has been terminated."))),
        Simple("All available commands", ListCommands,
            "All avaible comamnds", "All commands", "All comamnds")
    ];

    private static CommandDefinition Simple(string name, Func<IResult> execute, params string[] aliases) =>
        new(name, (_, _, _, _) => Task.FromResult(execute()), aliases);

    private static IResult ListCommands() => Results.Ok(new ServerMessage(
        $"Available commands: {string.Join(", ", Commands.Select(command => command.Name))}."));

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
            var definition = Commands.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, command, StringComparison.OrdinalIgnoreCase) ||
                candidate.Aliases.Any(alias =>
                    string.Equals(alias, command, StringComparison.OrdinalIgnoreCase)));

            return definition is null
                ? Results.BadRequest(new ServerMessage("Unknown command"))
                : await definition.Execute(context, configuration, logger, cancellationToken);
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
