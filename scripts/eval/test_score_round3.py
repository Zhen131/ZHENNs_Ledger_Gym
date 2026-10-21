"""Tests for score_round3.py. Run from the repository root:

    python -m unittest discover -s scripts/eval

Hand-written logs cover the threshold edges after rounding to four decimals, a missing
segment, the latest row, a run id the pattern does not match, the random-row half of
"did not learn" and an opponent's score. One test reads the real evaluations/log.csv
(found from this file's place in the repository) and fails if constant_mix_50's two
rows are missing or no longer pass both thresholds.
"""

from __future__ import annotations

import csv
import io
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import score_round3 as score  # noqa: E402

COLUMNS = [
    "timestamp_utc", "kind", "policy", "model_run_id", "model_sha256", "symbol", "segment", "segment_start",
    "segment_end", "fee_rate", "fixed_fee", "slippage", "seeds", "total_return", "max_drawdown", "sharpe",
    "trades", "rejected", "turnover", "fees_paid", "fees_pct", "exposure", "notes",
]
REAL_LOG = Path(__file__).resolve().parents[2] / "evaluations/log.csv"


def row(segment, total_return, max_drawdown, run_id="m-s1-20261101", kind="agent", policy="agent", trades="10",
        notes="deterministic=True; device=Burst; steps=8759; obs=long; mode=orders", time="2026-11-01T00:00:00.000Z",
        symbol="BTCUSDT", fee="0.001"):
    return {
        "timestamp_utc": time, "kind": kind, "policy": policy, "model_run_id": run_id if kind == "agent" else "",
        "model_sha256": "", "symbol": symbol, "segment": segment, "segment_start": "", "segment_end": "",
        "fee_rate": fee, "fixed_fee": "0", "slippage": "0", "seeds": "1", "total_return": total_return,
        "max_drawdown": max_drawdown, "sharpe": "0", "trades": trades, "rejected": "0", "turnover": "0",
        "fees_paid": "0", "fees_pct": "0", "exposure": "1", "notes": notes,
    }


def model(run_id, bear=("-0.30", "0.30"), recovery=("0.50", "0.20"), validation=("0.50", "0.20"), **kwargs):
    """一个模型在三段上的三行（收益, 回撤）。"""
    return [row("validation-bear", *bear, run_id=run_id, **kwargs),
            row("validation-recovery", *recovery, run_id=run_id, **kwargs),
            row("validation", *validation, run_id=run_id, **kwargs)]


def randoms(bear="-0.90", recovery="-0.80", validation="-0.70"):
    return [row(s, r, "0.9", kind="baseline", policy="random", notes="seeds mixed")
            for s, r in (("validation-bear", bear), ("validation-recovery", recovery), ("validation", validation))]


def as_log(rows):
    """写成真正的 CSV 再读回来（带 _line），和读文件走同一条路。"""
    handle = io.StringIO()
    writer = csv.DictWriter(handle, fieldnames=COLUMNS, lineterminator="\n")
    writer.writeheader()
    writer.writerows(rows)
    with tempfile.TemporaryDirectory() as folder:
        path = Path(folder) / "log.csv"
        path.write_text(handle.getvalue(), encoding="utf-8")
        return score.read_log(path)


def entries(rows, **kwargs):
    models, baselines, rand = score.build_entries(as_log(rows), kwargs.get("symbol", "BTCUSDT"), kwargs.get("fee", 0.001),
                                                  kwargs.get("prefixes"))
    return {e.name: e for e in models}, {e.name: e for e in baselines}, rand


