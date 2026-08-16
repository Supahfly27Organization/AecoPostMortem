#!/usr/bin/env python3
"""
check-claude-md.py — enforce the size and shape budget on CLAUDE.md routers and
their colocated `docs/` sidecars.

Why this exists
---------------
Measured 2026-08-14, before the split: 13 CLAUDE.md files held 2,248 lines and
311,232 bytes. The largest, `src/AecoLedger.Insights.Domain/CLAUDE.md`, was
84,174 bytes across 174 lines — it would have passed a 200-line cap while being
the biggest file in the repo, because 97 of its lines exceeded 400 bytes and its
longest single line was 1,710. Rows are only a meaningful budget once lines are
wrapped.

The growth mechanism was story-by-story appending: 73 of that file's 174 lines
carried an `S-nn`/`#nn` reference, restating in prose what git and GitHub already
hold. The root CLAUDE.md already said module docs "should never just accumulate";
they accumulated anyway. Intent does not hold a size budget — a checker does.

The model is the same one `check-claims.py` applies to specs: state the rule
mechanically, fail the build, and let the exceptions be explicit.

Rules
-----
    1  router  <= 120 lines and <=  8,000 bytes
    2  sidecar <= 200 lines and <= 14,000 bytes
    3  no line over 100 columns (fenced code, table rows and unbreakable
       single-token lines are exempt)
    4  every sidecar is referenced by its own module's router
    5  every slash-bearing path reference resolves on disk
    6  no changelog voice in a router (S-nn / #nn / "used to" / "replaced")
    7  every sidecar opens with a `> **Scope:**` / `> **Read when:**` blockquote
    8  no bare back-references ("see above", "those same", ...)
    9  every sidecar is listed in docs/claude/DOCS_MAP.md
   10  a decision is a `###` heading, not a multi-sentence list bullet

Files in PENDING below are reported but do not fail the run: they predate the
convention and are migrated module by module. Anything not listed is checked in
full, so a new file is strict from birth. Removing a name from PENDING is the
last step of migrating it.

Usage
-----
    python scripts/check-claude-md.py                  # walk the repo
    python scripts/check-claude-md.py src/AecoLedger.Core
    python scripts/check-claude-md.py --strict         # PENDING files fail too
    python scripts/check-claude-md.py --pending        # show migration backlog

Exit status is 1 when a non-pending file breaks a rule, so it can gate a commit.
"""

from __future__ import annotations

import argparse
import pathlib
import re
import sys

REPO = pathlib.Path(__file__).resolve().parent.parent

ROUTER_MAX_LINES, ROUTER_MAX_BYTES = 120, 8_000
SIDECAR_MAX_LINES, SIDECAR_MAX_BYTES = 200, 14_000
MAX_COLUMNS = 100

DOCS_MAP = REPO / "docs" / "claude" / "DOCS_MAP.md"

# Not yet migrated to the router + sidecar convention. Empty this list.
PENDING: set[str] = set()

SKIP_DIRS = {".git", "node_modules", "bin", "obj", "dist", ".vs", ".idea"}

# Rule 6 — a router states what is true now; history lives in git and GitHub.
CHANGELOG = re.compile(
    r"\bS-\d+\b|#\d{2,}\b|\bused to\b|\breplaced (?:#|the |its )|\bbriefly shipped\b"
    r"|\bno longer\b|\bpreviously\b|\bwas added (?:in|by)\b",
    re.I,
)

# Rule 8 — a reference that only resolves by reading the file top to bottom.
BACKREF = re.compile(
    r"\bsee above\b|\bsee below\b|\bthose same\b|\bas above\b|\bnoted above\b"
    r"|\bmentioned earlier\b|\bthe same \d+\b",
    re.I,
)

# Rule 5 — a path reference worth resolving carries a separator and an extension.
PATH_REF = re.compile(r"`([A-Za-z0-9_.][A-Za-z0-9_./-]*/[A-Za-z0-9_./-]+\.[A-Za-z]{1,6})`")
MD_LINK = re.compile(r"\[[^\]]*\]\(([^)#\s]+)(?:#[^)\s]*)?\)")

