#!/usr/bin/env python3
"""
freeze-corpus-manifest.py — freeze the reference corpus as hashes plus a
post-exclusion census, and later verify disk against that manifest.

Why this exists
---------------
FR-55 requires every Phase A exit criterion to be stated against a frozen
fixture, not against live `~/.copilot/`. Live counts cannot serve as a gate for
two reasons the PRD gives: the window rotates (measured 111 days, and one
session directory has already gone since the 2026-08-16 measurement), and FR-7
removes sessions from the census by design. A criterion pinned to live counts is
either unachievable or vacuous, and nobody can tell which.

The session bytes are deliberately **not** checked in. They hold the operator's
source code, prompts and possibly secrets, and 176.7 MB of them would break the
repository's liftability. What is checked in is this manifest: per session, the
file's content hash, its size, and its post-exclusion event census. A mismatch
is then detectable without the bytes ever being in the repository.

The census is **post-exclusion** (FR-7): sessions whose `session.start` cwd falls
under an exclusion path are recorded with their reason and contribute nothing to
the totals. Whether any session in the reference corpus is itself an analysis
session was never measured before this script ran — the `excluded` count in the
manifest is that measurement.

Usage
-----
    python scripts/freeze-corpus-manifest.py                 # write the manifest
    python scripts/freeze-corpus-manifest.py --check         # verify disk against it
    python scripts/freeze-corpus-manifest.py --exclude F:/git/Other
    python scripts/freeze-corpus-manifest.py --source /path/to/session-state

Exit status is 1 when `--check` finds a mismatch, so it can gate a commit.
Reads only; the source directory is never written to.
"""

from __future__ import annotations

import argparse
import collections
import datetime
import hashlib
import json
import pathlib
import sys

REPO = pathlib.Path(__file__).resolve().parent.parent
DEFAULT_SOURCE = pathlib.Path.home() / ".copilot" / "session-state"
DEFAULT_OUT = REPO / "fixtures" / "corpus-manifest.json"
SCHEMA = 1


def normalise(path: str) -> str:
    """Case-folded, forward-slashed, trailing-slash-free — for prefix comparison."""
    return path.replace("\\", "/").rstrip("/").casefold()


def is_excluded(cwd: str | None, exclusions: list[str]) -> str | None:
    """Return the exclusion path that captures this cwd, or None."""
    if not cwd:
        return None
    target = normalise(cwd)
    for raw in exclusions:
        root = normalise(raw)
        if target == root or target.startswith(root + "/"):
            return raw
    return None


def read_session(path: pathlib.Path) -> dict:
    """
    Hash and census one events.jsonl.

    Ingestion stops at the last newline-terminated line and records that
    high-water offset (FR-6): the file is live-written, so a trailing partial
    line is unfinished, not malformed. The hash covers the whole file as it sits
    on disk, so a grown file is detectable as a change rather than silently
    matching its own prefix.
    """
    raw = path.read_bytes()
    digest = hashlib.sha256(raw).hexdigest()

    high_water = raw.rfind(b"\n") + 1  # 0 when the file holds no complete line
    body = raw[:high_water]

    events: collections.Counter[str] = collections.Counter()
    lines = malformed = 0
    first_ts = last_ts = None
    copilot_version = schema_version = cwd = repository = session_id = None

    for line in body.splitlines():
        if not line.strip():
            continue
        lines += 1
        try:
            event = json.loads(line)
        except (json.JSONDecodeError, UnicodeDecodeError):
            malformed += 1
            continue

        events[event.get("type", "<none>")] += 1

        stamp = event.get("timestamp")
        if stamp:
            first_ts = stamp if first_ts is None else min(first_ts, stamp)
            last_ts = stamp if last_ts is None else max(last_ts, stamp)

        if event.get("type") == "session.start":
            data = event.get("data") or {}
            session_id = data.get("sessionId")
            copilot_version = data.get("copilotVersion")
            schema_version = data.get("version")
            context = data.get("context") or {}
            cwd = context.get("cwd")
            repository = context.get("repository")

    return {
        "session_id": session_id or path.parent.name,
        "directory": path.parent.name,
        "sha256": digest,
        "bytes": len(raw),
        "high_water_offset": high_water,
        "lines": lines,
        "malformed_lines": malformed,
        "copilot_version": copilot_version,
        "event_schema_version": schema_version,
        "first_timestamp": first_ts,
        "last_timestamp": last_ts,
        "cwd": cwd,
        "repository": repository,
        "events": dict(sorted(events.items())),
    }


