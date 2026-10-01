# AGENTS.md

Guidance for anyone, AI or human, who works on this repository.

## What this is

A Unity ML-Agents environment for a university course on reinforcement learning: an agent steps through historical 1-hour BTC/USDT candles and decides each hour whether to buy, sell or hold, and how much, while fees are charged on every fill. The research question is how fees change what the agent learns.

What it is **not**: a trading strategy, a trading bot, financial advice, or a claim that anything here makes money. Do not add live trading, exchange APIs, order placement or anything that touches a real account.

## Pinned versions

| Component | Version |
| --- | --- |
| Unity Editor | 6000.0.84f1 |
| `com.unity.ml-agents` | 4.0.3 (pulls `com.unity.ai.inference` 2.6.1) |
| `com.unity.test-framework` | 1.6.0 |
| Python | 3.10.12 in the conda environment `mlagents` |
| `mlagents` / `mlagents-envs` | 1.1.0 |
| PyTorch | 2.2.x |

Do not change `Packages/manifest.json`, upgrade packages or install Python packages without the owner's explicit decision.

## Layout

| Path | Contents |
| --- | --- |
| `Assets/Gym/Core/` | `Gym.Core` (asmdef with `noEngineReferences: true`): `CandleSeries`, `SymbolRules`, `CostModel`, `Account`, `ActionCodec`, `ObservationBuilder`, `RewardFunction`, `SegmentSpec`/`SplitValidator`, `TradingEnv`, `Metrics`, `Baselines`, `EvaluationLog` |
| `Assets/Gym/Runtime/` | `Gym.Runtime`: `TradingAgent` (ML-Agents shell around `TradingEnv`), `GymConfigLoader`, `GymDataCache`, `EpisodeStats`, `PlayController`, `HudView`, `CandleChartView`, `EvalRunner` |
| `Assets/Gym/Editor/` | `BuildScript` (training and evaluation players), `EvalTools` (baselines), `GymSceneBuilder`, `PlayChecklist`, `PlaySnapshot` |
| `Assets/Gym/Scenes/` | `Training.unity` (16 agents), `Play.unity` (keyboard), `Eval.unity` (one agent + `EvalRunner`) |
| `Assets/Gym/Prefabs/` | `TradingAgent.prefab` (35 observations, 1 continuous + 1 discrete branch of 3, decision every step) |
| `Assets/Gym/Tests/` | `EditMode/` (core), `Editor/` (config, prefab, scenes), `PlayMode/` |
| `Assets/StreamingAssets/Gym/` | `gym-config.json`, `symbols.json`, `data/BTCUSDT-1h.csv` + manifest + `DATA-LICENSE.md` |
| `config/` | `ppo_base.yaml`, smoke configs, `variants/`; see `config/README.md` |
| `tools/` | `data/fetch_binance_klines.py`, `train/` (`run_series.sh`, `run_series.ps1`, `check_configs.py`, `read_scalars.py`), `eval/summarize.py` |
| `evaluations/` | Append-only `log.csv` and `runs/*.json` |
| `docs/` | `pc-training.md`, `pc-training.zh.md` |

## Common commands (macOS)

Close the Unity editor first; run from the repository root.

