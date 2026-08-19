#!/usr/bin/env python3
"""
check-apply-patch-roundtrip.py — FR-4's corpus round-trip gate: "a single
failure fails the build" (S-03's third acceptance scenario).

Why this exists
----------------
tool.execution_start.data.arguments is polymorphic (FR-4): an object for
most tools, a bare JSON string for apply_patch's whole patch envelope. A
parser that assumes an object silently drops every patch — PRD §3.9's first
listed failure mode, because finding class 3 loses its entire input,
silently and without error.

The check itself lives in C#, next to the parser it proves:
test/AecoPostMortem.Ingestion.Tests/ApplyPatchCorpusRoundTripTests.cs
exercises AecoPostMortem.Ingestion.ToolArguments directly, over every
apply_patch call in the live reference corpus, rather than a Python
re-implementation that could drift from the real parser. This script is the
CI entry point: it runs that one test in isolation and forwards its exit
code — the same shape as freeze-corpus-manifest.py's own --check gate.

The corpus bytes are not checked in (fixtures/README.md) — only their
hashes. The test reads the live source directory recorded in
fixtures/corpus-manifest.json (or AECOPOSTMORTEM_CORPUS_SOURCE) and skips,
rather than fails, on a machine that does not have it — so this script's
exit code is 0 whether the test passed or was skipped, and 1 only on an
actual round-trip failure.

Usage
-----
    python scripts/check-apply-patch-roundtrip.py

Exit status is 1 on any round-trip failure, so it can gate a commit. Reads
only; nothing under ~/.copilot/ is ever written to.
"""

from __future__ import annotations

import pathlib
import subprocess
import sys

REPO = pathlib.Path(__file__).resolve().parent.parent
TEST_PROJECT = REPO / "test" / "AecoPostMortem.Ingestion.Tests" / "AecoPostMortem.Ingestion.Tests.csproj"
FILTER = "FullyQualifiedName~ApplyPatchCorpusRoundTripTests"


def main() -> int:
    result = subprocess.run(
        ["dotnet", "test", str(TEST_PROJECT), "--filter", FILTER],
        cwd=REPO,
        check=False,
    )
    return result.returncode


if __name__ == "__main__":
    sys.exit(main())
