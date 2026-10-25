using System.Net;
using System.Text;
using System.Text.Json;

namespace OSDM.Core;

/// <summary>
/// Small local HTTP server the browser extension talks to (http://localhost:&lt;port&gt;).
/// Only requests from browser extensions (or local programs, which send no Origin) are accepted,
/// so ordinary web pages cannot add downloads.
/// </summary>
public sealed class BrowserBridge : IDisposable
{
    public const string Version = "0.3.0";
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    readonly HttpListener _listener = new();

    /// <summary>Called for every download the browser sends. Return false to refuse it.</summary>
    public Func<AddRequest, bool>? OnAdd { get; set; }

    /// <summary>Called when the extension's "Open OSDM" button is clicked.</summary>
    public Action? OnShow { get; set; }

    /// <summary>Lists the qualities a video page offers, for the extension's quality menu.</summary>
    public Func<AddRequest, Task<VideoInfo>>? OnFormats { get; set; }

    public string? StartError { get; private set; }
    public int Port { get; private set; }

    public bool Start(int port)
    {
        Port = port;
        _listener.Prefixes.Add($"http://localhost:{port}/");
        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            StartError = ex.Message;
            return false;
        }
        _ = Task.Run(LoopAsync);
        return true;
    }

    async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (Exception) { break; }
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    static bool TrustedOrigin(string? origin) =>
        origin == null ||
        origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
        origin.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase) ||
        origin.StartsWith("extension://", StringComparison.OrdinalIgnoreCase);

    async Task HandleAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        try
        {
            string? origin = req.Headers["Origin"];
            if (!TrustedOrigin(origin))
            {
                res.StatusCode = 403;
                return;
            }
            if (origin != null)
            {
                res.AddHeader("Access-Control-Allow-Origin", origin);
                res.AddHeader("Access-Control-Allow-Headers", "Content-Type");
                res.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            }

            switch (req.HttpMethod, req.Url?.AbsolutePath)
            {
                case ("OPTIONS", _):
                    res.StatusCode = 204;
                    break;
                case ("GET", "/ping"):
                    await WriteJsonAsync(res, new { app = "OSDM", version = Version });
                    break;
                case ("POST", "/add"):
                {
                    if (await ReadRequestAsync(req, res) is not { } add) break;
                    bool accepted = OnAdd?.Invoke(add) ?? false;
                    await WriteJsonAsync(res, new { ok = accepted });
                    break;
                }
                case ("POST", "/formats"):
                {
                    if (await ReadRequestAsync(req, res) is not { } page) break;
                    if (OnFormats == null) { res.StatusCode = 404; break; }
                    try
                    {
                        var info = await OnFormats(page);
                        await WriteJsonAsync(res, new { ok = true, title = info.Title, qualities = info.Qualities });
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException or System.ComponentModel.Win32Exception)
                    {
                        await WriteJsonAsync(res, new { ok = false, error = ex.Message });
                    }
                    break;
                }
                case ("POST", "/show"):
                    OnShow?.Invoke();
                    await WriteJsonAsync(res, new { ok = OnShow != null });
                    break;
                default:
                    res.StatusCode = 404;
                    break;
            }
        }
        catch (JsonException)
        {
            res.StatusCode = 400;
        }
        catch (Exception)
        {
            try { res.StatusCode = 500; } catch { }
        }
        finally
        {
            try { res.Close(); } catch { }
        }
    }

    /// <summary>Reads an <see cref="AddRequest"/> body with an http(s) URL, or sets an error status and returns null.</summary>
    static async Task<AddRequest?> ReadRequestAsync(HttpListenerRequest req, HttpListenerResponse res)
    {
        if (req.ContentLength64 > 1_000_000) { res.StatusCode = 413; return null; }
        using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
        var add = JsonSerializer.Deserialize<AddRequest>(await reader.ReadToEndAsync(), Json);
        bool valid = add != null && Uri.TryCreate(add.Url, UriKind.Absolute, out var uri) &&
                     (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        if (!valid) res.StatusCode = 400;
        return valid ? add : null;
    }

    static async Task WriteJsonAsync(HttpListenerResponse res, object value)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        res.ContentType = "application/json";
        res.ContentLength64 = body.Length;
        await res.OutputStream.WriteAsync(body);
    }

    /// <summary>Used by a second copy of OSDM to hand its command-line URL to the running copy.</summary>
    public static async Task<bool> SendToRunningAsync(int port, AddRequest request)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var content = new StringContent(JsonSerializer.Serialize(request, Json), Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync($"http://localhost:{port}/add", content);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        try { _listener.Stop(); _listener.Close(); } catch { }
    }
}
