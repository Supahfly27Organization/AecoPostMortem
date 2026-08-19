# The findings contract and the check registry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish the finding record shape and the check-registry shape in `AecoPostMortem.Findings`,
so that nineteen downstream stories (S-02, S-05, S-06, S-13 … S-51) can build against a shape instead
of inventing their own.

**Architecture:** Seven pure C# types with no persistence and no EF Core: `Finding` (the record every
surface reads), its three enums (`FindingClass`, `Provenance`, `OperatorResponse`), four small value
types it carries (`EvidenceItem`, `Recurrence`/`RecurrenceOccurrence`, `Resolution`, `Suggestion`), a
static `FindingClassRegistry` declaring each class's recurrence key, and `CheckRegistry` /
`CheckRegistryEntry` recording every check's run status and population for a completed analysis run.

**Tech Stack:** .NET 10, C#, xUnit v3. No `AecoPostMortem.Data`, no `AecoPostMortem.Rules`, no EF
Core — this story adds nothing to either dependency and touches neither project.

**Spec:** `docs/superpowers/specs/2026-08-19-findings-contract-design.md`

## Global Constraints

- Target framework `net10.0`; `Nullable` enabled; `TreatWarningsAsErrors` true; `EnforceCodeStyleInBuild` true. A warning fails the build, including an unresolved `<see cref>` or an unused `using`.
- File-scoped namespaces (`namespace AecoPostMortem.Findings;`), matching every existing file in the solution.
- Every new public type is a `sealed record` unless it is an enum — matching `AecoPostMortem.Data.Execution`'s style.
- Do not add a `<see cref="...">` to a type that has not landed in an earlier task — it is a compile warning, and this build treats warnings as errors. Use `<c>TypeName.Member</c>` for a forward reference instead.
- No changes to `src/AecoPostMortem.Data/`, `src/AecoPostMortem.Rules/`, or their `.csproj` files. `src/AecoPostMortem.Findings/AecoPostMortem.Findings.csproj`'s existing `ProjectReference`s to `Rules` and `Data` are untouched — this story does not use them yet.
- Run `python scripts/check-claude-md.py src/AecoPostMortem.Findings/CLAUDE.md` after Task 6's edit; it must report no findings.
- Every commit message ends with:
  ```
  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01E6Aua8SoJ1D9qb2oxuYxNp
  ```
- Every commit summary line ends with ` (#23)`.
- Work happens on branch `s-44-findings-contract`, which already exists and holds the spec commit.
- Full test command: `dotnet test AecoPostMortem.sln --nologo -v q`. Single project: `dotnet test test/AecoPostMortem.Findings.Tests/AecoPostMortem.Findings.Tests.csproj --nologo -v q`.

## File Structure

**Created:**
- `src/AecoPostMortem.Findings/Evidence.cs` — `EvidenceItem`
- `src/AecoPostMortem.Findings/Resolution.cs` — `Resolution`
- `src/AecoPostMortem.Findings/Suggestion.cs` — `Suggestion`
- `src/AecoPostMortem.Findings/Recurrence.cs` — `Recurrence`, `RecurrenceOccurrence`
- `src/AecoPostMortem.Findings/Finding.cs` — `FindingClass`, `Provenance`, `OperatorResponse`, `Finding`
- `src/AecoPostMortem.Findings/FindingClassRegistry.cs` — `FindingClassRegistration`, `FindingClassRegistry`
- `src/AecoPostMortem.Findings/CheckRegistry.cs` — `CheckRunStatus`, `CheckRegistryEntry`, `CheckRegistry`
- `test/AecoPostMortem.Findings.Tests/SupportingShapesTests.cs`
- `test/AecoPostMortem.Findings.Tests/RecurrenceTests.cs`
- `test/AecoPostMortem.Findings.Tests/FindingTests.cs`
- `test/AecoPostMortem.Findings.Tests/FindingClassRegistryTests.cs`
- `test/AecoPostMortem.Findings.Tests/CheckRegistryTests.cs`

**Modified:**
- `src/AecoPostMortem.Findings/CLAUDE.md` — describe the shapes instead of "Empty"

**Deleted:**
- `test/AecoPostMortem.Findings.Tests/ProjectReferenceTests.cs` — the placeholder every empty project
  got from S-47; its own comment says the first story to land real content here replaces it, and
  `test/AecoPostMortem.Data.Tests` already shows that happening for S-01.

---

### Task 1: The three leaf value types — `Evidence`, `Resolution`, `Suggestion`

