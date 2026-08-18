using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

public static class ServerDiagnostics
{
    public static object GetServerInfo()
    {
        var memory = GetMemoryInfo();
        var disk = GetDiskInfo();

        return new
        {
            hostname = Environment.MachineName,
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.OSArchitecture.ToString(),
            processorCount = Environment.ProcessorCount,
            totalMemoryMb = memory.TotalMb,
            availableMemoryMb = memory.AvailableMb,
            memoryUsedPercent = memory.UsedPercent,
            diskTotalGb = disk.TotalGb,
            diskFreeGb = disk.FreeGb,
            uptime = GetServerUptime().ToString(@"dd\.hh\:mm\:ss"),
            dotnetVersion = Environment.Version.ToString(),
            serverTimeUtc = DateTimeOffset.UtcNow
        };
    }

    public static object GetDiagnostics()
    {
        var memory = GetMemoryInfo();
        var disk = GetDiskInfo();
        var uptime = GetServerUptime();
        using var process = Process.GetCurrentProcess();

        var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(x => x.OperationalStatus == OperationalStatus.Up)
            .Select(x => new
            {
                name = x.Name,
                type = x.NetworkInterfaceType.ToString(),
                speedMbps = x.Speed > 0 ? Math.Round(x.Speed / 1_000_000.0, 2) : 0,
                addresses = x.GetIPProperties().UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString())
                    .ToArray()
            })
            .ToArray();

        var usedDiskGb = disk.TotalGb - disk.FreeGb;

        return new
        {
            status = "OK",
            server = new
            {
                hostname = Environment.MachineName,
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.OSArchitecture.ToString(),
                framework = RuntimeInformation.FrameworkDescription,
                processorCount = Environment.ProcessorCount,
                serverTimeUtc = DateTimeOffset.UtcNow
            },
            memory = new
            {
                totalMb = memory.TotalMb,
                availableMb = memory.AvailableMb,
                usedMb = Math.Round(memory.TotalMb - memory.AvailableMb, 2),
                usedPercent = memory.UsedPercent
            },
            disk = new
            {
                totalGb = disk.TotalGb,
                freeGb = disk.FreeGb,
                usedGb = Math.Round(usedDiskGb, 2),
                usedPercent = disk.TotalGb > 0
                    ? Math.Round(usedDiskGb / disk.TotalGb * 100, 2)
                    : 0
            },
            uptime = new
            {
                totalSeconds = Math.Round(uptime.TotalSeconds),
                formatted = uptime.ToString(@"dd\.hh\:mm\:ss")
            },
            process = new
            {
                pid = process.Id,
                processName = process.ProcessName,
                memoryMb = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 2),
                startedAtUtc = process.StartTime.ToUniversalTime(),
                threads = process.Threads.Count
            },
            network = networkInterfaces
        };
    }

    private static (double TotalMb, double AvailableMb, double UsedPercent) GetMemoryInfo()
    {
        long totalBytes;
        long availableBytes;

        if (OperatingSystem.IsLinux() && File.Exists("/proc/meminfo"))
        {
            var values = File.ReadLines("/proc/meminfo")
                .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(parts => parts.Length >= 2 && parts[0] is "MemTotal:" or "MemAvailable:")
                .ToDictionary(parts => parts[0], parts => long.Parse(parts[1]) * 1024);
            totalBytes = values.GetValueOrDefault("MemTotal:");
            availableBytes = values.GetValueOrDefault("MemAvailable:");
        }
        else
        {
            var memory = GC.GetGCMemoryInfo();
            totalBytes = memory.TotalAvailableMemoryBytes;
            availableBytes = Math.Max(0, totalBytes - memory.MemoryLoadBytes);
        }

        if (totalBytes <= 0)
            return (0, 0, 0);

        var totalMb = totalBytes / 1024.0 / 1024.0;
        var availableMb = availableBytes / 1024.0 / 1024.0;
        var usedPercent = (totalBytes - availableBytes) / (double)totalBytes * 100;

        return (
            Math.Round(totalMb, 2),
            Math.Round(availableMb, 2),
            Math.Round(usedPercent, 2));
    }

    private static (double TotalGb, double FreeGb) GetDiskInfo()
    {
        var root = Path.GetPathRoot(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException("Unable to determine the application drive.");
        var drive = new DriveInfo(root);

        return (
            Math.Round(drive.TotalSize / 1024.0 / 1024.0 / 1024.0, 2),
            Math.Round(drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0, 2));
    }

    private static TimeSpan GetServerUptime() =>
        TimeSpan.FromMilliseconds(Environment.TickCount64);
}
