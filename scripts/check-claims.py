#!/usr/bin/env python3
"""
check-claims.py — flag quantitative claims in spec documents that carry no source.

Why this exists
---------------
Across four review rounds of the AecoLedger Insights PRD, the single recurring
defect was a number that fit the sentence but came from nowhere: a corpus size
misread from a research paper, an invented per-result token cost, an explanation
for a discrepancy that the cited source contradicted. Every one of them read as
plausible, because plausibility is what produced them.

Intent does not catch this — it failed four times. A mechanical check does.
The rule the product applies to its own findings ("every fact carries its
provenance") applied to the documents that specify it.

What counts as sourced
----------------------
A flagged number is considered sourced if its line carries any evidence marker:
a section cross-reference, a named source document, an explicit measurement,
an explicit seed/estimate/target label, or a URL. See MARKERS below.

Usage
-----
    python scripts/check-claims.py docs/product-superpowers/prds/*.md
    python scripts/check-claims.py docs/            # walks *.md
    python scripts/check-claims.py --strict docs/   # also flag single digits
    python scripts/check-claims.py --list-markers

Exit status is 1 when unsourced claims are found, so it can gate a commit.
"""

from __future__ import annotations

import argparse
import pathlib
import re
import sys

# A line containing any of these is treated as carrying its source.
MARKERS = (
    "§",                 # cross-reference to a section, here or in a cited doc
    "discovery",         # the evidence base
    "research",          # the UX research reference
    "measured",
    "measurement",
    "seed",              # explicitly labelled starting value, not a finding
    "estimate",
    "estimated",
    "illustrative",      # explicitly labelled as not real
    "target",            # a goal we chose, not a fact we found
    "budget",            # ditto
    "http://",
    "https://",
    "per FR-",           # derived from a stated requirement
    "<!--src:",          # explicit escape: state the reason inline, e.g. <!--src: chosen, not derived-->
)

# Numbers that are structure, not claims.
IGNORE = (
    re.compile(r"FR-\d+"),                     # requirement ids
    re.compile(r"\bS-\d+"),                    # story ids
    re.compile(r"\bE\d+\b"),                   # epic ids
    re.compile(r"\bwave \d+", re.I),           # wave numbers in a schedule
    re.compile(r"§[\d.]+"),                    # section refs
    re.compile(r"\b\d{4}-\d{2}-\d{2}\b"),      # ISO dates
    re.compile(r"\bnet\d+\.\d+\b"),            # target frameworks
    re.compile(r"\bv\d+(\.\d+)*\b"),           # version strings, v-prefixed
    re.compile(r"\b\d+\.\d+\.\d+\b"),          # version strings, bare semver (React 19.2.8)
    re.compile(r"\.NET \d+(\.\d+)*"),          # platform versions (.NET 10)
    re.compile(r"\b(?:SHA|ISO|UTF|RFC)-?\d+\b"),             # standard/algorithm names
    re.compile(r"\b(?:gpt|claude|gemini|llama|mistral)[-\w.]*[0-9][\w.-]*", re.I),  # model names
    re.compile(r"\b\d+(?:\.\d+)?(?:px|em|rem|vh|vw|ch)\b"),  # CSS lengths are code, not findings
    re.compile(r"\blocalhost:\d+|\bport \d+\b"),             # port numbers
    re.compile(r"\b(?:200|201|204|301|302|400|401|403|404|409|422|500|503)\b"),  # HTTP status codes
    re.compile(r"\bPart \d+\b"),               # our own part numbering
    re.compile(r"\bRule \d+\b"),
    re.compile(r"\bPhase [A-C]\b"),
    re.compile(r"\bQ\d\b"),                    # open-question refs
)

# Two or more digits, optional thousands separators / decimal, optional %.
# Single digits are usually prose ("three phases") and are noise; --strict opts in.
NUMBER = re.compile(r"\b\d{1,3}(?:,\d{3})+\b|\b\d+\.\d+\b|\b\d{2,}\b|\b\d+%")
NUMBER_STRICT = re.compile(r"\b\d{1,3}(?:,\d{3})+\b|\b\d+\.\d+\b|\b\d+\b|\b\d+%")


