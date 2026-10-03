#!/usr/bin/env python3
"""Download Binance Vision spot klines and build a gap-filled hourly CSV.

Standard library only. Typical use, from the repository root:

    python scripts/data/fetch_binance_klines.py --symbol BTCUSDT
    python scripts/data/fetch_binance_klines.py --self-test

With no other options the first command rebuilds exactly the committed
BTCUSDT-1h.csv: it stops at 2026-08 (the end of the test segment) and drops
off-hour candles. Pass --end YYYY-MM to add newer months.

What it does:

1. Downloads the monthly archives
   https://data.binance.vision/data/spot/monthly/klines/{S}/{I}/{S}-{I}-{YYYY-MM}.zip
   together with their .CHECKSUM files, and verifies every archive's SHA-256.
   Archives that are already on disk and still match their checksum are reused.
2. Keeps the first six columns (open_time, open, high, low, close, volume).
   Timestamps from 2025-01-01 on are in microseconds; they are converted to
   milliseconds so the whole file uses one unit.
3. Checks that open times are strictly ascending, unique and on the hour.
   Real archives contain a few candles that are not on the hour (BTCUSDT
   2018-02-09 .. 2018-02-11, after an exchange outage the candles started at
   hh:28:14). --off-hour decides what happens to them: "drop" (default)
   discards them so the hours are forward-filled like any other gap, "error"
   stops, "floor" moves them back to the start of their hour.
4. Fills every missing hour with a flat candle at the previous close
   (open = high = low = close = previous close, volume = 0). Only earlier
   values are ever used, so the filler never looks into the future.
5. Writes {out}/{S}-1h.csv and {out}/{S}-1h.manifest.json.
6. Warns loudly when the result has off-hour candles or gaps over 24 hours
   that the committed data does not have (BTCUSDT 1h: 43 off-hour candles,
   one 75-hour gap from 2018-02-08 01:00), so a new stretch of flat filler
   cannot slip in unnoticed when --end adds months.

Numbers are copied as the original strings; nothing is passed through float.
On another machine the manifest differs only in downloaded_at_utc (the time
the archives were saved there); that change need not be committed.
"""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import io
import json
import os
import sys
import time
import urllib.error
import urllib.request
import zipfile
from pathlib import Path

BASE_URL = "https://data.binance.vision/data/spot/monthly/klines"
FIRST_MONTH = (2017, 8)
HOUR_MS = 3_600_000
MICROSECOND_THRESHOLD = 10**15
CSV_HEADER = "open_time_ms,open,high,low,close,volume"
TERMS = "Binance Vision Terms and Conditions v1.0 (2026-08-26)"
LICENSE = "CC BY-NC-SA 4.0"
SUPPORTED_INTERVALS = ("1h",)
OFF_HOUR_POLICIES = ("error", "drop", "floor")
DEFAULT_OFF_HOUR = "drop"  # 提交的数据丢掉了 2018-02 那 43 根不在整点的 candle
# 默认取到的最后一个月：测试段的结尾（2026-08-31），这样默认命令每次都重建出同一个文件。
# 要加更新的月份，传 --end。
DEFAULT_END_MONTH = (2026, 8)
# 已知提交的数据里有什么。重建之后出现别的情况就打印警告：被丢掉的 candle 会变成平的填充，
# 不警告的话，它们只会在 manifest 里显示成一个计数。
KNOWN_OFF_HOUR_ROWS = {("BTCUSDT", "1h"): 43}
KNOWN_LONG_GAPS = {("BTCUSDT", "1h"): {"2018-02-08T01:00:00Z"}}

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_RAW_DIR = REPO_ROOT / "data" / "raw"
DEFAULT_OUT_DIR = REPO_ROOT / "unity" / "Assets" / "StreamingAssets" / "Gym" / "data"


class DataError(Exception):
    """任何让输出不可信的问题。脚本会停下。"""


# ---------------------------------------------------------------- 月份


