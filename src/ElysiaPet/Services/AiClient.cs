using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ElysiaPet.Models;

namespace ElysiaPet.Services;

/// <summary>
/// 大模型客户端。旧版每个请求都新建一个 QThread + requests.post（verify=False），
/// 这里改成 .NET 原生的 HttpClient + async/await：复用连接、可取消、不阻塞界面、
/// 并且所有异常都被归一化成可读的中文错误。
/// </summary>
public sealed class AiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;

    /// <summary>
    /// <paramref name="handler"/> 仅供自检注入假的 HTTP 处理器，
    /// 正常运行时传 null，走真实的 <see cref="SocketsHttpHandler"/>。
    /// </summary>
    public AiClient(HttpMessageHandler? handler = null)
    {
        _http = new HttpClient(handler ?? new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(8),
        })
        {
            Timeout = Timeout.InfiniteTimeSpan, // 由每次调用的 CancellationToken 控制超时
        };
    }

    /// <summary>带历史上下文的对话请求。</summary>
    public Task<string> ChatAsync(
        AppConfig config,
        string prompt,
        IReadOnlyList<ChatTurn> history,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>
        {
            new("system", config.RolePreset),
        };

        foreach (var turn in history)
        {
            if (!string.IsNullOrWhiteSpace(turn.User)) messages.Add(new ChatMessage("user", turn.User));
            if (!string.IsNullOrWhiteSpace(turn.Pet)) messages.Add(new ChatMessage("assistant", turn.Pet));
        }

        messages.Add(new ChatMessage("user", prompt));
        return SendAsync(config, messages, 1.0, timeout, cancellationToken);
    }

    /// <summary>待机时让模型即兴说一句短句。</summary>
    public Task<string> IdleLineAsync(
        AppConfig config,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var name = config.UserInfo?.Name ?? "主人";
        var prompt =
            $"请以你的角色身份，对主人【{name}】说一句简短的日常待机冒泡提示语" +
            "（比如提醒休息、关心、傲娇或问候）。字数严格控制在30字以内，语气要极其自然并符合你的人设。" +
            "不要带任何前缀、旁白或括号解释，直接输出你对她说的话本身。";

        var messages = new List<ChatMessage>
        {
            new("system", config.RolePreset),
            new("user", prompt),
        };

        return SendAsync(config, messages, 0.85, timeout, cancellationToken);
    }

    private async Task<string> SendAsync(
        AppConfig config,
        List<ChatMessage> messages,
        double temperature,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new AiException("还没有绑定 API Key，先去后台「系统设置」里填一个吧~");

        var payload = new ChatRequest(config.Model, messages, temperature);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        using var request = new HttpRequestMessage(HttpMethod.Post, config.ApiBaseUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey.Trim());

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, linked.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiException("连接超时了，妖精小姐的魔法信号好像有点弱……稍后再试试？");
        }
        catch (HttpRequestException ex)
        {
            throw new AiException($"连不上服务器（{ex.Message}）。检查一下网络代理或者接口地址吧？", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var hint = (int)response.StatusCode switch
                {
                    401 => "API Key 好像不对哦。",
                    402 => "账户余额不足啦。",
                    429 => "请求太频繁了，让我喘口气~",
                    >= 500 => "服务器那边出了点小状况。",
                    _ => "接口返回了非预期状态。",
                };
                AppLog.Warn($"AI 接口返回 {(int)response.StatusCode}: {Truncate(body, 300)}");
                throw new AiException($"{(int)response.StatusCode} {hint}");
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<ChatResponse>(body, JsonOptions);
                var content = parsed?.Choices is { Count: > 0 } choices ? choices[0].Message?.Content : null;
                if (string.IsNullOrWhiteSpace(content))
                    throw new AiException("模型这次什么都没说，再问一次试试？");
                return content!;
            }
            catch (JsonException ex)
            {
                AppLog.Warn($"解析 AI 响应失败: {Truncate(body, 300)}");
                throw new AiException("返回内容看不懂呢，可能是接口地址填错了。", ex);
            }
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    public void Dispose() => _http.Dispose();

    // ---------------- 私有 DTO ----------------

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] List<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature);

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")]
        public ResponseMessage? Message { get; set; }
    }

    private sealed class ResponseMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}

/// <summary>面向用户的、消息已经很友好的 AI 异常。</summary>
public sealed class AiException : Exception
{
    public AiException(string message) : base(message) { }

    public AiException(string message, Exception inner) : base(message, inner) { }
}
