#!/usr/bin/env python3
"""Check the comparison configs in config/variants/ and config/v2/ against their base.

Usage, from the repository root, inside the mlagents environment:

    python scripts/train/check_configs.py

Every *.yaml in config/variants/ and config/v2/ is a variant, unless its file
name starts with "smoke": then it is a smoke config, wherever it is, like
config/smoke*.yaml. A variant's base is config/ppo_base.yaml, unless its first
line declares another one, for example
"# base: config/v2/ppo_v2.yaml — Differs from ppo_v2.yaml only in ..."
(a path from the repository root). Round two's configs in config/v2/ build on
each other that way: ppo_v2 on ppo_base, long and long-ta on ppo_v2, the
long-risk-* files on long.

Rules (exit code 1 if any fails):
- every variant differs from its base in exactly one setting, except the two
  marked "MULTIPLE CHANGES" in their first line (sac-base, teacher-style);
  a declared base must exist;
- smoke configs are not compared with any base (they shorten a run in several
  settings); they only have to pass the rules below;
- every file, including the smoke configs, starts with a comment line saying
  how it differs and why;
- every file has the single behavior TradingAgent, the environment parameters
  fee_rate, fixed_fee and slippage, and torch_settings.device cpu. The one
  exception is smoke-100k-cuda.yaml, the PC's speed test on the graphics card,
  which must say cuda;
- the cost parameters are plain numbers inside the ranges the environment's
  CostModel accepts: fee_rate in [0, 1), slippage in [0, 0.1), fixed_fee >= 0.
  ML-Agents accepts any number, but outside these ranges every episode of the
  training player would fail when it starts;
- the optional environment parameter obs_set (the observation set) is 0 (base),
  1 (long) or 2 (long-ta); any other value stops the training player;
- the optional environment parameter risk_aversion (the reward's risk
  coefficient) is a plain number in [0, 100]. The player accepts any finite
  value >= 0; above 100 the penalty dwarfs the return, so it is almost
  certainly a typo;
- the optional environment parameter action_mode is 0 (orders, rounds one and
  two) or 1 (dial, round three); any other value stops the training player;
- the optional environment parameters of the dial mode are held to round
  three's decision document (22A, section two), which is narrower than what the
  player accepts: decision_hours only 24 (item 5, one decision a day; the
  player takes any whole number >= 1), rebalance_band a plain number in
  [0.02, 0.10] (item 6; the player takes anything inside (0, 0.5)).

If the mlagents package is importable, each file is also parsed with ML-Agents'
own RunOptions, which catches misspelt or misplaced settings.
"""

from __future__ import annotations

import argparse
import glob
import math
import re
import sys
from pathlib import Path

import yaml

MULTIPLE_MARK = "MULTIPLE CHANGES"
# 首行注释里声明基础，例如 "# base: config/v2/ppo_v2.yaml — …"；没声明时和 --base 比。
BASE_DECLARATION = re.compile(r"^#\s*base:\s*(\S+)")
SMOKE_PREFIX = "smoke"
EXPECTED_MULTIPLE = {"sac-base.yaml", "teacher-style.yaml"}
ENV_PARAMS = ("fee_rate", "fixed_fee", "slippage")
# 可选的环境参数：观测集。TradingAgent 只认 0、1、2，别的值让训练玩家报错退出。
# 动作模式：0 = 下单、1 = 旋钮；别的值让训练玩家报错退出。
# 旋钮模式每次决策走几根：22A 第二节第 5 条定死 24（每天一次）；训练玩家收任何正整数，这里按 00A 收紧。
OPTIONAL_ENV_PARAMS = {"obs_set": (0, 1, 2), "action_mode": (0, 1), "decision_hours": (24,)}
# 可选的环境参数：奖励的风险系数。TradingAgent 接受任何有限的非负数；这里有意更严，超过 100 几乎一定是写错。
OPTIONAL_ENV_RANGES = {
    "risk_aversion": (lambda value: 0 <= value <= 100, "in [0, 100]"),
    # 旋钮模式的调仓带宽：22A 第二节第 6 条允许在 2～10 个点之间试；训练玩家收 (0, 0.5)，这里按 00A 收紧。
    "rebalance_band": (lambda value: 0.02 <= value <= 0.10, "in [0.02, 0.10]"),
}
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
    for name, allowed in OPTIONAL_ENV_PARAMS.items():
        if name not in params:
            continue
        value = params[name]
        if isinstance(value, bool) or not isinstance(value, (int, float)) or value not in allowed:
            problems.append(f"environment_parameters.{name} should be one of {list(allowed)}, got {value!r}")
    for name, (in_range, text) in OPTIONAL_ENV_RANGES.items():
        if name not in params:
            continue
        value = params[name]
        if isinstance(value, bool) or not isinstance(value, (int, float)):
            problems.append(f"environment_parameters.{name} should be a plain number, got {value!r}")
        elif not in_range(value):
            problems.append(f"environment_parameters.{name} = {value!r} is outside the allowed range ({text})")
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


