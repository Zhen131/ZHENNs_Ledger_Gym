#!/usr/bin/env python3
"""Score round-three models on BTC's three validation segments: two thresholds, then a ranking.

Usage, from the repository root:

    python scripts/eval/score_round3.py [evaluations/log.csv] [--symbol BTCUSDT] [--fee 0.001] [--runs long-,mix-]

Standard library only. It reads the evaluation log and prints three Markdown tables.

Rules (22A section two item 3 and its addendum, 22 section three):

- For each model (agent rows, by model_run_id) take, on each of validation-bear,
  validation-recovery and validation, the latest row with this symbol and fee rate
  (largest timestamp_utc; on a tie, the one later in the file). A model that lacks a
  segment is listed as "missing segment" and not ranked.
- Threshold one: the max_drawdown on validation-bear is at most MAX_BEAR_DRAWDOWN.
  Threshold two: the total_return on validation is at least MIN_VALIDATION_RETURN.
  Both numbers are compared after rounding the log's full-precision values to four
  decimals (half up). A model that misses either is out; it stays in the table,
  marked with the threshold it missed.
- Segment score = total_return / max(max_drawdown, 0.01); model score = the mean of
  the three segment scores. Scores are compared at four decimals; on a tie the smaller
  mean drawdown comes first, then the run id.
- Configuration = the run id without its "-s<seed>-<yyyymmdd>" end (regular expression
  ^(.+)-s(\\d+)-(\\d{8})$); a run id that does not match is a configuration of its own
  and is marked. Configuration score = the mean model score over ALL its seeds,
  whether they pass the thresholds or not, shown next to "seeds past both thresholds
  / seeds".
- The final model is picked among all models that pass both thresholds: the one with
  the highest model score. Configurations are not picked first.
- "Did not learn": a dial-mode model whose three rows all say changes=0 in their notes;
  an order-mode model (any row whose notes have no mode=, which is every row before
  batch 24) that made no trade on any of the three segments, or whose total_return on
  some segment is below the latest random baseline row of the same symbol, segment and
  fee rate (the two halves of 18B section 0.5).
- Table three puts buy_and_hold, cash, momentum_90d, sma_cross, vol_target,
  constant_mix_50 and dial_momentum_90d through the same thresholds and scores
  (latest baseline row per segment); level changes are "-" for the old rules.
"""

from __future__ import annotations

import argparse
import csv
import re
import sys
from decimal import ROUND_HALF_UP, Decimal
from pathlib import Path

# 来自 22A 第二节第 3 条及追加节追-1：对手 constant_mix_50 的真实成绩（BTC、费率 0.001，24 批流水第 543、547 行），
# 四舍五入到 4 位小数。改这两个数就是改 00A。
MAX_BEAR_DRAWDOWN = Decimal("0.4132")
MIN_VALIDATION_RETURN = Decimal("0.3901")

# 比较门槛前把流水里的数四舍五入到几位小数（22A 追加节追-1）；得分也按这个位数比较（18B 第 0.5 节）。
DECIMALS = 4
QUANTUM = Decimal(1).scaleb(-DECIMALS)
# 单段得分的分母下限：回撤小于它时按它算。
MIN_DRAWDOWN = 0.01

SEGMENTS = ("validation-bear", "validation-recovery", "validation")
BEAR, RECOVERY, VALIDATION = SEGMENTS
RUN_ID = re.compile(r"^(.+)-s(\d+)-(\d{8})$")
# 表三的同台对照：第二轮的老规则，加第三轮的两个对手。
BASELINES = ("buy_and_hold", "cash", "momentum_90d", "sma_cross", "vol_target", "constant_mix_50", "dial_momentum_90d")
OLD_RULES = ("buy_and_hold", "cash", "momentum_90d", "sma_cross", "vol_target")


def round4(text: str) -> Decimal:
    """流水里的一个数（全精度文本）四舍五入到 4 位小数。"""
    return Decimal(text).quantize(QUANTUM, rounding=ROUND_HALF_UP)


def passes_bear(max_drawdown: str) -> bool:
    """门槛一：validation-bear 的回撤（四舍五入到 4 位）不超过 MAX_BEAR_DRAWDOWN。"""
    return round4(max_drawdown) <= MAX_BEAR_DRAWDOWN


def passes_validation(total_return: str) -> bool:
    """门槛二：validation 的收益（四舍五入到 4 位）不低于 MIN_VALIDATION_RETURN。"""
    return round4(total_return) >= MIN_VALIDATION_RETURN