# A table whose header declares its values are chosen rather than found —
# targets, seeds, firing thresholds, exit criteria — is self-labelling.
HEADER_HINTS = ("target", "basis", "seed", "fires when", "criterion", "status", "verdict",
                "result", "method", "constant", "measured")

HEADING = re.compile(r"^\s*#{1,6}\s+[\d.]+\s")


def strip_ignorable(line: str) -> str:
    """Blank out structural numbers so they are not mistaken for claims."""
    # A heading's own section number is structure, not a claim.
    if HEADING.match(line):
        line = re.sub(r"^(\s*#{1,6}\s+)[\d.]+", lambda m: m.group(1), line)
    for pat in IGNORE:
        line = pat.sub(lambda m: "_" * len(m.group(0)), line)
    return line


# A cross-reference to another requirement is a source: the number is defined there.
FR_REF = re.compile(r"FR-\d+")


def is_sourced(line: str) -> bool:
    low = line.lower()
    if any(m.lower() in low for m in MARKERS):
        return True
    # "(FR-29)", "per FR-51", "FR-18's" — the value is defined elsewhere and traceable.
    return bool(FR_REF.search(line))


def scan(path: pathlib.Path, strict: bool = False) -> list[tuple[int, str, str]]:
    """Return (line_no, numbers, line_text) for lines with unsourced numbers."""
    findings: list[tuple[int, str, str]] = []
    in_fence = False
    self_labelling_table = False
    prev_line = ""
    pattern = NUMBER_STRICT if strict else NUMBER

    for n, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        stripped = raw.strip()

        if stripped.startswith("```"):
            in_fence = not in_fence
            continue
        if in_fence:
            continue

        # A separator row means the line above it was a header: decide whether
        # this table declares its own values as chosen rather than measured.
        if re.fullmatch(r"\|?[\s:|-]+\|?", stripped) and "|" in stripped:
            self_labelling_table = any(h in prev_line.lower() for h in HEADER_HINTS)
            prev_line = raw
            continue
        if stripped in {"---", "***"} or re.fullmatch(r"\|?[\s:|-]+\|?", stripped):
            prev_line = raw
            continue
        if not stripped.startswith("|"):
            self_labelling_table = False
        prev_line = raw
        if self_labelling_table and stripped.startswith("|"):
            continue
        # ordered-list markers are structure
        body = re.sub(r"^\s*\d+\.\s", "", raw)

        hits = pattern.findall(strip_ignorable(body))
        if hits and not is_sourced(raw):
            findings.append((n, ", ".join(dict.fromkeys(hits)), stripped))

    return findings


def collect(paths: list[str]) -> list[pathlib.Path]:
    out: list[pathlib.Path] = []
    for p in paths:
        path = pathlib.Path(p)
        if path.is_dir():
            out.extend(
                f for f in sorted(path.rglob("*.md"))
                if not {"node_modules", "bin", "obj", "dist"} & set(f.parts)
            )
        elif path.is_file():
            out.append(path)
    return out


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("paths", nargs="*", help="markdown files or directories")
    ap.add_argument("--strict", action="store_true", help="also flag single-digit numbers")
    ap.add_argument("--quiet", action="store_true", help="print only the summary")
    ap.add_argument("--list-markers", action="store_true", help="print what counts as a source and exit")
    args = ap.parse_args()

    if args.list_markers:
        print("A line is treated as sourced if it contains any of:")
        for m in MARKERS:
            print(f"  {m}")
        return 0

    if not args.paths:
        ap.print_help()
        return 2

    files = collect(args.paths)
    if not files:
        print("no markdown files found", file=sys.stderr)
        return 2

    total = 0
    for path in files:
        findings = scan(path, strict=args.strict)
        if not findings:
            continue
        total += len(findings)
        if args.quiet:
            continue
        print(f"\n{path}")
        for line_no, nums, text in findings:
            excerpt = text if len(text) <= 110 else text[:107] + "..."
            print(f"  {line_no:>5}  [{nums}]  {excerpt}")

    print()
    if total:
        print(f"{total} unsourced quantitative claim(s) across {len(files)} file(s).")
        print("Cite the source, label it a seed/estimate/target, or delete the number.")
        return 1

    print(f"No unsourced quantitative claims in {len(files)} file(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
