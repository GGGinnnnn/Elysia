using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ElysiaPet.Models;

namespace ElysiaPet.Services;

/// <summary>
/// 配置读写服务：原子写入 + 串行化，杜绝旧版「多线程同时写 json 把文件写坏」的问题。
/// 配置文件固定放在 exe（或开发目录）旁边，不会被系统临时目录清掉。
/// </summary>
public sealed class ConfigService
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;

    public ConfigService(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(AppPaths.ExecutableDirectory, "config.json");
    }

    public string FilePath => _path;

    /// <summary>当前内存中的配置。仅允许在 UI 线程修改。</summary>
    public AppConfig Current { get; private set; } = new();

    /// <summary>从磁盘载入配置；文件不存在或损坏时回退到默认配置并写出一份新的。</summary>
    public async Task<AppConfig> LoadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(_path))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_path).ConfigureAwait(false);
                    var config = JsonSerializer.Deserialize<AppConfig>(json, ReadOptions);
                    if (config is not null)
                    {
                        config.Normalize();
                        Current = config;
                        return Current;
                    }
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    // 配置损坏：备份一份，然后用默认配置重建，绝不静默丢数据
                    BackupBrokenFile(ex);
                }
            }

            Current = new AppConfig();
            Current.Normalize();
            return Current;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>保存配置（先写临时文件再替换，避免写到一半崩溃导致配置全丢）。</summary>
    public async Task SaveAsync(AppConfig? config = null)
    {
        var target = config ?? Current;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(target, WriteOptions);
            var temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, json).ConfigureAwait(false);

            if (File.Exists(_path)) File.Replace(temp, _path, null);
            else File.Move(temp, _path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"写入配置文件失败: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private void BackupBrokenFile(Exception ex)
    {
        AppLog.Warn($"读取配置失败，将重建配置。原因: {ex.Message}");
        try
        {
            var backup = _path + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(_path, backup, overwrite: true);
        }
        catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"备份损坏配置失败: {copyEx.Message}");
        }
    }
}

/// <summary>程序运行目录与资源目录的统一解析（兼容单文件发布与源码运行）。</summary>
public static class AppPaths
{
    /// <summary>exe 真正所在的物理目录，配置文件和日志都落在这里。</summary>
    public static string ExecutableDirectory { get; } = ResolveExecutableDirectory();

    /// <summary>素材目录（GIF / 图标）。单文件发布会先解包到临时目录，所以这里优先用 BaseDirectory。</summary>
    public static string AssetDirectory { get; } = ResolveAssetDirectory();

    public static string Gif(string state) => System.IO.Path.Combine(AssetDirectory, "Assets", "gifs", state + ".gif");

    public static string Icon => System.IO.Path.Combine(AssetDirectory, "Assets", "icon.ico");

    /// <summary>
    /// 内嵌素材的 pack URI。所有 GIF 都以 Resource 形式编译进程序集，
    /// 这样即使旁边没有任何素材文件，单文件 exe 也能正常显示桌宠。
    /// </summary>
    public static Uri EmbeddedGifUri(string state) =>
        new($"pack://application:,,,/Assets/gifs/{state}.gif", UriKind.Absolute);

    /// <summary>内嵌图标的 pack URI，供托盘图标在没有外部文件时回退使用。</summary>
    public static Uri EmbeddedIconUri { get; } = new("pack://application:,,,/Assets/icon.ico", UriKind.Absolute);

    private static string ResolveExecutableDirectory()
    {
        var exe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe))
        {
            var dir = System.IO.Path.GetDirectoryName(exe);
            if (!string.IsNullOrEmpty(dir)) return dir;
        }
        return AppContext.BaseDirectory;
    }

    private static string ResolveAssetDirectory()
    {
        // 单文件发布时 BaseDirectory 指向解包目录；源码运行时它就是输出目录，素材已被复制进来。
        var baseDir = AppContext.BaseDirectory;
        if (Directory.Exists(System.IO.Path.Combine(baseDir, "Assets", "gifs"))) return baseDir;
        return ExecutableDirectory;
    }
}
