# Evaluation log

Commands run from the repository root, with `UNITY` set as in the README's [quick start](../README.md#quick-start-mac).

Every evaluation is one full pass over one evaluation segment: start at the first candle in cash, no randomness, the same fills and fees as in training. Each one appends a row to `evaluations/log.csv` and writes a JSON file with all metrics and settings to `evaluations/runs/`. **The log is append-only**: rows are never edited or removed, and a log whose header differs from the current columns is refused. Running the same evaluation twice adds a second row.

Metrics: total return, maximum drawdown, annualised Sharpe ratio (hourly log returns, √8760), trades, rejected orders, turnover (traded value ÷ average equity), fees (USDT and % of the starting equity) and exposure (share of steps that end holding coin).

## Segments

`gym-config.json` names one training segment and four evaluation segments; `-gymSegment` takes the evaluation segments' names:

| Segment | Dates | BTC over the segment | Used for |
| --- | --- | ---: | --- |
| `train` | 2017-08-17 to 2021-12-31 | | training only |
| `validation-bear` | 2022-01-01 to 2022-12-31 | −64.5 % | choosing models: a falling market |
| `validation-recovery` | 2023-01-01 to 2024-08-31 | +256.8 % | choosing models: the recovery |
| `validation` | 2024-09-01 to 2025-08-31 | +83.7 % | choosing models: a rising market (the only validation segment of round one) |
| `test` | 2025-09-01 to 2026-08-31 | −27.4 % | the final numbers, once |

`validation` and `test` are fields of their own in `gym-config.json`; the other evaluation segments are listed in `extraSegments`, each with a `name`, `start` and `end`. A name may only use lower-case letters, digits and hyphens, must not repeat and must not be `train`, `validation` or `test`; no two segments may overlap, and every segment must lie inside the data. Round one trained on 2017-08-17 to 2024-08-31; its rows in the log keep the dates they were run with.

## Baselines

Six policies, all on the same environment as the agent (same fills, fees and mask):

| `-gymPolicies` name | `policy` in the log | What it does |
| --- | --- | --- |
| `buyhold` | `buy_and_hold` | buys with all its cash at the first step and holds to the end |
| `cash` | `cash` | stays in cash |
| `random` | `random` | picks one of the allowed choices and a fraction at random every step, over seeds 0–99; the row has the medians and the 5th and 95th percentile of the return |
| `momentum` | `momentum_90d` | target position 1 while the close is above the close 2160 hours (90 days) earlier, otherwise 0 |
| `sma` | `sma_cross` | target position 1 while the mean close of the last 168 hours (1 week) is above that of the last 720 hours (30 days), otherwise 0 |
| `voltarget` | `vol_target` | target position min(1, 0.0045 ÷ σ), σ the standard deviation of the last 720 hourly log returns (taken as at least 1e-6) |

The three rules decide every hour from the closes up to the decision candle only (the order fills at the next open, as for the agent). The target position is a share of the equity in coin; without enough history (2160, 720 and 720 hours) the target is 0. With p the coin's share of the equity, a rule buys when the target is more than 0.10 above p, with the fraction min(1, (target − p) × equity ÷ cash) of its cash, sells when it is more than 0.10 below, with the fraction min(1, (p − target) ÷ p) of its coin, and otherwise holds; an order the mask does not allow becomes a hold.

```bash
# Appends to the tracked evaluations/log.csv; run once per segment and fee set
"$UNITY" -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.EvalTools.RunBaselines -gymSegment validation-bear -gymOut "$PWD/evaluations" -quit -logFile "$PWD/unity/Logs/baselines.log"
python scripts/eval/summarize.py evaluations/log.csv
```

Options: `-gymSegment <name>` (**required**: without it nothing is written and the exit code is 1; an unknown name is refused with the list of names), `-gymFeeRates 0,0.001,0.003`, `-gymRandomSeeds 100`, `-gymPolicies buyhold,cash,random,momentum,sma,voltarget` (default: all six), `-gymOut <dir>`, `-gymConfig <file>`. Every other `-gym*` argument is handed on to the configuration loader as given; only `-gymMode` is always `eval`. The log already has all six policies on `validation-bear` and `validation-recovery` and the three rules on `validation` at fees 0, 0.1 % and 0.3 % (buy-and-hold, cash and random on `validation` and `test` are from round one); the rules have not been run on `test`, which is kept for the final evaluation.

A trained model is evaluated in its own build (`BuildMacEval` / `BuildWindowsEval` with `-gymModel`), which records the run id and the model's SHA-256 in every row; `-gymFixedFee` and `-gymSlippage` set the other costs. The evaluation player refuses to start (exit code 1, nothing written) without `-gymMode eval`, `-gymSegment <name>` and `-gymOut <dir>`; it has no default segment, so the test segment is never evaluated by accident. A name that is not an evaluation segment in `gym-config.json` also stops it with exit code 1 before anything is written; the player's log lists the names.

`BuildMacEval` signs `GymEval.app` again after adding `build-info.json`, so a fresh build passes `codesign --verify --deep`. ML-Agents, however, writes its timer file into the app (`Contents/ML-Agents/Timers/`) every time the player runs, which breaks the signature again. It still runs on this Mac; to give the app to another Mac, copy it before its first run, or sign it again first: `codesign --force --deep -s - unity/Builds/mac/GymEval.app`.
