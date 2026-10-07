using System;
using System.Diagnostics;
using System.IO;

namespace ElysiaPet.Services;

/// <summary>
/// 外部程序启动器。旧版用 <c>subprocess.Popen(cmd, shell=True)</c>，
/// shell 启动既慢又容易被安全软件拦截；这里统一走 Process.Start，
/// 系统工具用 UseShellExecute 让 Windows 自己解析（calc、msc 控制台等）。
/// </summary>
public static class ProcessLauncher
{
    /// <summary>启动系统菜单/命令行工具（斜杠指令）。</summary>
    public static bool TryLaunchSystemTool(string target, out string error)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
            });
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            error = ex.Message;
            AppLog.Warn($"启动系统工具 {target} 失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>启动用户绑定的快捷应用（.exe 或任意可打开文件）。</summary>
    public static bool TryLaunchApp(string path, out string error)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            error = "文件不存在";
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = Path.GetDirectoryName(path) ?? AppPaths.ExecutableDirectory,
                UseShellExecute = true,
            });
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            error = ex.Message;
            AppLog.Warn($"启动应用 {path} 失败: {ex.Message}");
            return false;
        }
    }
}
