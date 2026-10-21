# ZHENN Ledger Gym

A Unity ML-Agents environment in which a reinforcement-learning agent steps through historical 1-hour BTC/USDT candles and decides at every hour whether to buy, sell or hold, and how much, while trading fees eat into every order (the order mode of rounds one and two, still the default). In round three's dial mode it only turns a dial once a day (every 24 candles): a target share of its equity in coin of 30 %, 50 % or 70 %, and a rebalancing rule buys or sells whenever the position drifts more than 5 points from that target. Four more coins (ETH, ADA, DOGE, XRP) are there to evaluate on. What the agent sees is one of three observation sets: `base`, the first round's 35 numbers (the last 32 closes, its position, its open profit and the time since its last trade; the default), `long`, which adds six long-horizon numbers (returns over a day, a week, 30 and 90 days scaled by volatility, the 30-day volatility and the drawdown from the 90-day high), and `long-ta`, which adds daily MACD and RSI to `long` as a control group. Every hour the agent scores the log return of its equity (× 100, clipped to ±1); an optional risk coefficient (the environment parameter `risk_aversion`, default 0, which leaves the score unchanged) takes an extra penalty on the squared loss from every losing hour.

This is a university course project (Introduction to Reinforcement Learning, University of Debrecen, Fall 2026). It is **not** a trading strategy and **not** financial advice, and it promises no returns. The question it asks is how fees change what an agent learns to do, not whether it can make money.

*中文说明见 [README.zh.md](README.zh.md)。*

## Status

Working on macOS: the environment and its tests, the keyboard Play scene, the Watch scene for a trained model, the training build, short smoke training runs, the evaluation pipeline with baselines, the comparison configs and the series scripts. Next: the full training runs on the Windows PC ([docs/pc-training.md](docs/pc-training.md)). The Windows builds and `run_series.ps1` have not been run on a PC yet.

## Quick start (Mac)

