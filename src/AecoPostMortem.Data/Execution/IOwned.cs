namespace AecoPostMortem.Data.Execution;

/// <summary>Who ran the thing: the main thread, or a named subagent.</summary>
public enum OwnerKind
{
    Main,
    Agent,
}

/// <summary>
/// Carried by every derived entity that a subagent can own. <see cref="OwnerKind"/> is not nullable,
/// so there is no null a caller could read as "attribution unknown" — absence of an agent id means
/// main thread, exactly, and the data map measured that rather than assuming it.
/// </summary>
public interface IOwned
{
    OwnerKind OwnerKind { get; init; }

    string? AgentId { get; init; }
}

public static class Owned
{
    public static bool IsMainThread(this IOwned owned)
    {
        ArgumentNullException.ThrowIfNull(owned);
        return owned.OwnerKind == OwnerKind.Main;
    }
}