def segment_score(total_return: str, max_drawdown: str) -> float:
    """单段得分 = total_return ÷ max(max_drawdown, 0.01)。"""
    return float(total_return) / max(float(max_drawdown), MIN_DRAWDOWN)


def parse_notes(notes: str) -> dict[str, str]:
    """notes 里 'key=value' 的各项（以 '; ' 分隔）；不是这个形状的项忽略。"""
    items: dict[str, str] = {}
    for part in (notes or "").split(";"):
        key, sep, value = part.strip().partition("=")
        if sep:
            items[key.strip()] = value.strip()
    return items


def mode_of(row: dict) -> str:
    """一行的动作模式：notes 里的 mode=；没有 mode= 的老行一律当下单模式（orders）。"""
    return parse_notes(row.get("notes", "")).get("mode", "orders")


def changes_of(row: dict) -> int | None:
    """notes 里的 changes=（旋钮模式的换档次数）；没有时为 None。"""
    text = parse_notes(row.get("notes", "")).get("changes")
    return int(text) if text is not None and text.isdigit() else None


def configuration_of(run_id: str) -> tuple[str, bool]:
    """run-id 去掉种子和日期后的配置名，以及正则配没配上。"""
    match = RUN_ID.match(run_id)
    return (match.group(1), True) if match else (run_id, False)


def read_log(path: Path) -> list[dict]:
    """流水的每一行（字典），另加 _line：文件里的行号（表头是第 1 行）。"""
    with path.open(newline="", encoding="utf-8") as handle:
        rows = list(csv.DictReader(handle))
    for index, row in enumerate(rows):
        row["_line"] = index + 2
    return rows


def matches(row: dict, symbol: str, fee: float) -> bool:
    try:
        return row["symbol"] == symbol and float(row["fee_rate"]) == fee and row["segment"] in SEGMENTS
    except (KeyError, ValueError):
        return False


def latest(rows: list[dict]) -> dict:
    """timestamp_utc 最大的一行；相同时取文件里靠后的。"""
    return max(rows, key=lambda row: (row["timestamp_utc"], row["_line"]))


def latest_by(rows: list[dict], key) -> dict[tuple, dict]:
    """按 (key(row), segment) 分组，每组取最新一行。"""
    groups: dict[tuple, list[dict]] = {}
    for row in rows:
        groups.setdefault((key(row), row["segment"]), []).append(row)
    return {group: latest(items) for group, items in groups.items()}


class Entry:
    """一个模型或一条基准规则在三个验证段上的成绩和判定。"""

    def __init__(self, name: str, rows: dict[str, dict]):
        self.name = name
        self.rows = rows  # 段名 -> 行
        self.missing = [segment for segment in SEGMENTS if segment not in rows]
        self.complete = not self.missing
        self.pass_bear = passes_bear(rows[BEAR]["max_drawdown"]) if BEAR in rows else None
        self.pass_validation = passes_validation(rows[VALIDATION]["total_return"]) if VALIDATION in rows else None
        self.passed = bool(self.complete and self.pass_bear and self.pass_validation)
        if self.complete:
            scores = [segment_score(rows[s]["total_return"], rows[s]["max_drawdown"]) for s in SEGMENTS]
            self.score = sum(scores) / len(scores)
            self.mean_drawdown = sum(float(rows[s]["max_drawdown"]) for s in SEGMENTS) / len(SEGMENTS)
        else:
            self.score = None
            self.mean_drawdown = None
        modes = {mode_of(row) for row in rows.values()}
        self.mode = modes.pop() if len(modes) == 1 else ("mixed" if modes else "-")

    @property
    def rounded_score(self) -> Decimal | None:
        return None if self.score is None else Decimal(repr(self.score)).quantize(QUANTUM, rounding=ROUND_HALF_UP)

    def rank_key(self):
        """排名：得分（4 位小数）高的在前；相同时平均回撤小的在前，再按名字。"""
        return (-self.rounded_score, self.mean_drawdown, self.name)

    def changes_text(self) -> str:
        if self.mode != "dial":
            return "-"
        cells = []
        for s in SEGMENTS:
            value = changes_of(self.rows[s]) if s in self.rows else None
            cells.append("·" if s not in self.rows else "?" if value is None else str(value))
        return "/".join(cells)


