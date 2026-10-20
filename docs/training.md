# Training

One smoke training run is step 3 of the README's [quick start](../README.md#quick-start-mac). This page covers a series of runs on the Mac, the two optional environment parameters `obs_set` and `risk_aversion`, training on several coins at once, and what the `Trading/*` curves mean.

Long runs happen on a Windows PC: see [pc-training.md](pc-training.md) (English) or [pc-training.zh.md](pc-training.zh.md) (Chinese). In short: same Unity and Python versions, `BuildWindowsTraining`, 100k-step speed tests on the CPU (`config/smoke-100k.yaml`) and on the graphics card (`config/smoke-100k-cuda.yaml`), then the comparison series with `scripts/train/run_series.ps1` on whichever was faster (`-Device cpu|cuda`). Round two (configs in `config/v2/`, the coins given with `-EnvArgs`) has its own sections there, with the smoke checks the Mac cannot run.

## A series on the Mac

`scripts/train/run_series.sh` runs every config × every seed, one `mlagents-learn` run each; `--help` lists its options. With the training build from the quick start and the `mlagents` environment active, from the repository root:

```bash
scripts/train/run_series.sh --env unity/Builds/mac/Gym.app --seeds 1 --smoke config/ppo_base.yaml config/variants/fee-0.003.yaml
```

What each config changes, and how to check the configs after editing them: [config/README.md](../config/README.md). Round two's configs are in `config/v2/`: `ppo_v2.yaml` (the base, `normalize: true`), `long.yaml` and `long-ta.yaml` (`obs_set` 1 and 2), three `long-risk-*.yaml` (`risk_aversion`) and `smoke-long.yaml`.

Words for the player go in `--env-args`, as one quoted string; this is how a series trains on several coins:

```bash
scripts/train/run_series.sh --env unity/Builds/mac/Gym.app --seeds 1 --dry-run --env-args "-gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT" config/v2/smoke-long.yaml
```

The script splits the string at spaces and puts the words last on the `mlagents-learn` command line, after its `--env-args`: `mlagents-learn` hands every word after that option to the player, one argument each, so any option after it would go to the player too, and a name with its value in one word (`"-gymObs long"`) would be ignored by the player without an error. `--dry-run` prints the command and shows a word with a space in it in double quotes. Keep the observation set and the risk coefficient in the config (`obs_set`, `risk_aversion`, recorded in `config-used.yaml`); when both are given, the environment parameter wins over `-gymObs` and `-gymRiskAversion`. Do not pass `-gymSegment` (training only takes `train`) or `-logFile` (the clock check reads the player logs ML-Agents writes). Every run also writes `results/<run-id>/env-args.txt` (the words on one line, or an empty file) and copies the training build's `build-info.json` to `results/<run-id>/build-info.json`. `BuildMacTraining` writes that file into the app, `Gym.app/Contents/Resources/Data/StreamingAssets/Gym/build-info.json` (build time, Unity version and the SHA-256 of the build's `gym-config.json`), and signs the app again; a build made before has none, and the script says so at the start. The training player also logs one line `training player: observations …, current symbol …, training symbols …, segment …, gym-config.json sha256 …` with the set its agents really use and the SHA-256 of the `gym-config.json` it read.

## Observation set (`obs_set`)

