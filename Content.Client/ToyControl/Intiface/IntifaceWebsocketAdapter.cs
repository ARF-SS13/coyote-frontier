using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false,
    };

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
            using var socket = await ConnectAsync(cancel);
            await SendServerInfoHandshakeAsync(socket, cancel);
            return true;
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
            using var socket = await ConnectAsync(cancel);
            await SendServerInfoHandshakeAsync(socket, cancel);

            var deviceIndex = await GetFirstDeviceIndexAsync(socket, cancel);
            if (deviceIndex == null)
                return IntifaceCommandResult.NoDevice;

            var command = BuildScalarCommand(deviceIndex.Value, payload);
            if (command == null)
                return IntifaceCommandResult.Success;

            await SendButtplugMessageAsync(socket, command, cancel);
            return IntifaceCommandResult.Success;
        }
        catch (WebSocketException e)
        {
            LastError = e.Message;
            return IntifaceCommandResult.ConnectionFailed;
        }
        catch (Exception e)
        {
            LastError = e.Message;
            return IntifaceCommandResult.SendFailed;
        }
    }

    private async Task<ClientWebSocket> ConnectAsync(CancellationToken cancel)
    {
        if (!Uri.TryCreate(_serverAddress, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Invalid Intiface websocket address.");

        var socket = new ClientWebSocket();
        try
        {
            await socket.ConnectAsync(uri, cancel);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async Task SendServerInfoHandshakeAsync(ClientWebSocket socket, CancellationToken cancel)
    {
        var msg = new JsonObject
        {
            ["RequestServerInfo"] = new JsonObject
            {
                ["Id"] = 1,
                ["ClientName"] = "coyote-frontier",
                ["MessageVersion"] = 3,
            }
        };

        await SendButtplugMessageAsync(socket, msg, cancel);
    }

    private static JsonObject? BuildScalarCommand(uint deviceIndex, IntifaceCommandPayload payload)
    {
        if (!float.IsNaN(payload.Vibrate))
        {
            return new JsonObject
            {
                ["ScalarCmd"] = new JsonObject
                {
                    ["Id"] = 2,
                    ["DeviceIndex"] = deviceIndex,
                    ["Scalars"] = new JsonArray(
                        new JsonObject
                        {
                            ["Index"] = 0,
                            ["ActuatorType"] = "Vibrate",
                            ["Scalar"] = Clamp01(payload.Vibrate),
                        })
                }
            };
        }

        if (!float.IsNaN(payload.Oscillate))
        {
            return new JsonObject
            {
                ["ScalarCmd"] = new JsonObject
                {
                    ["Id"] = 2,
                    ["DeviceIndex"] = deviceIndex,
                    ["Scalars"] = new JsonArray(
                        new JsonObject
                        {
                            ["Index"] = 0,
                            ["ActuatorType"] = "Oscillate",
                            ["Scalar"] = Clamp01(payload.Oscillate),
                        })
                }
            };
        }

        if (!float.IsNaN(payload.Inflate))
        {
            return new JsonObject
            {
                ["ScalarCmd"] = new JsonObject
                {
                    ["Id"] = 2,
                    ["DeviceIndex"] = deviceIndex,
                    ["Scalars"] = new JsonArray(
                        new JsonObject
                        {
                            ["Index"] = 0,
                            ["ActuatorType"] = "Inflate",
                            ["Scalar"] = Clamp01(payload.Inflate),
                        })
                }
            };
        }

        if (!float.IsNaN(payload.Constrict))
        {
            return new JsonObject
            {
                ["ScalarCmd"] = new JsonObject
                {
                    ["Id"] = 2,
                    ["DeviceIndex"] = deviceIndex,
                    ["Scalars"] = new JsonArray(
                        new JsonObject
                        {
                            ["Index"] = 0,
                            ["ActuatorType"] = "Constrict",
                            ["Scalar"] = Clamp01(payload.Constrict),
                        })
                }
            };
        }

        return null;
    }

    private async Task<uint?> GetFirstDeviceIndexAsync(ClientWebSocket socket, CancellationToken cancel)
    {
        var listDevices = new JsonObject
        {
            ["RequestDeviceList"] = new JsonObject
            {
                ["Id"] = 3,
            }
        };

        await SendButtplugMessageAsync(socket, listDevices, cancel);
        var response = await ReceiveTextMessageAsync(socket, cancel);
        if (string.IsNullOrWhiteSpace(response))
            return null;

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(response);
        }
        catch
        {
            return null;
        }

        var list = node?[
            "DeviceList"]?[
            "Devices"] as JsonArray;

        if (list == null || list.Count == 0)
            return null;

        var first = list[0] as JsonObject;
        var indexNode = first?["DeviceIndex"];
        if (indexNode == null)
            return null;

        return indexNode.GetValue<uint>();
    }

    private static async Task SendButtplugMessageAsync(ClientWebSocket socket, JsonObject payload, CancellationToken cancel)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancel);
    }

    private static async Task<string> ReceiveTextMessageAsync(ClientWebSocket socket, CancellationToken cancel)
    {
        var buffer = new byte[4096];
        using var ms = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancel);
            if (result.MessageType == WebSocketMessageType.Close)
                break;

            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static float Clamp01(float value)
    {
        if (float.IsNaN(value))
            return value;

        return Math.Clamp(value, 0f, 1f);
    }

    public void Dispose()
    {
        // No persistent resources are held between calls.
    }
}