def did_not_learn(entry: Entry, randoms: dict[str, dict]) -> str:
    """「没学会」的判定，写成表里的一格：no，或 yes 加原因；判不了时写原因。"""
    if not entry.complete:
        return "-"
    if entry.mode == "dial":
        changes = [changes_of(entry.rows[s]) for s in SEGMENTS]
        if any(c is None for c in changes):
            return "? (no changes= in notes)"
        return "yes (never changed level)" if all(c == 0 for c in changes) else "no"
    if entry.mode != "orders":
        return "? (rows in different modes)"
    if all(float(entry.rows[s]["trades"] or 0) == 0 for s in SEGMENTS):
        return "yes (no trades)"
    below = []
    for s in SEGMENTS:
        random_row = randoms.get(s)
        if random_row is None:
            return "? (no random row on " + s + ")"
        if float(entry.rows[s]["total_return"]) < float(random_row["total_return"]):
            below.append(s)
    return f"yes (below random on {', '.join(below)})" if below else "no"


def threshold_text(passed: bool | None) -> str:
    return "-" if passed is None else ("pass" if passed else "**fail**")


def fraction(row: dict | None, column: str) -> str:
    return "-" if row is None else f"{round4(row[column])}"


def score_text(entry: Entry) -> str:
    return "-" if entry.rounded_score is None else f"{entry.rounded_score}"


def status_text(entry: Entry, rank: int | None = None) -> str:
    if not entry.complete:
        return "missing segment: " + ", ".join(entry.missing)
    if entry.passed:
        return f"ranked #{rank}" if rank is not None else "past both thresholds"
    missed = [label for label, ok in (("threshold one", entry.pass_bear), ("threshold two", entry.pass_validation)) if not ok]
    return "out: " + " and ".join(missed)


def entry_cells(entry: Entry) -> str:
    r = entry.rows
    return (f"{fraction(r.get(BEAR), 'total_return')} | {fraction(r.get(BEAR), 'max_drawdown')} | "
            f"{fraction(r.get(RECOVERY), 'total_return')} | {fraction(r.get(RECOVERY), 'max_drawdown')} | "
            f"{fraction(r.get(VALIDATION), 'total_return')} | {fraction(r.get(VALIDATION), 'max_drawdown')} | "
            f"{threshold_text(entry.pass_bear)} | {threshold_text(entry.pass_validation)} | {score_text(entry)}")


def ordered(entries: list[Entry]) -> list[Entry]:
    """过了两道门槛的按排名在前，其余有得分的按同一规则在后，缺段的最后（按名字）。"""
    ranked = sorted((e for e in entries if e.passed), key=Entry.rank_key)
    out_ = sorted((e for e in entries if e.complete and not e.passed), key=Entry.rank_key)
    missing = sorted((e for e in entries if not e.complete), key=lambda e: e.name)
    return ranked + out_ + missing


def build_entries(rows: list[dict], symbol: str, fee: float, prefixes: list[str] | None):
    """(模型, 基准规则, 每段最新的 random 行)。"""
    picked = [row for row in rows if matches(row, symbol, fee)]
    agents = [row for row in picked if row["kind"] == "agent"]
    if prefixes:
        agents = [row for row in agents if any(row["model_run_id"].startswith(p) for p in prefixes)]
    by_model: dict[str, dict[str, dict]] = {}
    for (run_id, segment), row in latest_by(agents, lambda r: r["model_run_id"]).items():
        by_model.setdefault(run_id, {})[segment] = row
    models = [Entry(run_id, segs) for run_id, segs in by_model.items()]

    baseline_rows = [row for row in picked if row["kind"] == "baseline"]
    by_policy: dict[str, dict[str, dict]] = {}
    for (policy, segment), row in latest_by(baseline_rows, lambda r: r["policy"]).items():
        by_policy.setdefault(policy, {})[segment] = row
    baselines = [Entry(policy, by_policy.get(policy, {})) for policy in BASELINES]
    randoms = by_policy.get("random", {})
    return models, baselines, randoms


