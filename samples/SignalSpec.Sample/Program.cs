using Microsoft.AspNetCore.SignalR;
using SignalSpec.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalSpec();
var app = builder.Build();
app.MapSignalSpec("/signalr/spec.json");
app.Run();

public interface IChatClient
{
    [Frontend] Task ReceiveMessage(Message message);
    [Backend] Task<Message> SendMessage(string text);
}
public sealed class ChatHub : Hub<IChatClient>
{
    public Task<Message> SendMessage(string text) => Task.FromResult(new Message(text));
}
public sealed record Message(string Text);

public sealed class UntypedHub : Hub;
