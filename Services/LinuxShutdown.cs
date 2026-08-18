using System.Diagnostics;

public static class LinuxShutdown
{
    private static readonly string[] ShutdownPaths =
    [
        "/usr/sbin/shutdown",
        "/sbin/shutdown",
        "/usr/bin/shutdown"
    ];

    public static Task ScheduleAsync(int delayMinutes, CancellationToken cancellationToken) =>
        SchedulePowerOperationAsync("--poweroff", "shutdown", delayMinutes, cancellationToken);

    public static Task ScheduleRestartAsync(int delayMinutes, CancellationToken cancellationToken) =>
        SchedulePowerOperationAsync("--reboot", "restart", delayMinutes, cancellationToken);

    private static async Task SchedulePowerOperationAsync(
        string operationArgument,
        string operationName,
        int delayMinutes,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException($"Linux is required to schedule a {operationName}.");

        var executable = ShutdownPaths.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("The Linux shutdown executable was not found.");

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add(operationArgument);
        startInfo.ArgumentList.Add($"+{delayMinutes}");
        startInfo.ArgumentList.Add($"{operationName} requested through TestServer");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start the {operationName} command.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"The {operationName} command did not finish within 10 seconds.");
        }

        var output = (await standardOutput).Trim();
        var error = (await standardError).Trim();
        if (process.ExitCode == 0)
            return;

        throw new InvalidOperationException(string.IsNullOrEmpty(error)
            ? $"The {operationName} command exited with code {process.ExitCode}: {output}"
            : $"The {operationName} command failed: {error}");
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process exited between the timeout and the kill attempt.
        }
    }
}