These three carry no dependency on anything else in the contract, and `Finding` (Task 3) needs all
three to exist first.

**Files:**
- Create: `src/AecoPostMortem.Findings/Evidence.cs`
- Create: `src/AecoPostMortem.Findings/Resolution.cs`
- Create: `src/AecoPostMortem.Findings/Suggestion.cs`
- Test: `test/AecoPostMortem.Findings.Tests/SupportingShapesTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `AecoPostMortem.Findings.EvidenceItem` with `required string Field`, `required string Value`; `AecoPostMortem.Findings.Resolution` with `required string OperandLayer`, `required int CallCount`; `AecoPostMortem.Findings.Suggestion` with `required string Text`.

- [ ] **Step 1: Write the failing test**

Create `test/AecoPostMortem.Findings.Tests/SupportingShapesTests.cs`:

```csharp
namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// The three leaf value types a <see cref="Finding"/> carries: a quoted event field (FR-59's
/// evidence), an adherence figure's resolution (FR-33), and a deterministic suggestion template
/// (FR-56). None depends on the others.
/// </summary>
public sealed class SupportingShapesTests
{
    [Fact]
    public void An_evidence_item_carries_the_field_and_the_quoted_value()
    {
        var evidence = new EvidenceItem { Field = "data.toolName", Value = "grep" };

        Assert.Equal("data.toolName", evidence.Field);
        Assert.Equal("grep", evidence.Value);
    }

    [Fact]
    public void A_resolution_carries_the_operand_layer_and_the_call_count()
    {
        var resolution = new Resolution { OperandLayer = "NORMALIZED", CallCount = 12 };

        Assert.Equal("NORMALIZED", resolution.OperandLayer);
        Assert.Equal(12, resolution.CallCount);
    }

