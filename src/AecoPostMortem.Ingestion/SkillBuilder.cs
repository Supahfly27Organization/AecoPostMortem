using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;

namespace AecoPostMortem.Ingestion;

/// <summary>
/// Builds one <see cref="Skill"/> row per <c>skill.invoked</c> event — the NORMALIZED layer's own
/// record of which of the operator's process scaffolding actually ran (FR-25), populated at ingest
/// time so <c>SessionRecording.Build</c> has real rows to read instead of an empty table.
/// </summary>
public static class SkillBuilder
{
    public static IReadOnlyList<Skill> Build(string sessionId, IReadOnlyList<RawEvent> events)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(events);

        var skills = new List<Skill>();

        foreach (var raw in events)
        {
            if (raw.EventType != "skill.invoked" || !EventEnvelopeReader.TryRead(raw, out var envelope))
            {
                continue;
            }

            skills.Add(new Skill
            {
                SessionId = sessionId,
                EventId = envelope.Id,
                Name = JsonElementReading.StringOrEmpty(envelope.Data, "name"),
                Path = JsonElementReading.StringOrNull(envelope.Data, "path"),
                Description = JsonElementReading.StringOrNull(envelope.Data, "description"),
                PluginName = JsonElementReading.StringOrNull(envelope.Data, "pluginName"),
                PluginVersion = JsonElementReading.StringOrNull(envelope.Data, "pluginVersion"),
                InvokedAt = raw.Timestamp,
                OwnerKind = envelope.AgentId is null ? OwnerKind.Main : OwnerKind.Agent,
                AgentId = envelope.AgentId,
            });
        }

        return skills;
    }
}