def declared_base(first_line: str) -> str | None:
    """首行注释里声明的基础路径；没声明时返回 None。"""
    match = BASE_DECLARATION.match(first_line)
    return match.group(1) if match else None


def check_variant(path: Path, default_base_path: Path, bases: dict) -> tuple[bool, bool]:
    """打印一个 variant 的各项检查（和它声明的基础比，没声明就和默认基础比）；返回（是否失败，是否标成多处改动）。"""
    text = path.read_text()
    first_line = text.split("\n", 1)[0]
    data = yaml.safe_load(text)
    declared = declared_base(first_line)
    base_path = Path(declared) if declared else default_base_path
    problems = []
    base = None
    if declared and base_path.resolve() == path.resolve():
        problems.append(f"declares itself as its base ({declared})")
    elif not base_path.is_file():
        problems.append(f"declared base {declared} not found")
    else:
        key = base_path.resolve()
        if key not in bases:
            bases[key] = yaml.safe_load(base_path.read_text())
        base = bases[key]
    diffs = differences(base, data) if base is not None else []
    multiple = MULTIPLE_MARK in first_line
    problems += common_problems(path, data, first_line)
    if multiple and path.name not in EXPECTED_MULTIPLE:
        problems.append(f"marked {MULTIPLE_MARK!r} but only {sorted(EXPECTED_MULTIPLE)} may be")
    if base is not None and not multiple and len(diffs) != 1:
        problems.append(f"must differ from the base in exactly one setting, found {len(diffs)}")
    if multiple and len(diffs) < 2:
        problems.append(f"marked {MULTIPLE_MARK!r} but differs in {len(diffs)} setting(s)")
    parsed = mlagents_parse(path)
    if parsed not in (None, "skipped"):
        problems.append(f"ML-Agents rejects it: {parsed}")

    status = "FAIL" if problems else "ok"
    kind = "multiple" if multiple else "single"
    print(f"{status:4} {path.as_posix()} ({kind}, {len(diffs)} difference{'s' if len(diffs) != 1 else ''} "
          f"from {base_path.as_posix()}; mlagents {'ok' if parsed is None else parsed})")
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
    print(f"{'FAIL' if problems else 'ok':4} {path.as_posix()} (smoke; mlagents {'ok' if parsed is None else parsed})")
    for problem in problems:
        print(f"     ! {problem}")
    return bool(problems)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--base", default="config/ppo_base.yaml",
                        help="the base of every variant whose first line declares no other")
    parser.add_argument("--variants", nargs="+", default=["config/variants", "config/v2"],
                        help="folders whose *.yaml are variants (files named smoke* in them are smoke configs)")
    parser.add_argument("--smoke", default="config/smoke*.yaml", help="glob of the smoke configs")
    args = parser.parse_args(argv)

    base_path = Path(args.base)
    base_text = base_path.read_text()
    base = yaml.safe_load(base_text)
    failed = check_base(base_path, base, base_text)

    # 文件名以 smoke 开头的一律按冒烟配置查，不论在哪个文件夹；其余的是变体。
    variants, smoke = [], []
    for folder in args.variants:
        found = sorted(Path(folder).glob("*.yaml"))
        if not found:
            print(f"FAIL no configs in {folder}")
            return 1
        for path in found:
            (smoke if path.name.startswith(SMOKE_PREFIX) else variants).append(path)
    bases = {base_path.resolve(): base}
    seen_multiple = set()
    for path in variants:
        variant_failed, multiple = check_variant(path, base_path, bases)
        if multiple:
            seen_multiple.add(path.name)
        failed |= variant_failed

    smoke_glob = sorted(Path(p) for p in glob.glob(args.smoke))
    if not smoke_glob:
        print(f"FAIL no smoke configs match {args.smoke}")
        failed = True
    in_glob = {path.resolve() for path in smoke_glob}
    smoke = smoke_glob + [path for path in smoke if path.resolve() not in in_glob]
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
