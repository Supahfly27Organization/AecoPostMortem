# Scanning Tools

## Primary Responsibilities

| Tool | Use For |
|------|---------|
| **SonarQube** | Code quality, bugs, maintainability, complexity, coverage, technical debt |
| **Semgrep** | Source-code security: injection, XSS, auth mistakes, secrets in code, dangerous APIs |
| **Trivy** | Dependency CVEs, Docker images, IaC, secrets in config/repo files |

## When to Use Which

| Scenario | Primary | Also Use |
|----------|---------|----------|
| Code quality / refactoring | SonarQube | Semgrep if security-sensitive code |
| Application security | Semgrep | SonarQube for quality context; Trivy if deps/Docker involved |
| Dependencies / Docker / IaC | Trivy | Semgrep for source patterns |
| Secrets in source code | Semgrep | Trivy too if practical |
| Secrets in config/repo/CI files | Trivy | Semgrep too if practical |

## Scan Order

**Normal dev:** SonarQube for quality → Semgrep for security → Trivy only if deps/Docker/config touched.

**Before PR:** SonarQube (changed code) → Semgrep (changed files) → Trivy if package/Docker/CI files changed.

**Before release:** All three.

## Token Discipline

- Prefer targeted scans (changed files, affected folder) — full-repo only when explicitly requested, releasing, or no clear scope.
- Summarize output; don't paste raw reports unless asked.
- Priority: exploitable security → critical/high CVEs → broken correctness → maintainability → style.

## Conflict Resolution

- **Semgrep** wins for source-code security.
- **Trivy** wins for dependency/container/IaC vulnerabilities.
- **SonarQube** wins for maintainability, reliability, and quality trends.
