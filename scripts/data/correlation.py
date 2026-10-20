#!/usr/bin/env python3
"""Pearson correlation of daily and hourly log returns between several hourly CSVs.

Usage, from the repository root:

    python scripts/data/correlation.py \
        unity/Assets/StreamingAssets/Gym/data/BTCUSDT-1h.csv \
        unity/Assets/StreamingAssets/Gym/data/ETHUSDT-1h.csv \
        unity/Assets/StreamingAssets/Gym/data/BNBUSDT-1h.csv [--end 2021-12-31]

Standard library only. Input files are the CSVs written by fetch_binance_klines.py
(open_time_ms,open,high,low,close,volume; one row per hour, gaps already filled).
The symbol of a file is its name up to "-1h" (BTCUSDT-1h.csv -> BTCUSDT).

Window: every file must have data in it. It starts at the first candle of the
symbol that was listed last and ends with the last candle of --end (default
2021-12-31, the end of the training segment), so no validation or test data is
used.

Daily returns: the close of the candle that opens at 00:00 UTC on each day of the
window, r = ln(close[d] / close[d-1]). Hourly returns: every candle of the window,
r = ln(close[h] / close[h-1]). Hours that the fetcher filled with a flat candle
give a return of 0. Output: two Markdown tables (daily, hourly), each with the
window and the number of returns.
"""

from __future__ import annotations

import argparse
import datetime as dt
import math
import sys
from pathlib import Path

HOUR_MS = 3_600_000
DAY_MS = 24 * HOUR_MS


def symbol_of(path: Path) -> str:
    """文件名里 "-1h" 之前的部分就是币名。"""
    name = path.name
    return name.split("-1h", 1)[0] if "-1h" in name else path.stem


def read_closes(path: Path) -> dict[int, float]:
    """open_time_ms -> close。只读表头和前五列里的 close。"""
    closes: dict[int, float] = {}
    with path.open(encoding="ascii") as handle:
        header = handle.readline().strip().split(",")
        if header[:5] != ["open_time_ms", "open", "high", "low", "close"]:
            raise SystemExit(f"error: {path} does not look like a fetch_binance_klines.py CSV (header {header})")
        for line_no, line in enumerate(handle, start=2):
            cells = line.strip().split(",")
            if len(cells) < 5:
                raise SystemExit(f"error: {path} line {line_no}: expected at least 5 columns")
            closes[int(cells[0])] = float(cells[4])
    if not closes:
        raise SystemExit(f"error: {path} has no rows")
    return closes


def parse_day(text: str) -> int:
    """yyyy-MM-dd -> 那天 00:00 UTC 的毫秒数。"""
    day = dt.datetime.strptime(text, "%Y-%m-%d").replace(tzinfo=dt.timezone.utc)
    return int(day.timestamp() * 1000)


def utc(ms: int) -> str:
    return dt.datetime.fromtimestamp(ms / 1000, tz=dt.timezone.utc).strftime("%Y-%m-%d %H:%M")


def log_returns(closes: dict[int, float], times: list[int]) -> list[float]:
    """按给定的时间点取 close，算相邻两点的 ln 收益率；缺任何一个点就停下报错。"""
    values = []
    for t in times:
        if t not in closes:
            raise SystemExit(f"error: no candle at {utc(t)} UTC")
        values.append(closes[t])
    return [math.log(b / a) for a, b in zip(values, values[1:])]


def pearson(x: list[float], y: list[float]) -> float:
    n = len(x)
    mean_x, mean_y = sum(x) / n, sum(y) / n
    cov = sum((a - mean_x) * (b - mean_y) for a, b in zip(x, y))
    var_x = sum((a - mean_x) ** 2 for a in x)
    var_y = sum((b - mean_y) ** 2 for b in y)
    if var_x == 0 or var_y == 0:
        return float("nan")
    return cov / math.sqrt(var_x * var_y)


def matrix_table(title: str, symbols: list[str], returns: dict[str, list[float]], window: str) -> list[str]:
    """一张 Markdown 表：行列都是币，格子是皮尔逊相关系数，保留 4 位小数。"""
    count = len(next(iter(returns.values())))
    lines = [f"### {title}", "", f"{window}; {count} returns per symbol.", ""]
    lines.append("| | " + " | ".join(symbols) + " |")
    lines.append("| --- | " + " | ".join("---:" for _ in symbols) + " |")
    for row in symbols:
        cells = [f"{pearson(returns[row], returns[col]):.4f}" for col in symbols]
        lines.append(f"| {row} | " + " | ".join(cells) + " |")
    lines.append("")
    return lines


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("csv", nargs="+", help="hourly CSVs (two or more)")
    parser.add_argument("--end", default="2021-12-31",
                        help="last day of the window, yyyy-MM-dd (default: 2021-12-31, the end of the training segment)")
    args = parser.parse_args(argv)
    paths = [Path(p) for p in args.csv]
    if len(paths) < 2:
        parser.error("give at least two CSVs")
    symbols = [symbol_of(p) for p in paths]
    if len(set(symbols)) != len(symbols):
        parser.error(f"each symbol may be given once (got {symbols})")

    closes = {s: read_closes(p) for s, p in zip(symbols, paths)}
    # 窗口：所有币都有数据的那段。起点 = 最晚上市那只的第一根，终点 = --end 那天 23:00 的那根。
    start = max(min(c) for c in closes.values())
    end = parse_day(args.end) + DAY_MS - HOUR_MS
    end = min([end] + [max(c) for c in closes.values()])
    if start >= end:
        raise SystemExit(f"error: the files have no common hours up to {args.end}")
    latest = max(symbols, key=lambda s: min(closes[s]))

    hours = list(range(start, end + 1, HOUR_MS))
    first_midnight = start if start % DAY_MS == 0 else start - start % DAY_MS + DAY_MS
    days = list(range(first_midnight, end + 1, DAY_MS))

    daily = {s: log_returns(closes[s], days) for s in symbols}
    hourly = {s: log_returns(closes[s], hours) for s in symbols}

    out = [
        f"Window starts at the first candle of {latest} ({utc(start)} UTC) and ends {utc(end)} UTC.",
        "",
    ]
    out += matrix_table("Daily log returns (close of the 00:00 UTC candle)", symbols, daily,
                        f"Days {utc(days[0])[:10]} .. {utc(days[-1])[:10]}")
    out += matrix_table("Hourly log returns", symbols, hourly, f"Hours {utc(hours[0])} .. {utc(hours[-1])} UTC")
    print("\n".join(out))
    return 0


if __name__ == "__main__":
    sys.exit(main())
