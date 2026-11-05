# SignalSpec.AspNetCore

Standalone SignalR client specifications for .NET 10.

```csharp
using SignalSpec.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalSpec();
var app = builder.Build();
app.MapSignalSpec("/signalr/spec.json");
app.Run();
```

Install with `dotnet add package SignalSpec.AspNetCore` after publishing the package.
All concrete hubs in the application's assembly are mapped at `/hubs/{ClassName}`.
Add other assemblies with `options.Assemblies.Add(typeof(MyHub).Assembly)`.
To manage routes yourself, set `options.AutoMapHubs = false` and call standard
`app.MapHub<MyHub>("/custom-route")`. Discovery and specification generation still run.
Call `MapSignalSpec` once. Its returned convention builder supports `.RequireAuthorization()`.
Hub authorization and other conventions can be configured through manual hub mapping.
`AddSignalSpec` returns SignalR's builder for normal protocol configuration.

```csharp
public interface IChatClient
{
    [Frontend] Task ReceiveMessage(Message message);
    [Backend] Task<Message> SendMessage(string text);
}
public class ChatHub : Microsoft.AspNetCore.SignalR.Hub<IChatClient> { }
public class Message { public string Text { get; set; } = ""; }
```

The root JSON contains `metadata`, `hubs`, and `dtos`. Hubs are keyed by client
interface name, with `hubName` and `hubContract.methods`. Methods contain `name`,
`parameters`, `returns`, and `methodType`. DTOs contain `properties` with `name`
and TypeScript `type` strings. There is no OpenAPI wrapper.
`[Backend]` means `backendMethod`, `[Frontend]` means `frontendMethod`; both or neither
means `default`. Untyped hubs are mapped without a generated client contract.

External DTOs and nested DTOs are supported, including recursive references.
Supported types include numeric primitives, strings, booleans, object, Guid,
date/time values, numeric enums, arrays, generic collections, string-keyed dictionaries,
and Task/ValueTask returns. Nullable annotations produce `| null | undefined`.
DTO property names lowercase their first character, preserving Tokyo's format.
Custom JSON converters and serialization naming overrides are not interpreted.
Enums with converter attributes are rejected. Unsupported types and ambiguous DTO,
contract, or automatic route names fail during endpoint setup.
The specification is generated once at setup; no filesystem output is required.

Build and validate with `dotnet test SignalSpec.AspNetCore.slnx -c Release`.
Pack with `dotnet pack src/SignalSpec.AspNetCore -c Release -o artifacts`.
