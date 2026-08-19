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
