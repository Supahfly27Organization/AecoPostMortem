# Docs Map

The index of every sidecar in this repository. A router points at the sidecars its own module owns;
this file lists them all in one place, so a document cannot exist without being findable.

## Root router sidecars — `docs/claude/`

| Sidecar | Read when |
|---|---|
| `docs/claude/DOMAIN_MODEL.md` | adding or changing an entity, writing a query, or checking what a column means |
| `docs/claude/NORMALIZED_MODEL.md` | writing a query against a derived table, or checking what a NORMALIZED column means |
| `docs/claude/PATTERNS.md` | following or establishing a coding convention or architectural rule |
| `docs/claude/SCANNING_TOOLS.md` | choosing between SonarQube, Semgrep and Trivy for a security or quality pass |
| `docs/claude/KNOWLEDGE_TOOLS.md` | navigating the codebase, or deciding which knowledge tool answers a question |

## Module sidecars

Each module's router carries its own architecture, decisions and playbook; a module gets a `docs/`
sidecar when its router runs up against its size budget (`scripts/check-claude-md.py`).

| Sidecar | Read when |
|---|---|
| `src/AecoPostMortem.Data/docs/DERIVED_LAYER.md` | mapping a new derived entity, changing an existing one, or debugging why a check constraint fired |
