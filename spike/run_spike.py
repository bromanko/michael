#!/usr/bin/env python3
"""
Runner script for the conversational input parser spike.

Usage:
    python spike/run_spike.py --provider anthropic
    python spike/run_spike.py --provider openai
    python spike/run_spike.py --provider gemini
    python spike/run_spike.py --provider all

Requires the appropriate API key environment variables to be set.
"""

from __future__ import annotations

import argparse
import sys
import textwrap
from datetime import datetime

from parser import Provider, parse, ParseResult
from test_cases import TEST_CASES


# Fixed reference datetime for reproducibility
REFERENCE_DT = "2026-01-30T10:00:00-05:00"
REFERENCE_TZ = "America/New_York"


def format_result(result: ParseResult, indent: int = 4) -> str:
    """Format a ParseResult for human-readable display."""
    pad = " " * indent
    lines: list[str] = []

    if result.error:
        lines.append(f"{pad}ERROR: {result.error}")
        return "\n".join(lines)

    # Availability windows
    if result.availability_windows:
        lines.append(f"{pad}Availability windows:")
        for i, w in enumerate(result.availability_windows, 1):
            tz_note = f"  (tz: {w.timezone})" if w.timezone else ""
            lines.append(f"{pad}  {i}. {w.start}  ->  {w.end}{tz_note}")
    else:
        lines.append(f"{pad}Availability windows: (none extracted)")

    # Scalar fields
    for label, value in [
        ("Duration", f"{result.duration_minutes} min" if result.duration_minutes else None),
        ("Title", result.title),
        ("Description", result.description),
        ("Name", result.name),
        ("Email", result.email),
        ("Phone", result.phone),
    ]:
        if value:
            lines.append(f"{pad}{label}: {value}")

    # Missing fields
    if result.missing_fields:
        lines.append(f"{pad}Missing: {', '.join(result.missing_fields)}")
    else:
        lines.append(f"{pad}Missing: (nothing -- all required fields present)")

    return "\n".join(lines)


def divider(char: str = "-", width: int = 78) -> str:
    return char * width


def run_test_case(case: dict, providers: list[Provider]) -> dict[str, ParseResult]:
    """Run a single test case against the specified providers."""
    results: dict[str, ParseResult] = {}
    for provider in providers:
        results[provider.value] = parse(
            provider=provider,
            user_input=case["input"],
            reference_dt=REFERENCE_DT,
            reference_tz=REFERENCE_TZ,
        )
    return results


def print_case_results(case: dict, results: dict[str, ParseResult]) -> None:
    """Pretty-print a single test case and its results across providers."""
    print(divider("="))
    print(f"Test: {case['id']}")
    print(f"  {case['description']}")
    print()
    print(f"  Input:")
    for line in case["input"].splitlines():
        print(f"    {line}")
    print()
    print(f"  Expected notes:")
    for line in textwrap.wrap(case["notes"], width=70):
        print(f"    {line}")
    print()

    if len(results) == 1:
        provider_name, result = next(iter(results.items()))
        print(f"  Result ({provider_name}):")
        print(format_result(result))
    else:
        for provider_name, result in results.items():
            print(f"  Result ({provider_name}):")
            print(format_result(result))
            print()

    print()


def print_summary(
    all_results: list[tuple[dict, dict[str, ParseResult]]],
    providers: list[Provider],
) -> None:
    """Print a summary table comparing providers."""
    print(divider("="))
    print("SUMMARY")
    print(divider("="))
    print()

    header = f"{'Test Case':<30}"
    for p in providers:
        header += f" | {p.value:^14}"
    print(header)
    print(divider("-", len(header)))

    for case, results in all_results:
        row = f"{case['id']:<30}"
        for p in providers:
            r = results.get(p.value)
            if r is None:
                status = "skipped"
            elif r.error:
                status = "ERROR"
            else:
                n_windows = len(r.availability_windows)
                n_missing = len(r.missing_fields)
                status = f"{n_windows}w / {n_missing}m"
            row += f" | {status:^14}"
        print(row)

    print()
    print("Legend: Nw = N availability windows extracted, Nm = N required fields missing")
    print()


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Run the conversational input parser spike against AI providers."
    )
    parser.add_argument(
        "--provider",
        required=True,
        choices=["anthropic", "openai", "gemini", "all"],
        help="Which AI provider(s) to test.",
    )
    parser.add_argument(
        "--case",
        default=None,
        help="Run only the test case with this ID (default: run all).",
    )
    args = parser.parse_args()

    if args.provider == "all":
        providers = [Provider.ANTHROPIC, Provider.OPENAI, Provider.GEMINI]
    else:
        providers = [Provider(args.provider)]

    cases = TEST_CASES
    if args.case:
        cases = [c for c in cases if c["id"] == args.case]
        if not cases:
            print(f"No test case found with id '{args.case}'", file=sys.stderr)
            sys.exit(1)

    print()
    print(f"Michael — Conversational Input Parser Spike")
    print(f"Reference datetime: {REFERENCE_DT}")
    print(f"Reference timezone: {REFERENCE_TZ}")
    print(f"Providers: {', '.join(p.value for p in providers)}")
    print(f"Test cases: {len(cases)}")
    print()

    all_results: list[tuple[dict, dict[str, ParseResult]]] = []

    for i, case in enumerate(cases, 1):
        print(f"[{i}/{len(cases)}] Running: {case['id']} ...", flush=True)
        results = run_test_case(case, providers)
        all_results.append((case, results))
        print_case_results(case, results)

    if len(providers) > 1:
        print_summary(all_results, providers)


if __name__ == "__main__":
    main()
