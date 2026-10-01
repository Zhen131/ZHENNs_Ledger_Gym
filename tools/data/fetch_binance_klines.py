#!/usr/bin/env python3
"""Download Binance Vision spot klines and build a gap-filled hourly CSV.

Standard library only. Typical use, from the repository root:

    python tools/data/fetch_binance_klines.py --symbol BTCUSDT --end 2026-08
    python tools/data/fetch_binance_klines.py --self-test

What it does:

1. Downloads the monthly archives
   https://data.binance.vision/data/spot/monthly/klines/{S}/{I}/{S}-{I}-{YYYY-MM}.zip
   together with their .CHECKSUM files, and verifies every archive's SHA-256.
   Archives that are already on disk and still match their checksum are reused.
2. Keeps the first six columns (open_time, open, high, low, close, volume).
   Timestamps from 2025-01-01 on are in microseconds; they are converted to
   milliseconds so the whole file uses one unit.
3. Checks that open times are strictly ascending, unique and on the hour.
4. Fills every missing hour with a flat candle at the previous close
   (open = high = low = close = previous close, volume = 0). Only earlier
   values are ever used, so the filler never looks into the future.
5. Writes {out}/{S}-1h.csv and {out}/{S}-1h.manifest.json.

Numbers are copied as the original strings; nothing is passed through float.
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

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_RAW_DIR = REPO_ROOT / "data" / "raw"
DEFAULT_OUT_DIR = REPO_ROOT / "Assets" / "StreamingAssets" / "Gym" / "data"


class DataError(Exception):
    """Any problem that makes the output untrustworthy. The script stops."""


# ---------------------------------------------------------------- months


def parse_month(text: str) -> tuple[int, int]:
    try:
        year_s, month_s = text.split("-")
        year, month = int(year_s), int(month_s)
    except ValueError:
        raise argparse.ArgumentTypeError(f"expected YYYY-MM, got {text!r}")
    if not 1 <= month <= 12 or len(year_s) != 4 or len(month_s) != 2:
        raise argparse.ArgumentTypeError(f"expected YYYY-MM, got {text!r}")
    return year, month


def format_month(ym: tuple[int, int]) -> str:
    return f"{ym[0]:04d}-{ym[1]:02d}"


def next_month(ym: tuple[int, int]) -> tuple[int, int]:
    year, month = ym
    return (year + 1, 1) if month == 12 else (year, month + 1)


def months_between(start: tuple[int, int], end: tuple[int, int]):
    ym = start
    while ym <= end:
        yield ym
        ym = next_month(ym)


def previous_full_month(today: dt.date) -> tuple[int, int]:
    return (today.year - 1, 12) if today.month == 1 else (today.year, today.month - 1)


def archive_name(symbol: str, interval: str, ym: tuple[int, int]) -> str:
    return f"{symbol}-{interval}-{format_month(ym)}.zip"


def archive_url(symbol: str, interval: str, ym: tuple[int, int]) -> str:
    return f"{BASE_URL}/{symbol}/{interval}/{archive_name(symbol, interval, ym)}"


# ---------------------------------------------------------------- download


def http_get(url: str, attempts: int = 3) -> bytes | None:
    """Return the body, or None on 404. Other failures are retried, then raised."""
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


def fetch_month(symbol: str, interval: str, ym: tuple[int, int], raw_dir: Path):
    """Return (zip bytes, sha256), or None if Binance Vision has no such month."""
    name = archive_name(symbol, interval, ym)
    zip_path = raw_dir / name
    checksum_path = raw_dir / (name + ".CHECKSUM")

    if zip_path.exists() and checksum_path.exists():
        expected = parse_checksum(checksum_path.read_text(encoding="ascii"), name)
        data = zip_path.read_bytes()
        if sha256_hex(data) == expected:
            return data, expected
        print(f"  {name}: cached copy fails its checksum, downloading again", file=sys.stderr)

    url = archive_url(symbol, interval, ym)
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


# ---------------------------------------------------------------- parse / check / fill


def parse_archive(data: bytes, label: str) -> list[tuple]:
    """Rows of (open_time_ms:int, open, high, low, close, volume) as strings."""
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
    """Forward-fill missing hours. Returns (rows, [(gap_start_ms, hours), ...])."""
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


def build_rows(archives: list[tuple[str, bytes]]) -> tuple[list[tuple], list[tuple[int, int]]]:
    rows = []
    for label, data in archives:
        rows.extend(parse_archive(data, label))
    check_rows(rows)
    return fill_gaps(rows)


def render_csv(rows: list[tuple]) -> bytes:
    lines = [CSV_HEADER]
    lines.extend(f"{r[0]},{r[1]},{r[2]},{r[3]},{r[4]},{r[5]}" for r in rows)
    return ("\n".join(lines) + "\n").encode("ascii")


def utc_text(ms: int) -> str:
    return dt.datetime.fromtimestamp(ms / 1000, tz=dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def gap_summary(gaps: list[tuple[int, int]]) -> dict:
    longest = max(gaps, key=lambda g: g[1]) if gaps else None
    return {
        "count": len(gaps),
        "total_hours": sum(g[1] for g in gaps),
        "longest": None if longest is None else {"start_utc": utc_text(longest[0]), "hours": longest[1]},
        "over_24h": [{"start_utc": utc_text(s), "hours": h} for s, h in gaps if h > 24],
    }


def build_manifest(symbol, interval, months, rows, gaps, csv_bytes, downloaded_at) -> dict:
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
        "csv_sha256": sha256_hex(csv_bytes),
        "downloaded_at_utc": downloaded_at,
        "terms": TERMS,
        "license": LICENSE,
    }


# ---------------------------------------------------------------- main


def run(args) -> int:
    symbol = args.symbol.upper()
    interval = args.interval
    end = args.end or previous_full_month(dt.datetime.now(dt.timezone.utc).date())
    probing = args.start is None
    start = args.start or FIRST_MONTH
    if start > end:
        raise DataError(f"--start {format_month(start)} is after --end {format_month(end)}")
    raw_dir = Path(args.raw_dir) / symbol / interval
    out_dir = Path(args.out_dir)

    archives, months, found_first = [], [], False
    for ym in months_between(start, end):
        name = archive_name(symbol, interval, ym)
        result = fetch_month(symbol, interval, ym, raw_dir)
        if result is None:
            if probing and not found_first:
                print(f"  {name}: not on Binance Vision, still looking for the first month")
                continue
            hint = ""
            if ym == end and args.end is None:
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

    rows, gaps = build_rows(archives)
    csv_bytes = render_csv(rows)
    newest = max((raw_dir / name).stat().st_mtime for name, _ in months)
    downloaded_at = dt.datetime.fromtimestamp(newest, tz=dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    manifest = build_manifest(symbol, interval, months, rows, gaps, csv_bytes, downloaded_at)

    out_dir.mkdir(parents=True, exist_ok=True)
    csv_path = out_dir / f"{symbol}-{interval}.csv"
    manifest_path = out_dir / f"{symbol}-{interval}.manifest.json"
    csv_path.write_bytes(csv_bytes)
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8", newline="\n")

    summary = manifest["filled_gaps"]
    print(f"{symbol} {interval}: {manifest['rows']} rows, "
          f"{manifest['first_open_time_utc']} .. {manifest['last_open_time_utc']}")
    print(f"filled gaps: {summary['count']} segments, {summary['total_hours']} hours, "
          f"longest {summary['longest']}")
    print(f"wrote {csv_path}")
    print(f"wrote {manifest_path}")
    return 0


# ---------------------------------------------------------------- self-test


def _kline_line(open_time: int, o: str, h: str, l: str, c: str, v: str) -> str:
    # 12 columns, the last one is Binance's "Ignore" field.
    return ",".join([str(open_time), o, h, l, c, v, str(open_time + HOUR_MS - 1),
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

    t0 = 1735671600000  # 2024-12-31T19:00:00Z, milliseconds
    # Archive A: milliseconds, three hours ending 2024-12-31 21:00.
    zip_ms = _make_zip("X-1h-2024-12.csv", [
        _kline_line(t0 + 0 * HOUR_MS, "100.10", "101.00", "99.00", "100.50", "1.5"),
        _kline_line(t0 + 1 * HOUR_MS, "100.50", "102.00", "100.00", "101.25000000", "2.0"),
        _kline_line(t0 + 2 * HOUR_MS, "101.25", "101.30", "100.90", "101.00000000", "0.25"),
    ])
    # Archive B: microseconds, starts 2025-01-01 01:00, so 22:00, 23:00, 00:00 are missing.
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

    rows, gaps = build_rows([("ms", zip_ms), ("us", zip_us)])
    expect(len(rows) == 8, f"expected 8 rows after filling, got {len(rows)}")
    expect(gaps == [(t0 + 3 * HOUR_MS, 3)], f"gap list {gaps}")
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

    # Rejections.
    dup = _make_zip("d.csv", [_kline_line(t0, "1", "1", "1", "1", "1"), _kline_line(t0, "1", "1", "1", "1", "1")])
    expect_error(lambda: build_rows([("dup", dup)]), "duplicate")
    back = _make_zip("b.csv", [_kline_line(t0 + HOUR_MS, "1", "1", "1", "1", "1"), _kline_line(t0, "1", "1", "1", "1", "1")])
    expect_error(lambda: build_rows([("back", back)]), "backwards")
    odd = _make_zip("o.csv", [_kline_line(t0 + 60_000, "1", "1", "1", "1", "1")])
    expect_error(lambda: build_rows([("odd", odd)]), "not on the hour")
    header = _make_zip("h.csv", ["open_time,open,high,low,close,volume,close_time,q,n,tb,tq,ignore"])
    expect_error(lambda: parse_archive(header, "header"), "not an integer")
    short = _make_zip("s.csv", [f"{t0},1,1,1,1,1"])
    expect_error(lambda: parse_archive(short, "short"), "12 columns")
    expect_error(lambda: parse_checksum("abc  X.zip", "X.zip"), "malformed")
    expect_error(lambda: parse_checksum("0" * 64 + "  Y.zip", "X.zip"), "expected")
    expect(parse_checksum("A" * 64 + "  X.zip\n", "X.zip") == "a" * 64, "checksum parse")

    expect(list(months_between((2017, 11), (2018, 2))) == [(2017, 11), (2017, 12), (2018, 1), (2018, 2)], "months")
    expect(previous_full_month(dt.date(2026, 1, 15)) == (2025, 12), "previous month in January")
    expect(previous_full_month(dt.date(2026, 10, 1)) == (2026, 9), "previous month")

    print("self-test OK")
    return 0


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--symbol", help="spot symbol, e.g. BTCUSDT (required unless --self-test)")
    parser.add_argument("--interval", default="1h", choices=SUPPORTED_INTERVALS)
    parser.add_argument("--start", type=parse_month,
                        help="first month YYYY-MM (default: probe from 2017-08 for the first month with data)")
    parser.add_argument("--end", type=parse_month, help="last month YYYY-MM (default: the previous full month)")
    parser.add_argument("--raw-dir", default=str(DEFAULT_RAW_DIR), help="where the zip archives are cached")
    parser.add_argument("--out-dir", default=str(DEFAULT_OUT_DIR), help="where the CSV and manifest go")
    parser.add_argument("--self-test", action="store_true", help="offline test of parsing and gap filling")
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
