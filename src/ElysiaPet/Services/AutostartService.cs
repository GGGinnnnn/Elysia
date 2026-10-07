using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ElysiaPet.Services;

/// <summary>
/// 开机自启动。旧版有两条路径（注册表 Run 键 / 启动文件夹快捷方式），
/// 这里原样保留两种能力，但把路径探测、错误上报和幂等性做扎实了。
/// </summary>
public static class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "ElysiaDesktopPet";
    private const string ShortcutName = "ElysiaDesktopPet.lnk";

    private static string StartupFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Windows\Start Menu\Programs\Startup");

    private static string ShortcutPath => Path.Combine(StartupFolder, ShortcutName);

    /// <summary>要写入自启动的启动命令（开发环境下会带上 dotnet 宿主与 dll 参数）。</summary>
    public static string ResolveLaunchCommand()
    {
        var processPath = Environment.ProcessPath ?? string.Empty;

        // 单文件发布 / 直接运行 exe：直接用 exe 路径
        if (!string.IsNullOrEmpty(processPath) &&
            !Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return Quote(processPath);
        }

        // 开发环境：dotnet run -> dotnet "xxx.dll"
        var assemblyPath = Environment.ProcessPath is not null ? Environment.GetCommandLineArgs()[0] : string.Empty;
        return $"{Quote(processPath)} {Quote(Path.GetFullPath(assemblyPath))}";
    }

    private static string Quote(string value) => string.IsNullOrEmpty(value) ? "\"\"" : $"\"{value}\"";

    /// <summary>注册表方式自启动（HKCU，无需管理员权限）。</summary>
    public static bool SetRegistryAutostart(bool enabled, out string message)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                message = "无法打开注册表启动项（可能被安全软件拦截）";
                return false;
            }

            if (enabled)
            {
                key.SetValue(RunValueName, ResolveLaunchCommand(), RegistryValueKind.String);
                message = "已通过注册表设置开机自启动";
            }
            else
            {
                if (key.GetValue(RunValueName) is not null) key.DeleteValue(RunValueName, throwOnMissingValue: false);
                message = "已取消注册表开机自启动";
            }

            AppLog.Info(message);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            message = $"设置注册表自启动失败：{ex.Message}";
            AppLog.Warn(message);
            return false;
        }
    }

    /// <summary>启动文件夹快捷方式方式自启动（兼容性最好，不需要管理员）。</summary>
    public static bool SetShortcutAutostart(bool enabled, out string message)
    {
        try
        {
            if (!enabled)
            {
                if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
                message = "已移除启动文件夹快捷方式";
                AppLog.Info(message);
                return true;
            }

            Directory.CreateDirectory(StartupFolder);
            if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);

            var target = Environment.ProcessPath ?? string.Empty;
            var needsArguments = Path.GetFileName(target).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase);
            var arguments = needsArguments ? Quote(Path.GetFullPath(Environment.GetCommandLineArgs()[0])) : string.Empty;

            if (!CreateShortcut(ShortcutPath, target, arguments, AppPaths.ExecutableDirectory, out var error))
            {
                message = $"创建快捷方式失败：{error}";
                AppLog.Warn(message);
                return false;
            }

            message = "已通过启动文件夹设置开机自启动";
            AppLog.Info(message);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = $"设置快捷方式自启动失败：{ex.Message}";
            AppLog.Warn(message);
            return false;
        }
    }

    /// <summary>
    /// 通过 PowerShell 的 WScript.Shell COM 生成 .lnk。
    /// 相比手写 IShellLink 互操作代码，这种方式最短也最不容易出错，
    /// 且 CreateNoWindow 保证不会闪出黑框。
    /// </summary>
    private static bool CreateShortcut(string linkPath, string target, string arguments, string workingDirectory, out string error)
    {
        var script =
            "$ws = New-Object -ComObject WScript.Shell; " +
            $"$sc = $ws.CreateShortcut('{Escape(linkPath)}'); " +
            $"$sc.TargetPath = '{Escape(target)}'; " +
            (string.IsNullOrEmpty(arguments) ? string.Empty : $"$sc.Arguments = '{Escape(arguments)}'; ") +
            $"$sc.WorkingDirectory = '{Escape(workingDirectory)}'; " +
            "$sc.WindowStyle = 1; $sc.Description = 'Elysia Desktop Pet'; $sc.Save()";

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + script.Replace("\"", "\\\"") + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            });

            if (process is null)
            {
                error = "无法启动 PowerShell";
                return false;
            }

            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(15000);

            if (File.Exists(linkPath))
            {
                error = string.Empty;
                return true;
            }

            error = string.IsNullOrWhiteSpace(stderr) ? "PowerShell 未生成快捷方式文件" : stderr.Trim();
            return false;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string Escape(string value) => value.Replace("'", "''");

    /// <summary>读取当前自启动状态，用于打开设置页时回显真实情况。</summary>
    public static (bool Registry, bool Shortcut) GetCurrentState()
    {
        var registry = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            registry = key?.GetValue(RunValueName) is not null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            AppLog.Warn($"读取注册表自启动状态失败: {ex.Message}");
        }

        return (registry, File.Exists(ShortcutPath));
    }
}
