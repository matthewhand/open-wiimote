namespace OpenWiimote.Core.Protocol;

public sealed record WiimoteWatcherOptions
{
    public bool AutoConnect { get; init; } = true;
    public int PollIntervalMs { get; init; } = 1000;
    public int ReconnectDelayMs { get; init; } = 3000;
    public int StaleTimeoutMs { get; init; } = 6000;
    public int StatusRequestIntervalMs { get; init; } = 1000;
    public byte LedMask { get; init; } = WiimoteProtocol.Led1;
    public bool RumbleOnConnect { get; init; }

    /// <summary>Switch the IR camera of the remote on when the connection starts.</summary>
    public bool IrEnabled { get; init; }

    public WiimoteWatcherOptions Normalize()
    {
        return this with
        {
            PollIntervalMs = Math.Clamp(PollIntervalMs, 250, 10000),
            ReconnectDelayMs = Math.Clamp(ReconnectDelayMs, 500, 60000),
            StaleTimeoutMs = Math.Clamp(StaleTimeoutMs, 2000, 60000),
            StatusRequestIntervalMs = Math.Clamp(StatusRequestIntervalMs, 200, 10000),
            LedMask = (byte)(LedMask & 0xF0)
        };
    }
}
