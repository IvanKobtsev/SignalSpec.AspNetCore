using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SignalSpec.AspNetCore;
using Xunit;

public class SpecificationTests
{
    private static JsonElement Generate(params Type[] hubs) => JsonSerializer.SerializeToElement(new SpecificationGenerator().Generate(hubs));

    [Fact]
    public void TokyoShapeIsPreservedAtRoot()
    {
        var json = Generate(typeof(LegacyHub));
        using var expected = JsonDocument.Parse("""
        {"metadata":{"description":"This is a SignalR specification that can be used by the automated SignalR client code generator."},"hubs":{"ILegacy":{"hubName":"LegacyHub","hubContract":{"methods":[{"name":"Send","parameters":[{"name":"text","type":"string"}],"returns":"Reply","methodType":"backendMethod"}]}}},"dtos":{"Reply":{"properties":[{"name":"isSuccessful","type":"boolean"}]}}}
        """);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, json));
    }

    [Fact]
    public void ExpandedTypesAndInheritedMethodsAreDescribed()
    {
        var json = Generate(typeof(ModernHub));
        var methods = json.GetProperty("hubs").GetProperty("IModern").GetProperty("hubContract").GetProperty("methods").EnumerateArray().ToDictionary(m => m.GetProperty("name").GetString()!);
        Assert.Equal("frontendMethod", methods["Receive"].GetProperty("methodType").GetString());
        Assert.Equal("default", methods["Both"].GetProperty("methodType").GetString());
        Assert.Equal("default", methods["Neither"].GetProperty("methodType").GetString());
        Assert.Equal("Node | null | undefined", methods["Receive"].GetProperty("parameters")[0].GetProperty("type").GetString());
        Assert.Equal("Node", methods["Get"].GetProperty("returns").GetString());
        Assert.Equal("frontendMethod", methods["Get"].GetProperty("methodType").GetString());
        var properties = json.GetProperty("dtos").GetProperty("Node").GetProperty("properties").EnumerateArray().ToDictionary(p => p.GetProperty("name").GetString()!, p => p.GetProperty("type").GetString());
        Assert.Equal("(string | null | undefined)[]", properties["names"]);
        Assert.Equal("{ [key: string]: Node | null | undefined }", properties["children"]);
        Assert.Equal("Node | null | undefined", properties["next"]);
        Assert.Equal("0 | 1", properties["state"]);
        Assert.Equal("string", properties["id"]);
    }

    [Fact]
    public void UnsupportedAndDuplicateTypesFailClearly()
    {
        Assert.Contains("not supported", Assert.Throws<InvalidOperationException>(() => Generate(typeof(BadHub))).Message);
        Assert.Contains("contract name collision", Assert.Throws<InvalidOperationException>(() => Generate(typeof(LegacyHub), typeof(OtherLegacyHub))).Message);
        Assert.Contains("DTO name collision", Assert.Throws<InvalidOperationException>(() => Generate(typeof(CollisionHub))).Message);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task EndpointAndHubMappingWork(bool autoMap, bool extraAssembly)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = (extraAssembly ? typeof(SignalSpecOptions) : typeof(ChatHub)).Assembly.GetName().Name });
        builder.WebHost.UseTestServer();
        builder.Services.AddSignalSpec(o => { o.AutoMapHubs = autoMap; if (extraAssembly) o.Assemblies.Add(typeof(ChatHub).Assembly); });
        await using var app = builder.Build();
        app.MapSignalSpec("/custom/contracts.json");
        if (!autoMap) app.MapHub<ChatHub>("/manual");
        await app.StartAsync();
        var client = app.GetTestClient();
        var response = await client.GetAsync("/custom/contracts.json");
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("IChatClient", await response.Content.ReadAsStringAsync());
        var route = autoMap ? "/hubs/ChatHub" : "/manual";
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(route + "/negotiate?negotiateVersion=1", null)).StatusCode);
        if (autoMap) Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/hubs/UntypedHub/negotiate?negotiateVersion=1", null)).StatusCode);
        if (!autoMap) Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/hubs/ChatHub/negotiate?negotiateVersion=1", null)).StatusCode);
    }

    public interface ILegacy { [Backend] Task<LegacyHub.Reply> Send(string text); }
    public class LegacyHub : Hub<ILegacy> { public class Reply { public bool IsSuccessful { get; set; } } }
    public class OtherLegacyHub : Hub<ILegacy>;
    public interface IBase { [Frontend] Task Receive(Node? node); }
    public interface IModern : IBase { [Backend, Frontend] Task Both(); Task Neither(); [Frontend] ValueTask<Node> Get(); }
    public abstract class ModernBase : Hub<IModern>;
    public class ModernHub : ModernBase;
    public enum State { First, Second }
    public class Node
    {
        public Guid Id { get; set; }
        public DateTime When { get; set; }
        public State State { get; set; }
        public Node? Next { get; set; }
        public List<string?> Names { get; set; } = [];
        public Dictionary<string, Node?> Children { get; set; } = [];
    }
    public interface IBad { Task Send(Uri uri); }
    public class BadHub : Hub<IBad>;
    public class One { public class Dto; }
    public class Two { public class Dto; }
    public interface ICollision { Task Send(One.Dto first, Two.Dto second); }
    public class CollisionHub : Hub<ICollision>;

    [Fact]
    public void ArraysDictionariesAndNullableAsyncReturnsWork()
    {
        var json = Generate(typeof(TypesHub));
        var methods = json.GetProperty("hubs").GetProperty("ITypes").GetProperty("hubContract").GetProperty("methods").EnumerateArray().ToDictionary(m => m.GetProperty("name").GetString()!);
        Assert.Equal("string[]", methods["Array"].GetProperty("returns").GetString());
        Assert.Equal("number | null | undefined", methods["Nullable"].GetProperty("returns").GetString());
        Assert.Equal("{ [key: string]: number }", methods["Dictionary"].GetProperty("returns").GetString());
        Assert.Equal("void", methods["NoResult"].GetProperty("returns").GetString());
    }
    public interface ITypes
    {
        Task<string[]> Array();
        Task<int?> Nullable();
        ValueTask<IReadOnlyDictionary<string, double>> Dictionary();
        ValueTask NoResult();
    }
    public class TypesHub : Hub<ITypes>;

    [Fact]
    public async Task AutomaticRouteCollisionsFailBeforeMapping()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(SpecificationTests).Assembly.GetName().Name });
        builder.Services.AddSignalSpec();
        await using var app = builder.Build();
        Assert.Contains("route collision", Assert.Throws<InvalidOperationException>(() => app.MapSignalSpec("/spec.json")).Message);
    }
    public class RouteOne { public class DuplicateHub : Hub; }
    public class RouteTwo { public class DuplicateHub : Hub; }
}
