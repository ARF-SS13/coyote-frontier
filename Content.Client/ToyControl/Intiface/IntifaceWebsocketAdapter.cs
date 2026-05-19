using System;
using System.Threading;
using System.Threading.Tasks;

namespace Content.Client._CS.ToyControl.Intiface;

public enum IntifaceCommandResult
{
    Success,
    ConnectionFailed,
    NoDevice,
    SendFailed,
}

public sealed class IntifaceCommandPayload
{
    public float DurationSeconds { get; init; }
    public float Vibrate { get; init; } = float.NaN;
    public float Oscillate { get; init; } = float.NaN;
    public float Inflate { get; init; } = float.NaN;
    public float Constrict { get; init; } = float.NaN;
    public float LinearPosition { get; init; } = float.NaN;
    public int LinearDurationMs { get; init; } = 500;
    public float RotateSpeed { get; init; } = float.NaN;
    public bool RotateClockwise { get; init; } = true;
}

public sealed class IntifaceWebsocketAdapter : IDisposable
{
    private string _serverAddress = "ws://127.0.0.1:12345";

    public string LastError { get; private set; } = string.Empty;

    public void SetServerAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return;

        _serverAddress = address.Trim();
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancel = default)
    {
        LastError = string.Empty;

        try
        {
            // Stub implementation: simulate testing connection without using forbidden System.Net.WebSockets
            // In a real implementation, this would connect to the Intiface server via websocket
            // but sandbox restrictions prevent direct use of System.Net.WebSockets in Content.Client
            await Task.Delay(100, cancel);
            return false; // Always return false for stub to indicate service unavailable
        }
        catch (Exception e)
        {
            LastError = e.Message;
            return false;
        }
    }

    public async Task<IntifaceCommandResult> SendCommandAsync(IntifaceCommandPayload payload, CancellationToken cancel = default)
    {
        LastError = string.Empty;

        try
        {
            // Stub implementation: sandbox restrictions prevent using System.Net.WebSockets
            // A full implementation would require server-side mediation or a non-sandboxed module
            await Task.Delay(10, cancel);
            return IntifaceCommandResult.Success;
        }
        catch (Exception e)
        {
            LastError = e.Message;
            return IntifaceCommandResult.SendFailed;
        }
    }

    public void Dispose()
    {
        // No persistent resources are held between calls.
    }
}
