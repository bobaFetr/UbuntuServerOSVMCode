public enum PowerOperation
{
    Restart,
    Shutdown
}

public static class PowerOperations
{
    public static bool IsEnabled(PowerOperation operation, IConfiguration configuration) =>
        configuration.GetValue<bool>($"{operation}:Enabled");

    public static async Task<IResult> ScheduleAsync(
        PowerOperation operation,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken,
        bool legacyResponse = false)
    {
        var operationName = operation.ToString().ToLowerInvariant();
        if (!IsEnabled(operation, configuration))
        {
            return Results.Json(
                new ServerMessage(
                    $"{operation} is disabled. Set {operation}:Enabled to true to allow it."),
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (!OperatingSystem.IsLinux())
        {
            return Results.Json(
                new ServerMessage($"{operation} is only supported on Linux."),
                statusCode: StatusCodes.Status501NotImplemented);
        }

        var delayMinutes = Math.Clamp(
            configuration.GetValue($"{operation}:DelayMinutes", 1),
            1,
            60);

        if (operation == PowerOperation.Restart)
            await LinuxShutdown.ScheduleRestartAsync(delayMinutes, cancellationToken);
        else
            await LinuxShutdown.ScheduleAsync(delayMinutes, cancellationToken);

        logger.LogWarning(
            "Linux machine {Operation} scheduled in {DelayMinutes} minute(s).",
            operationName,
            delayMinutes);

        if (legacyResponse)
        {
            return Results.Accepted(value: new ServerMessage(
                $"Linux {operationName} scheduled in {delayMinutes} minute(s). " +
                "Run 'shutdown -c' on the machine to cancel it."));
        }

        return Results.Accepted(value: new
        {
            operation = operationName,
            delayMinutes,
            scheduledAtUtc = DateTimeOffset.UtcNow,
            message = $"Linux {operationName} scheduled. Run 'shutdown -c' on the machine to cancel it."
        });
    }
}
