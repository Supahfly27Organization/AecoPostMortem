namespace AecoPostMortem.Cli;

/// <summary>
/// The single source of truth for what commands exist. The listing is rendered from this table and
/// invocations are dispatched from it, so a command cannot exist without being documented.
/// </summary>
public static class CommandSurface
{
    public static IReadOnlyList<CommandSpec> Commands { get; } =
    [
        new(
            "ingest",
            "[path]",
            "stdout — the coverage report",
            "Read the Copilot session state and re-derive from it.",
            "the ingestion stories in E1"),
        new(
            "rebuild",
            "",
            "stdout — the re-derivation summary",
            "Re-derive the normalized and findings layers from RAW.",
            "the ingestion stories in E1"),
        new(
            "purge",
            "",
            "stdout — what was deleted",
            "Delete the local store.",
            "S-01 (local store and its governance)"),
        new(
            "serve",
            "[--port <n>]",
            "stdout — the listening URL",
            "Start the local API and web shell.",
            "S-48 (API host, web shell and the zero-data state)"),
    ];

    public static CommandSpec? Find(string name) =>
        Commands.FirstOrDefault(
            command => string.Equals(command.Name, name, StringComparison.OrdinalIgnoreCase));
}