class ThresholdTests(unittest.TestCase):
    def test_the_numbers_come_from_22A_and_appear_once_in_the_script(self):
        self.assertEqual(str(score.MAX_BEAR_DRAWDOWN), "0.4132")
        self.assertEqual(str(score.MIN_VALIDATION_RETURN), "0.3901")
        source = Path(score.__file__).read_text(encoding="utf-8")
        self.assertEqual(source.count("0.4132"), 1)
        self.assertEqual(source.count("0.3901"), 1)
        above = source.split("\nMAX_BEAR_DRAWDOWN =")[0].splitlines()[-2:]
        self.assertIn("22A", " ".join(above), "the comment above the constants names where they come from")

    def test_a_value_that_rounds_to_the_threshold_passes_and_one_more_in_the_last_place_fails(self):
        # 门槛一（回撤 ≤ 0.4132）：0.41324 四舍五入成 0.4132，过；0.41325 成 0.4133，不过
        self.assertTrue(score.passes_bear("0.4132"))
        self.assertTrue(score.passes_bear("0.41324999"))
        self.assertTrue(score.passes_bear("0.41320517404320689"))
        self.assertFalse(score.passes_bear("0.41325"))
        self.assertFalse(score.passes_bear("0.4133"))
        # 门槛二（收益 ≥ 0.3901）：0.39005 成 0.3901，过；0.39004999 成 0.3900，不过
        self.assertTrue(score.passes_validation("0.3901"))
        self.assertTrue(score.passes_validation("0.39005"))
        self.assertTrue(score.passes_validation("0.39013319485813991"))
        self.assertFalse(score.passes_validation("0.39004999"))
        self.assertFalse(score.passes_validation("0.39"))

    def test_the_thresholds_decide_who_is_ranked(self):
        models, _, _ = entries(
            model("edge-s1-20261101", bear=("-0.3", "0.41324"), validation=("0.39005", "0.2"))
            + model("bear-s1-20261101", bear=("-0.3", "0.41325"), validation=("0.9", "0.2"))
            + model("bull-s1-20261101", bear=("-0.3", "0.2"), validation=("0.39004999", "0.2")))
        self.assertTrue(models["edge-s1-20261101"].passed)
        self.assertFalse(models["bear-s1-20261101"].pass_bear)
        self.assertTrue(models["bear-s1-20261101"].pass_validation)
        self.assertTrue(models["bull-s1-20261101"].pass_bear)
        self.assertFalse(models["bull-s1-20261101"].pass_validation)
        self.assertEqual(score.status_text(models["bear-s1-20261101"]), "out: threshold one")
        self.assertEqual(score.status_text(models["bull-s1-20261101"]), "out: threshold two")


class SelectionTests(unittest.TestCase):
    def test_a_model_missing_a_segment_is_listed_but_not_ranked(self):
        rows = model("full-s1-20261101") + model("part-s1-20261101")[:2]
        models, _, _ = entries(rows)
        part = models["part-s1-20261101"]
        self.assertFalse(part.complete)
        self.assertEqual(part.missing, ["validation"])
        self.assertIsNone(part.score)
        self.assertFalse(part.passed)
        self.assertEqual(score.status_text(part), "missing segment: validation")
        text = score.report(as_log(rows), "BTCUSDT", 0.001, None)
        self.assertIn("`part-s1-20261101`", text)
        self.assertIn("missing segment: validation", text)
        self.assertIn("past both thresholds: `full-s1-20261101`", text)

    def test_the_latest_row_of_each_segment_counts_and_on_a_tie_the_later_one_in_the_file(self):
        rows = model("m-s1-20261101", time="2026-11-02T00:00:00.000Z")
        # 文件里靠后、时间却更早的一行：不算
        rows.append(row("validation", "0.10", "0.20", run_id="m-s1-20261101", time="2026-11-01T00:00:00.000Z"))
        # 同一时间的两行：取文件里靠后的
        rows.append(row("validation-bear", "-0.20", "0.25", run_id="m-s1-20261101", time="2026-11-02T00:00:00.000Z"))
        models, _, _ = entries(rows)
        m = models["m-s1-20261101"]
        self.assertEqual(m.rows["validation"]["total_return"], "0.50")
        self.assertEqual(m.rows["validation-bear"]["max_drawdown"], "0.25")

    def test_only_this_symbol_and_fee_count_and_the_fee_is_compared_as_a_number(self):
        rows = model("m-s1-20261101", fee="0.0010") + model("m-s1-20261101", fee="0.003", validation=("0.10", "0.2"),
                                                          time="2026-11-09T00:00:00.000Z")
        rows += model("m-s1-20261101", symbol="ETHUSDT", validation=("0.05", "0.2"), time="2026-11-09T00:00:00.000Z")
        models, _, _ = entries(rows)
        self.assertEqual(models["m-s1-20261101"].rows["validation"]["total_return"], "0.50")

    def test_the_runs_option_keeps_models_whose_run_id_starts_with_a_prefix(self):
        rows = model("long-s1-20261101") + model("base-s1-20261101")
        models, _, _ = entries(rows, prefixes=["long-"])
        self.assertEqual(list(models), ["long-s1-20261101"])

    def test_scores_rank_the_models_past_both_thresholds(self):
        rows = (model("a-s1-20261101", validation=("0.60", "0.20"))      # 得分 (−1 + 2.5 + 3) / 3 = 1.5
                + model("b-s1-20261101", validation=("0.90", "0.20"))    # (−1 + 2.5 + 4.5) / 3 = 2
                + model("c-s1-20261101", bear=("-0.3", "0.6"), validation=("2.0", "0.2")))  # 门槛一不过
        models, _, _ = entries(rows)
        self.assertAlmostEqual(models["a-s1-20261101"].score, 1.5)
        self.assertAlmostEqual(models["b-s1-20261101"].score, 2.0)
        order = [e.name for e in score.ordered(list(models.values()))]
        self.assertEqual(order, ["b-s1-20261101", "a-s1-20261101", "c-s1-20261101"])
        text = score.report(as_log(rows), "BTCUSDT", 0.001, None)
        self.assertIn("Highest score among the models past both thresholds: `b-s1-20261101` (score 2.0000).", text)

    def test_a_drawdown_below_one_percent_counts_as_one_percent(self):
        self.assertAlmostEqual(score.segment_score("0.05", "0.001"), 5.0)
        self.assertAlmostEqual(score.segment_score("0", "0"), 0.0)
        self.assertAlmostEqual(score.segment_score("0.5", "0.25"), 2.0)


