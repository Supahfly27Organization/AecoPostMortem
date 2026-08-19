using System.Text.Json.Serialization;
using AecoPostMortem.Findings;

namespace AecoPostMortem.Api;

/// <summary>
/// FR-56 in the response contract: a finding class with no suggestion template must still carry a
/// suggestion field — the absence is an explicit, discriminated state
/// (<see cref="Absent"/>), never a missing or nullable field a serialiser could simply drop. Closed
/// to exactly two shapes: the private constructor means only the nested <see cref="Present"/> and
/// <see cref="Absent"/> records — the only types that can see it — can derive from this one, the same
/// reasoning <c>AecoPostMortem.Findings/CLAUDE.md</c> gives for <c>Provenance</c> being
/// <c>required</c> rather than validated.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "state")]
[JsonDerivedType(typeof(Present), "present")]
[JsonDerivedType(typeof(AbsentSuggestion), "absent")]
public abstract record SuggestionEnvelope
{
    private SuggestionEnvelope()
    {
    }

    /// <summary>The one value representing "this finding class has no suggestion template".</summary>
    public static SuggestionEnvelope Absent { get; } = new AbsentSuggestion();

    /// <summary>Maps a domain <see cref="Suggestion"/> — nullable because FR-56 says only some finding
    /// classes have a template — onto the envelope's two explicit states.</summary>
    public static SuggestionEnvelope Of(Suggestion? suggestion) =>
        suggestion is null ? Absent : new Present { Text = suggestion.Text };

    public sealed record Present : SuggestionEnvelope
    {
        public required string Text { get; init; }
    }

    public sealed record AbsentSuggestion : SuggestionEnvelope;
}
