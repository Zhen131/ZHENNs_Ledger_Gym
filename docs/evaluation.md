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

`validation` and `test` are fields of their own in `gym-config.json`; the other evaluation segments are listed in `extraSegments`, each with a `name`, `start` and `end`. A name may only use lower-case letters, digits and hyphens, must not repeat and must not be `train`, `validation` or `test`; no two segments may overlap, and every segment must lie inside the data of every symbol. Round one trained on 2017-08-17 to 2024-08-31; its rows in the log keep the dates they were run with.

## Symbols

`gym-config.json` names a main symbol (`symbol` and `dataFile`, BTCUSDT) and lists every symbol it knows in `symbols`, each with its `dataFile` and `evalOnly`: BTCUSDT, ETHUSDT, ADAUSDT, DOGEUSDT (`evalOnly`: only evaluated, never trained on) and XRPUSDT. `-gymSymbol <name>` picks the current symbol for the evaluation player, the baseline tool, the Play scene and the Watch scene (and their checklist and snapshot tools); without it the current symbol is the main one, and a name that is not in `symbols` is refused (exit code 1, nothing written) with the list of names. The segments have the same dates for every symbol; the evaluation segments must lie inside every symbol's data, and a training segment that starts before a symbol's first candle starts on that day instead (a line in the log, not a warning). The `symbol` column of the log and the `symbol` and `data_file` of every details file are the current symbol's. `python scripts/eval/summarize.py` makes one table per symbol, segment and cost setting.

Buy-and-hold at a fee of 0.1 % (`total_return` from the log):

| Symbol | `validation-bear` | `validation-recovery` | `validation` |
| --- | ---: | ---: | ---: |
| BTCUSDT | −64.58 % | +256.42 % | +83.50 % |
| ETHUSDT | −67.90 % | +110.24 % | +75.12 % |
| ADAUSDT | −81.54 % | +40.91 % | +135.40 % |
| DOGEUSDT | −59.17 % | +44.96 % | +111.15 % |
| XRPUSDT | −59.68 % | +67.10 % | +390.61 % |

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

Round three adds two opponents for the dial mode, defined in `Core/Evaluation/Baselines.cs` (`RunConstantMix50`, `RunDialMomentum`). They are not among the six that run by default: name them with `-gymPolicies constmix,dialmom`.

| `-gymPolicies` name | `policy` in the log | What it does |
| --- | --- | --- |
| `constmix` | `constant_mix_50` | the dial always at the 50 % level |
| `dialmom` | `dial_momentum_90d` | once a day, the 70 % level while the close is above the close 2160 hours (90 days) earlier, otherwise the 30 % level; also 30 % without 2160 hours of history |