def parse_month(text: str) -> tuple[int, int]:
    try:
        year_s, month_s = text.split("-")
        year, month = int(year_s), int(month_s)
    except ValueError:
        raise argparse.ArgumentTypeError(f"expected YYYY-MM, got {text!r}")
    if not 1 <= month <= 12 or len(year_s) != 4 or len(month_s) != 2:
        raise argparse.ArgumentTypeError(f"expected YYYY-MM, got {text!r}")
    return year, month


def format_month(year_month: tuple[int, int]) -> str:
    return f"{year_month[0]:04d}-{year_month[1]:02d}"


def next_month(year_month: tuple[int, int]) -> tuple[int, int]:
    year, month = year_month
    return (year + 1, 1) if month == 12 else (year, month + 1)


def months_between(start: tuple[int, int], end: tuple[int, int]):
    year_month = start
    while year_month <= end:
        yield year_month
        year_month = next_month(year_month)


def archive_name(symbol: str, interval: str, year_month: tuple[int, int]) -> str:
    return f"{symbol}-{interval}-{format_month(year_month)}.zip"


def archive_url(symbol: str, interval: str, year_month: tuple[int, int]) -> str:
    return f"{BASE_URL}/{symbol}/{interval}/{archive_name(symbol, interval, year_month)}"


# ---------------------------------------------------------------- 下载


def http_get(url: str, attempts: int = 3) -> bytes | None:
    """返回响应体；404 时返回 None。其他失败会重试，最后抛出。"""
    request = urllib.request.Request(url, headers={"User-Agent": "ZHENN_Ledger_Gym-data/1.0"})
    for attempt in range(1, attempts + 1):
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                return response.read()
        except urllib.error.HTTPError as error:
            if error.code == 404:
                return None
            if attempt == attempts:
                raise DataError(f"HTTP {error.code} for {url}")
        except (urllib.error.URLError, TimeoutError, ConnectionError) as error:
            if attempt == attempts:
                raise DataError(f"network error for {url}: {error}")
        time.sleep(2 * attempt)
    raise AssertionError("unreachable")


def parse_checksum(text: str, expected_name: str) -> str:
    parts = text.split()
    if len(parts) != 2 or len(parts[0]) != 64:
        raise DataError(f"malformed checksum file for {expected_name}: {text!r}")
    digest, name = parts[0].lower(), parts[1]
    if name != expected_name:
        raise DataError(f"checksum file names {name!r}, expected {expected_name!r}")
    return digest