class ConfigurationTests(unittest.TestCase):
    def test_the_configuration_is_the_run_id_without_seed_and_date_and_others_stand_alone(self):
        self.assertEqual(score.configuration_of("long-risk-0.01-s3-20261020"), ("long-risk-0.01", True))
        self.assertEqual(score.configuration_of("ppo_base-curve-s1-20261019-ckpt1199936"),
                         ("ppo_base-curve-s1-20261019-ckpt1199936", False))
        self.assertEqual(score.configuration_of("smoke"), ("smoke", False))

    def test_the_configuration_score_averages_every_seed_and_counts_those_past_the_thresholds(self):
        rows = (model("c-s1-20261101", validation=("0.60", "0.20"))                         # 1.5，过
                + model("c-s2-20261101", bear=("-0.3", "0.6"), validation=("0.60", "0.20"))  # 门槛一不过
                + model("odd-run", validation=("0.60", "0.20")))
        text = score.report(as_log(rows), "BTCUSDT", 0.001, None)
        failing = (-0.3 / 0.6 + 2.5 + 3.0) / 3
        expected = round((1.5 + failing) / 2, 4)
        self.assertIn(f"| `c` | 1 / 2 | {expected:.4f} |", text)
        self.assertIn("| `odd-run` | 1 / 1 |", text)
        self.assertIn("run id does not match -s<seed>-<yyyymmdd>", text)


class DidNotLearnTests(unittest.TestCase):
    def test_an_order_model_below_random_on_a_segment_traded_at_random(self):
        rows = model("m-s1-20261101", recovery=("-0.85", "0.9")) + randoms(recovery="-0.80")
        models, _, rand = entries(rows)
        self.assertEqual(score.did_not_learn(models["m-s1-20261101"], rand), "yes (below random on validation-recovery)")

    def test_an_order_model_above_random_with_trades_learned_something(self):
        models, _, rand = entries(model("m-s1-20261101") + randoms())
        self.assertEqual(score.did_not_learn(models["m-s1-20261101"], rand), "no")

    def test_an_order_model_with_no_trades_on_all_three_segments_did_not_learn(self):
        models, _, rand = entries(model("m-s1-20261101", trades="0") + randoms())
        self.assertEqual(score.did_not_learn(models["m-s1-20261101"], rand), "yes (no trades)")

    def test_the_latest_random_row_is_the_one_compared(self):
        rows = model("m-s1-20261101", validation=("-0.60", "0.7")) + randoms(validation="-0.50")
        rows.append(row("validation", "-0.70", "0.9", kind="baseline", policy="random", time="2026-11-05T00:00:00.000Z"))
        models, _, rand = entries(rows)
        self.assertEqual(score.did_not_learn(models["m-s1-20261101"], rand), "no")

    def test_an_old_row_without_mode_is_an_order_model(self):
        models, _, rand = entries(model("m-s1-20261101", notes="deterministic=True; device=Burst; steps=8759; obs=base",
                                        trades="0") + randoms())
        self.assertEqual(models["m-s1-20261101"].mode, "orders")
        self.assertEqual(score.did_not_learn(models["m-s1-20261101"], rand), "yes (no trades)")

    def test_a_dial_model_that_never_changed_level_did_not_learn(self):
        dial = "deterministic=True; device=Burst; steps=365; obs=long; mode=dial; band=0.05; hours=24; changes={}"
        rows = [row("validation-bear", "-0.3", "0.3", run_id="d-s1-20261101", notes=dial.format(0)),
                row("validation-recovery", "0.5", "0.2", run_id="d-s1-20261101", notes=dial.format(0)),
                row("validation", "0.5", "0.2", run_id="d-s1-20261101", notes=dial.format(0)),
                row("validation-bear", "-0.3", "0.3", run_id="e-s1-20261101", notes=dial.format(0), trades="0"),
                row("validation-recovery", "0.5", "0.2", run_id="e-s1-20261101", notes=dial.format(3), trades="0"),
                row("validation", "0.5", "0.2", run_id="e-s1-20261101", notes=dial.format(0), trades="0")]
        models, _, rand = entries(rows)
        self.assertEqual(models["d-s1-20261101"].mode, "dial")
        self.assertEqual(score.did_not_learn(models["d-s1-20261101"], rand), "yes (never changed level)")
        self.assertEqual(models["d-s1-20261101"].changes_text(), "0/0/0")
        self.assertEqual(score.did_not_learn(models["e-s1-20261101"], rand), "no", "trades do not matter in dial mode")
        self.assertEqual(models["e-s1-20261101"].changes_text(), "0/3/0")


