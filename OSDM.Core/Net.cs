using System.Net;
using System.Net.Http.Headers;

namespace OSDM.Core;

/// <summary>Shared HttpClient and request building. Redirects are followed by hand so cookies are not sent to other sites.</summary>
public static class Net
{
    public const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

    static readonly object Lock = new();
    static HttpClient? _client;
    static string? _clientProxy;

    public static HttpClient Client(string proxy)
    {
        lock (Lock)
        {
            if (_client == null || _clientProxy != proxy)
            {
                var handler = new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.None, // byte ranges must match the bytes on disk
                    UseCookies = false,
                    MaxConnectionsPerServer = 64,
                    ConnectTimeout = TimeSpan.FromSeconds(20),
                    PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                };
                if (!string.IsNullOrWhiteSpace(proxy))
                {
                    handler.Proxy = new WebProxy(proxy.Trim()); // http://, https:// and socks4/5:// are supported
                    handler.UseProxy = true;
                }
                _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
                _clientProxy = proxy;
            }
            return _client;
        }
    }

    /// <summary>Sends a GET for bytes from..to (to = null means "to the end"; from = null means no Range header).</summary>
    public static async Task<HttpResponseMessage> SendAsync(DownloadItem item, string proxy, long? from, long? to,
        bool useIfRange, CancellationToken ct)
    {
        var client = Client(proxy);
        var uri = new Uri(item.Url);
        string firstSite = Site(uri.Host);
        string? cookies = item.Cookies;

        for (int hop = 0; hop < 15; hop++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.TryAddWithoutValidation("User-Agent", item.UserAgent ?? DefaultUserAgent);
            req.Headers.TryAddWithoutValidation("Accept", "*/*");
            if (!string.IsNullOrEmpty(item.Referrer)) req.Headers.TryAddWithoutValidation("Referer", item.Referrer);
            if (!string.IsNullOrEmpty(cookies)) req.Headers.TryAddWithoutValidation("Cookie", cookies);
            if (from != null) req.Headers.Range = new RangeHeaderValue(from, to);
            if (useIfRange && from > 0) AddIfRange(req, item);

            var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)resp.StatusCode is 301 or 302 or 303 or 307 or 308 && resp.Headers.Location is { } location)
            {
                resp.Dispose();
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (Site(uri.Host) != firstSite) cookies = null;
                continue;
            }
            return resp;
        }
        throw new HttpRequestException("Too many redirects.");
    }

    static void AddIfRange(HttpRequestMessage req, DownloadItem item)
    {
        try
        {
            if (item.ETag != null)
                req.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue(item.ETag));
            else if (item.LastModified != null && DateTimeOffset.TryParse(item.LastModified, out var lm))
                req.Headers.IfRange = new RangeConditionHeaderValue(lm);
        }
        catch (FormatException) { }
    }

    /// <summary>Last two labels of a host name ("dl.example.com" -> "example.com"); cookies stay within it.</summary>
    static string Site(string host)
    {
        var parts = host.ToLowerInvariant().Split('.');
        return parts.Length <= 2 ? host.ToLowerInvariant() : string.Join('.', parts[^2..]);
    }

    public static bool IsRetryable(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } code } =>
            (int)code >= 500 || code is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests,
        HttpRequestException or IOException or OperationCanceledException => true, // OCE here = read timeout
        _ => false,
    };
}