Which numbers the agent sees is chosen by the environment parameter `obs_set` in the config's `environment_parameters`: 0 is `base` (the first round's 35 numbers), 1 is `long` (41: six long-horizon numbers added), 2 is `long-ta` (43: daily MACD and RSI added to `long`). The formulas are in [architecture.md](architecture.md#observation-action-reward). `config/variants/obs-long.yaml` and `config/variants/obs-long-ta.yaml` set it; `ppo_base.yaml` leaves it out, and then the player uses `-gymObs` or `observationSet` in `gym-config.json` (default `base`). Any other value stops the training player with exit code 1, and so does a set whose history length (2160 candles for `long` and `long-ta`) leaves the training segment too short or `playStart` too early. Every agent writes its set to the player log, for example `observations long (41 numbers, history 2160)`; check that line in a smoke run, because the Mac tests cannot send environment parameters. The configuration line (`config …, observations …`) is written before the trainer's `obs_set` is read and keeps the config's set. A model trained on one set only works with that set.

## Risk coefficient (`risk_aversion`)

The environment parameter `risk_aversion` is the reward's risk coefficient λ: every hour scores r − λ × min(r, 0)², so only losing hours pay the extra penalty (the formula is in [architecture.md](architecture.md#observation-action-reward)). `config/variants/risk-0.005.yaml`, `risk-0.01.yaml` and `risk-0.02.yaml` set it; `ppo_base.yaml` leaves it out, and then the player uses `-gymRiskAversion` or the agent's default 0, which is the first round's reward bit for bit. The agent reads it at the start of every episode, like the costs. A negative, NaN or infinite value stops the training player with exit code 1; `check_configs.py` is stricter and only accepts [0, 100] in a config. The Mac tests cannot send environment parameters, so check `Trading/RiskAversion` in a smoke run: it should be the config's value.

## Training on several coins (`trainSymbols`, `-gymTrainSymbols`)

By default an agent trains on one coin: the current symbol, which is the main symbol BTCUSDT, or the one `-gymSymbol` picks. To mix coins, list them in `trainSymbols` in `gym-config.json` (empty by default), for example `"trainSymbols": ["BTCUSDT", "ETHUSDT", "ADAUSDT", "XRPUSDT"]`, or give them on the player's command line, which overrides the config: `-gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT`. Every name must be one of the config's `symbols` (case does not matter, no repeats), and none may be `evalOnly`: DOGEUSDT is kept as a coin the agent has never seen, so listing it stops the player with exit code 1 and a list of the coins that can be trained on. The config's list is checked even when the command line overrides it. A training agent also refuses `-gymSymbol DOGEUSDT`; evaluation, Play and Watch still take it.

With a list, each agent builds one environment per coin, each on that coin's training segment (clipped to its first candle: ADA from 2018-04-17, XRP from 2018-05-04) with the agent's observation set, and at the start of every episode picks one from the episode's seed (`Core/Env/SymbolPicker.cs`, details in [architecture.md](architecture.md#seeds)). Over many episodes each coin gets about the same share, whatever its data length. The costs, the risk coefficient and the order rules work as before; each coin keeps its own order rules (`symbols.json`). Without a list, or with one coin, nothing changes: the same episode seeds and the same start candles as before. Every agent writes its list to the player log, for example `training symbols BTCUSDT (train 2017-08-17..2021-12-31), ADAUSDT (train 2018-04-17..2021-12-31); each episode picks one from its episode seed`; check that line and the `Trading/SymbolShare/*` curves below in a smoke run. Evaluation still runs one coin at a time (`-gymSymbol`).

## Training curves (`Trading/*` in TensorBoard)

During training every agent reports eleven numbers at the end of each episode, plus one per coin it trains on; TensorBoard shows their average over the episodes that ended in each summary period. They describe training episodes (720 steps from a random start, possibly holding coin at the start), so they are not the evaluation metrics of [evaluation.md](evaluation.md), even where the names match.

| Tag | Meaning |
| --- | --- |
| `Trading/Return` | Final equity ÷ starting equity − 1 |
| `Trading/Trades` | Filled orders |
| `Trading/Rejected` | Orders that were not filled: masked, too small (below the minimum order or one coin step), or eaten by the fixed fee |
| `Trading/FeesPaidPct` | Fees paid ÷ starting equity × 100 (a percentage) |
| `Trading/Exposure` | Share of steps that ended holding coin |
| `Trading/Turnover` | Traded value ÷ **starting** equity. The evaluation log's `turnover` divides by the **average** equity instead, so the two differ whenever equity moves |
| `Trading/RewardClips` | Steps whose r (the log-return part of the reward) was clipped to ±1; the risk penalty on top of it is not clipped |
| `Trading/FeeRate` | Fee rate the episode used, to check that the config's `fee_rate` arrived |
| `Trading/FixedFee` | Fixed fee per order the episode used |
| `Trading/RiskAversion` | Risk coefficient λ the episode used, to check that the config's `risk_aversion` arrived (0 when it is not set) |
| `Trading/RiskPenalty` | The episode's total risk penalty Σ λ × min(r, 0)², already taken out of `Environment/Cumulative Reward` (0 when λ = 0) |
| `Trading/SymbolShare/<symbol>` | One per coin the agent trains on: 1 when the episode used that coin, else 0, so the curve is that coin's share of the episodes (with four coins each should be near 0.25; with one coin a single curve at 1) |