def sha256_hex(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def fetch_month(symbol: str, interval: str, year_month: tuple[int, int], raw_dir: Path):
    """返回（zip 字节，sha256）；Binance Vision 没有这个月时返回 None。"""
    name = archive_name(symbol, interval, year_month)
    zip_path = raw_dir / name
    checksum_path = raw_dir / (name + ".CHECKSUM")

    if zip_path.exists() and checksum_path.exists():
        expected = parse_checksum(checksum_path.read_text(encoding="ascii"), name)
        data = zip_path.read_bytes()
        if sha256_hex(data) == expected:
            return data, expected
        print(f"  {name}: cached copy fails its checksum, downloading again", file=sys.stderr)

    url = archive_url(symbol, interval, year_month)
    checksum_body = http_get(url + ".CHECKSUM")
    if checksum_body is None:
        return None
    checksum_text = checksum_body.decode("ascii")
    expected = parse_checksum(checksum_text, name)
    data = http_get(url)
    if data is None:
        raise DataError(f"{name}: checksum exists but the archive returned 404")
    actual = sha256_hex(data)
    if actual != expected:
        raise DataError(f"{name}: SHA-256 mismatch (expected {expected}, got {actual})")

    raw_dir.mkdir(parents=True, exist_ok=True)
    for path, payload in ((zip_path, data), (checksum_path, checksum_body)):
        tmp = path.with_name(path.name + ".part")
        tmp.write_bytes(payload)
        os.replace(tmp, path)
    return data, actual


# ---------------------------------------------------------------- 解析 / 检查 / 补齐


def parse_archive(data: bytes, label: str) -> list[tuple]:
    """若干行 (open_time_ms:int, open, high, low, close, volume)，其余各列是字符串。"""
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        members = [n for n in archive.namelist() if n.endswith(".csv")]
        if len(members) != 1:
            raise DataError(f"{label}: expected one CSV inside the archive, found {members}")
        text = archive.read(members[0]).decode("utf-8")

    rows = []
    for line_no, line in enumerate(text.splitlines(), start=1):
        if not line.strip():
            continue
        columns = line.strip().split(",")
        if len(columns) != 12:
            raise DataError(f"{label} line {line_no}: expected 12 columns, got {len(columns)}")
        try:
            open_time = int(columns[0])
        except ValueError:
            raise DataError(f"{label} line {line_no}: open_time {columns[0]!r} is not an integer")
        if open_time >= MICROSECOND_THRESHOLD:
            open_time //= 1000
        rows.append((open_time, columns[1], columns[2], columns[3], columns[4], columns[5]))
    if not rows:
        raise DataError(f"{label}: archive contains no rows")
    return rows


def check_rows(rows: list[tuple]) -> None:
    previous = None
    for row in rows:
        open_time = row[0]
        if open_time % HOUR_MS != 0:
            raise DataError(f"open_time {open_time} ({utc_text(open_time)}) is not on the hour")
        if previous is not None:
            if open_time == previous:
                raise DataError(f"duplicate open_time {open_time} ({utc_text(open_time)})")
            if open_time < previous:
                raise DataError(f"open_time {open_time} ({utc_text(open_time)}) goes backwards")
        previous = open_time


def fill_gaps(rows: list[tuple]) -> tuple[list[tuple], list[tuple[int, int]]]:
    """用前一个值补齐缺失的小时。返回 (rows, [(gap_start_ms, hours), ...])。"""
    filled = [rows[0]]
    gaps = []
    for row in rows[1:]:
        last = filled[-1]
        missing = (row[0] - last[0]) // HOUR_MS - 1
        if missing > 0:
            close = last[4]
            gap_start = last[0] + HOUR_MS
            for k in range(missing):
                filled.append((gap_start + k * HOUR_MS, close, close, close, close, "0"))
            gaps.append((gap_start, missing))
        filled.append(row)
    return filled, gaps


def apply_off_hour_policy(rows: list[tuple], policy: str) -> tuple[list[tuple], list[int]]:
    """返回（rows，被处理掉的那些不在整点的行原来的 open time）。"""
    off_hour = [r[0] for r in rows if r[0] % HOUR_MS != 0]
    if not off_hour or policy == "error":
        return rows, []  # 由 check_rows 报告第一个
    if policy == "drop":
        return [r for r in rows if r[0] % HOUR_MS == 0], off_hour
    if policy == "floor":
        return [(r[0] - r[0] % HOUR_MS,) + tuple(r[1:]) for r in rows], off_hour
    raise ValueError(policy)


def build_rows(archives: list[tuple[str, bytes]], off_hour: str = "error"):
    """返回（rows，gaps，被丢掉或被向下取整到整点的那些行的 open time）。"""
    rows = []
    for label, data in archives:
        rows.extend(parse_archive(data, label))
    rows, handled = apply_off_hour_policy(rows, off_hour)
    check_rows(rows)
    filled, gaps = fill_gaps(rows)
    return filled, gaps, handled


def render_csv(rows: list[tuple]) -> bytes:
    lines = [CSV_HEADER]
    lines.extend(f"{r[0]},{r[1]},{r[2]},{r[3]},{r[4]},{r[5]}" for r in rows)
    return ("\n".join(lines) + "\n").encode("ascii")


def utc_text(ms: int) -> str:
    return utc_seconds_text(ms / 1000)


def utc_seconds_text(seconds: float) -> str:
    return dt.datetime.fromtimestamp(seconds, tz=dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def gap_summary(gaps: list[tuple[int, int]]) -> dict:
    longest = max(gaps, key=lambda gap: gap[1]) if gaps else None
    return {
        "count": len(gaps),
        "total_hours": sum(gap[1] for gap in gaps),
        "longest": None if longest is None else {"start_utc": utc_text(longest[0]), "hours": longest[1]},
        "over_24h": [{"start_utc": utc_text(start), "hours": hours} for start, hours in gaps if hours > 24],
    }


def off_hour_summary(policy: str, handled: list[int]) -> dict:
    return {
        "policy": policy,
        "count": len(handled),
        "first_original_utc": utc_text(handled[0]) if handled else None,
        "last_original_utc": utc_text(handled[-1]) if handled else None,
    }


def data_warnings(symbol: str, interval: str, handled: list[int], gaps: list[tuple[int, int]]) -> list[str]:
    """超出提交数据已有情况的、不在整点的 candle，或超过 24 小时的缺口。"""
    warnings = []
    known_off_hour = KNOWN_OFF_HOUR_ROWS.get((symbol, interval), 0)
    if len(handled) != known_off_hour:
        warnings.append(f"{len(handled)} off-hour candles were handled, {known_off_hour} expected; "
                        "check off_hour_rows in the manifest before using this data")
    known_gaps = KNOWN_LONG_GAPS.get((symbol, interval), set())
    for start, hours in gaps:
        if hours > 24 and utc_text(start) not in known_gaps:
            warnings.append(f"new gap of {hours} hours from {utc_text(start)} is filled with a flat line")
    return warnings


def build_manifest(symbol, interval, months, rows, gaps, csv_bytes, downloaded_at,
                   off_hour="error", handled=()) -> dict:
    return {
        "symbol": symbol,
        "interval": interval,
        "source": f"{BASE_URL}/{symbol}/{interval}/",
        "months": [{"file": name, "sha256": digest} for name, digest in months],
        "rows": len(rows),
        "first_open_time_ms": rows[0][0],
        "last_open_time_ms": rows[-1][0],
        "first_open_time_utc": utc_text(rows[0][0]),
        "last_open_time_utc": utc_text(rows[-1][0]),
        "filled_gaps": gap_summary(gaps),
        "off_hour_rows": off_hour_summary(off_hour, list(handled)),
        "csv_sha256": sha256_hex(csv_bytes),
        "downloaded_at_utc": downloaded_at,
        "terms": TERMS,
        "license": LICENSE,
    }


# ---------------------------------------------------------------- 主流程


def run(args) -> int:
    symbol = args.symbol.upper()
    interval = args.interval
    end = args.end or DEFAULT_END_MONTH
    probing = args.start is None
    start = args.start or FIRST_MONTH
    if start > end:
        raise DataError(f"--start {format_month(start)} is after --end {format_month(end)}")
    raw_dir = Path(args.raw_dir) / symbol / interval
    out_dir = Path(args.out_dir)

    archives, months, found_first = [], [], False
    for year_month in months_between(start, end):
        name = archive_name(symbol, interval, year_month)
        result = fetch_month(symbol, interval, year_month, raw_dir)
        if result is None:
            if probing and not found_first:
                print(f"  {name}: not on Binance Vision, still looking for the first month")
                continue
            hint = ""
            if year_month == end:
                hint = (" (monthly archives appear on the first Monday of the next month;"
                        " pass --end to stop at an earlier month)")
            raise DataError(f"{name}: 404 on Binance Vision{hint}")
        found_first = True
        data, digest = result
        archives.append((name, data))
        months.append((name, digest))
        print(f"  {name}: ok ({len(data)} bytes)")
    if not archives:
        raise DataError(f"no data for {symbol} {interval} between {format_month(start)} and {format_month(end)}")

    rows, gaps, handled = build_rows(archives, args.off_hour)
    csv_bytes = render_csv(rows)
    downloaded_at = archives_saved_at(raw_dir, months)
    manifest = build_manifest(symbol, interval, months, rows, gaps, csv_bytes, downloaded_at,
                              args.off_hour, handled)
    csv_path, manifest_path = write_outputs(out_dir, f"{symbol}-{interval}", csv_bytes, manifest)

    print_summary(symbol, interval, manifest, args.off_hour, handled)
    print(f"wrote {csv_path}")
    print(f"wrote {manifest_path}")
    for warning in data_warnings(symbol, interval, handled, gaps):
        print(f"!!! WARNING: {warning}", file=sys.stderr)
    return 0


def archives_saved_at(raw_dir: Path, months: list[tuple[str, str]]) -> str:
    """最新的那个压缩包存进 raw_dir 的 UTC 时间（downloaded_at_utc）。"""
    newest = max((raw_dir / name).stat().st_mtime for name, _ in months)
    return utc_seconds_text(newest)


def write_outputs(out_dir: Path, stem: str, csv_bytes: bytes, manifest: dict) -> tuple[Path, Path]:
    """把 {stem}.csv 和 {stem}.manifest.json 写进 out_dir；返回两个路径。"""
    out_dir.mkdir(parents=True, exist_ok=True)
    csv_path = out_dir / f"{stem}.csv"
    manifest_path = out_dir / f"{stem}.manifest.json"
    csv_path.write_bytes(csv_bytes)
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8", newline="\n")
    return csv_path, manifest_path


def print_summary(symbol: str, interval: str, manifest: dict, off_hour: str, handled: list[int]) -> None:
    summary = manifest["filled_gaps"]
    print(f"{symbol} {interval}: {manifest['rows']} rows, "
          f"{manifest['first_open_time_utc']} .. {manifest['last_open_time_utc']}")
    print(f"filled gaps: {summary['count']} segments, {summary['total_hours']} hours, "
          f"longest {summary['longest']}")
    if handled:
        print(f"off-hour rows ({off_hour}): {manifest['off_hour_rows']}")


# ---------------------------------------------------------------- 自测


def _kline_line(open_time: int, open_price: str, high: str, low: str, close: str, volume: str) -> str:
    # 12 列，最后一列是 Binance 的 "Ignore" 字段。
    return ",".join([str(open_time), open_price, high, low, close, volume, str(open_time + HOUR_MS - 1),
                     "0", "1", "0", "0", "7"])


def _make_zip(member: str, lines: list[str]) -> bytes:
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w", zipfile.ZIP_DEFLATED) as archive:
        archive.writestr(member, "\n".join(lines) + "\n")
    return buffer.getvalue()


def self_test() -> int:
    def expect(condition, message):
        if not condition:
            raise AssertionError(message)

    def expect_error(fn, fragment):
        try:
            fn()
        except DataError as error:
            expect(fragment in str(error), f"error {error!r} lacks {fragment!r}")
            return
        raise AssertionError(f"expected DataError containing {fragment!r}")

    t0 = 1735671600000  # 2024-12-31T19:00:00Z，毫秒
    # 压缩包 A：毫秒，三个小时，到 2024-12-31 21:00 为止。
    zip_ms = _make_zip("X-1h-2024-12.csv", [
        _kline_line(t0 + 0 * HOUR_MS, "100.10", "101.00", "99.00", "100.50", "1.5"),
        _kline_line(t0 + 1 * HOUR_MS, "100.50", "102.00", "100.00", "101.25000000", "2.0"),
        _kline_line(t0 + 2 * HOUR_MS, "101.25", "101.30", "100.90", "101.00000000", "0.25"),
    ])
    # 压缩包 B：微秒，从 2025-01-01 01:00 开始，所以缺 22:00、23:00、00:00。
    t_b = t0 + 6 * HOUR_MS
    zip_us = _make_zip("X-1h-2025-01.csv", [
        _kline_line(t_b * 1000, "102.00", "103.00", "101.50", "102.50", "3.0"),
        _kline_line((t_b + HOUR_MS) * 1000, "102.50", "102.60", "102.00", "102.10", "0.75"),
    ])

    rows_ms = parse_archive(zip_ms, "ms")
    rows_us = parse_archive(zip_us, "us")
    expect([r[0] for r in rows_ms] == [t0, t0 + HOUR_MS, t0 + 2 * HOUR_MS], "ms timestamps")
    expect([r[0] for r in rows_us] == [t_b, t_b + HOUR_MS], "microseconds converted to milliseconds")
    expect(rows_ms[1][4] == "101.25000000", "number strings are kept verbatim")

    rows, gaps, handled = build_rows([("ms", zip_ms), ("us", zip_us)])
    expect(len(rows) == 8, f"expected 8 rows after filling, got {len(rows)}")
    expect(gaps == [(t0 + 3 * HOUR_MS, 3)], f"gap list {gaps}")
    expect(handled == [], "no off-hour rows")
    for k in range(3):
        filler = rows[3 + k]
        expect(filler == (t0 + (3 + k) * HOUR_MS, "101.00000000", "101.00000000",
                          "101.00000000", "101.00000000", "0"), f"filler row {filler}")
    expect(all(b[0] - a[0] == HOUR_MS for a, b in zip(rows, rows[1:])), "rows are contiguous")
    expect(utc_text(rows[6][0]) == "2025-01-01T01:00:00Z", "first microsecond row lands at 01:00")

    csv_text = render_csv(rows).decode("ascii")
    expected_csv = "\n".join([
        CSV_HEADER,
        f"{t0},100.10,101.00,99.00,100.50,1.5",
        f"{t0 + HOUR_MS},100.50,102.00,100.00,101.25000000,2.0",
        f"{t0 + 2 * HOUR_MS},101.25,101.30,100.90,101.00000000,0.25",
        f"{t0 + 3 * HOUR_MS},101.00000000,101.00000000,101.00000000,101.00000000,0",
        f"{t0 + 4 * HOUR_MS},101.00000000,101.00000000,101.00000000,101.00000000,0",
        f"{t0 + 5 * HOUR_MS},101.00000000,101.00000000,101.00000000,101.00000000,0",
        f"{t_b},102.00,103.00,101.50,102.50,3.0",
        f"{t_b + HOUR_MS},102.50,102.60,102.00,102.10,0.75",
    ]) + "\n"
    expect(csv_text == expected_csv, "CSV text")

    summary = gap_summary(gaps + [(t0 + 100 * HOUR_MS, 30)])
    expect(summary["count"] == 2 and summary["total_hours"] == 33, f"gap summary {summary}")
    expect(summary["longest"]["hours"] == 30 and len(summary["over_24h"]) == 1, "longest / over_24h")

    # 会被拒绝的情况。
    dup = _make_zip("d.csv", [_kline_line(t0, "1", "1", "1", "1", "1"), _kline_line(t0, "1", "1", "1", "1", "1")])
    expect_error(lambda: build_rows([("dup", dup)]), "duplicate")
    back = _make_zip("b.csv", [_kline_line(t0 + HOUR_MS, "1", "1", "1", "1", "1"), _kline_line(t0, "1", "1", "1", "1", "1")])
    expect_error(lambda: build_rows([("back", back)]), "backwards")
    odd = _make_zip("o.csv", [_kline_line(t0 + 60_000, "1", "1", "1", "1", "1")])
    expect_error(lambda: build_rows([("odd", odd)]), "not on the hour")

    # 不在整点的行，形状和 BTCUSDT 2018-02 一样：00:00 一根 candle，然后一段从 hh:28 开始的 candle，
    # 之后又回到整点。
    shifted = 28 * 60_000 + 14_789
    off = _make_zip("f.csv", [
        _kline_line(t0, "10", "11", "9", "10.5", "1"),
        _kline_line(t0 + 2 * HOUR_MS + shifted, "10.6", "12", "10", "11", "2"),
        _kline_line(t0 + 3 * HOUR_MS + shifted, "11", "13", "11", "12", "3"),
        _kline_line(t0 + 5 * HOUR_MS, "12", "12", "12", "12.5", "4"),
    ])
    expect_error(lambda: build_rows([("off", off)]), "not on the hour")
    dropped, dropped_gaps, dropped_times = build_rows([("off", off)], "drop")
    expect([r[0] for r in dropped] == [t0 + k * HOUR_MS for k in range(6)], "drop keeps the hourly grid")
    expect(all(r[1:] == ("10.5", "10.5", "10.5", "10.5", "0") for r in dropped[1:5]), "dropped hours are flat")
    expect(dropped_gaps == [(t0 + HOUR_MS, 4)], f"drop gaps {dropped_gaps}")
    expect(dropped_times == [t0 + 2 * HOUR_MS + shifted, t0 + 3 * HOUR_MS + shifted], "dropped times")
    floored, floored_gaps, _ = build_rows([("off", off)], "floor")
    expect([r[0] for r in floored] == [t0 + k * HOUR_MS for k in range(6)], "floor keeps the hourly grid")
    expect(floored[2][1:] == ("10.6", "12", "10", "11", "2"), "floored row keeps its numbers")
    expect(floored_gaps == [(t0 + HOUR_MS, 1), (t0 + 4 * HOUR_MS, 1)], f"floor gaps {floored_gaps}")
    expect(off_hour_summary("drop", dropped_times)["count"] == 2, "off-hour summary")

    # 新出现的一段不在整点的 candle，或者新的长缺口，会被标出来；已知的不会。
    known_gap = (1518051600000, 75)  # 2018-02-08T01:00:00Z
    expect(utc_text(known_gap[0]) == "2018-02-08T01:00:00Z", "known gap start")
    expect(data_warnings("BTCUSDT", "1h", list(range(43)), [known_gap, (t0, 3)]) == [], "committed data: no warning")
    new_off_hour = data_warnings("BTCUSDT", "1h", dropped_times, dropped_gaps)
    expect(len(new_off_hour) == 1 and "2 off-hour candles" in new_off_hour[0], f"new off-hour warning {new_off_hour}")
    new_gap = data_warnings("BTCUSDT", "1h", list(range(43)), [known_gap, (t0, 30)])
    expect(len(new_gap) == 1 and "30 hours" in new_gap[0], f"new gap warning {new_gap}")
    expect(len(data_warnings("ETHUSDT", "1h", dropped_times, [known_gap])) == 2, "other symbols have no known list")

    header = _make_zip("h.csv", ["open_time,open,high,low,close,volume,close_time,q,n,tb,tq,ignore"])
    expect_error(lambda: parse_archive(header, "header"), "not an integer")
    short = _make_zip("s.csv", [f"{t0},1,1,1,1,1"])
    expect_error(lambda: parse_archive(short, "short"), "12 columns")
    expect_error(lambda: parse_checksum("abc  X.zip", "X.zip"), "malformed")
    expect_error(lambda: parse_checksum("0" * 64 + "  Y.zip", "X.zip"), "expected")
    expect(parse_checksum("A" * 64 + "  X.zip\n", "X.zip") == "a" * 64, "checksum parse")

    expect(list(months_between((2017, 11), (2018, 2))) == [(2017, 11), (2017, 12), (2018, 1), (2018, 2)], "months")
    defaults = main_parser().parse_args(["--symbol", "BTCUSDT"])
    expect(defaults.end is None and defaults.off_hour == "drop", "CLI defaults")
    expect(DEFAULT_END_MONTH == (2026, 8), "default end month")

    print("self-test OK")
    return 0


def main_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--symbol", help="spot symbol, e.g. BTCUSDT (required unless --self-test)")
    parser.add_argument("--interval", default="1h", choices=SUPPORTED_INTERVALS)
    parser.add_argument("--start", type=parse_month,
                        help="first month YYYY-MM (default: probe from 2017-08 for the first month with data)")
    parser.add_argument("--end", type=parse_month,
                        help=f"last month YYYY-MM (default: {format_month(DEFAULT_END_MONTH)}, the end of the test segment)")
    parser.add_argument("--raw-dir", default=str(DEFAULT_RAW_DIR), help="where the zip archives are cached")
    parser.add_argument("--out-dir", default=str(DEFAULT_OUT_DIR), help="where the CSV and manifest go")
    parser.add_argument("--off-hour", default=DEFAULT_OFF_HOUR, choices=OFF_HOUR_POLICIES,
                        help="candles not on the hour: drop them (default), stop with an error, or floor them to the hour")
    parser.add_argument("--self-test", action="store_true", help="offline test of parsing and gap filling")
    return parser


def main(argv=None) -> int:
    parser = main_parser()
    args = parser.parse_args(argv)

    if args.self_test:
        return self_test()
    if not args.symbol:
        parser.error("--symbol is required")
    try:
        return run(args)
    except DataError as error:
        print(f"error: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
