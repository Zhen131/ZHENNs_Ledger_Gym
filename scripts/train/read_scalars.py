#!/usr/bin/env python3
"""Print every TensorBoard scalar of one ML-Agents run.

Usage, from the repository root:

    python scripts/train/read_scalars.py results/smoke-mac-01

For each tag: number of points, last step and last value. Uses only the
tensorboard package that mlagents already installs.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from tensorboard.backend.event_processing.event_accumulator import EventAccumulator


def find_event_dirs(run_dir: Path) -> list[Path]:
    """run_dir 下面含有 TensorBoard event 文件的文件夹。"""
    return sorted({path.parent for path in run_dir.rglob("events.out.tfevents.*")})


def read_scalars(event_dir: Path) -> dict[str, list]:
    accumulator = EventAccumulator(str(event_dir), size_guidance={"scalars": 0})
    accumulator.Reload()
    return {tag: accumulator.Scalars(tag) for tag in accumulator.Tags().get("scalars", [])}


def print_scalars(event_dir: Path, run_dir: Path) -> None:
    """先打印 event_dir 的标题，然后每个 tag 一行：点数、最后一个 step、最后一个值。"""
    scalars = read_scalars(event_dir)
    print(f"== {event_dir.relative_to(run_dir.parent) if event_dir != run_dir else event_dir}")
    if not scalars:
        print("   (no scalars)")
        return
    width = max(len(tag) for tag in scalars)
    print(f"   {'tag'.ljust(width)}  {'points':>6}  {'last step':>9}  last value")
    for tag in sorted(scalars):
        points = scalars[tag]
        last = points[-1]
        print(f"   {tag.ljust(width)}  {len(points):>6}  {last.step:>9}  {last.value:.6g}")


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("run_dir", help="an ML-Agents run directory, e.g. results/smoke-mac-01")
    args = parser.parse_args(argv)

    run_dir = Path(args.run_dir)
    if not run_dir.is_dir():
        print(f"error: {run_dir} is not a directory", file=sys.stderr)
        return 1
    event_dirs = find_event_dirs(run_dir)
    if not event_dirs:
        print(f"error: no TensorBoard event files under {run_dir}", file=sys.stderr)
        return 1

    for event_dir in event_dirs:
        print_scalars(event_dir, run_dir)
    return 0


if __name__ == "__main__":
    sys.exit(main())
