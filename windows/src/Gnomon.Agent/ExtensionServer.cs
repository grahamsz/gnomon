using System.Net;
using System.Text;
using System.Text.Json;
using Gnomon.Core;

namespace Gnomon.Agent;

public sealed class ExtensionServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly AgentConfig _config;
    private readonly AgentStatus _status;
    private CancellationTokenSource? _cts;
    public string? CurrentDomain { get; private set; }
    public DateTimeOffset? LastSeen { get; private set; }

    public ExtensionServer(AgentConfig config, AgentStatus status)
    {
        _config = config; _status = status;
        _listener.Prefixes.Add($"http://127.0.0.1:{config.ExtensionPort}/");
    }

    public Task StartAsync(CancellationToken parent)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(parent);
        _listener.Start();
        return Task.Run(() => LoopAsync(_cts.Token), _cts.Token);
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync().WaitAsync(token); }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) when (token.IsCancellationRequested) { break; }
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            context.Response.Headers["Access-Control-Allow-Headers"] = "content-type";
            if (context.Request.HttpMethod == "OPTIONS")
            {
                context.Response.StatusCode = 204; context.Response.Close(); continue;
            }
            if (context.Request.HttpMethod == "POST" && context.Request.Url?.AbsolutePath == "/active-domain")
            {
                try
                {
                    using var document = await JsonDocument.ParseAsync(context.Request.InputStream, cancellationToken: token);
                    // The schema intentionally accepts one privacy-safe field only.
                    var domain = document.RootElement.GetProperty("domain").GetString()?.Trim().ToLowerInvariant();
                    if (!string.IsNullOrWhiteSpace(domain) && !domain.Contains('/') && !domain.Contains(':'))
                    {
                        CurrentDomain = domain; LastSeen = DateTimeOffset.UtcNow;
                        _status.Update(x => x with { ExtensionReachable = true });
                        context.Response.StatusCode = 204;
                    }
                    else context.Response.StatusCode = 400;
                }
                catch { context.Response.StatusCode = 400; }
            }
            else if (context.Request.HttpMethod == "GET" && context.Request.Url?.AbsolutePath == "/status")
            {
                context.Response.ContentType = "application/json";
                var bytes = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    agent = "up", rulesVersion = _status.Snapshot.RulesVersion,
                    domain = CurrentDomain, category = _status.Snapshot.Category
                });
                await context.Response.OutputStream.WriteAsync(bytes, token);
            }
            else context.Response.StatusCode = 404;
            context.Response.Close();
        }
    }

    public ValueTask DisposeAsync()
    {
        _cts?.Cancel(); _listener.Close(); _cts?.Dispose(); return ValueTask.CompletedTask;
    }
}
