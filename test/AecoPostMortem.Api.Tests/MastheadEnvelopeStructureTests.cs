using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// Scenario "The masthead reads stored counters, not live counts" (issue #44, S-36), enforced at the
/// layer that can actually break it.
///
/// <para><c>ProcessDigestStructureTests</c> (<c>AecoPostMortem.Findings.Tests</c>) already proves the
/// domain builder cannot be handed a live data source — but <c>AecoPostMortem.Findings</c> has no
/// reference to <c>AecoPostMortem.Data</c> at all, so that guarantee costs it nothing. This project
/// does reference <c>Data</c> (<c>ApiHost.DiagnoseAppState</c> and <c>GetSession</c> both read
/// through it), which makes <see cref="MastheadEnvelope"/> the first point on the masthead's path
/// where a live <c>COUNT</c> could plausibly be introduced — by a later story wiring
/// <c>/api/digest</c> and reaching for <c>context.RawEvents.Count()</c> rather than the counter
/// maintained at ingest.</para>
///
/// <para>Measurement is why this is a structural guard and not a review note: counting a million rows
/// measured 126 ms on SQLite and 118 ms on Postgres
/// (<c>docs/product-superpowers/research/2026-08-16-sqlite-vs-postgres-query-latency.md</c>), so no
/// engine change tunes it away later. The masthead has to read pre-maintained counters, and the way
/// to keep that true is to make the alternative unrepresentable rather than merely discouraged.</para>
/// </summary>
public sealed class MastheadEnvelopeStructureTests
{
    /// <summary>Every type <see cref="MastheadEnvelope"/>'s public surface mentions: its properties,
    /// and the parameters and return types of its public methods.</summary>
    static IEnumerable<Type> PublicSurfaceTypes()
    {
        foreach (var property in typeof(MastheadEnvelope).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            yield return property.PropertyType;
        }

        var methods = typeof(MastheadEnvelope)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .Where(method => method.DeclaringType == typeof(MastheadEnvelope))
            // Records synthesise operator==/!= and Equals/GetHashCode — compiler-generated equality,
            // not part of the surface this test is proving anything about.
            .Where(method => !method.IsSpecialName);

        foreach (var method in methods)
        {
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }

            yield return method.ReturnType;
        }
    }

    static IEnumerable<Type> Unwrap(Type type)
    {
        yield return Nullable.GetUnderlyingType(type) ?? type;

        // A query source hidden behind a generic argument (IQueryable<RawEvent>, Func<DbContext,…>)
        // is the same defect as one named outright, so the check follows generic arguments down.
        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var nested in Unwrap(argument))
            {
                yield return nested;
            }
        }
    }

    [Fact]
    public void The_served_masthead_never_mentions_a_type_capable_of_issuing_a_query()
    {
        var surface = PublicSurfaceTypes().SelectMany(Unwrap).Distinct().ToArray();

        Assert.NotEmpty(surface);

        foreach (var type in surface)
        {
            Assert.False(
                typeof(IQueryable).IsAssignableFrom(type),
                $"{type.Name} is an IQueryable: the masthead would be able to run an aggregate at render time.");

            Assert.False(
                typeof(IQueryable).IsAssignableFrom(type) || typeof(Expression).IsAssignableFrom(type),
                $"{type.Name} can carry a query expression, which the masthead must never evaluate at render time.");
        }
    }

    /// <summary>The complement of the check above: not merely "no query type", but "nothing from the
    /// storage layer at all". <see cref="MastheadEnvelope"/> is assembled from an in-memory
    /// <c>Masthead</c> whose counters were resolved at ingest; a <c>DbContext</c>, an entity type, or
    /// anything else out of <c>AecoPostMortem.Data</c> appearing here would mean the render path had
    /// been handed the store itself and could count from it.</summary>
    [Fact]
    public void The_served_masthead_never_mentions_a_type_from_the_storage_layer()
    {
        var surface = PublicSurfaceTypes().SelectMany(Unwrap).Distinct().ToArray();

        foreach (var type in surface)
        {
            var assembly = type.Assembly.GetName().Name;

            Assert.False(
                assembly is "AecoPostMortem.Data" or "Microsoft.EntityFrameworkCore",
                $"{type.Name} comes from {assembly}: the masthead must be built from counters maintained at "
                    + "ingest, never from the store at render time.");
        }
    }
}