class OpponentTests(unittest.TestCase):
    def test_an_opponent_gets_the_same_thresholds_and_score_and_its_level_changes(self):
        notes = "mode=dial; band=0.05; hours=24; changes={}"
        rows = [row("validation-bear", "-0.38", "0.40", kind="baseline", policy="dial_momentum_90d", notes=notes.format(14)),
                row("validation-recovery", "1.00", "0.20", kind="baseline", policy="dial_momentum_90d", notes=notes.format(36)),
                row("validation", "0.40", "0.16", kind="baseline", policy="dial_momentum_90d", notes=notes.format(11)),
                row("validation", "0.80", "0.30", kind="baseline", policy="buy_and_hold", notes="")]
        _, baselines, _ = entries(rows)
        mom = baselines["dial_momentum_90d"]
        self.assertTrue(mom.passed)
        self.assertAlmostEqual(mom.score, (-0.38 / 0.40 + 1.00 / 0.20 + 0.40 / 0.16) / 3)
        self.assertEqual(mom.changes_text(), "14/36/11")
        hold = baselines["buy_and_hold"]
        self.assertFalse(hold.complete)
        text = score.report(as_log(rows), "BTCUSDT", 0.001, None)
        self.assertIn("| dial_momentum_90d | -0.3800 | 0.4000 | 1.0000 | 0.2000 | 0.4000 | 0.1600 | pass | pass | 2.1833 | 14/36/11 |",
                      text)
        self.assertIn("| buy_and_hold | - | - | - | - | 0.8000 | 0.3000 | - | pass | - | - | missing segment:", text)


class RealLogTests(unittest.TestCase):
    """读真实流水：门槛以后被谁悄悄改了、或者对手的行不见了，这条就红（23D 建议 4）。"""

    def test_constant_mix_50_passes_both_thresholds_in_the_real_log(self):
        self.assertTrue(REAL_LOG.is_file(), f"{REAL_LOG} is missing")
        rows = score.read_log(REAL_LOG)

        def latest_row(segment):
            found = [r for r in rows if r["kind"] == "baseline" and r["policy"] == "constant_mix_50"
                     and r["symbol"] == "BTCUSDT" and float(r["fee_rate"]) == 0.001 and r["segment"] == segment]
            self.assertTrue(found, f"no constant_mix_50 row on BTCUSDT {segment} at fee 0.001 in {REAL_LOG}")
            return score.latest(found)

        bear, validation = latest_row("validation-bear"), latest_row("validation")
        self.assertTrue(score.passes_bear(bear["max_drawdown"]), f"line {bear['_line']}: {bear['max_drawdown']}")
        self.assertTrue(score.passes_validation(validation["total_return"]),
                        f"line {validation['_line']}: {validation['total_return']}")
        _, baselines, _ = score.build_entries(rows, "BTCUSDT", 0.001, None)
        mix = {e.name: e for e in baselines}["constant_mix_50"]
        self.assertTrue(mix.complete)
        self.assertTrue(mix.passed)


if __name__ == "__main__":
    unittest.main()
