# AecoPostMortem.Data

The `DbContext`, the entity model and the EF Core migrations; the only project that owns the schema.

## References

Nothing. It is the base of the dependency graph — `Ingestion`, `Findings` and `Api` all read and
write through it, so it cannot depend back on any of them without creating a cycle.

## Non-obvious decisions

RAW appends bypass EF Core change tracking: batched raw SQL, because a measured 56,138 rows arrive
in one full ingest (PRD §3.1). Change tracking at that volume is the wrong tool; the batched-SQL
path exists for that reason, not as a style preference.

Only RAW carries a migration. NORMALIZED and FINDINGS are re-derived from RAW, never migrated — a
migration against them is a defect (Repo Rule 4, PRD §3.8).

## Status

Empty. S-47 created it; S-01 populates it.
