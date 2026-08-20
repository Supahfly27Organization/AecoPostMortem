#!/usr/bin/env python3
"""
check-corpus-verification.py — S-45's Phase A exit-criterion gate (PRD §3.5,
FR-55, issue #9): "every session reconstructs; a re-run adds no duplicate
events; RAW replays byte-identically; and the event census reproduces the
frozen fixture corpus's post-exclusion census."

Why this exists
----------------
Phase A's exit criterion cannot be stated against live `~/.copilot/`: the
window rotates (a measured 111 days, and one session directory has already
gone since the 2026-08-16 freeze) and FR-7 removes sessions from the census
by design, so a criterion pinned to live counts is either unachievable or
vacuous. FR-55's frozen manifest (`fixtures/corpus-manifest.json`) is what
every scenario below is measured against instead.

The check itself lives in C#, next to the ingestion path it proves:
test/AecoPostMortem.Ingestion.Tests/CorpusVerificationTests.cs drives a real
full ingest and a real incremental re-ingest of the live reference corpus
through AecoPostMortem.Ingestion.SessionDiscovery/SessionIngestor, and
checks four things against the frozen manifest and PRD §3.7:

  1. The RAW event census matches the manifest's per-type counts exactly.
  2. Every RAW row re-serialises byte-identically to its own source line.
  3. A full ingest from an empty store finishes inside PRD §3.7's 3-minute
     target.
  4. An incremental re-ingest with no new events finishes inside PRD §3.7's
     15-second target.

This script is the CI entry point: it runs those tests in isolation and
forwards their exit code — the same shape as freeze-corpus-manifest.py's own
--check gate and check-apply-patch-roundtrip.py.

The corpus bytes are not checked in (fixtures/README.md) — only their
hashes and post-exclusion census are. The tests read the live source
directory recorded in fixtures/corpus-manifest.json (or
AECOPOSTMORTEM_CORPUS_SOURCE) and skip, rather than fail, on a machine that
does not have it — so this script's exit code is 0 whether the tests passed
or were skipped, and 1 only on an actual verification failure. The time
targets are targets, not measurements (PRD §3.7): a miss fails the test
loudly, with the measured elapsed time in the assertion message, rather than
being silently absorbed.

Usage
-----
    python scripts/check-corpus-verification.py

Exit status is 1 on any verification failure, so it can gate a commit.
Reads only; nothing under ~/.copilot/ is ever written to.
"""

from __future__ import annotations

import pathlib
import subprocess
import sys

REPO = pathlib.Path(__file__).resolve().parent.parent
TEST_PROJECT = REPO / "test" / "AecoPostMortem.Ingestion.Tests" / "AecoPostMortem.Ingestion.Tests.csproj"
FILTER = "FullyQualifiedName~CorpusVerificationTests"


def main() -> int:
    result = subprocess.run(
        ["dotnet", "test", str(TEST_PROJECT), "--filter", FILTER],
        cwd=REPO,
        check=False,
    )
    return result.returncode


if __name__ == "__main__":
    sys.exit(main())
