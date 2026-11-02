using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace N0vaCacheWallpaperTool;

internal static partial class N0vaStartupRepairer
{
    public static N0vaStartupRepairResult Repair(string hostDirectory)
    {
        string executable = Path.Combine(hostDirectory, "N0vaDesktop.exe");
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException(
                "找不到 N0vaDesktop.exe，无法修复启动状态。",
                executable);
        }

        List<string> actions = [];
        void Record(string action)
        {
            actions.Add(action);
            ToolLogger.Info("修复启动：" + action);
        }

        string? serviceName = FindServiceName();
        bool serviceRestarted = false;
        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            Record($"找到人工桌面服务：{serviceName}");
            ScResult stopResult = RunSc("stop", serviceName);
            Record(
                stopResult.ExitCode == 0
                    ? $"已发送停止服务请求：{serviceName}"
                    : $"停止服务返回代码 {stopResult.ExitCode}：{Compact(stopResult.Output)}");
            WaitForServiceState(serviceName, running: false, TimeSpan.FromSeconds(20));
        }
        else
        {
            Record("没有找到 N0va 服务，将只清理进程并重新启动程序");
        }

        StopProcesses("DPPlayer", Record);
        StopProcesses("N0vaDesktop", Record);

        if (!string.IsNullOrWhiteSpace(serviceName))
        {
            ScResult startResult = RunSc("start", serviceName);
            Record(
                startResult.ExitCode == 0
                    ? $"已发送启动服务请求：{serviceName}"
                    : $"启动服务返回代码 {startResult.ExitCode}：{Compact(startResult.Output)}");
            serviceRestarted = WaitForServiceState(
                serviceName,
                running: true,
                TimeSpan.FromSeconds(20));
            Record(serviceRestarted ? "人工桌面服务已运行" : "人工桌面服务未进入运行状态");
            Thread.Sleep(1500);
        }

        if (!IsProcessRunning("N0vaDesktop"))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "explorer.exe"),
                Arguments = "\"" + executable + "\"",
                UseShellExecute = true
            });
            Record("服务没有自动拉起程序，已手动启动 N0vaDesktop.exe");
        }

        bool n0vaStarted = WaitForProcess(
            "N0vaDesktop",
            TimeSpan.FromSeconds(20));
        bool playerStarted = WaitForProcess(
            "DPPlayer",
            TimeSpan.FromSeconds(20));
        Record(n0vaStarted ? "N0vaDesktop.exe 已运行" : "N0vaDesktop.exe 未启动");
        Record(playerStarted ? "DPPlayer.exe 已运行" : "DPPlayer.exe 未启动");

        return new N0vaStartupRepairResult
        {
            ServiceName = serviceName ?? "",
            ServiceRestarted = serviceRestarted,
            N0vaStarted = n0vaStarted,
            PlayerStarted = playerStarted,
            Actions = actions
        };
    }

    internal static string? FindServiceName()
    {
        const string knownName = "N0vaDesktop Service";
        if (RunSc("query", knownName).ExitCode == 0) return knownName;

        try
        {
            using RegistryKey? services = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services");
            if (services is null) return null;

            foreach (string serviceName in services.GetSubKeyNames())
            {
                using RegistryKey? key = services.OpenSubKey(serviceName);
                if (key is null) continue;
                string displayName = key.GetValue("DisplayName") as string ?? "";
                string imagePath = key.GetValue("ImagePath") as string ?? "";
                if (serviceName.Contains("N0va", StringComparison.OrdinalIgnoreCase) ||
                    displayName.Contains("N0va", StringComparison.OrdinalIgnoreCase) ||
                    imagePath.Contains("N0va", StringComparison.OrdinalIgnoreCase))
                {
                    return serviceName;
                }
            }
        }
        catch (Exception ex)
        {
            ToolLogger.Error("查找 N0va 服务失败", ex);
        }
        return null;
    }

    private static void StopProcesses(
        string processName,
        Action<string> record)
    {
        Process[] processes = Process.GetProcessesByName(processName);
        foreach (Process process in processes)
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        record($"已结束进程 {processName}.exe（PID {process.Id}）");
                    }
                }
                catch (Exception ex)
                {
                    record($"结束 {processName}.exe（PID {process.Id}）失败：{ex.Message}");
                }
            }
        }

        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (!IsProcessRunning(processName)) return;
            Thread.Sleep(250);
        }
    }

    private static bool WaitForServiceState(
        string serviceName,
        bool running,
        TimeSpan timeout)
    {
        string expectedState = running ? "RUNNING" : "STOPPED";
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ScResult result = RunSc("query", serviceName);
            if (result.ExitCode == 0 &&
                ServiceStateRegex().Match(result.Output).Groups["state"].Value
                    .Equals(expectedState, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            Thread.Sleep(500);
        }
        return false;
    }

    private static bool WaitForProcess(string processName, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (IsProcessRunning(processName)) return true;
            Thread.Sleep(500);
        }
        return false;
    }

    private static bool IsProcessRunning(string processName)
    {
        bool running = false;
        foreach (Process process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                if (!process.HasExited) running = true;
            }
        }
        return running;
    }

    private static ScResult RunSc(string action, string serviceName)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "sc.exe"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add(action);
        process.StartInfo.ArgumentList.Add(serviceName);
        process.Start();
        string output = process.StandardOutput.ReadToEnd() +
            process.StandardError.ReadToEnd();
        if (!process.WaitForExit(15_000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
            return new ScResult(-1, output + "\nsc.exe 执行超时");
        }
        return new ScResult(process.ExitCode, output.Trim());
    }

    private static string Compact(string value)
    {
        string compact = Regex.Replace(value, @"\s+", " ").Trim();
        return compact.Length <= 300 ? compact : compact[..300];
    }

    private sealed record ScResult(int ExitCode, string Output);

    [GeneratedRegex(
        @"STATE\s*:\s*\d+\s+(?<state>[A-Z_]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex ServiceStateRegex();
}