Prerequisites: Unity Hub with Unity **6000.0.84f1**, and the `mlagents` conda environment from [docs/setup.md](docs/setup.md#python-environment). Close the Unity editor before running any command-line Unity step; the project cannot be open twice. Run every command from the root of the cloned repository (the folder that holds this README). The Windows commands are in [docs/pc-training.md](docs/pc-training.md).

```bash
UNITY=/Applications/Unity/Hub/Editor/6000.0.84f1/Unity.app/Contents/MacOS/Unity

# 1. Tests (exit code 0 means all passed)
"$UNITY" -batchmode -nographics -projectPath "$PWD/unity" -runTests -testPlatform EditMode -testResults "$PWD/unity/Logs/editmode-results.xml" -logFile "$PWD/unity/Logs/editmode.log"
"$UNITY" -batchmode -nographics -projectPath "$PWD/unity" -runTests -testPlatform PlayMode -testResults "$PWD/unity/Logs/playmode-results.xml" -logFile "$PWD/unity/Logs/playmode.log"

# 2. Training build: unity/Builds/mac/Gym.app
"$UNITY" -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.BuildScript.BuildMacTraining -quit -logFile "$PWD/unity/Logs/build-mac.log"

# 3. A 30k-step smoke training run (about 15 seconds on an M5)
conda activate mlagents
RUN=smoke-$(date +%Y%m%d-%H%M%S)
mlagents-learn config/smoke.yaml --env unity/Builds/mac/Gym.app --run-id $RUN --no-graphics
python scripts/train/read_scalars.py results/$RUN

# 4. Evaluate that model on the validation segment (results go to the git-ignored evaluations/smoke/)
"$UNITY" -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.BuildScript.BuildMacEval -gymModel "$PWD/results/$RUN/TradingAgent.onnx" -quit -logFile "$PWD/unity/Logs/build-eval.log"
unity/Builds/mac/GymEval.app/Contents/MacOS/ZHENN_Ledger_Gym -batchmode -nographics -gymMode eval -gymSegment validation -gymFeeRate 0.001 -gymOut "$PWD/evaluations/smoke"
python scripts/eval/summarize.py evaluations/smoke/log.csv
```

To play by hand, open the project in Unity (the `unity/` folder), open `Assets/Gym/Scenes/Play.unity`, set the Game view's aspect ratio to 16:9 and press Play. Click into the Game view, then: `1`–`4` pick 10 / 25 / 50 / 100 %, `B` buys, `S` sells, `H` or Space holds (each key moves one candle), `P` toggles auto-play, `R` restarts, and `L` or the button in the top-left corner switches the on-screen text between Chinese (the default) and English. The top-left panel shows the readings; the chart has a price scale on the right and a date under every UTC midnight; the pocket in the bottom-left shows the cash and the unrealized and realized profit and loss, and a filled buy floats a red `-$amount` up out of it while a filled sell drops a green `+$amount` in. The scene runs at 30 frames per second (`FrameRateLimiter` in the scene; set 60 there for smoother motion). A small framed avatar stands on the newest candle and moves with every step: it hops on a hold, hops and grows on a filled buy (`BUY 25%` above it), hops and leans on a filled sell (`SELL 50%`), and shakes sideways without hopping on a rejected order (`REJECTED`). When older candles under it are higher, it rises above them and points down to the newest candle.

To watch a trained model trade, use the Unity menu **Gym > Watch a Model...** and pick an `.onnx` file (training writes it to `results/<run-id>/TradingAgent.onnx`). The model is imported into `unity/Assets/Gym/Models/Imported/` (ignored by Git), remembered for this project in `unity/UserSettings/Gym/watch-model.json`, and `Assets/Gym/Scenes/Watch.unity` opens and starts playing; next time, opening `Watch.unity` and pressing Play shows the same model. The Watch scene has the same screen as the Play scene, driven by the model instead of the keyboard: `P` pauses and resumes, `N` takes one step while paused, `[` and `]` go slower and faster through 1, 2, 5, 10, 20, 50, 100, 200 and 500 steps per second (5 at the start), `R` starts the segment again, and `L` switches the language. It watches the validation segment from its first candle with the agent's default costs (fee rate 0.1 %, no fixed fee, no slippage); the segment and the three costs are fields on `WatchController` in the Inspector. Keep the test segment for the final numbers and pick it only then. At the end of the segment the screen stops on the last step until `R`. Watching runs the evaluation player's steps: the same inference settings (Inference Only, deterministic, Burst) and the same stepping; with the smoke-training model it produced the evaluation's log row and final equity digit for digit. The official numbers still come from the evaluation player. Without a model the screen says which menu to use. The Watch scene uses the observation set of `gym-config.json` (`observationSet`, `base` by default) or `-gymObs`: to watch a model trained on `long` or `long-ta`, set `observationSet` to that set first (or start the editor with `-gymObs`); with the wrong set the menu refuses the model and says that its input size does not match. The Watch scene is for the Unity editor only and is not in the build list.

To give the avatar a face, put `avatar.png` (or `avatar.jpg`, `avatar.jpeg`) into `unity/Assets/StreamingAssets/Gym/avatar/`; a square picture of about 256 × 256 pixels works best, and other shapes keep their proportions. Without a picture, or with a file that does not decode (a warning says so), a gray placeholder figure is shown. Details: [avatar/README.md](unity/Assets/StreamingAssets/Gym/avatar/README.md).

## Repository layout

| Path | What is there |
| --- | --- |
| `unity/` | The Unity project; open this folder in Unity |
| `unity/Assets/Gym/Core/` | `Gym.Core`: the trading environment in plain C# (no UnityEngine), one folder and namespace per concept: `Market/` (candles, symbol rules, splits: `CandleSeries`, `SymbolRules`, `SegmentSpec`, `SplitValidator`), `Accounting/` (account and fees: `Account`, `CostModel`), `Env/` (actions and mask, the two action modes and the rebalancing rule, observation sets, reward, episode, seeds, the coin of a mixed-training episode: `TradingEnv`, `TradeAction`, `ActionCodec`, `ActionMode`, `DialSettings`, `Rebalancer`, `ActionShape`, `ObservationSet`, `ObservationBuilder`, `RewardFunction`, `SeedMixer`, `SymbolPicker`), `Evaluation/` (metrics, baselines, evaluation log: `Metrics`, `Baselines`, `EvaluationLog`, `ActionRecord`, `JsonWriter`) |
| `unity/Assets/Gym/Runtime/` | `Gym.Runtime`: `Agents/` (`TradingAgent`, the ML-Agents agent around `TradingEnv`, and `EpisodeStats`), `Configuration/` (config loading: `GymConfigLoader`, `GymDataCache`), `Evaluation/` (the evaluation runner `EvalRunner`), `Play/` (the Play scene: `PlayController`; the views `HudView`, `CandleChartView`, `WalletView`, `AvatarView`, `PlayLanguageSwitch`, `FrameRateLimiter`; the text table `PlayText` and the system font lookup `PlayFont`; the avatar picture `AvatarPictureLoader` and `AvatarPlaceholder`; the pure helpers `HudReadout`, `PriceScale`, `TimeAxis`, `MoneyText`, `FloatingAmount`, `AvatarAnimation`, `AvatarPose`, `AvatarHint`; the drawing helpers `WorldText`, `ShapeLayer`, `ColoredMeshBuilder`, `PocketShape`, `PlayLayout`, `PlayPalette`), `Watch/` (the Watch scene: `WatchController`, its playback `WatchPlayback` and the agent it steps `AgentWatchTarget`; `WatchNotice`; the remembered model `WatchModelMemory`) |
| `unity/Assets/Gym/Editor/` | `BuildScript` (training and evaluation players), `EvalTools` (baseline evaluation), `GymSceneBuilder` (scene builder), `PlayChecklist` and `PlaySnapshot` (Play checklist and snapshot tools), `WatchSetup` (the **Gym > Watch a Model...** menu), `WatchModelAttacher` (puts the remembered model on the Watch agent), `WatchSnapshot` (Watch scene snapshots) |
| `unity/Assets/Gym/Scenes/` | `Training` (16 agents), `Play` (keyboard), `Watch` (a trained model plays; editor only, not in the build list), `Eval` (one agent plus the runner) |
| `unity/Assets/Gym/Prefabs/` | `TradingAgent.prefab` (it declares the 35 `base` observations and the order mode's actions; the agent switches to the size of its observation set, and in the dial mode to the dial's action shape, when it starts); its observations and actions are in [docs/architecture.md](docs/architecture.md) |
| `unity/Assets/Gym/Materials/` | `CandleChart.mat` for the Play scene's candle chart |
| `unity/Assets/Gym/Models/` | Models imported by an evaluation build or by **Gym > Watch a Model...** go to `Imported/`, which Git ignores ([Models/README.md](unity/Assets/Gym/Models/README.md)) |
| `unity/Assets/Gym/Tests/` | EditMode tests in `Core/` (the core, in the same four folders), `Runtime/` and `Editor/` (the agent prefab, the scenes and the baseline tool `EvalTools`); PlayMode tests in `PlayMode/` |
| `unity/Assets/StreamingAssets/Gym/` | `gym-config.json`, `symbols.json` and the data (`data/<SYMBOL>-1h.csv` for BTCUSDT, ETHUSDT, ADAUSDT, DOGEUSDT and XRPUSDT, a manifest next to each, and `DATA-LICENSE.md`); `avatar/`, where an avatar picture goes; shipped inside every build |
| `unity/Packages/` | `manifest.json` and `packages-lock.json`: the Unity packages and their versions |
| `unity/ProjectSettings/` | Unity's project settings, including the editor version (`ProjectVersion.txt`) |
| `config/` | Training configs (`ppo_base.yaml`, the smoke configs), the comparison variants (`variants/`) and round two's configs (`v2/`); see [config/README.md](config/README.md) |
| `scripts/data/` | The Binance Vision fetcher (`fetch_binance_klines.py`) and the correlation of returns between coins (`correlation.py`) |
| `scripts/train/` | Series runners (`run_series.sh`, `run_series.ps1`), the config checker (`check_configs.py`) and the TensorBoard scalar reader (`read_scalars.py`) |
| `scripts/eval/` | `summarize.py` for evaluation logs; `score_round3.py`, round three's ruler (two thresholds on BTC's falling and rising validation segments, then the mean over the three validation segments of return ÷ drawdown), with its tests `test_score_round3.py` |
| `evaluations/` | The append-only evaluation log (`log.csv`) and its per-run details (`runs/*.json`), tracked |
| `models/` | Models kept in the repository, one folder per round: `round2/ppo_v2-s3-20261020/` is round two's final model (`TradingAgent.onnx` with its `build-info.json`, `config-used.yaml` and `env-args.txt`); an EditMode test reads its action shape |
| `docs/` | The pages listed below |
| `results/`, `data/raw/`, `unity/Builds/`, `evaluations/smoke/`, `unity/UserSettings/` | Local output and this machine's settings (including the remembered Watch model), ignored by Git |

