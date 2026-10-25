using System.Diagnostics;

namespace OSDM.Core;

/// <summary>Token bucket shared by every connection, so the limit applies to total speed.</summary>
public sealed class SpeedLimiter
{
    readonly object _lock = new();
    long _bytesPerSecond;
    double _tokens;
    long _last = Stopwatch.GetTimestamp();

    public long BytesPerSecond
    {
        get => Interlocked.Read(ref _bytesPerSecond);
        set => Interlocked.Exchange(ref _bytesPerSecond, Math.Max(0, value));
    }

    public async ValueTask WaitAsync(int bytes, CancellationToken ct)
    {
        while (true)
        {
            long limit = BytesPerSecond;
            if (limit <= 0) return;

            double waitSeconds;
            lock (_lock)
            {
                long now = Stopwatch.GetTimestamp();
                _tokens = Math.Min(limit, _tokens + (now - _last) * (double)limit / Stopwatch.Frequency);
                _last = now;
                // A full bucket lets one oversized chunk through, so chunks bigger than the limit still pass.
                if (_tokens >= bytes || _tokens >= limit)
                {
                    _tokens -= bytes;
                    return;
                }
                waitSeconds = (bytes - _tokens) / limit;
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(waitSeconds, 0.01, 1.0)), ct);
        }
    }
}
