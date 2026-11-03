using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;

namespace SignalSpec.AspNetCore;

internal sealed class SpecificationGenerator
{
    private readonly SortedDictionary<string, object> _dtos = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Type> _names = new(StringComparer.Ordinal);
    private readonly NullabilityInfoContext _nullability = new();

    public object Generate(IEnumerable<Type> hubs)
    {
        var contracts = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var hub in hubs)
        {
            Type? contract = null;
            for (var current = hub.BaseType; current != null; current = current.BaseType)
                if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(Hub<>))
                    contract = current.GetGenericArguments()[0];
            if (contract == null) continue;
            if (!contract.IsInterface)
                throw new InvalidOperationException($"Hub client contract {contract} must be an interface.");
            if (contracts.ContainsKey(contract.Name))
                throw new InvalidOperationException($"Hub contract name collision: {contract.Name}.");
            foreach (var nested in hub.GetNestedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
                Describe(nested, null);
            var methods = contract.GetInterfaces().Append(contract).SelectMany(t => t.GetMethods()).Distinct()
                .OrderBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => m.ToString(), StringComparer.Ordinal)
                .Select(m => new
                {
                    name = m.Name,
                    parameters = m.GetParameters().Select(p => new { name = p.Name, type = Describe(p.ParameterType, _nullability.Create(p)) }).ToArray(),
                    returns = Describe(m.ReturnType, _nullability.Create(m.ReturnParameter)),
                    methodType = MethodType(m),
                }).ToArray();
            contracts.Add(contract.Name, new { hubName = hub.Name, hubContract = new { methods } });
        }
        return new
        {
            metadata = new { description = "This is an extension data used by the automated SignalR client code generator." },
            hubs = contracts,
            dtos = _dtos,
        };
    }

    private static string MethodType(MethodInfo method)
    {
        var backend = method.IsDefined(typeof(Backend));
        var frontend = method.IsDefined(typeof(Frontend));
        return backend == frontend ? SignalRMethodType.Default : frontend ? SignalRMethodType.FrontendEvent : SignalRMethodType.BackendMethod;
    }

    private string Describe(Type type, NullabilityInfo? info)
    {
        string Child(int index) => Describe(type.GetGenericArguments()[index], info?.GenericTypeArguments.ElementAtOrDefault(index));
        if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>)))
            return Child(0);
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return Describe(underlying, null) + " | null | undefined";
        var suffix = info?.ReadState == NullabilityState.Nullable ? " | null | undefined" : "";
        string result;
        if (type == typeof(void) || type == typeof(Task) || type == typeof(ValueTask)) result = "void";
        else if (type == typeof(string) || type == typeof(char) || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan)) result = "string";
        else if (type == typeof(bool)) result = "boolean";
        else if (type == typeof(object)) result = "any";
        else if (type.IsPrimitive && type != typeof(IntPtr) && type != typeof(UIntPtr) || type == typeof(decimal)) result = "number";
        else if (type.IsEnum)
        {
            if (type.GetCustomAttributes().Any(a => a is JsonConverterAttribute))
                throw new InvalidOperationException($"Custom JSON converters are unsupported for {type}.");
            result = string.Join(" | ", Enum.GetValues(type).Cast<object>().Select(v => Convert.ChangeType(v, Enum.GetUnderlyingType(type), System.Globalization.CultureInfo.InvariantCulture).ToString()).Distinct());
            if (result.Length == 0) result = "number";
        }
        else if (type.IsArray && type.GetArrayRank() == 1)
            result = ArrayOf(Describe(type.GetElementType()!, info?.ElementType));
        else if (type.IsGenericType && DictionaryType(type) is { } dictionary)
        {
            if (dictionary.GetGenericArguments()[0] != typeof(string)) throw Unsupported(type);
            var value = dictionary.GetGenericArguments()[1];
            var valueInfo = type.GetGenericArguments().Length == 2 ? info?.GenericTypeArguments.ElementAtOrDefault(1) : null;
            result = $"{{ [key: string]: {Describe(value, valueInfo)} }}";
        }
        else if (type.IsGenericType && EnumerableType(type) is { } enumerable)
        {
            var element = enumerable.GetGenericArguments()[0];
            result = ArrayOf(Describe(element, type.GetGenericArguments().Length == 1 ? info?.GenericTypeArguments.ElementAtOrDefault(0) : null));
        }
        else
        {
            if ((!type.IsClass && !type.IsValueType) || type.IsGenericType || type.IsArray || type.IsPointer || type.IsByRef || type.Namespace == "System" || type.Namespace?.StartsWith("System.", StringComparison.Ordinal) == true)
                throw Unsupported(type);
            if (_names.TryGetValue(type.Name, out var existing) && existing != type)
                throw new InvalidOperationException($"DTO name collision: {existing} and {type}.");
            if (!_names.ContainsKey(type.Name))
            {
                _names.Add(type.Name, type);
                _dtos.Add(type.Name, new object()); // Reserve before traversing recursive properties.
                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.GetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0)
                    .OrderBy(p => p.Name, StringComparer.Ordinal)
                    .Select(p => new { name = char.ToLowerInvariant(p.Name[0]) + p.Name[1..], type = Describe(p.PropertyType, _nullability.Create(p)) }).ToArray();
                _dtos[type.Name] = new { properties };
            }
            result = type.Name;
        }
        return result + suffix;
    }

    private static Type? DictionaryType(Type type) => type.GetInterfaces().Prepend(type).FirstOrDefault(t => t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(IDictionary<,>) || t.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
    private static Type? EnumerableType(Type type) => type.GetInterfaces().Prepend(type).FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>));
    private static string ArrayOf(string element) => element.Contains('|') ? $"({element})[]" : element + "[]";
    private static InvalidOperationException Unsupported(Type type) => new($"The type '{type}' is not supported in a SignalR specification.");
}