## Documentation

- [docs/setup.md](docs/setup.md): pinned versions, the Python environment (macOS and Windows), reading the code in VS Code
- [docs/architecture.md](docs/architecture.md): the three layers, one step, the three observation sets and their formulas, the two action modes (orders and dial) and the rebalancing rule, the reward and its risk coefficient, seeds, where to start reading
- [docs/data.md](docs/data.md): the data of the five coins, its licence, how to rebuild it, gaps, the coins' correlation with BTC and the segments (training 2017-08-17 to 2021-12-31; validation-bear 2022, validation-recovery 2023-01 to 2024-08, validation 2024-09 to 2025-08; test 2025-09 to 2026-08)
- [docs/evaluation.md](docs/evaluation.md): the evaluation log, its metrics, the evaluation segments, choosing a coin with `-gymSymbol` and an observation set with `-gymObs`, the action mode with `-gymActionMode`, the checks of a model's input size and action shape, the six baselines (buy-and-hold, cash, random, and three simple rules: `momentum_90d`, `sma_cross`, `vol_target`) and round three's two dial opponents (`constant_mix_50`, `dial_momentum_90d`), and round three's scores with `score_round3.py`
- [docs/training.md](docs/training.md): a training series on the Mac, the observation set `obs_set`, the risk coefficient `risk_aversion`, the action mode `action_mode` with `decision_hours` and `rebalance_band`, training on several coins (`trainSymbols`, `-gymTrainSymbols`) and the `Trading/*` curves
- [docs/development.md](docs/development.md): regenerating scenes, the Play checklist and snapshots, the tests that need a trained model; what Unity writes by itself
- [docs/pc-training.md](docs/pc-training.md) ([Chinese](docs/pc-training.zh.md)): training on the Windows PC
- [config/README.md](config/README.md): the training configs and comparison variants
- [AGENTS.md](AGENTS.md): the rules for anyone, human or AI, who changes the code

## Data and licence

The environment replays spot 1-hour candles from [Binance Vision](https://data.binance.vision) of BTC/USDT (the main symbol) and of ETH, ADA, DOGE and XRP against USDT (picked with `-gymSymbol`). Training uses the current symbol, the one `-gymSymbol` picks (BTC without it); to mix BTC, ETH, ADA and XRP, one coin per episode, see the training list (`-gymTrainSymbols`, or `trainSymbols` in `gym-config.json`) in [docs/training.md](docs/training.md); DOGE is only ever evaluated. The data is licensed [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/); see [`DATA-LICENSE.md`](unity/Assets/StreamingAssets/Gym/data/DATA-LICENSE.md) for the attribution and the list of changes. This project is not affiliated with, sponsored or endorsed by Binance. The code has no licence file yet. Source, terms, rebuilding and gap filling: [docs/data.md](docs/data.md).
