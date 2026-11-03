using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SignalSpec.AspNetCore;

public static class SignalSpecExtensions
{
    public static ISignalRServerBuilder AddSignalSpec(this IServiceCollection services,
        Action<SignalSpecOptions>? configure = null)
    {
        var options = new SignalSpecOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        return services.AddSignalR();
    }

    public static IEndpointConventionBuilder MapSignalSpec(this IEndpointRouteBuilder endpoints, string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        var options = endpoints.ServiceProvider.GetRequiredService<SignalSpecOptions>();
        var environment = endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var assemblies = options.Assemblies.Append(Assembly.Load(environment.ApplicationName)).Distinct();
        var hubs = assemblies.SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters && typeof(Hub).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();
        if (options.AutoMapHubs)
        {
            var duplicate = hubs.GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
                throw new InvalidOperationException($"Automatic hub route collision: {duplicate.Key}.");
        }
        var json = JsonSerializer.SerializeToUtf8Bytes(new SpecificationGenerator().Generate(hubs));
        if (options.AutoMapHubs)
        {
            var map = typeof(HubEndpointRouteBuilderExtensions).GetMethods()
                .Single(m => m.Name == "MapHub" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
            foreach (var hub in hubs)
                map.MakeGenericMethod(hub).Invoke(null, [endpoints, $"/hubs/{hub.Name}"]);
        }
        return endpoints.MapGet(pattern, () => Results.Bytes(json, "application/json"));
    }
}
