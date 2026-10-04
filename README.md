# ZHENN Ledger Gym

A Unity ML-Agents environment in which a reinforcement-learning agent steps through historical 1-hour BTC/USDT candles and decides at every hour whether to buy, sell or hold, and how much, while trading fees eat into every order.

This is a university course project (Introduction to Reinforcement Learning, University of Debrecen, Fall 2026). It is **not** a trading strategy and **not** financial advice, and it promises no returns. The question it asks is how fees change what an agent learns to do, not whether it can make money.

*中文说明见 [README.zh.md](README.zh.md)。*

## Status

Working on macOS: the environment and its tests, the keyboard Play scene, the training build, short smoke training runs, the evaluation pipeline with baselines, the comparison configs and the series scripts. Next: the full training runs on the Windows PC ([docs/pc-training.md](docs/pc-training.md)). The Windows builds and `run_series.ps1` have not been run on a PC yet.

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

To play by hand, open the project in Unity (the `unity/` folder), open `Assets/Gym/Scenes/Play.unity`, set the Game view's aspect ratio to 16:9 and press Play. Click into the Game view, then: `1`–`4` pick 10 / 25 / 50 / 100 %, `B` buys, `S` sells, `H` or Space holds (each key moves one candle), `P` toggles auto-play, `R` restarts, and `L` or the button in the top-left corner switches the on-screen text between Chinese (the default) and English. The top-left panel shows the readings; the chart has a price scale on the right and a date under every UTC midnight; the pocket in the bottom-left shows the cash and the unrealized and realized profit and loss, and a filled buy floats a red `-$amount` up out of it while a filled sell drops a green `+$amount` in. The scene runs at 30 frames per second (`FrameRateLimiter` in the scene; set 60 there for smoother motion).

## Repository layout

| Path | What is there |
| --- | --- |
| `unity/` | The Unity project; open this folder in Unity |
| `unity/Assets/Gym/Core/` | `Gym.Core`: the trading environment in plain C# (no UnityEngine), one folder and namespace per concept: `Market/` (candles, symbol rules, splits: `CandleSeries`, `SymbolRules`, `SegmentSpec`, `SplitValidator`), `Accounting/` (account and fees: `Account`, `CostModel`), `Env/` (actions and mask, observation, reward, episode, seeds: `TradingEnv`, `TradeAction`, `ActionCodec`, `ObservationBuilder`, `RewardFunction`, `SeedMixer`), `Evaluation/` (metrics, baselines, evaluation log: `Metrics`, `Baselines`, `EvaluationLog`, `JsonWriter`) |
| `unity/Assets/Gym/Runtime/` | `Gym.Runtime`: `Agents/` (`TradingAgent`, the ML-Agents agent around `TradingEnv`, and `EpisodeStats`), `Configuration/` (config loading: `GymConfigLoader`, `GymDataCache`), `Evaluation/` (the evaluation runner `EvalRunner`), `Play/` (the Play scene: `PlayController`; the views `HudView`, `CandleChartView`, `WalletView`, `PlayLanguageSwitch`, `FrameRateLimiter`; the text table `PlayText` and the system font lookup `PlayFont`; the pure helpers `HudReadout`, `PriceScale`, `TimeAxis`, `MoneyText`, `FloatingAmount`; the drawing helpers `WorldText`, `ShapeLayer`, `ColoredMeshBuilder`, `PocketShape`, `PlayLayout`, `PlayPalette`) |
| `unity/Assets/Gym/Editor/` | `BuildScript` (training and evaluation players), `EvalTools` (baseline evaluation), `GymSceneBuilder` (scene builder), `PlayChecklist` and `PlaySnapshot` (Play checklist and snapshot tools) |
| `unity/Assets/Gym/Scenes/` | `Training` (16 agents), `Play` (keyboard), `Eval` (one agent plus the runner) |
| `unity/Assets/Gym/Prefabs/` | `TradingAgent.prefab`; its observations and actions are in [docs/architecture.md](docs/architecture.md) |
| `unity/Assets/Gym/Materials/` | `CandleChart.mat` for the Play scene's candle chart |
| `unity/Assets/Gym/Models/` | Models imported by an evaluation build go to `Imported/`, which Git ignores ([Models/README.md](unity/Assets/Gym/Models/README.md)) |
| `unity/Assets/Gym/Tests/` | EditMode tests in `Core/` (the core, in the same four folders), `Runtime/` and `Editor/` (the agent prefab, the scenes and the baseline tool `EvalTools`); PlayMode tests in `PlayMode/` |
| `unity/Assets/StreamingAssets/Gym/` | `gym-config.json`, `symbols.json` and the data (`data/BTCUSDT-1h.csv`, its manifest and `DATA-LICENSE.md`); shipped inside every build |
| `unity/Packages/` | `manifest.json` and `packages-lock.json`: the Unity packages and their versions |
| `unity/ProjectSettings/` | Unity's project settings, including the editor version (`ProjectVersion.txt`) |
| `config/` | Training configs (`ppo_base.yaml`, the smoke configs) and the comparison variants (`variants/`); see [config/README.md](config/README.md) |
| `scripts/data/` | The Binance Vision fetcher (`fetch_binance_klines.py`) |
| `scripts/train/` | Series runners (`run_series.sh`, `run_series.ps1`), the config checker (`check_configs.py`) and the TensorBoard scalar reader (`read_scalars.py`) |
| `scripts/eval/` | `summarize.py` for evaluation logs |
| `evaluations/` | The append-only evaluation log (`log.csv`) and its per-run details (`runs/*.json`), tracked |
| `docs/` | The pages listed below |
| `results/`, `data/raw/`, `unity/Builds/`, `evaluations/smoke/` | Local output, ignored by Git |

## Documentation

- [docs/setup.md](docs/setup.md): pinned versions, the Python environment (macOS and Windows), reading the code in VS Code
- [docs/architecture.md](docs/architecture.md): the three layers, one step, observation, action, reward and seeds, where to start reading
- [docs/data.md](docs/data.md): the data, its licence, how to rebuild it, gaps and the three segments
- [docs/evaluation.md](docs/evaluation.md): the evaluation log, its metrics and the baselines
- [docs/training.md](docs/training.md): a training series on the Mac and the `Trading/*` curves
- [docs/development.md](docs/development.md): regenerating scenes, the Play checklist and snapshots; what Unity writes by itself
- [docs/pc-training.md](docs/pc-training.md) ([Chinese](docs/pc-training.zh.md)): training on the Windows PC
- [config/README.md](config/README.md): the training configs and comparison variants
- [AGENTS.md](AGENTS.md): the rules for anyone, human or AI, who changes the code

## Data and licence

The environment replays BTC/USDT spot 1-hour candles from [Binance Vision](https://data.binance.vision). The data is licensed [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/); see [`DATA-LICENSE.md`](unity/Assets/StreamingAssets/Gym/data/DATA-LICENSE.md) for the attribution and the list of changes. This project is not affiliated with, sponsored or endorsed by Binance. The code has no licence file yet. Source, terms, rebuilding and gap filling: [docs/data.md](docs/data.md).
