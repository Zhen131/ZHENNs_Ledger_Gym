#!/usr/bin/env python3
"""Plot TensorBoard scalars of ML-Agents runs, and the validation learning curve.

Usage, from the repository root, in the mlagents environment (matplotlib installed):

    # one PNG per tag; several runs of the same config share one colour
    python scripts/eval/plot_curves.py scalars results/ppo_base-s1-20261008 results/ppo_base-s2-20261008 \
        --tags "Environment/Cumulative Reward" "Trading/Trades" \
        --files reward.png trades.png --out plots

    # steps,total_return rows of one checkpointed run, read from the evaluation log
    python scripts/eval/plot_curves.py curve-csv evaluations/log.csv \
        --run-id ppo_base-curve-s1-20261019 --out plots/learning-curve.csv

    # checkpoint steps -> validation return, with buy-and-hold from the log as a dashed line
    # (--symbol picks whose buy-and-hold; default BTCUSDT)
    python scripts/eval/plot_curves.py curve plots/learning-curve.csv \
        --log evaluations/log.csv --out plots/learning-curve.png

Rules: 1600x900 pixels (16x9 inches, dpi 100); x axis is steps; the title is the tag;
the legend shows the config name, which is the run id without the `-s<seed>-<yyyyMMdd>`
suffix and without a `rerun<n>-` prefix. Config names are sorted and take the
`tab10` colours in that order, so the same input always gives the same picture
(byte for byte with the same matplotlib version). No pandas.
"""

from __future__ import annotations

import argparse
import csv
import re
import sys
from pathlib import Path

import matplotlib

matplotlib.use("Agg")  # 不用交互后端（默认 tkagg），重跑才能逐字节相同

import matplotlib.pyplot as plt  # noqa: E402
from tensorboard.backend.event_processing.event_accumulator import EventAccumulator  # noqa: E402

FIGSIZE = (16, 9)
DPI = 100
RUN_ID_PATTERN = re.compile(r"^(?:rerun\d+-)?(?P<config>.+)-s\d+-\d{8}$")
CKPT_PATTERN = re.compile(r"-ckpt(?P<steps>\d+)$")


def config_name(run_id: str) -> str:
    """run id 去掉 `-s<种子>-<日期>` 后缀和 `rerun<n>-` 前缀，得到配置名；对不上规律就原样返回。"""
    match = RUN_ID_PATTERN.match(run_id)
    return match.group("config") if match else run_id


def colours_for(configs: list[str]) -> dict[str, tuple]:
    """配置名按字母序分配 tab10 颜色，同样的输入永远同样的颜色。"""
    palette = plt.get_cmap("tab10").colors
    return {name: palette[index % len(palette)] for index, name in enumerate(sorted(set(configs)))}


def read_run_scalars(run_dir: Path) -> dict[str, list[tuple[int, float]]]:
    """读一个 run 文件夹下所有 event 文件夹（和 read_scalars.py 同一套 EventAccumulator），tag -> [(step, value)]。"""
    event_dirs = sorted({path.parent for path in run_dir.rglob("events.out.tfevents.*")})
    if not event_dirs:
        raise SystemExit(f"error: no TensorBoard event files under {run_dir}")
    merged: dict[str, dict[int, float]] = {}
    for event_dir in event_dirs:
        accumulator = EventAccumulator(str(event_dir), size_guidance={"scalars": 0})
        accumulator.Reload()
        for tag in accumulator.Tags().get("scalars", []):
            points = merged.setdefault(tag, {})
            for event in accumulator.Scalars(tag):
                points[event.step] = event.value
    return {tag: sorted(points.items()) for tag, points in merged.items()}


def default_file_name(tag: str) -> str:
    return re.sub(r"[^A-Za-z0-9]+", "-", tag).strip("-").lower() + ".png"


def new_figure():
    figure, axes = plt.subplots(figsize=FIGSIZE, dpi=DPI)
    axes.grid(True, alpha=0.3)
    return figure, axes


