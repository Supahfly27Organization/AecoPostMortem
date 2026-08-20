namespace AecoPostMortem.Rules;

/// <summary>
/// FR-27's version computation (S-20, issue #33): a rule-set version is identified by the block set
/// its sessions carried, scoped per repository — sessions sharing an identical block set share a
/// version, and a version's window is stated as the first and last session that carried it, in that
/// repository's own chronological order.
/// </summary>
public static class RuleSetVersioning
{
    public static IReadOnlyList<RuleSetVersion> Compute(IEnumerable<SessionRuleSet> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var versions = new List<RuleSetVersion>();

        foreach (var repositoryGroup in sessions.GroupBy(session => session.Repository))
        {
            var chronological = repositoryGroup
                .OrderBy(session => session.StartedAt, StringComparer.Ordinal)
                .ThenBy(session => session.SessionId, StringComparer.Ordinal)
                .ToArray();

            var byHash = chronological
                .Select(session => (Session: session, Hash: RuleSetVersionHasher.ComputeHash(session.Blocks)))
                .GroupBy(entry => entry.Hash);

            foreach (var hashGroup in byHash)
            {
                var members = hashGroup.ToArray();
                versions.Add(new RuleSetVersion
                {
                    Id = new RuleSetVersionId { Repository = repositoryGroup.Key, Hash = hashGroup.Key },
                    FirstSessionId = members[0].Session.SessionId,
                    FirstSessionStartedAt = members[0].Session.StartedAt,
                    LastSessionId = members[^1].Session.SessionId,
                    SessionCount = members.Length,
                });
            }
        }

        return versions
            .OrderBy(version => version.Repository, StringComparer.Ordinal)
            .ThenBy(version => version.FirstSessionStartedAt, StringComparer.Ordinal)
            .ThenBy(version => version.FirstSessionId, StringComparer.Ordinal)
            .ToArray();
    }
}
