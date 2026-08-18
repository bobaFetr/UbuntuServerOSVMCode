using System.Diagnostics;

public static class LinuxShutdown
{
    private static readonly string[] ShutdownPaths =
    [
        "/usr/sbin/shutdown",
        "/sbin/shutdown",
        "/usr/bin/shutdown"
    ];

    public static void Schedule(int delayMinutes)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Linux is required to schedule a shutdown.");

        var executable = ShutdownPaths.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("The Linux shutdown executable was not found.");

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        startInfo.ArgumentList.Add("--poweroff");
        startInfo.ArgumentList.Add($"+{delayMinutes}");
        startInfo.ArgumentList.Add("Shutdown requested through TestServer");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the shutdown command.");

        process.WaitForExit();
        if (process.ExitCode == 0)
            return;

        var error = process.StandardError.ReadToEnd().Trim();
        throw new InvalidOperationException(
            string.IsNullOrEmpty(error)
                ? $"The shutdown command exited with code {process.ExitCode}."
                : $"The shutdown command failed: {error}");
    }
}
