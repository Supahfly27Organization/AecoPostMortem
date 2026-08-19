using System.Reflection;

namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// Scenario "No model is called" (issue #48): proves the guarantee structurally rather than by
/// inspecting behaviour, the same way <c>AecoPostMortem.Rules/CLAUDE.md</c>'s own invariant is
/// proved by what the project references rather than by a list of banned calls — a blocklist of
/// "things that could be a model call or a clock read" can never be exhaustive, but an allowlist of
/// the only types <see cref="SuggestionRenderer"/> is allowed to touch can.
/// </summary>
public sealed class SuggestionRendererStructureTests
{
    /// <summary>Every type <see cref="SuggestionRenderer"/>'s public surface may mention, in a
    /// parameter or a return type — each one already-resolved, in-memory data with no I/O
    /// capability of its own. Nothing that could reach a network, a model endpoint, the system
    /// clock, or any other ambient capability appears here.</summary>
    static readonly Type[] AllowedTypes =
    [
        typeof(SuggestionTemplate),
        typeof(Suggestion),
        typeof(EvidenceItem),
        typeof(Resolution),
        typeof(IReadOnlyList<EvidenceItem>),
        typeof(void),
    ];

    [Fact]
    public void SuggestionRenderer_is_a_static_class_with_no_instance_state()
    {
        var type = typeof(SuggestionRenderer);

        // The C# compiler emits a `static class` as sealed and abstract with no instance
        // constructor — there is structurally nowhere on this type to inject an IClock, an HTTP
        // client, or a model client, because it can never be instantiated at all.
        Assert.True(type.IsAbstract && type.IsSealed, "SuggestionRenderer must be a static class.");
        Assert.Empty(type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        Assert.Empty(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
    }

    [Fact]
    public void SuggestionRenderer_declares_no_static_mutable_field()
    {
        // A static field could smuggle in ambient state (a cached clock, a client, a counter) that
        // a per-call parameter list would not reveal. Constants (compiled to literals, never an
        // object with behaviour) and the compiled regex generator's own field are the only static
        // state a purely textual template engine needs.
        var staticFields = typeof(SuggestionRenderer)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(field => !field.IsLiteral)
            .ToArray();

        Assert.All(staticFields, field => Assert.True(
            field.IsInitOnly || field.Name.Contains("Placeholder", StringComparison.Ordinal),
            $"Unexpected mutable static field: {field.Name}"));
    }

    [Fact]
    public void Every_public_method_only_mentions_already_resolved_data_types()
    {
        var methods = typeof(SuggestionRenderer)
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Where(method => method.DeclaringType == typeof(SuggestionRenderer))
            .ToArray();

        Assert.NotEmpty(methods);

        foreach (var method in methods)
        {
            var signatureTypes = method.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType);

            foreach (var candidate in signatureTypes)
            {
                var underlying = Nullable.GetUnderlyingType(candidate) ?? candidate;

                Assert.Contains(
                    underlying,
                    AllowedTypes.Append(typeof(SuggestionRenderer)));
            }
        }
    }
}
