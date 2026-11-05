using System.Reflection;

namespace SignalSpec.AspNetCore;

/// <summary>Marks a client contract method as invoked by the frontend and handled by the backend.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class Backend : Attribute;

/// <summary>Marks a client contract method as invoked by the backend and handled by the frontend.</summary>
/// <remarks>Frontend methods may return a result to the caller.</remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class Frontend : Attribute;

/// <summary>Defines method direction values emitted in SignalR specifications.</summary>
public static class SignalRMethodType
{
    /// <summary>A method with neither direction attribute, or with both attributes.</summary>
    public const string Default = "default";
    /// <summary>A method invoked by the backend and handled by the frontend, optionally returning a result.</summary>
    public const string FrontendMethod = "frontendMethod";
    /// <summary>A method invoked by the frontend and handled by the backend.</summary>
    public const string BackendMethod = "backendMethod";
}

/// <summary>Configures hub discovery and automatic endpoint mapping.</summary>
public sealed class SignalSpecOptions
{
    /// <summary>Gets or sets whether discovered hubs are mapped at <c>/hubs/{ClassName}</c>. Defaults to <see langword="true"/>.</summary>
    /// <remarks>When disabled, map hubs manually using the standard SignalR endpoint extensions. Specification discovery remains enabled.</remarks>
    public bool AutoMapHubs { get; set; } = true;
    /// <summary>Gets additional assemblies to scan for hubs alongside the host application's assembly.</summary>
    public ICollection<Assembly> Assemblies { get; } = new List<Assembly>();
}
