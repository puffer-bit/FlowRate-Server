using System.Reflection;
using Server.Core.Models.User;
using Server.Infrastructure.Database;
using Xunit;
using Server.Application.Coordinator;

namespace Server.Architecture.Tests;

public class LayerDependencyTests
{
    private static readonly Assembly Core = typeof(UserModel).Assembly;
    private static readonly Assembly Application = typeof(CoordinatorInstance).Assembly;
    private static readonly Assembly Infrastructure = typeof(ServerDbContext).Assembly;

    // Frameworks that belong to the outer layers only
    private static readonly string[] InfrastructureFrameworks =
    [
        "Fleck", "Grpc", "Google.Protobuf", "Microsoft.EntityFrameworkCore", "Serilog", "Microsoft.AspNetCore"
    ];

    // Generated from the Sfu/mediasoup *.proto files (csharp_namespace option)
    private static readonly string[] GeneratedNamespaces = ["Server.Api.Grpc", "Server.Infrastructure.Mediasoup"];

    public static TheoryData<string> LayerAssemblies => new() { "Server.Core", "Server.Application", "Server.Infrastructure" };

    [Fact]
    public void Core_DoesNotDependOnOuterLayers()
    {
        AssertNoReferences(Core, ["Server.Application", "Server.Infrastructure", "Server.Asp", "Server.Api"]);
        AssertNoReferences(Core, InfrastructureFrameworks);
    }

    [Fact]
    public void Application_DoesNotDependOnInfrastructureOrPresentation()
    {
        AssertNoReferences(Application, ["Server.Infrastructure", "Server.Asp", "Server.Api"]);
        AssertNoReferences(Application, InfrastructureFrameworks);
    }

    [Fact]
    public void Infrastructure_DoesNotDependOnPresentation()
    {
        AssertNoReferences(Infrastructure, ["Server.Asp"]);
    }

    [Theory]
    [MemberData(nameof(LayerAssemblies))]
    public void Namespaces_MatchProject(string assemblyName)
    {
        var assembly = new[] { Core, Application, Infrastructure }.Single(a => a.GetName().Name == assemblyName);

        var offenders = assembly.GetTypes()
            .Where(t => !IsCompilerGenerated(t))
            .Where(t => !GeneratedNamespaces.Any(ns => t.Namespace?.StartsWith(ns) == true))
            .Where(t => t.Namespace is null
                        || (t.Namespace != assemblyName && !t.Namespace.StartsWith(assemblyName + ".")))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Types outside the '{assemblyName}' namespace:\n{string.Join("\n", offenders)}");
    }

    [Fact]
    public void Core_DoesNotExposeTransportDtos()
    {
        // Shared enums are fine; request/response/DTO classes describe the wire protocol and stay out of Core
        var offenders = Core.GetTypes()
            .Where(t => !IsCompilerGenerated(t))
            .SelectMany(t => MemberTypes(t).Select(mt => (Owner: t, Used: mt)))
            .Where(x => x.Used.Namespace?.StartsWith("Shared.Models") == true && !x.Used.IsEnum)
            .Select(x => $"{x.Owner.FullName} -> {x.Used.FullName}")
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0, $"Core uses transport DTOs:\n{string.Join("\n", offenders)}");
    }

    [Fact]
    public void Core_StateChangesOnlyThroughMethods()
    {
        // Ids are assigned by the database after the first save; everything else is set through
        // constructors, init accessors or domain methods
        var offenders = Core.GetTypes()
            .Where(t => !IsCompilerGenerated(t))
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(p => p.SetMethod is { IsPublic: true } setter && !IsInitOnly(setter) && p.Name != "Id")
            .Select(p => $"{p.DeclaringType!.FullName}.{p.Name}")
            .ToList();

        Assert.True(offenders.Count == 0, $"Core properties with public setters:\n{string.Join("\n", offenders)}");
    }

    private static void AssertNoReferences(Assembly assembly, IEnumerable<string> forbiddenPrefixes)
    {
        var offenders = assembly.GetReferencedAssemblies()
            .Select(r => r.Name!)
            .Where(name => forbiddenPrefixes.Any(p => name == p || name.StartsWith(p + ".")))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"{assembly.GetName().Name} must not reference: {string.Join(", ", offenders)}");
    }

    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers()
            .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");

    private static bool IsCompilerGenerated(Type t) =>
        t.Name.StartsWith('<') || t.FullName?.Contains("<") == true || t.Name == "<Module>";

    private static IEnumerable<Type> MemberTypes(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                 BindingFlags.Static | BindingFlags.DeclaredOnly;

        var types = new List<Type>();
        if (type.BaseType != null) types.Add(type.BaseType);
        types.AddRange(type.GetInterfaces());
        types.AddRange(type.GetFields(all).Select(f => f.FieldType));
        types.AddRange(type.GetProperties(all).Select(p => p.PropertyType));
        foreach (var m in type.GetMethods(all))
        {
            types.Add(m.ReturnType);
            types.AddRange(m.GetParameters().Select(p => p.ParameterType));
        }
        foreach (var c in type.GetConstructors(all))
            types.AddRange(c.GetParameters().Select(p => p.ParameterType));

        return types.SelectMany(Unwrap);
    }

    private static IEnumerable<Type> Unwrap(Type t)
    {
        if (t.HasElementType)
        {
            foreach (var e in Unwrap(t.GetElementType()!)) yield return e;
            yield break;
        }
        yield return t;
        if (t.IsGenericType)
            foreach (var a in t.GetGenericArguments().SelectMany(Unwrap))
                yield return a;
    }
}
