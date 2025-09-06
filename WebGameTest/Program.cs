using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var players = new Dictionary<Guid, State>();
var sockets = new Dictionary<Guid, WebSocket>();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets();

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    var socket = await context.WebSockets.AcceptWebSocketAsync();
    var id = Guid.NewGuid();

    // игрок спавнится в центре
    players[id] = new State(500, 500);
    sockets[id] = socket;

    // рассылка всем, что появился новый игрок
    await Broadcast(players);

    var buffer = new byte[1024];
    while (socket.State == WebSocketState.Open)
    {
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        if (result.MessageType == WebSocketMessageType.Close)
            break;

        var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
        try
        {
            var update = JsonSerializer.Deserialize<Dictionary<string, int>>(msg);
            if (update != null && update.ContainsKey("x") && update.ContainsKey("y"))
            {
                players[id] = new State(update["x"], update["y"]);
                await Broadcast(players);
            }
        }
        catch { }
    }

    players.Remove(id);
    sockets.Remove(id);
    await Broadcast(players);
    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
});

app.Run();

async Task Broadcast(Dictionary<Guid, State> state)
{
    var json = JsonSerializer.Serialize(state);
    var bytes = Encoding.UTF8.GetBytes(json);
    var seg = new ArraySegment<byte>(bytes);

    foreach (var ws in sockets.Values.ToList())
    {
        if (ws.State == WebSocketState.Open)
            await ws.SendAsync(seg, WebSocketMessageType.Text, true, CancellationToken.None);
    }
}

record State(int x, int y);