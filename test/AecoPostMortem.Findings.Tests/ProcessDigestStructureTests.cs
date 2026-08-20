using System.Reflection;

namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// Scenario "The masthead reads stored counters, not live counts" (issue #44): proves structurally,
/// the same way <see cref="SuggestionRendererStructureTests"/> proves "no model is called", that
/// <see cref="ProcessDigest.Build"/> can only ever be handed already-resolved, in-memory data — an
/// allowlist of the only types its public surface may mention rules out an <c>IQueryable</c>, a
/// <c>DbContext</c>, or any other type capable of issuing a query of its own. A method that cannot
/// accept a live data source cannot run an aggregate scan when it is called.
/// </summary>
public sealed class ProcessDigestStructureTests
{
    static readonly Type[] AllowedTypes =
    [
        typeof(MastheadCounters),
        typeof(CheckRegistry),
        typeof(Finding),
        typeof(IReadOnlyList<Finding>),
        typeof(RepositoryScope),
        typeof(ProcessDigest),
        typeof(int),
        typeof(void),
    ];

    [Fact]
    public void Every_public_method_only_mentions_already_resolved_data_types()
    {
        var methods = typeof(ProcessDigest)
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Where(method => method.DeclaringType == typeof(ProcessDigest))
            // Records synthesise static bool operator==/!= — compiler-generated equality, not part
            // of the surface this test is proving anything about.
            .Where(method => !method.IsSpecialName)
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

                Assert.Contains(underlying, AllowedTypes);
            }
        }
    }
}
