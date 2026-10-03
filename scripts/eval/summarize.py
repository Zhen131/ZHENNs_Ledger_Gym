#!/usr/bin/env python3
"""Summarise an evaluation log as Markdown tables, one per segment and cost setting.

Usage, from the repository root:

    python scripts/eval/summarize.py evaluations/log.csv

Standard library only. Each row of the log becomes one table row: policy, total
return, max drawdown, Sharpe, trades, fees as a share of the starting equity and
exposure (share of steps holding coin). Random baselines are medians over their seeds.
Rows are grouped by segment, fee rate, fixed fee and slippage, so runs with
different costs never share a table; the heading names a fixed fee or slippage
only when it is not zero.
"""

from __future__ import annotations

import argparse
import csv
import sys
from collections import defaultdict
from pathlib import Path

POLICY_ORDER = {"buy_and_hold": 0, "cash": 1, "random": 2, "agent": 3}


def policy_label(row: dict) -> str:
    policy = row["policy"]
    if row["kind"] == "agent":
        return f"agent `{row['model_run_id'] or '?'}`"
    if policy == "random":
        return f"random (median of {row['seeds']})"
    return policy


def format_percent(text: str, digits: int = 2) -> str:
    return f"{float(text) * 100:.{digits}f} %" if text else ""


def format_number(text: str, digits: int = 2) -> str:
    if not text:
        return ""
    value = float(text)
    return f"{value:.0f}" if value.is_integer() else f"{value:.{digits}f}"


def group_by_costs(rows: list[dict]) -> dict[tuple, list[dict]]:
    """把行按（分段、费率、固定 fee、slippage）分组。"""
    groups: dict[tuple, list[dict]] = defaultdict(list)
    for row in rows:
        key = (row["segment"], float(row["fee_rate"]), float(row["fixed_fee"] or 0), float(row["slippage"] or 0))
        groups[key].append(row)
    return groups


def cost_text(fee: float, fixed_fee: float, slippage: float) -> str:
    """费率；固定 fee 和 slippage 不为零时也写上。"""
    costs = f"fee {fee * 100:g} %"
    if fixed_fee:
        costs += f", fixed fee {fixed_fee:g} USDT"
    if slippage:
        costs += f", slippage {slippage * 10_000:g} bp"
    return costs


def table_lines(items: list[dict]) -> list[str]:
    """一组的 Markdown 表格，policy 按固定顺序排列；会就地排序 items。"""
    lines = [
        "| Policy | Return | Max drawdown | Sharpe | Trades | Fees % of start | Exposure | Logged (UTC) |",
        "| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |",
    ]
    items.sort(key=lambda row: (POLICY_ORDER.get(row["policy"], 9), row["timestamp_utc"]))
    for row in items:
        lines.append(
            f"| {policy_label(row)} | {format_percent(row['total_return'])} | {format_percent(row['max_drawdown'])} | "
            f"{format_number(row['sharpe'])} | {format_number(row['trades'], 1)} | {float(row['fees_pct']):.3f} % | "
            f"{format_percent(row['exposure'], 1)} | {row['timestamp_utc'][:16].replace('T', ' ')} |"
        )
        if row["policy"] == "random" and row["notes"]:
            lines.append(f"|  ↳ {row['notes']} | | | | | | | |")
    return lines


def summarize(path: Path) -> str:
    with path.open(newline="", encoding="utf-8") as handle:
        rows = list(csv.DictReader(handle))
    if not rows:
        return f"{path}: no rows\n"

    out = []
    for (segment, fee, fixed_fee, slippage), items in sorted(group_by_costs(rows).items()):
        first = items[0]
        costs = cost_text(fee, fixed_fee, slippage)
        out.append(f"### {segment} ({first['segment_start']} to {first['segment_end']}), {costs}\n")
        out.extend(table_lines(items))
        out.append("")
    return "\n".join(out)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("log", nargs="?", default="evaluations/log.csv", help="path to log.csv")
    args = parser.parse_args(argv)
    path = Path(args.log)
    if not path.is_file():
        print(f"error: {path} not found", file=sys.stderr)
        return 1
    print(summarize(path))
    return 0


if __name__ == "__main__":
    sys.exit(main())