def build(source: pathlib.Path, exclusions: list[str], frozen_at: str) -> dict:
    if not source.is_dir():
        raise SystemExit(f"no session-state directory at {source}")

    dirs = sorted(p for p in source.iterdir() if p.is_dir())
    sessions = []
    for directory in dirs:
        events_file = directory / "events.jsonl"
        if not events_file.is_file():
            continue
        record = read_session(events_file)
        reason = is_excluded(record["cwd"], exclusions)
        record["excluded"] = reason is not None
        record["exclusion_reason"] = (
            f"cwd falls under the exclusion path {reason}" if reason else None
        )
        sessions.append(record)

    sessions.sort(key=lambda s: s["directory"])
    included = [s for s in sessions if not s["excluded"]]

    census: collections.Counter[str] = collections.Counter()
    carrying: collections.Counter[str] = collections.Counter()
    for session in included:
        for kind, count in session["events"].items():
            census[kind] += count
            carrying[kind] += 1

    stamps = [s["last_timestamp"] for s in included if s["last_timestamp"]]
    starts = [s["first_timestamp"] for s in included if s["first_timestamp"]]

    return {
        "schema": SCHEMA,
        "frozen_at": frozen_at,
        "source": source.as_posix(),
        "exclusions": exclusions,
        "totals": {
            "session_directories": len(dirs),
            "directories_without_events": len(dirs) - len(sessions),
            "sessions_found": len(sessions),
            "sessions_excluded": len(sessions) - len(included),
            "sessions_included": len(included),
            "bytes": sum(s["bytes"] for s in included),
            "lines": sum(s["lines"] for s in included),
            "malformed_lines": sum(s["malformed_lines"] for s in included),
            "earliest_event": min(starts) if starts else None,
            "latest_event": max(stamps) if stamps else None,
            "copilot_versions": sorted(
                {s["copilot_version"] for s in included if s["copilot_version"]}
            ),
            "repositories": sorted(
                {s["repository"] for s in included if s["repository"]}
            ),
            "event_types": len(census),
            "event_census": dict(sorted(census.items(), key=lambda kv: (-kv[1], kv[0]))),
            "sessions_carrying_event_type": dict(
                sorted(carrying.items(), key=lambda kv: (-kv[1], kv[0]))
            ),
        },
        "sessions": sessions,
    }


def comparable(manifest: dict) -> dict:
    """Everything a check compares — the freeze date is metadata, not evidence."""
    return {"totals": manifest["totals"], "sessions": manifest["sessions"]}


def check(manifest: dict, source: pathlib.Path) -> int:
    current = build(source, manifest["exclusions"], manifest["frozen_at"])
    frozen_by_dir = {s["directory"]: s for s in manifest["sessions"]}
    live_by_dir = {s["directory"]: s for s in current["sessions"]}

    problems: list[str] = []
    for directory, frozen in frozen_by_dir.items():
        live = live_by_dir.get(directory)
        if live is None:
            problems.append(f"{directory}: gone from disk — the window rotated")
            continue
        if live["sha256"] != frozen["sha256"]:
            problems.append(
                f"{directory}: content changed "
                f"({frozen['bytes']:,} -> {live['bytes']:,} bytes)"
            )
        elif live["events"] != frozen["events"]:
            problems.append(f"{directory}: same bytes, different census — parser drift")

    for directory in sorted(set(live_by_dir) - set(frozen_by_dir)):
        problems.append(f"{directory}: new session on disk, not in the manifest")

    if not problems:
        totals = manifest["totals"]
        print(
            f"Corpus matches the manifest frozen {manifest['frozen_at']}: "
            f"{totals['sessions_included']} sessions, "
            f"{totals['lines']:,} lines, {totals['bytes']:,} bytes."
        )
        return 0

    print(f"{len(problems)} difference(s) against the manifest "
          f"frozen {manifest['frozen_at']}:")
    for problem in problems:
        print(f"  {problem}")
    print("\nA rotated-away session is expected and is why the manifest exists; "
          "re-freeze deliberately, never silently.")
    return 1


def main() -> int:
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    ap.add_argument("--source", type=pathlib.Path, default=DEFAULT_SOURCE,
                    help="Copilot session-state directory")
    ap.add_argument("--out", type=pathlib.Path, default=DEFAULT_OUT,
                    help="manifest path")
    ap.add_argument("--exclude", action="append", default=None,
                    help="exclude sessions whose cwd falls under this path "
                         "(repeatable; defaults to this repository root)")
    ap.add_argument("--date", default=None,
                    help="freeze date to record (default: today)")
    ap.add_argument("--check", action="store_true",
                    help="verify disk against the existing manifest and exit")
    args = ap.parse_args()

    exclusions = args.exclude if args.exclude is not None else [REPO.as_posix()]

    if args.check:
        if not args.out.is_file():
            print(f"no manifest at {args.out} — run without --check first",
                  file=sys.stderr)
            return 2
        return check(json.loads(args.out.read_text(encoding="utf-8")), args.source)

    frozen_at = args.date or datetime.date.today().isoformat()
    manifest = build(args.source, exclusions, frozen_at)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )

    totals = manifest["totals"]
    print(f"Froze {totals['sessions_included']} sessions to {args.out}")
    print(f"  {totals['session_directories']} directories, "
          f"{totals['directories_without_events']} without events.jsonl")
    print(f"  {totals['sessions_excluded']} excluded by cwd "
          f"({', '.join(exclusions)})")
    print(f"  {totals['lines']:,} lines, {totals['malformed_lines']:,} malformed, "
          f"{totals['bytes']:,} bytes")
    print(f"  {totals['event_types']} event types, "
          f"{totals['earliest_event']} -> {totals['latest_event']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
