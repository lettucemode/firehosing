
using System.Net.WebSockets;
using System.Text.Json;
using firehosing;

var DATA_SIZE = 1024 * 32;
var receiveBuffer = new byte[DATA_SIZE];
var messageBuffer = new byte[DATA_SIZE];
var soFar = 0;

var serializeOptions = new JsonSerializerOptions();
serializeOptions.Converters.Add(new ShortenByteArrayJsonConverter());

using var ws = new ClientWebSocket();
await ws.ConnectAsync(new Uri("wss://bsky.network/xrpc/com.atproto.sync.subscribeRepos"), CancellationToken.None);

while (ws.State == WebSocketState.Open) {
    var receiveResult = await ws.ReceiveAsync(new Memory<byte>(receiveBuffer), CancellationToken.None);

    Array.Copy(receiveBuffer, 0, messageBuffer, soFar, receiveResult.Count);
    soFar += receiveResult.Count;
    if (!receiveResult.EndOfMessage) continue;

    var thing = AtprotoFrame.FromBytes(messageBuffer);
    Console.WriteLine(JsonSerializer.Serialize(thing, serializeOptions));
    soFar = 0;
    messageBuffer = new byte[DATA_SIZE];    // todo: leverage Span<byte> to avoid re-allocating every message/frame
    if (ws.State == WebSocketState.CloseReceived) break;
}

await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);