def report(rows: list[dict], symbol: str, fee: float, prefixes: list[str] | None) -> str:
    models, baselines, randoms = build_entries(rows, symbol, fee, prefixes)
    head = ("| {first} | Bear return | Bear drawdown | Recovery return | Recovery drawdown | Validation return | "
            "Validation drawdown | Threshold one | Threshold two | Score |")
    rule = "| --- | ---: | ---: | ---: | ---: | ---: | ---: | :---: | :---: | ---: |"
    out = [
        f"## Round three scores · {symbol} · fee {fee:g}",
        "",
        f"Threshold one: validation-bear max_drawdown <= {MAX_BEAR_DRAWDOWN}. Threshold two: validation total_return >= "
        f"{MIN_VALIDATION_RETURN}. Both compared after rounding the log's values to {DECIMALS} decimals (22A section two "
        "item 3 and addendum 1: the real results of constant_mix_50). Segment score = total_return / max(max_drawdown, "
        f"{MIN_DRAWDOWN}); model score = the mean over the three validation segments. Returns and drawdowns below are "
        f"fractions rounded to {DECIMALS} decimals; each is the latest row for its segment.",
        "",
        "### Table 1: every model",
        "",
        "The final model is the one with the highest score among all models that pass both thresholds (configurations are "
        "not picked first). Ranked models come first, by score; then the models that are out; then those missing a segment.",
        "",
        head.format(first="Model") + " Mode | Level changes (bear/recovery/validation) | Did not learn | Status |",
        rule + " --- | :---: | --- | --- |",
    ]
    for index, entry in enumerate(ordered(models)):
        out.append(f"| `{entry.name}` | {entry_cells(entry)} | {entry.mode} | {entry.changes_text()} | "
                   f"{did_not_learn(entry, randoms)} | {status_text(entry, index + 1 if entry.passed else None)} |")
    if not models:
        out.append("| (no agent rows) | | | | | | | | | | | | | |")
    passed = sorted((e for e in models if e.passed), key=Entry.rank_key)
    out.append("")
    out.append("Highest score among the models past both thresholds: " +
               (f"`{passed[0].name}` (score {score_text(passed[0])})." if passed else "none (no model passes both thresholds)."))

    out += [
        "",
        "### Table 2: every configuration",
        "",
        "Configuration = the run id without -s<seed>-<yyyymmdd>. Configuration score = the mean model score over all its "
        "seeds that have the three segments, whether they pass the thresholds or not (22A addendum 2). Mean drawdown = the "
        "mean max_drawdown over those seeds x three segments.",
        "",
        "| Configuration | Seeds past both thresholds / seeds | Configuration score | Mean drawdown | Missing a segment | Note |",
        "| --- | :---: | ---: | ---: | :---: | --- |",
    ]
    configs: dict[str, list[Entry]] = {}
    unmatched: set[str] = set()
    for entry in models:
        name, matched = configuration_of(entry.name)
        configs.setdefault(name, []).append(entry)
        if not matched:
            unmatched.add(name)
    lines = []
    for name, entries in configs.items():
        complete = [e for e in entries if e.complete]
        if complete:
            score = sum(e.score for e in complete) / len(complete)
            drawdown = sum(e.mean_drawdown for e in complete) / len(complete)
            rounded = Decimal(repr(score)).quantize(QUANTUM, rounding=ROUND_HALF_UP)
        else:
            score = drawdown = rounded = None
        note = "run id does not match -s<seed>-<yyyymmdd>" if name in unmatched else ""
        lines.append(((0, -rounded, drawdown, name) if rounded is not None else (1, 0, 0, name),
                      f"| `{name}` | {sum(e.passed for e in complete)} / {len(complete)} | "
                      f"{'-' if rounded is None else rounded} | "
                      f"{'-' if drawdown is None else Decimal(repr(drawdown)).quantize(QUANTUM, rounding=ROUND_HALF_UP)} | "
                      f"{len(entries) - len(complete)} | {note} |"))
    for _, line in sorted(lines, key=lambda item: item[0]):
        out.append(line)
    if not configs:
        out.append("| (no agent rows) | | | | | |")

    out += [
        "",
        "### Table 3: side by side",
        "",
        "The old rules and the two round-three opponents through the same thresholds and score (latest baseline row per "
        "segment). Level changes are - for the old rules.",
        "",
        head.format(first="Policy") + " Level changes (bear/recovery/validation) | Status |",
        rule + " :---: | --- |",
    ]
    for entry in baselines:
        changes = "-" if entry.name in OLD_RULES else entry.changes_text()
        out.append(f"| {entry.name} | {entry_cells(entry)} | {changes} | {status_text(entry)} |")
    return "\n".join(out) + "\n"


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("log", nargs="?", default="evaluations/log.csv", help="path to log.csv")
    parser.add_argument("--symbol", default="BTCUSDT", help="symbol to score (default BTCUSDT)")
    parser.add_argument("--fee", type=float, default=0.001, help="fee rate, compared as a float (default 0.001)")
    parser.add_argument("--runs", default=None,
                        help="only models whose run id starts with one of these comma-separated prefixes (default: all)")
    args = parser.parse_args(argv)
    path = Path(args.log)
    if not path.is_file():
        print(f"error: {path} not found", file=sys.stderr)
        return 1
    prefixes = [p.strip() for p in args.runs.split(",") if p.strip()] if args.runs else None
    print(report(read_log(path), args.symbol, args.fee, prefixes), end="")
    return 0


if __name__ == "__main__":
    sys.exit(main())
