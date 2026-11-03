using System.Reflection;

namespace SignalSpec.AspNetCore;

[AttributeUsage(AttributeTargets.Method)]
public sealed class Backend : Attribute;

[AttributeUsage(AttributeTargets.Method)]
public sealed class Frontend : Attribute;

public static class SignalRMethodType
{
    public const string Default = "default";
    public const string FrontendEvent = "frontendEvent";
    public const string BackendMethod = "backendMethod";
}

public sealed class SignalSpecOptions
{
    public bool AutoMapHubs { get; set; } = true;
    public ICollection<Assembly> Assemblies { get; } = new List<Assembly>();
}
