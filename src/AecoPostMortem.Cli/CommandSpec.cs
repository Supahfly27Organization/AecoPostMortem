namespace AecoPostMortem.Cli;

/// <summary>One command on the surface FR-58 enumerates.</summary>
/// <param name="Name">The word the operator types.</param>
/// <param name="Arguments">Its arguments as the listing shows them; empty when it takes none.</param>
/// <param name="OutputChannel">Where its output goes, and what that output is.</param>
/// <param name="Summary">What it does, in one line.</param>
/// <param name="ArrivesWith">The story that implements it. S-47 ships the surface, not the behaviour.</param>
public sealed record CommandSpec(
    string Name,
    string Arguments,
    string OutputChannel,
    string Summary,
    string ArrivesWith);