    [Fact]
    public void A_suggestion_carries_its_rendered_text()
    {
        var suggestion = new Suggestion
        {
            Text = "rewrite the rule in your agent's own vocabulary: name `rg`",
        };

        Assert.Equal("rewrite the rule in your agent's own vocabulary: name `rg`", suggestion.Text);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/AecoPostMortem.Findings.Tests/AecoPostMortem.Findings.Tests.csproj --nologo -v q`

Expected: FAIL to compile — `EvidenceItem`, `Resolution` and `Suggestion` do not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/AecoPostMortem.Findings/Evidence.cs`:

```csharp
namespace AecoPostMortem.Findings;

/// <summary>
/// One field/value pair quoted from the event that produced a finding. A pair rather than a raw
/// string so a UI can render "the actual event fields" (PRD Part 4) as fields, not as an opaque
/// blob — the Raw tab is "the provenance guarantee made clickable," which needs to know what it is
/// pointing at.
/// </summary>
public sealed record EvidenceItem
{
    public required string Field { get; init; }

    public required string Value { get; init; }
}
```

Create `src/AecoPostMortem.Findings/Resolution.cs`:

```csharp
namespace AecoPostMortem.Findings;

/// <summary>
/// FR-33: every adherence figure renders with the resolution that produced it — the layer used per
/// operand and the resulting call counts — because a measured fivefold spread on one rule came from
/// that choice alone. Carried on <c>Finding.Resolution</c> only where one applies.
/// </summary>
public sealed record Resolution
{
    public required string OperandLayer { get; init; }

    public required int CallCount { get; init; }
}
```

Create `src/AecoPostMortem.Findings/Suggestion.cs`:

```csharp
namespace AecoPostMortem.Findings;

/// <summary>
/// FR-56: a deterministic template's rendered text, bound to a check shape and populated from the
/// same operands and resolution the finding used. Never generated — §3.8 forbids a model call. A
/// finding class with no template ships with its evidence and no suggestion, never a generic one, so
/// <c>Finding.Suggestion</c> is nullable rather than defaulting to this type.
/// </summary>
public sealed record Suggestion
{
    public required string Text { get; init; }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/AecoPostMortem.Findings.Tests/AecoPostMortem.Findings.Tests.csproj --nologo -v q`

Expected: PASS — 3 tests.

- [ ] **Step 5: Commit**

```bash
git add src/AecoPostMortem.Findings/Evidence.cs src/AecoPostMortem.Findings/Resolution.cs src/AecoPostMortem.Findings/Suggestion.cs test/AecoPostMortem.Findings.Tests/SupportingShapesTests.cs
git commit -m "$(cat <<'EOF'
Publish evidence, resolution and suggestion as leaf value types (#23)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E6Aua8SoJ1D9qb2oxuYxNp
EOF
)"
```

---

### Task 2: `Recurrence` — FR-57's version-independent identity

**Files:**
- Create: `src/AecoPostMortem.Findings/Recurrence.cs`
- Test: `test/AecoPostMortem.Findings.Tests/RecurrenceTests.cs`

**Interfaces:**
- Consumes: nothing new (do not `cref` `FindingClass` here — it does not exist until Task 3; use `<c>` instead).
- Produces: `AecoPostMortem.Findings.Recurrence` with `required string Key`, `required IReadOnlyList<RecurrenceOccurrence> Occurrences`; `AecoPostMortem.Findings.RecurrenceOccurrence` with `required string SessionId`, `string? RuleSetVersion`.

- [ ] **Step 1: Write the failing test**

Create `test/AecoPostMortem.Findings.Tests/RecurrenceTests.cs`:

```csharp
namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// FR-57: a finding's identity is <c>(class, class-specific key)</c> and is version-independent. A
/// finding whose rule spans several rule-set versions is one finding, not several — the per-version
/// breakdown is carried as <see cref="Recurrence.Occurrences"/> on that one value.
/// </summary>
public sealed class RecurrenceTests
{
    [Fact]
    public void A_recurrence_carries_its_key_and_at_least_one_occurrence()
    {
        var recurrence = new Recurrence
        {
            Key = "prefer rg over grep",
            Occurrences =
            [
                new RecurrenceOccurrence { SessionId = "session-1", RuleSetVersion = "v3" },
            ],
        };

        Assert.Equal("prefer rg over grep", recurrence.Key);
        Assert.Single(recurrence.Occurrences);
    }

    [Fact]
    public void A_finding_spanning_several_rule_set_versions_is_one_recurrence_with_several_occurrences()
    {
        var recurrence = new Recurrence
        {
            Key = "prefer rg over grep",
            Occurrences =
            [
                new RecurrenceOccurrence { SessionId = "session-1", RuleSetVersion = "v2" },
                new RecurrenceOccurrence { SessionId = "session-2", RuleSetVersion = "v3" },
            ],
        };

        Assert.Equal(2, recurrence.Occurrences.Count);
        Assert.Equal("prefer rg over grep", recurrence.Key);
    }

    [Fact]
    public void An_occurrence_may_carry_no_rule_set_version()
    {
        var occurrence = new RecurrenceOccurrence { SessionId = "session-1" };

        Assert.Null(occurrence.RuleSetVersion);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/AecoPostMortem.Findings.Tests/AecoPostMortem.Findings.Tests.csproj --nologo -v q`

Expected: FAIL to compile — `Recurrence` and `RecurrenceOccurrence` do not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/AecoPostMortem.Findings/Recurrence.cs`:

```csharp
namespace AecoPostMortem.Findings;

/// <summary>
/// FR-57's decided identity: a finding's recurrence is <c>(class, class-specific key)</c> and is
/// version-independent. A finding whose rule spans several rule-set versions is one finding, not
/// several — the per-version breakdown lives on <see cref="Occurrences"/>, an attribute of this one
/// value, so there is no constructor that could produce a second <c>Finding</c> for the same key.
/// </summary>
public sealed record Recurrence
{
    public required string Key { get; init; }

    public required IReadOnlyList<RecurrenceOccurrence> Occurrences { get; init; }
}

/// <summary>One session in which a finding's key recurred.</summary>
public sealed record RecurrenceOccurrence
{
    public required string SessionId { get; init; }

    /// <summary>Null for finding classes that carry no rule-set version, such as
    /// <c>FindingClass.Waste</c> and <c>FindingClass.MissingCapability</c>.</summary>
    public string? RuleSetVersion { get; init; }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/AecoPostMortem.Findings.Tests/AecoPostMortem.Findings.Tests.csproj --nologo -v q`

Expected: PASS — 6 tests.

- [ ] **Step 5: Commit**

```bash
git add src/AecoPostMortem.Findings/Recurrence.cs test/AecoPostMortem.Findings.Tests/RecurrenceTests.cs
git commit -m "$(cat <<'EOF'
Publish recurrence as a version-independent identity (#23)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E6Aua8SoJ1D9qb2oxuYxNp
EOF
)"
```

---

### Task 3: `Finding` — the core contract

Scenario 1 and Scenario 2 of the finding contract (issue #23). This is the task the other six exist
to support: `Provenance` fails construction structurally, and the record carries exactly the seven
fields the acceptance criteria name.

**Files:**
- Create: `src/AecoPostMortem.Findings/Finding.cs`
- Test: `test/AecoPostMortem.Findings.Tests/FindingTests.cs`

**Interfaces:**
- Consumes: `AecoPostMortem.Findings.EvidenceItem`, `Resolution`, `Suggestion` (Task 1); `Recurrence`, `RecurrenceOccurrence` (Task 2).
- Produces: `AecoPostMortem.Findings.FindingClass` (enum: `RuleAdherenceToolChoice = 1`, `Waste = 2`, `RuleAdherenceWrittenContent = 3`, `MissingCapability = 4`); `AecoPostMortem.Findings.Provenance` (enum: `Observed`, `Derived`, `Inferred`); `AecoPostMortem.Findings.OperatorResponse` (enum: `Ignored`, `Accepted`, `Rejected`); `AecoPostMortem.Findings.Finding` with `required FindingClass Class`, `required Provenance Provenance`, `required IReadOnlyList<EvidenceItem> Evidence`, `required Recurrence Recurrence`, `Resolution? Resolution`, `Suggestion? Suggestion`, `OperatorResponse OperatorResponse` (defaults to `Ignored`).

- [ ] **Step 1: Write the failing test**

Create `test/AecoPostMortem.Findings.Tests/FindingTests.cs`:

```csharp
using System.Reflection;
using System.Runtime.CompilerServices;

namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// Scenario 1 and Scenario 2 of the finding contract (issue #23): construction fails without a
/// provenance level, and the record carries everything the surfaces need — class, provenance,
/// evidence, recurrence, the resolution used where one applies, its suggestion, and the operator's
/// response. No other field.
/// </summary>
public sealed class FindingTests
{
    [Fact]
    public void Provenance_is_a_required_member()
    {
        var property = typeof(Finding).GetProperty(nameof(Finding.Provenance));

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<RequiredMemberAttribute>());
    }

    [Fact]
    public void The_record_carries_everything_the_surfaces_need()
    {
        var finding = new Finding
        {
            Class = FindingClass.RuleAdherenceToolChoice,
            Provenance = Provenance.Derived,
            Evidence = [new EvidenceItem { Field = "data.toolName", Value = "grep" }],
            Recurrence = new Recurrence
            {
                Key = "prefer rg over grep",
                Occurrences = [new RecurrenceOccurrence { SessionId = "session-1", RuleSetVersion = "v3" }],
            },
            Resolution = new Resolution { OperandLayer = "NORMALIZED", CallCount = 12 },
            Suggestion = new Suggestion { Text = "name `rg`" },
            OperatorResponse = OperatorResponse.Accepted,
        };

        Assert.Equal(FindingClass.RuleAdherenceToolChoice, finding.Class);
        Assert.Equal(Provenance.Derived, finding.Provenance);
        Assert.Single(finding.Evidence);
        Assert.Equal("prefer rg over grep", finding.Recurrence.Key);
        Assert.Equal(12, finding.Resolution!.CallCount);
        Assert.Equal("name `rg`", finding.Suggestion!.Text);
        Assert.Equal(OperatorResponse.Accepted, finding.OperatorResponse);
    }

    [Fact]
    public void Resolution_and_suggestion_are_optional_and_operator_response_defaults_to_ignored()
    {
        var finding = new Finding
        {
            Class = FindingClass.Waste,
            Provenance = Provenance.Derived,
            Evidence = [new EvidenceItem { Field = "data.path", Value = "src/foo.cs" }],
            Recurrence = new Recurrence
            {
                Key = "src/foo.cs",
                Occurrences = [new RecurrenceOccurrence { SessionId = "session-1" }],
            },
        };

        Assert.Null(finding.Resolution);
        Assert.Null(finding.Suggestion);
        Assert.Equal(OperatorResponse.Ignored, finding.OperatorResponse);
    }

    /// <summary>The edge case named in issue #23: a finding whose recurrence is one session still
    /// carries a recurrence value rather than omitting the field.</summary>
    [Fact]
    public void A_single_session_finding_still_carries_a_recurrence_value()
    {
        var finding = new Finding
        {
            Class = FindingClass.MissingCapability,
            Provenance = Provenance.Inferred,
            Evidence = [new EvidenceItem { Field = "data.toolName", Value = "web_fetch" }],
            Recurrence = new Recurrence
            {
                Key = "web_fetch",
                Occurrences = [new RecurrenceOccurrence { SessionId = "session-1" }],
            },
        };

        Assert.NotNull(finding.Recurrence);
        Assert.Single(finding.Recurrence.Occurrences);
    }

    [Fact]
    public void The_four_finding_classes_are_numbered_to_match_the_PRD_table()
    {
        Assert.Equal(1, (int)FindingClass.RuleAdherenceToolChoice);
        Assert.Equal(2, (int)FindingClass.Waste);
        Assert.Equal(3, (int)FindingClass.RuleAdherenceWrittenContent);
        Assert.Equal(4, (int)FindingClass.MissingCapability);
    }

    [Fact]
    public void The_three_provenance_levels_are_distinct()
    {
        Assert.Equal(3, Enum.GetValues<Provenance>().Length);
    }

    [Fact]
    public void The_three_operator_responses_are_distinct()
    {
        Assert.Equal(3, Enum.GetValues<OperatorResponse>().Length);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/AecoPostMortem.Findings.Tests/AecoPostMortem.Findings.Tests.csproj --nologo -v q`

Expected: FAIL to compile — `Finding`, `FindingClass`, `Provenance` and `OperatorResponse` do not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/AecoPostMortem.Findings/Finding.cs`:

```csharp
namespace AecoPostMortem.Findings;

/// <summary>
/// PRD §3.3's four finding classes, numbered to match that table rather than build order — §3.4.3
/// records the build order as 2, 1, 4, with 3 gated out of v1 and waiting on input.
/// </summary>
public enum FindingClass
{
    RuleAdherenceToolChoice = 1,
    Waste = 2,
    RuleAdherenceWrittenContent = 3,
    MissingCapability = 4,
}

/// <summary>PRD §3.8: the three levels the UI must render distinguishably.</summary>
public enum Provenance
{
    Observed,
    Derived,
    Inferred,
}

/// <summary>
/// FR-45's three responses. A finding nobody has looked at yet is <see cref="Ignored"/> — there is
/// no separate "pending" state, because FR-45 names exactly three.
/// </summary>
public enum OperatorResponse
{
    Ignored,
    Accepted,
    Rejected,
}

/// <summary>
/// One finding. <see cref="Provenance"/> is <c>required</c>, so an object initializer that omits it
/// is a compile error (CS9035) — that is what "construction fails" means for a type with no runtime
/// validation to fail at (issue #23, Scenario 1).
/// </summary>
public sealed record Finding
{
    public required FindingClass Class { get; init; }

    public required Provenance Provenance { get; init; }

    public required IReadOnlyList<EvidenceItem> Evidence { get; init; }

    public required Recurrence Recurrence { get; init; }

    /// <summary>FR-33: only adherence figures carry a resolution.</summary>
    public Resolution? Resolution { get; init; }

    /// <summary>FR-56: a finding class with no template ships with its evidence and no suggestion,
    /// never a generic one.</summary>
    public Suggestion? Suggestion { get; init; }

    public OperatorResponse OperatorResponse { get; init; } = OperatorResponse.Ignored;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/AecoPostMortem.Findings.Tests/AecoPostMortem.Findings.Tests.csproj --nologo -v q`

Expected: PASS — 13 tests (7 new, 6 from Tasks 1–2).

- [ ] **Step 5: Commit**

```bash
git add src/AecoPostMortem.Findings/Finding.cs test/AecoPostMortem.Findings.Tests/FindingTests.cs
git commit -m "$(cat <<'EOF'
Publish the finding record; provenance fails construction structurally (#23)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E6Aua8SoJ1D9qb2oxuYxNp
EOF
)"
```

---

### Task 4: `FindingClassRegistry` — Scenario 3's registration

**Files:**
- Create: `src/AecoPostMortem.Findings/FindingClassRegistry.cs`
- Test: `test/AecoPostMortem.Findings.Tests/FindingClassRegistryTests.cs`

**Interfaces:**
- Consumes: `AecoPostMortem.Findings.FindingClass` (Task 3).
- Produces: `AecoPostMortem.Findings.FindingClassRegistration` with `required FindingClass Class`, `required string RecurrenceKeyDescription`; `AecoPostMortem.Findings.FindingClassRegistry.All` as `IReadOnlyList<FindingClassRegistration>`.

- [ ] **Step 1: Write the failing test**

Create `test/AecoPostMortem.Findings.Tests/FindingClassRegistryTests.cs`:

```csharp
namespace AecoPostMortem.Findings.Tests;

/// <summary>Scenario 3 of the finding contract (issue #23): every finding class is registered, and
/// declares what makes two occurrences in two sessions the same finding.</summary>
public sealed class FindingClassRegistryTests
{
    [Fact]
    public void Every_finding_class_is_registered_exactly_once()
    {
        var registered = FindingClassRegistry.All
            .Select(registration => registration.Class)
            .OrderBy(value => value)
            .ToArray();

        var declared = Enum.GetValues<FindingClass>().OrderBy(value => value).ToArray();

        Assert.Equal(declared, registered);
        Assert.Equal(registered.Length, registered.Distinct().Count());
    }

    [Fact]
    public void Every_registration_declares_a_non_empty_recurrence_key()
    {
        Assert.All(
            FindingClassRegistry.All,
            registration => Assert.False(string.IsNullOrWhiteSpace(registration.RecurrenceKeyDescription)));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/AecoPostMortem.Findings.Tests/AecoPostMortem.Findings.Tests.csproj --nologo -v q`

Expected: FAIL to compile — `FindingClassRegistration` and `FindingClassRegistry` do not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/AecoPostMortem.Findings/FindingClassRegistry.cs`:

```csharp
namespace AecoPostMortem.Findings;

/// <summary>FR-57: what one <see cref="FindingClass"/> declares makes two occurrences in two
/// sessions the same finding.</summary>
public sealed record FindingClassRegistration
{
    public required FindingClass Class { get; init; }

    public required string RecurrenceKeyDescription { get; init; }
}

/// <summary>
/// Scenario 3 of the finding contract (issue #23): every finding class is registered, and declares
/// its recurrence key. Four entries, fixed — <see cref="FindingClass"/> is a closed set.
/// </summary>
public static class FindingClassRegistry
{
    public static readonly IReadOnlyList<FindingClassRegistration> All =
    [
        new()
        {
            Class = FindingClass.RuleAdherenceToolChoice,
            RecurrenceKeyDescription = "the rule statement",
        },
        new()
        {
            Class = FindingClass.Waste,
            RecurrenceKeyDescription =
                "the file path for a repeated read, or the hook identity for a hook failure",
        },
        new()
        {
            Class = FindingClass.RuleAdherenceWrittenContent,
            RecurrenceKeyDescription = "the rule statement",
        },
        new()
        {
            Class = FindingClass.MissingCapability,
            RecurrenceKeyDescription = "the tool name",
        },
    ];
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/AecoPostMortem.Findings.Tests/AecoPostMortem.Findings.Tests.csproj --nologo -v q`

Expected: PASS — 15 tests.

- [ ] **Step 5: Commit**

```bash
git add src/AecoPostMortem.Findings/FindingClassRegistry.cs test/AecoPostMortem.Findings.Tests/FindingClassRegistryTests.cs
git commit -m "$(cat <<'EOF'
Register the four finding classes with their recurrence keys (#23)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E6Aua8SoJ1D9qb2oxuYxNp
EOF
)"
```

---

### Task 5: `CheckRegistry` — Scenarios 4 and 5

**Files:**
- Create: `src/AecoPostMortem.Findings/CheckRegistry.cs`
- Test: `test/AecoPostMortem.Findings.Tests/CheckRegistryTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks — `CheckId` is an abstract `string`, not a `FindingClass` or a Rules type.
- Produces: `AecoPostMortem.Findings.CheckRunStatus` (enum: `Ran`, `Refused`); `AecoPostMortem.Findings.CheckRegistryEntry` with `required string CheckId`, `required CheckRunStatus Status`, `required int Population`, `int? FindingCount`, `string? RefusalReason`; `AecoPostMortem.Findings.CheckRegistry` with `required IReadOnlyList<CheckRegistryEntry> Entries`.

- [ ] **Step 1: Write the failing test**

Create `test/AecoPostMortem.Findings.Tests/CheckRegistryTests.cs`:

```csharp
namespace AecoPostMortem.Findings.Tests;

/// <summary>Scenarios 4 and 5 of the finding contract (issue #23): every check is registered whether
/// or not it fired, and a refused check is distinguishable from a clean one.</summary>
public sealed class CheckRegistryTests
{
    [Fact]
    public void Every_check_appears_regardless_of_status()
    {
        var registry = new CheckRegistry
        {
            Entries =
            [
                new CheckRegistryEntry
                {
                    CheckId = "contradiction-check",
                    Status = CheckRunStatus.Ran,
                    Population = 35,
                    FindingCount = 0,
                },
                new CheckRegistryEntry
                {
                    CheckId = "written-content-forbidden-symbol",
                    Status = CheckRunStatus.Refused,
                    Population = 3,
                    RefusalReason = "scope mechanism ambiguous",
                },
            ],
        };

        Assert.Equal(2, registry.Entries.Count);
        Assert.Contains(registry.Entries, entry => entry.CheckId == "contradiction-check");
        Assert.Contains(registry.Entries, entry => entry.CheckId == "written-content-forbidden-symbol");
    }

    [Fact]
    public void A_refused_check_is_distinguishable_from_a_clean_one_not_both_zero()
    {
        var refused = new CheckRegistryEntry
        {
            CheckId = "written-content-forbidden-symbol",
            Status = CheckRunStatus.Refused,
            Population = 3,
            RefusalReason = "scope mechanism ambiguous",
        };

        var clean = new CheckRegistryEntry
        {
            CheckId = "contradiction-check",
            Status = CheckRunStatus.Ran,
            Population = 35,
            FindingCount = 0,
        };

        Assert.Null(refused.FindingCount);
        Assert.NotNull(clean.FindingCount);
        Assert.Equal(0, clean.FindingCount);
        Assert.NotEqual(refused.Status, clean.Status);
    }

    [Fact]
    public void Population_is_required_even_when_refused()
    {
        var refused = new CheckRegistryEntry
        {
            CheckId = "written-content-forbidden-symbol",
            Status = CheckRunStatus.Refused,
            Population = 3,
            RefusalReason = "scope mechanism ambiguous",
        };

        Assert.Equal(3, refused.Population);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/AecoPostMortem.Findings.Tests/AecoPostMortem.Findings.Tests.csproj --nologo -v q`

Expected: FAIL to compile — `CheckRegistry`, `CheckRegistryEntry` and `CheckRunStatus` do not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/AecoPostMortem.Findings/CheckRegistry.cs`:

```csharp
namespace AecoPostMortem.Findings;

/// <summary>Scenario 5 of the finding contract (issue #23): a refused check and a check that ran and
/// found nothing are distinct states, not both zero.</summary>
public enum CheckRunStatus
{
    Ran,
    Refused,
}

/// <summary>
/// One check's outcome for one completed analysis run. <see cref="FindingCount"/> is <c>null</c> when
/// <see cref="Status"/> is <see cref="CheckRunStatus.Refused"/> and a real integer — including
/// <c>0</c> — when it is <see cref="CheckRunStatus.Ran"/>, so a refused check is never read as a
/// clean one that happened to find nothing. <see cref="CheckId"/> is an abstract identifier rather
/// than an enum: a check is open-ended — the eventual <c>AecoPostMortem.Rules</c> check-shape
/// catalogue plus PRD §3.9's special-purpose checks (contradiction, unresolvable-spawn,
/// malformed-line) — while <see cref="AecoPostMortem.Findings.FindingClass"/> is a closed set of four.
/// </summary>
public sealed record CheckRegistryEntry
{
    public required string CheckId { get; init; }

    public required CheckRunStatus Status { get; init; }

    /// <summary>The candidate set the check considered — e.g. sessions in the corpus — defined
    /// whether or not the check went on to run cleanly (Scenario 4).</summary>
    public required int Population { get; init; }

    public int? FindingCount { get; init; }

    public string? RefusalReason { get; init; }
}

/// <summary>Scenario 4 of the finding contract (issue #23): every check appears here, whether or not
/// it fired.</summary>
public sealed record CheckRegistry
{
    public required IReadOnlyList<CheckRegistryEntry> Entries { get; init; }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/AecoPostMortem.Findings.Tests/AecoPostMortem.Findings.Tests.csproj --nologo -v q`

Expected: PASS — 18 tests.

- [ ] **Step 5: Commit**

```bash
git add src/AecoPostMortem.Findings/CheckRegistry.cs test/AecoPostMortem.Findings.Tests/CheckRegistryTests.cs
git commit -m "$(cat <<'EOF'
Publish the check registry; refused and clean are never both zero (#23)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E6Aua8SoJ1D9qb2oxuYxNp
EOF
)"
```

---

### Task 6: Retire the placeholder test, update the router doc, verify the whole solution

The last real content to land in `AecoPostMortem.Findings` was nothing — `ProjectReferenceTests.cs`
exists solely to prove the project reference and target framework hold while the project is empty,
and its own comment says the first story to land real content here replaces it (`test/AecoPostMortem.Data.Tests`
already shows S-01 doing exactly this for `Data`). Five real test files now cover the same ground and
far more.

**Files:**
- Delete: `test/AecoPostMortem.Findings.Tests/ProjectReferenceTests.cs`
- Modify: `src/AecoPostMortem.Findings/CLAUDE.md`

**Interfaces:**
- Consumes: everything from Tasks 1–5.
- Produces: nothing new.

- [ ] **Step 1: Delete the placeholder test**

```bash
git rm test/AecoPostMortem.Findings.Tests/ProjectReferenceTests.cs
```

- [ ] **Step 2: Update the router doc**

Replace the full contents of `src/AecoPostMortem.Findings/CLAUDE.md` with:

```markdown
# AecoPostMortem.Findings

The four finding classes, provenance, recurrence, the Monitor comparison, suggestions.

## Structure

| File | What it holds |
|---|---|
| `Finding.cs` | `FindingClass`, `Provenance`, `OperatorResponse`, and the `Finding` record itself |
| `FindingClassRegistry.cs` | the four finding classes, each declaring its recurrence key (FR-57) |
| `Evidence.cs` | `EvidenceItem` — one quoted event field |
| `Recurrence.cs` | `Recurrence`, `RecurrenceOccurrence` — FR-57's version-independent identity |
| `Resolution.cs` | FR-33's layer-used-per-operand and call count, carried where an adherence figure has one |
| `Suggestion.cs` | FR-56's deterministic template text |
| `CheckRegistry.cs` | `CheckRunStatus`, `CheckRegistryEntry`, `CheckRegistry` — every check's run status and population, whether or not it fired |

## References

`Rules` and `Data` — it does the orchestration `Rules` deliberately cannot. `Rules` takes plain
inputs and returns results with no knowledge of storage or of what produced its inputs; `Findings`
is the project that reads through `Data`, feeds `Rules` its operands, and writes the results back.
That split is why the non-negotiable invariant in `AecoPostMortem.Rules/CLAUDE.md` holds: the
orchestrator can name tools and repositories, the checker never sees them.

Neither reference is used yet: the shapes in this project are pure C# types with no persistence and
no dependency on a concrete check. S-44 publishes the shape; the stories it blocks are what read
through `Data` and call into `Rules`.

## Non-obvious decisions

### The finding record has no `Id` and no `SessionId`

Scenario 2 of the finding contract (issue #23) names seven fields, and only those seven: class,
provenance, evidence, recurrence, the resolution used where one applies, its suggestion, and the
operator's response. A finding's identity is `(class, class-specific key)` per FR-57, not a row id or
a session — a session is where `Recurrence` says the finding recurred, not where the finding lives.

### `Provenance` fails construction by being `required`, not by validating

`Finding.Provenance` has no runtime check for presence — the C# compiler already refuses to compile
an object initializer that omits a `required` member. `FindingTests.Provenance_is_a_required_member`
proves the property still carries `RequiredMemberAttribute` rather than re-deriving the guarantee at
run time, the same reasoning `AecoPostMortem.Rules/CLAUDE.md` gives for its own invariant: structural
beats conventional because there is no commit at which it can be skipped by accident.

### A refused check and a clean check are distinguished by null, not by a third status

`CheckRegistryEntry.FindingCount` is `null` when `Status` is `Refused` and a real integer —
including `0` — when `Status` is `Ran`. `CheckRunStatus` has exactly two values because the
acceptance criteria (issue #23, Scenario 5) name exactly two states to distinguish; a third "never
attempted" status was considered and rejected as unmotivated by anything in FR-37 or FR-42.

## Status

The finding record and check-registry shapes. No finding class has detection logic yet, and no check
exists to register a real id — those arrive with S-02, S-05, S-06 and the rest of the stories this
contract unblocks.
```

- [ ] **Step 3: Check the router doc's size and shape budget**

Run: `python scripts/check-claude-md.py src/AecoPostMortem.Findings/CLAUDE.md`

Expected: no findings reported.

- [ ] **Step 4: Run the whole solution's tests**

Run: `dotnet test AecoPostMortem.sln --nologo -v q`

Expected: PASS — every project builds (`AecoPostMortem.Findings.Tests` no longer references
`ProjectReferenceTests.cs`), and `AecoPostMortem.Containment.Tests` still passes (this task adds no
project and changes no `.csproj`).

- [ ] **Step 5: Commit**

```bash
git add src/AecoPostMortem.Findings/CLAUDE.md
git commit -m "$(cat <<'EOF'
Retire the empty-project placeholder now that the contract has landed (#23)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01E6Aua8SoJ1D9qb2oxuYxNp
EOF
)"
```
