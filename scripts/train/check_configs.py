#!/usr/bin/env python3
"""Check the comparison configs in config/variants/ against config/ppo_base.yaml.

Usage, from the repository root, inside the mlagents environment:

    python scripts/train/check_configs.py

Rules (exit code 1 if any fails):
- every variant differs from the base in exactly one setting, except the two
  marked "MULTIPLE CHANGES" in their first line (sac-base, teacher-style);
- every file, including the smoke configs (config/smoke*.yaml), starts with a
  comment line saying how it differs and why;
- every file has the single behavior TradingAgent, the environment parameters
  fee_rate, fixed_fee and slippage, and torch_settings.device cpu. The one
  exception is smoke-100k-cuda.yaml, the PC's speed test on the graphics card,
  which must say cuda;
- the cost parameters are plain numbers inside the ranges the environment's
  CostModel accepts: fee_rate in [0, 1), slippage in [0, 0.1), fixed_fee >= 0.
  ML-Agents accepts any number, but outside these ranges every episode of the
  training player would fail when it starts.

If the mlagents package is importable, each file is also parsed with ML-Agents'
own RunOptions, which catches misspelt or misplaced settings.
"""

from __future__ import annotations

import argparse
import glob
import math
import sys
from pathlib import Path

import yaml

MULTIPLE_MARK = "MULTIPLE CHANGES"
EXPECTED_MULTIPLE = {"sac-base.yaml", "teacher-style.yaml"}
ENV_PARAMS = ("fee_rate", "fixed_fee", "slippage")
# 除了 PC 上的显卡测速配置，每份配置都用 CPU 训练；那份测速配置就是为了比较两者而存在的。
# 成批训练脚本的 --device 只改它们在 results/_tmp/ 里的副本，从不改这些文件。
DEVICE_EXCEPTIONS = {"smoke-100k-cuda.yaml": "cuda"}
# Gym.Core.Accounting.CostModel 强制的取值范围。
COST_RANGES = {
    "fee_rate": (lambda value: 0 <= value < 1, "in [0, 1)"),
    "fixed_fee": (lambda value: value >= 0 and math.isfinite(value), ">= 0"),
    "slippage": (lambda value: 0 <= value < 0.1, "in [0, 0.1)"),
}


def flatten(value, prefix=""):
    """{'a': {'b': 1}} -> {'a.b': 1}"""
    if isinstance(value, dict):
        out = {}
        for key, item in value.items():
            out.update(flatten(item, f"{prefix}.{key}" if prefix else str(key)))
        return out
    return {prefix: value}


def differences(base: dict, other: dict) -> list[str]:
    base_flat, other_flat = flatten(base), flatten(other)
    diffs = []
    for key in sorted(set(base_flat) | set(other_flat)):
        if key not in other_flat:
            diffs.append(f"{key}: {base_flat[key]!r} -> (removed)")
        elif key not in base_flat:
            diffs.append(f"{key}: (added) -> {other_flat[key]!r}")
        elif base_flat[key] != other_flat[key]:
            diffs.append(f"{key}: {base_flat[key]!r} -> {other_flat[key]!r}")
    return diffs


def common_problems(path: Path, data: dict, first_line: str) -> list[str]:
    problems = []
    if not first_line.startswith("#"):
        problems.append("first line is not a comment explaining the difference")
    behaviors = (data or {}).get("behaviors") or {}
    if list(behaviors) != ["TradingAgent"]:
        problems.append(f"behaviors should be exactly ['TradingAgent'], got {list(behaviors)}")
    params = (data or {}).get("environment_parameters") or {}
    missing = [p for p in ENV_PARAMS if p not in params]
    if missing:
        problems.append(f"environment_parameters is missing {missing}")
    for name, (in_range, text) in COST_RANGES.items():
        if name not in params:
            continue
        value = params[name]
        if isinstance(value, bool) or not isinstance(value, (int, float)):
            problems.append(f"environment_parameters.{name} should be a plain number, got {value!r}")
        elif not in_range(value):
            problems.append(f"environment_parameters.{name} = {value!r} is outside the range CostModel accepts ({text})")
    device = ((data or {}).get("torch_settings") or {}).get("device")
    expected_device = DEVICE_EXCEPTIONS.get(path.name, "cpu")
    if device != expected_device:
        problems.append(f"torch_settings.device should be {expected_device!r}, got {device!r}")
    return problems


