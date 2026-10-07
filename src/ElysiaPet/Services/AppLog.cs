using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ElysiaPet.Services;

/// <summary>
/// 轻量日志：旧版只用 print()，打包成窗口程序后控制台不可见，出错时完全无从查起。
/// 这里同时写 Debug 输出和运行目录下的 elysia.log（按天切分，超过 1MB 自动轮转）。
/// </summary>
public static class AppLog
{
    private const long MaxBytes = 1024 * 1024;

    private static readonly BlockingCollection<string> Queue = new(new ConcurrentQueue<string>());
    private static readonly string LogPath = System.IO.Path.Combine(AppPaths.ExecutableDirectory, "elysia.log");
    private static readonly Task Writer = Task.Factory.StartNew(WriteLoop, TaskCreationOptions.LongRunning);

    public static void Info(string message) => Enqueue("INFO ", message);

    public static void Warn(string message) => Enqueue("WARN ", message);

    public static void Error(string message) => Enqueue("ERROR", message);

    public static void Error(string message, Exception ex) => Enqueue("ERROR", $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Enqueue(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        Debug.WriteLine(line);
        try
        {
            if (!Queue.IsAddingCompleted) Queue.Add(line);
        }
        catch (InvalidOperationException)
        {
            // 队列已关闭（程序退出中），忽略即可
        }
    }

    private static void WriteLoop()
    {
        foreach (var line in Queue.GetConsumingEnumerable())
        {
            try
            {
                RotateIfNeeded();
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine("日志写入失败: " + ex.Message);
            }
        }
    }

    private static void RotateIfNeeded()
    {
        var info = new FileInfo(LogPath);
        if (!info.Exists || info.Length < MaxBytes) return;

        var archived = System.IO.Path.Combine(
            AppPaths.ExecutableDirectory,
            $"elysia-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        try
        {
            File.Move(LogPath, archived, overwrite: true);
        }
        catch (IOException)
        {
            // 轮转失败不影响主流程，下一次写入继续追加
        }
    }

    /// <summary>退出前冲刷日志队列。</summary>
    public static void Shutdown()
    {
        try
        {
            Queue.CompleteAdding();
            Writer.Wait(TimeSpan.FromSeconds(1));
        }
        catch (Exception ex) when (ex is ObjectDisposedException or AggregateException)
        {
            // 退出路径上不再追究
        }
    }
}