def save(figure, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    figure.tight_layout()
    figure.savefig(path, dpi=DPI)
    plt.close(figure)
    print(f"wrote {path}")


def command_scalars(args: argparse.Namespace) -> int:
    run_dirs = [Path(path) for path in args.runs]
    for run_dir in run_dirs:
        if not run_dir.is_dir():
            print(f"error: {run_dir} is not a directory", file=sys.stderr)
            return 1
    if args.files and len(args.files) != len(args.tags):
        print("error: --files needs one name per tag", file=sys.stderr)
        return 1
    files = args.files or [default_file_name(tag) for tag in args.tags]

    # run 按 run id 排序，画线顺序固定
    runs = sorted((run_dir.name, read_run_scalars(run_dir)) for run_dir in run_dirs)
    colours = colours_for([config_name(run_id) for run_id, _ in runs])

    for tag, file_name in zip(args.tags, files):
        figure, axes = new_figure()
        labelled: set[str] = set()
        for run_id, scalars in runs:
            if tag not in scalars:
                print(f"warning: {run_id} has no {tag}", file=sys.stderr)
                continue
            name = config_name(run_id)
            steps = [step for step, _ in scalars[tag]]
            values = [value for _, value in scalars[tag]]
            # 同一配置的几个种子同色，图例里配置名只出现一次
            label = name if name not in labelled else None
            labelled.add(name)
            axes.plot(steps, values, color=colours[name], linewidth=1.4, alpha=0.85, label=label)
        axes.set_title(tag)
        axes.set_xlabel("steps")
        axes.set_ylabel(tag)
        if labelled:
            axes.legend(loc="best")
        save(figure, Path(args.out) / file_name)
    return 0


def read_log(path: Path) -> list[dict]:
    with path.open(newline="", encoding="utf-8") as handle:
        return list(csv.DictReader(handle))


def command_curve_csv(args: argparse.Namespace) -> int:
    """从评估流水里挑出 `<run-id>-ckpt<步数>` 的行，写成 steps,total_return 两列。"""
    rows = []
    for row in read_log(Path(args.log)):
        run_id = row["model_run_id"]
        if not run_id.startswith(args.run_id + "-ckpt") or row["segment"] != args.segment:
            continue
        if float(row["fee_rate"]) != args.fee_rate:
            continue
        match = CKPT_PATTERN.search(run_id)
        if match:
            rows.append((int(match.group("steps")), row["total_return"]))
    if not rows:
        print(f"error: no rows for {args.run_id}-ckpt<steps> in {args.log}", file=sys.stderr)
        return 1
    steps_seen = [steps for steps, _ in rows]
    if len(steps_seen) != len(set(steps_seen)):
        print("error: the same checkpoint is logged more than once", file=sys.stderr)
        return 1
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    with out.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle, lineterminator="\n")
        writer.writerow(["steps", "total_return"])
        writer.writerows(sorted(rows))
    print(f"wrote {out} ({len(rows)} rows)")
    return 0


def baseline_return(log_path: Path, segment: str, fee_rate: float, symbol: str = "BTCUSDT") -> float:
    """流水里 buy_and_hold、指定币、段和费率那一行的 total_return；有多行取最新一行。
    流水里有好几只币的买入持有，不按币筛就会混进别的币。"""
    matches = [
        row for row in read_log(log_path)
        if row["policy"] == "buy_and_hold" and row["symbol"] == symbol and row["segment"] == segment
        and float(row["fee_rate"]) == fee_rate
    ]
    if not matches:
        raise SystemExit(f"error: no buy_and_hold,{symbol},{segment},{fee_rate} row in {log_path}")
    latest = max(matches, key=lambda row: row["timestamp_utc"])
    return float(latest["total_return"])


def command_curve(args: argparse.Namespace) -> int:
    with Path(args.csv).open(newline="", encoding="utf-8") as handle:
        points = sorted((int(row["steps"]), float(row["total_return"])) for row in csv.DictReader(handle))
    if not points:
        print(f"error: {args.csv} has no rows", file=sys.stderr)
        return 1
    hold = baseline_return(Path(args.log), args.segment, args.fee_rate, args.symbol)

    figure, axes = new_figure()
    steps = [step for step, _ in points]
    values = [value for _, value in points]
    axes.plot(steps, values, color=plt.get_cmap("tab10").colors[0], marker="o", linewidth=1.8, label="agent checkpoint")
    for step, value in points:
        axes.annotate(f"{value:.3f}", (step, value), textcoords="offset points", xytext=(0, 8), ha="center", fontsize=9)
    axes.axhline(hold, color="gray", linestyle="--", linewidth=1.4, label=f"buy and hold ({hold:.3f})")
    axes.set_title(f"{args.segment} total_return by checkpoint (fee {args.fee_rate:g})")
    axes.set_xlabel("checkpoint steps")
    axes.set_ylabel(f"{args.segment} total_return")
    axes.legend(loc="best")
    save(figure, Path(args.out))
    return 0


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)

    scalars = sub.add_parser("scalars", help="one PNG per TensorBoard tag over several runs")
    scalars.add_argument("runs", nargs="+", help="results/<run-id> folders")
    scalars.add_argument("--tags", nargs="+", required=True, help="TensorBoard tags, e.g. 'Trading/Trades'")
    scalars.add_argument("--files", nargs="+", help="output file names, one per tag (default: from the tag)")
    scalars.add_argument("--out", required=True, help="output folder")
    scalars.set_defaults(func=command_scalars)

    curve_csv = sub.add_parser("curve-csv", help="steps,total_return rows of one checkpointed run from the log")
    curve_csv.add_argument("log", help="evaluations/log.csv")
    curve_csv.add_argument("--run-id", required=True, help="run id; rows are <run-id>-ckpt<steps>")
    curve_csv.add_argument("--segment", default="validation")
    curve_csv.add_argument("--fee-rate", type=float, default=0.001)
    curve_csv.add_argument("--out", required=True, help="output CSV")
    curve_csv.set_defaults(func=command_curve_csv)

    curve = sub.add_parser("curve", help="checkpoint steps -> return, with buy-and-hold as a dashed line")
    curve.add_argument("csv", help="two columns: steps,total_return")
    curve.add_argument("--log", required=True, help="evaluations/log.csv, for the buy_and_hold row")
    curve.add_argument("--segment", default="validation")
    curve.add_argument("--fee-rate", type=float, default=0.001)
    curve.add_argument("--symbol", default="BTCUSDT", help="whose buy_and_hold row to draw (default: BTCUSDT)")
    curve.add_argument("--out", required=True, help="output PNG")
    curve.set_defaults(func=command_curve)

    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
