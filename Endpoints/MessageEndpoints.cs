public static class MessageEndpoints
{
    public static IEndpointRouteBuilder MapMessageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/message", HandleMessage);
        return endpoints;
    }

    private static IResult HandleMessage(
        ClientMessage request,
        IConfiguration configuration,
        ILogger<Program> logger)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return Results.BadRequest(new ServerMessage("Message is required."));

        var command = request.Message.Trim();
        logger.LogInformation("Received command: {Command}", command);

        try
        {
            return command switch
            {
                "Health status" => Results.Ok(new ServerMessage("Status OK")),
                "Ping" => Results.Ok(new ServerMessage("Pong")),
                "AI make this person pregnant" => Results.Ok(
                    new ServerMessage("I can't do that, but I can help answer questions.")),
                "What is the item the cursor is pointing at?" => ObjectRecognitionUnavailable(),
                // Retain compatibility with clients using the original misspelling.
                "What is the item the sursor is pointing at?" => ObjectRecognitionUnavailable(),
                "Server time" => Results.Ok(
                    new ServerMessage(DateTimeOffset.UtcNow.ToString("O"))),
                "Shutdown Linux machine" => ShutdownLinuxMachine(configuration, logger),
                "Server info" => Results.Ok(ServerDiagnostics.GetServerInfo()),
                "Server diagnostics" => Results.Ok(ServerDiagnostics.GetDiagnostics()),
                "All avaible comamnds" => Results.Ok(new ServerMessage("This command is stil not usable yet. It's purpose is to bring a list of all commands which can be used in this Console Platform.")),
                _ => Results.BadRequest(new ServerMessage("Unknown command"))
            };
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to process command: {Command}", command);
            return Results.Problem(
                title: "Unable to collect server information.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static IResult ObjectRecognitionUnavailable() => Results.Ok(
        new ServerMessage("Object recognition is unavailable because no image was provided."));

    private static IResult ShutdownLinuxMachine(
        IConfiguration configuration,
        ILogger<Program> logger)
    {
        if (!configuration.GetValue<bool>("Shutdown:Enabled"))
        {
            return Results.Json(
                new ServerMessage(
                    "Shutdown is disabled. Set Shutdown:Enabled to true to allow it."),
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (!OperatingSystem.IsLinux())
        {
            return Results.Json(
                new ServerMessage("Shutdown is only supported on Linux."),
                statusCode: StatusCodes.Status501NotImplemented);
        }

        var delayMinutes = Math.Clamp(
            configuration.GetValue("Shutdown:DelayMinutes", 1),
            1,
            60);

        LinuxShutdown.Schedule(delayMinutes);
        logger.LogWarning(
            "Linux machine shutdown scheduled in {DelayMinutes} minute(s).",
            delayMinutes);

        return Results.Accepted(
            value: new ServerMessage(
                $"Linux shutdown scheduled in {delayMinutes} minute(s). " +
                "Run 'shutdown -c' on the machine to cancel it."));
    }
}