SCOPE_HEADER = re.compile(r"^>\s*\*\*Scope:\*\*", re.M)
READ_WHEN_HEADER = re.compile(r"^>\s*\*\*Read when:\*\*", re.M)

SENTENCE_END = re.compile(r"[.!?](?:\s|$)")

# Explicit per-line escape, mirroring check-claims.py's `<!--src: reason-->`:
# state the reason inline rather than widening a rule. Used, for example, where a
# document quotes a banned phrase in order to ban it.
ESCAPE = re.compile(r"<!--\s*doc-ok:")

# An HTML comment renders as nothing, so it is not part of a line's visible width.
COMMENT = re.compile(r"<!--.*?-->")


class Finding:
    def __init__(self, rule: int, line: int, message: str) -> None:
        self.rule, self.line, self.message = rule, line, message

    def __str__(self) -> str:
        where = f"{self.line:>5}" if self.line else "    -"
        return f"  {where}  [rule {self.rule:>2}]  {self.message}"


def rel(path: pathlib.Path) -> str:
    try:
        return path.resolve().relative_to(REPO).as_posix()
    except ValueError:
        return path.as_posix()


def is_sidecar(path: pathlib.Path) -> bool:
    """
    A module sidecar is a .md inside the `docs/` folder of a directory that has a
    router. `docs/claude/` holds the root router's sidecars under the name the repo
    already used for them; DOCS_MAP.md is the index, not a sidecar.
    """
    if path.name == "CLAUDE.md":
        return False
    if rel(path).startswith("docs/claude/"):
        return path.name != "DOCS_MAP.md"
    return path.parent.name == "docs" and (path.parent.parent / "CLAUDE.md").exists()


def module_of(path: pathlib.Path) -> pathlib.Path:
    """The directory whose router owns this file."""
    if not is_sidecar(path):
        return path.parent
    return REPO if rel(path).startswith("docs/claude/") else path.parent.parent


def code_fence_mask(lines: list[str]) -> list[bool]:
    """True where the line sits inside a fenced code block."""
    mask, in_fence = [], False
    for raw in lines:
        stripped = raw.strip()
        if stripped.startswith("```") or stripped.startswith("~~~"):
            in_fence = not in_fence
            mask.append(True)
            continue
        mask.append(in_fence)
    return mask


def resolves(target: str, path: pathlib.Path) -> bool:
    """A reference resolves against the repo root, the file's dir, or its parent."""
    if target.startswith(("http://", "https://", "mailto:")):
        return True
    for base in (REPO, path.parent, path.parent.parent, path.parent.parent.parent):
        if (base / target).exists():
            return True
    return False