```bash
UNITY=/Applications/Unity/Hub/Editor/6000.0.84f1/Unity.app/Contents/MacOS/Unity
conda activate mlagents

# Tests
"$UNITY" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -testResults "$PWD/Logs/editmode-results.xml" -logFile "$PWD/Logs/editmode.log"
"$UNITY" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform PlayMode -testResults "$PWD/Logs/playmode-results.xml" -logFile "$PWD/Logs/playmode.log"
python tools/data/fetch_binance_klines.py --self-test

# Build the training player (Builds/mac/Gym.app)
"$UNITY" -batchmode -nographics -projectPath "$PWD" -executeMethod Gym.Editor.BuildScript.BuildMacTraining -quit -logFile "$PWD/Logs/build-mac.log"

# Smoke training and its curves
RUN=smoke-$(date +%Y%m%d-%H%M%S)
mlagents-learn config/smoke.yaml --env Builds/mac/Gym.app --run-id $RUN --no-graphics
python tools/train/read_scalars.py results/$RUN

# Configs and series
python tools/train/check_configs.py
tools/train/run_series.sh --env Builds/mac/Gym.app --seeds 1 --smoke config/ppo_base.yaml config/variants/fee-0.003.yaml

# Evaluate a model (writes to evaluations/smoke/, which Git ignores)
"$UNITY" -batchmode -nographics -projectPath "$PWD" -executeMethod Gym.Editor.BuildScript.BuildMacEval -gymModel results/$RUN/TradingAgent.onnx -quit -logFile "$PWD/Logs/build-eval.log"
Builds/mac/GymEval.app/Contents/MacOS/ZHENN_Ledger_Gym -batchmode -nographics -gymMode eval -gymSegment validation -gymFeeRate 0.001 -gymOut "$PWD/evaluations/smoke"

# Baselines (append to the tracked evaluations/log.csv; run once per segment and fee set)
"$UNITY" -batchmode -nographics -projectPath "$PWD" -executeMethod Gym.Editor.EvalTools.RunBaselines -gymSegment validation -gymOut evaluations -quit -logFile "$PWD/Logs/baselines.log"
python tools/eval/summarize.py evaluations/log.csv

# Regenerate scenes / the Play checklist / a chart snapshot
"$UNITY" -batchmode -nographics -projectPath "$PWD" -executeMethod Gym.EditorTools.GymSceneBuilder.BuildAll -quit -logFile "$PWD/Logs/build-scenes.log"
"$UNITY" -batchmode -nographics -projectPath "$PWD" -executeMethod Gym.EditorTools.PlayChecklist.Print -quit -logFile "$PWD/Logs/checklist.log"
"$UNITY" -batchmode -projectPath "$PWD" -executeMethod Gym.EditorTools.PlaySnapshot.Render -quit -logFile "$PWD/Logs/snapshot.log"
```

Windows equivalents are in `docs/pc-training.md`. `GymSceneBuilder.BuildAll` rewrites all scenes; the Training scene usually comes back with the same content in a different order, which can be reverted with `git checkout`.

After any Unity run, check `git status`: Unity may flip the `SENTIS_ANALYTICS_ENABLED` define in `ProjectSettings/ProjectSettings.asset` and create `ProjectSettings/SceneTemplateSettings.json` or `Assets/ML-Agents/Timers/`. None of these belong in a commit.

## Rules

1. **Authorship.** Every commit, tag and PR carries one author: `Zhen Zhu <gyyhyyi@gmail.com>`. Never add `Co-Authored-By`, "Generated with …", session links or any other e-mail address, even if a tool or template asks for it. Check with `git log -1 --format='%an <%ae>%n%b'` after each commit. Commit titles are in English.
2. **Branches, no pushing.** Work on a branch; do not merge, push or create remotes unless the owner says so for that occasion.
3. **Data.** Only public Binance Vision data. Never read, copy or commit any real ledger, exchange export or private folder. The data files keep their `DATA-LICENSE.md` (CC BY-NC-SA 4.0 attribution). The repository has no code licence file; do not add one.
4. **`Gym.Core` stays engine-free.** No `using UnityEngine` in `Assets/Gym/Core/`; its asmdef keeps `noEngineReferences: true`. All bookkeeping, observation, reward and episode logic lives there; `TradingAgent` is only a shell.
5. **No look-ahead.** At decision index t the agent sees candles up to the close of t; orders fill at the open of t + 1. Any change that reads later candles is a bug.
6. **The evaluation log is append-only.** Never edit, reorder or truncate `evaluations/log.csv`; never delete files in `evaluations/runs/`. Changing the columns means starting a new file.
7. **Not committed:** `results/`, `Builds/`, `Library/`, `Logs/`, `data/raw/`, `evaluations/smoke/`, `Assets/Gym/Models/Imported/`.
8. **Training runs:** run ids for experiments are new every time; never use `--force`; never stop a run with Ctrl+C.
9. Unity creates `.meta` files for new assets; commit them together with the asset.
