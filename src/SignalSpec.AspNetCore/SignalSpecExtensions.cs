using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SignalSpec.AspNetCore;

/// <summary>Registers SignalSpec services and maps specification and hub endpoints.</summary>
public static class SignalSpecExtensions
{
    /// <summary>Registers specification options and standard SignalR services.</summary>
    /// <param name="services">The application's service collection.</param>
    /// <param name="configure">An optional callback configuring discovery and automatic hub mapping.</param>
    /// <returns>The SignalR builder for further protocol and service configuration.</returns>
    public static ISignalRServerBuilder AddSignalSpec(this IServiceCollection services,
        Action<SignalSpecOptions>? configure = null)
    {
        var options = new SignalSpecOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        return services.AddSignalR();
    }

    /// <summary>Generates a JSON specification and maps its GET endpoint, optionally mapping discovered hubs.</summary>
    /// <param name="endpoints">The application's endpoint route builder.</param>
    /// <param name="pattern">The route pattern at which to serve the specification.</param>
    /// <returns>A convention builder for the specification endpoint, allowing authorization and other conventions.</returns>
    /// <remarks>Call once after registering services with <see cref="AddSignalSpec"/>. The specification is generated once during setup.</remarks>
    /// <exception cref="ArgumentException">The route pattern is empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Required services are missing, types are unsupported, or discovered names conflict.</exception>
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