Both run on the dial mode's mechanics: a decision every 24 candles and the rebalancing rule with a band of 0.05, levels 0.3, 0.5 and 0.7, starting in cash at the first candle of the segment. These parameters are the opponents' own, fixed constants (`OpponentBand`, `OpponentDecisionHours`, `OpponentLevels`, `DialMomentumLookback`, from round three's decision document): the opponents read neither `gym-config.json` nor environment parameters, so changing the model's band or levels does not move them. Each builds its own dial environment on the coin, candles, segment, cash and observation set of the environment it is given, so the call is the same as for the other rules; the fee comes from the caller.

An opponent's row has the notes `mode=dial; band=0.05; hours=24; changes=<k>` (k = how often it changed level; `constant_mix_50` never does), and its details file has the same seven action keys as an agent's (see [Action mode](#action-mode)); `dial_momentum_90d`'s also has `lookback` (2160). Like the three rules, the rebalancing rule inside a decision looks at every candle's close and may trade on any candle; only the level is chosen once a day.

The three rules decide every hour from the closes up to the decision candle only (the order fills at the next open, as for the agent). The target position is a share of the equity in coin; without enough history (2160, 720 and 720 hours) the target is 0. With p the coin's share of the equity, a rule buys when the target is more than 0.10 above p, with the fraction min(1, (target − p) × equity ÷ cash) of its cash, sells when it is more than 0.10 below, with the fraction min(1, (p − target) ÷ p) of its coin, and otherwise holds; an order the mask does not allow becomes a hold.

```bash
# Appends to the tracked evaluations/log.csv; run once per symbol, segment and fee set
"$UNITY" -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.EvalTools.RunBaselines -gymSegment validation-bear -gymOut "$PWD/evaluations" -quit -logFile "$PWD/unity/Logs/baselines.log"
"$UNITY" -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.EvalTools.RunBaselines -gymSymbol ETHUSDT -gymSegment validation-bear -gymFeeRates 0.001 -gymOut "$PWD/evaluations" -quit -logFile "$PWD/unity/Logs/baselines-ETHUSDT-validation-bear.log"
# The two opponents, fee 0.1 % only
"$UNITY" -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.EvalTools.RunBaselines -gymSymbol BTCUSDT -gymSegment validation-bear -gymPolicies constmix,dialmom -gymFeeRates 0.001 -gymOut "$PWD/evaluations" -quit -logFile "$PWD/unity/Logs/baselines-opponents.log"
python scripts/eval/summarize.py evaluations/log.csv
```

Options: `-gymSegment <name>` (**required**: without it nothing is written and the exit code is 1; an unknown name is refused with the list of names), `-gymSymbol <name>` (default: the main symbol; one not in `symbols` is refused the same way), `-gymFeeRates 0,0.001,0.003`, `-gymRandomSeeds 100`, `-gymPolicies buyhold,cash,random,momentum,sma,voltarget,constmix,dialmom` (default: the first six; the two opponents run only when named), `-gymOut <dir>`, `-gymConfig <file>`. Every other `-gym*` argument is handed on to the configuration loader as given; only `-gymMode` is always `eval`. For BTCUSDT the log already has all six policies on `validation-bear` and `validation-recovery` and the three rules on `validation` at fees 0, 0.1 % and 0.3 % (buy-and-hold, cash and random on `validation` and `test` are from round one); for ETHUSDT, ADAUSDT, DOGEUSDT and XRPUSDT it has all six policies on the three validation segments at a fee of 0.1 % only. The two opponents are in the log for all five symbols on the three validation segments, and for BTCUSDT on `test`, at a fee of 0.1 % only (batch 24, lines 543 to 574). On `test` the log has round one's buy-and-hold, cash and random rows for BTCUSDT; round two's final evaluation (the agents `normalize-on-s3-20261019` and `ppo_v2-s3-20261020`, the three rules for BTCUSDT and buy-and-hold for the other four symbols, fee 0.1 %); and the two opponents. Rules are not trained, so running them on `test` gives nothing away; a model is evaluated on `test` once, at the end of its round.

A trained model is evaluated in its own build (`BuildMacEval` / `BuildWindowsEval` with `-gymModel`), which records the run id and the model's SHA-256 in every row; `-gymFixedFee` and `-gymSlippage` set the other costs, and `-gymSymbol` the coin. The evaluation player refuses to start (exit code 1, nothing written) without `-gymMode eval`, `-gymSegment <name>` and `-gymOut <dir>`; it has no default segment, so the test segment is never evaluated by accident. A name that is not an evaluation segment in `gym-config.json` also stops it with exit code 1 before anything is written; the player's log lists the names.

`-gymRiskAversion` (the reward's risk coefficient, see [architecture.md](architecture.md#observation-action-reward)) changes no metric: the evaluation measures the equity, not the reward. The evaluation player and the baseline tool still check it like the costs (a negative value stops them with exit code 1 before anything is written); the baselines always run with 0.

## Action mode

`-gymActionMode orders|dial` chooses the action mode for the evaluation player and the Play scene, `-gymDecisionHours <n>` and `-gymBand <x>` the dial mode's candles per decision and band; without them they use `actionMode`, `decisionHours` and `rebalanceBand` in `gym-config.json` (`orders`, 24, 0.05). A name that is not a mode, a number of candles that is not a whole number ≥ 1, or a band outside (0, 0.5) is refused with exit code 1 before anything is written. The baseline tool hands these switches on like every other `-gym*` argument; the six policies above always run in the order mode, and the two opponents use their own fixed parameters. The Play scene does not support the dial mode yet: it stops with an error.

### The model's action shape

A model only fits the action mode it was trained with: an order-mode model has 1 continuous action and one branch of 3 (`1 continuous + [3]`), a dial-mode model with three levels has none and one branch of 3 (`0 continuous + [3]`). `BuildEval` reads the shape from the model's outputs `continuous_action_output_shape` and `discrete_action_output_shape` (one run of the model on zero inputs, on the CPU; a missing output counts as 0, because ML-Agents does not export the continuous one when there are no continuous actions; with both missing the build fails) and writes `continuous_actions`, `discrete_branches` and `action_shape_recorded: true` to `build-info.json`. Before the first step the evaluation player compares it with the shape its action mode needs (`ActionShape.For`, from `ActionCodec`) and, when they differ, exits with code 1 and writes nothing, giving both shapes, for example `the model's actions are 1 continuous + [3] but the action mode dial with 3 levels needs 0 continuous + [3]`. A build made before this check (no `action_shape_recorded`) is not checked, with a warning in the log; `continuous_actions: 0` alone does not mean an old build, since it is the dial mode's normal value. The **Gym > Watch a Model...** menu and the Watch scene make the same check against `actionMode` of `gym-config.json` (or `-gymActionMode`).

### What an evaluation records about the action mode

The details file of an agent evaluation (and of an opponent) has `action_mode` (`orders` or `dial`), `decision_hours`, `rebalance_band`, `dial_levels`, `dial_changes`, `decisions` and `last_decision_hours`. In the order mode `decision_hours` is 1, the band `null`, the levels empty, `dial_changes` 0 and `decisions` the number of steps. The `notes` of the agent's row end with `; mode=orders`, or with `; mode=dial; band=<x>; hours=<n>; changes=<k>`, in this order, for example `deterministic=True; device=Burst; steps=365; obs=long; mode=dial; band=0.05; hours=24; changes=7`. Rows from before batch 24 have no `mode=` and were all in the order mode.

Because the notes changed, the PlayMode test that compares watching a model with its evaluation (`GYM_WATCH_EVAL_LOG`, see [development.md](development.md)) only matches a log written by an evaluation build from batch 24 on; against an older log it differs in `notes` only, as expected.

## Round three scores

`scripts/eval/score_round3.py` (standard library only) scores the agent models of the log on BTCUSDT's three validation segments at a fee of 0.1 %:

```bash
python scripts/eval/score_round3.py evaluations/log.csv                      # every model
python scripts/eval/score_round3.py evaluations/log.csv --runs long-,mix-    # only run ids with these prefixes
python -m unittest discover -s scripts/eval                                  # its tests
```

For each model it takes the latest row per segment, then applies two thresholds: `validation-bear` max drawdown at most 0.4132 and `validation` return at least 0.3901, both compared after rounding the log's values to four decimals. These are the real results of `constant_mix_50` (round three's decision document, section two item 3 and its addendum); they are constants at the top of the script. A model that misses either is out but stays in the table. The score is the mean over the three segments of return ÷ max(drawdown, 0.01). The final model is the highest-scoring one among all models past both thresholds; configurations (the run id without `-s<seed>-<date>`) are scored by the mean over all their seeds, with the number of seeds past both thresholds beside it. A model that lacks a segment is listed and not ranked. "Did not learn" is a dial model that never changed level on the three segments, or an order model with no trades on all three or a return below the random baseline on one. The third table puts the old rules and the two opponents through the same thresholds and score. `--symbol` and `--fee` choose another symbol or fee.

## Observation set and the model's input size

`-gymObs base|long|long-ta` chooses the observation set for the evaluation player, the baseline tool and the Play scene; without it they use `observationSet` in `gym-config.json` (default `base`), and another name is refused (exit code 1, nothing written). The set's history length moves the first evaluated candle to max(segment start, history length): 32 candles for `base`, 2160 for `long` and `long-ta`; with the committed data every evaluation segment starts much later, so the baselines are the same for every set.

A model only fits the set it was trained on (35 inputs for `base`, 41 for `long`, 43 for `long-ta`; every round-one model is `base`). `BuildEval` reads the model's vector observation input size and writes it to `build-info.json` as `vector_observation_size`; before the first step the evaluation player compares it with the size of its set and, when they differ, exits with code 1 and writes nothing, saying both numbers, for example `the model takes 35 observations but the observation set long has 41`. A build made before this check (no `vector_observation_size`) is not checked, with a warning in the log. The **Gym > Watch a Model...** menu makes the same check in the editor and refuses a model that does not fit the set of `gym-config.json`.

The details file of an agent evaluation records `observation_set` and `history_length`; the `notes` of its row in the log end with `obs=<set>`, for example `deterministic=True; device=Burst; steps=8759; obs=base`. Rows from before have no `obs=` and were all `base`.

`BuildMacEval` signs `GymEval.app` again after adding `build-info.json`, so a fresh build passes `codesign --verify --deep`. ML-Agents, however, writes its timer file into the app (`Contents/ML-Agents/Timers/`) every time the player runs, which breaks the signature again. It still runs on this Mac; to give the app to another Mac, copy it before its first run, or sign it again first: `codesign --force --deep -s - unity/Builds/mac/GymEval.app`.
