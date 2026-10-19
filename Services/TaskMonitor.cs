using System.Diagnostics;
using System.Text.Json;

public sealed class TaskMonitor : IResult
{
    private static readonly SemaphoreSlim Sessions = new(4, 4);

    public async Task ExecuteAsync(HttpContext context)
    {
        if (!OperatingSystem.IsLinux())
        {
            await Results.Json(new ServerMessage("TaskM requires Linux."), statusCode: 501)
                .ExecuteAsync(context);
            return;
        }

        var executable = new[] { "/usr/bin/top", "/bin/top" }.FirstOrDefault(File.Exists);
        if (executable is null)
        {
            await Results.Json(new ServerMessage("The top executable is not installed."), statusCode: 503)
                .ExecuteAsync(context);
            return;
        }

        if (!await Sessions.WaitAsync(0, context.RequestAborted))
        {
            await Results.Json(new ServerMessage("Too many active TaskM sessions."), statusCode: 429)
                .ExecuteAsync(context);
            return;
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(
            context.RequestAborted,
            context.RequestServices.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
        using var process = new Process();
        try
        {
            process.StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in new[] { "-b", "-d", "1", "-w", "200" })
                process.StartInfo.ArgumentList.Add(argument);
            process.StartInfo.Environment["LC_ALL"] = "C";
            process.Start();
            // Drain stderr concurrently so it cannot block the child process.
            var errors = process.StandardError.ReadToEndAsync(stop.Token);

            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache, no-store";
            context.Response.Headers["X-Accel-Buffering"] = "no";
            await context.Response.StartAsync(stop.Token);

            while (await process.StandardOutput.ReadLineAsync(stop.Token) is { } line)
            {
                var payload = JsonSerializer.Serialize(new { line });
                await context.Response.WriteAsync($"event: top\ndata: {payload}\n\n", stop.Token);
                await context.Response.Body.FlushAsync(stop.Token);
            }

            await process.WaitForExitAsync(stop.Token);
            var error = await errors;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"top exited with code {process.ExitCode}: {error}");
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception exception)
        {
            context.RequestServices.GetRequiredService<ILogger<TaskMonitor>>()
                .LogError(exception, "TaskM streaming failed.");
            if (!context.Response.HasStarted)
                await Results.Problem(title: "Unable to start TaskM.", statusCode: 500).ExecuteAsync(context);
            else
                context.Abort();
        }
        finally
        {
            try
            {
                if (process.Id > 0 && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
            catch (InvalidOperationException) { }
            finally { Sessions.Release(); }
        }
    }
}