def check(path: pathlib.Path, docs_map_text: str) -> list[Finding]:
    text = path.read_text(encoding="utf-8")
    lines = text.splitlines()
    fenced = code_fence_mask(lines)
    sidecar = is_sidecar(path)
    findings: list[Finding] = []

    # Rules 1 and 2 — size budget.
    max_lines = SIDECAR_MAX_LINES if sidecar else ROUTER_MAX_LINES
    max_bytes = SIDECAR_MAX_BYTES if sidecar else ROUTER_MAX_BYTES
    kind = "sidecar" if sidecar else "router"
    nbytes = len(text.encode("utf-8"))
    if len(lines) > max_lines:
        findings.append(Finding(1 + sidecar, 0, f"{kind} is {len(lines)} lines, budget {max_lines}"))
    if nbytes > max_bytes:
        findings.append(Finding(1 + sidecar, 0, f"{kind} is {nbytes:,} bytes, budget {max_bytes:,}"))

    for n, raw in enumerate(lines, 1):
        if fenced[n - 1] or ESCAPE.search(raw):
            continue
        stripped = raw.strip()

        # Rule 3 — a table row and a lone unbreakable token cannot be wrapped, and an
        # HTML comment renders as nothing, so it does not count toward the width.
        visible = COMMENT.sub("", raw).rstrip()
        if len(visible) > MAX_COLUMNS and not stripped.startswith("|") and len(stripped.split()) > 1:
            findings.append(Finding(3, n, f"{len(visible)} columns: {stripped[:60]}..."))

        # Rule 5 — path references resolve.
        for target in PATH_REF.findall(raw) + MD_LINK.findall(raw):
            if not resolves(target, path):
                findings.append(Finding(5, n, f"unresolved reference `{target}`"))

        # Rule 6 — no changelog voice in a router.
        if not sidecar:
            hit = CHANGELOG.search(raw)
            if hit:
                findings.append(Finding(6, n, f"changelog voice \"{hit.group(0)}\" — state what is true now"))

        # Rule 8 — no bare back-references.
        hit = BACKREF.search(raw)
        if hit:
            findings.append(Finding(8, n, f"back-reference \"{hit.group(0)}\" — cite the target instead"))

        # Rule 10 — a decision is a heading, not a paragraph in a bullet.
        if stripped.startswith(("- ", "* ")) and len(stripped) > 250:
            if len(SENTENCE_END.findall(stripped)) >= 2:
                findings.append(Finding(10, n, "multi-sentence bullet — promote it to a `###` decision heading"))

    if sidecar:
        # Rule 7 — orientation header.
        if not SCOPE_HEADER.search(text) or not READ_WHEN_HEADER.search(text):
            findings.append(Finding(7, 0, "missing the `> **Scope:**` / `> **Read when:**` header"))

        # Rule 4 — the module's router points here.
        module = module_of(path)
        router = module / "CLAUDE.md"
        ref = path.resolve().relative_to(module.resolve()).as_posix()
        if not router.exists():
            findings.append(Finding(4, 0, f"no router at {rel(router)}"))
        elif ref not in router.read_text(encoding="utf-8"):
            findings.append(Finding(4, 0, f"not referenced by {rel(router)} (expected `{ref}`)"))

        # Rule 9 — the human index lists it.
        if rel(path) not in docs_map_text:
            findings.append(Finding(9, 0, f"not listed in {rel(DOCS_MAP)}"))

    return findings


def collect(paths: list[str]) -> list[pathlib.Path]:
    roots = [pathlib.Path(p) for p in paths] if paths else [REPO]
    out: set[pathlib.Path] = set()
    for root in roots:
        if root.is_file():
            out.add(root.resolve())
            continue
        for found in root.rglob("*.md"):
            if any(part in SKIP_DIRS for part in found.parts):
                continue
            if found.name == "CLAUDE.md" or is_sidecar(found):
                out.add(found.resolve())
    return sorted(out)


def main() -> int:
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    ap.add_argument("paths", nargs="*", help="files or directories (default: the repo)")
    ap.add_argument("--strict", action="store_true", help="PENDING files fail too")
    ap.add_argument("--pending", action="store_true", help="print the migration backlog and exit")
    ap.add_argument("--quiet", action="store_true", help="print only the summary")
    args = ap.parse_args()

    if args.pending:
        print(f"{len(PENDING)} file(s) awaiting migration:")
        for name in sorted(PENDING):
            print(f"  {name}")
        return 0

    files = collect(args.paths)
    if not files:
        print("no CLAUDE.md or sidecar files found", file=sys.stderr)
        return 2

    docs_map_text = DOCS_MAP.read_text(encoding="utf-8") if DOCS_MAP.exists() else ""
    if not docs_map_text:
        print(f"warning: {rel(DOCS_MAP)} is missing — rule 9 cannot be checked", file=sys.stderr)

    failures = pending_hits = 0
    for path in files:
        findings = check(path, docs_map_text)
        if not findings:
            continue
        deferred = rel(path) in PENDING and not args.strict
        if deferred:
            pending_hits += len(findings)
            if not args.quiet:
                print(f"\n{rel(path)}  ({len(findings)} finding(s), pending migration)")
            continue
        failures += len(findings)
        if args.quiet:
            continue
        print(f"\n{rel(path)}")
        for finding in findings:
            print(finding)

    print()
    if pending_hits:
        print(f"{pending_hits} finding(s) in {len(PENDING)} file(s) awaiting migration (not failing).")
    if failures:
        print(f"{failures} finding(s) in migrated files across {len(files)} file(s) checked.")
        print("Fix them, or move the detail into a colocated `docs/` sidecar.")
        return 1

    print(f"All migrated files pass across {len(files)} file(s) checked.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