def mlagents_parse(path: Path):
    """ML-Agents 接受这个文件时返回 None，不接受时返回报错信息，没装 mlagents 时返回 'skipped'。"""
    try:
        from mlagents.plugins.trainer_type import register_trainer_plugins
        from mlagents.trainers.settings import RunOptions
    except Exception:
        return "skipped"
    try:
        register_trainer_plugins()  # mlagents-learn 在解析之前也是这样注册 ppo/sac/poca 的
        RunOptions.from_dict(yaml.safe_load(path.read_text()))
        return None
    except Exception as error:  # noqa: BLE001 - 报告 ML-Agents 拒绝的任何问题
        return f"{type(error).__name__}: {error}"


def check_base(base_path: Path, base: dict, base_text: str) -> bool:
    """打印基础配置的各项检查；有任何一项失败时返回 True。"""
    failed = False
    print(f"base: {base_path}")
    for problem in common_problems(base_path, base, base_text.split("\n", 1)[0]):
        print(f"  FAIL {problem}")
        failed = True
    parsed = mlagents_parse(base_path)
    print(f"  mlagents RunOptions: {'ok' if parsed is None else parsed}")
    failed |= parsed not in (None, "skipped")
    return failed


def check_variant(path: Path, base: dict) -> tuple[bool, bool]:
    """打印一个 variant 的各项检查；返回（是否失败，是否标成多处改动）。"""
    text = path.read_text()
    first_line = text.split("\n", 1)[0]
    data = yaml.safe_load(text)
    diffs = differences(base, data)
    multiple = MULTIPLE_MARK in first_line
    problems = common_problems(path, data, first_line)
    if multiple and path.name not in EXPECTED_MULTIPLE:
        problems.append(f"marked {MULTIPLE_MARK!r} but only {sorted(EXPECTED_MULTIPLE)} may be")
    if not multiple and len(diffs) != 1:
        problems.append(f"must differ from the base in exactly one setting, found {len(diffs)}")
    if multiple and len(diffs) < 2:
        problems.append(f"marked {MULTIPLE_MARK!r} but differs in {len(diffs)} setting(s)")
    parsed = mlagents_parse(path)
    if parsed not in (None, "skipped"):
        problems.append(f"ML-Agents rejects it: {parsed}")

    status = "FAIL" if problems else "ok"
    kind = "multiple" if multiple else "single"
    print(f"{status:4} {path.name} ({kind}, {len(diffs)} difference{'s' if len(diffs) != 1 else ''}; "
          f"mlagents {'ok' if parsed is None else parsed})")
    for diff in diffs:
        print(f"       {diff}")
    for problem in problems:
        print(f"     ! {problem}")
    return bool(problems), multiple


def check_smoke(path: Path) -> bool:
    """打印一份冒烟配置的各项检查；有任何一项失败时返回 True。"""
    text = path.read_text()
    problems = common_problems(path, yaml.safe_load(text), text.split("\n", 1)[0])
    parsed = mlagents_parse(path)
    if parsed not in (None, "skipped"):
        problems.append(f"ML-Agents rejects it: {parsed}")
    print(f"{'FAIL' if problems else 'ok':4} {path.name} (smoke; mlagents {'ok' if parsed is None else parsed})")
    for problem in problems:
        print(f"     ! {problem}")
    return bool(problems)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--base", default="config/ppo_base.yaml")
    parser.add_argument("--variants", default="config/variants")
    parser.add_argument("--smoke", default="config/smoke*.yaml", help="glob of the smoke configs")
    args = parser.parse_args(argv)

    base_path = Path(args.base)
    base_text = base_path.read_text()
    base = yaml.safe_load(base_text)
    failed = check_base(base_path, base, base_text)

    variants = sorted(Path(args.variants).glob("*.yaml"))
    if not variants:
        print(f"FAIL no variants in {args.variants}")
        return 1
    seen_multiple = set()
    for path in variants:
        variant_failed, multiple = check_variant(path, base)
        if multiple:
            seen_multiple.add(path.name)
        failed |= variant_failed

    smoke = sorted(Path(p) for p in glob.glob(args.smoke))
    if not smoke:
        print(f"FAIL no smoke configs match {args.smoke}")
        failed = True
    for path in smoke:
        failed |= check_smoke(path)

    missing_multiple = EXPECTED_MULTIPLE - seen_multiple
    if missing_multiple:
        print(f"FAIL expected multi-change variants not found or not marked: {sorted(missing_multiple)}")
        failed = True

    print("FAILED" if failed else f"all {len(variants)} variants and {len(smoke)} smoke configs ok")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
