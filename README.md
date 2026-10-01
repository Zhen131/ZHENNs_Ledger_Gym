# ZHENN Ledger Gym

A Unity ML-Agents environment in which a reinforcement-learning agent steps through historical 1-hour BTC/USDT candles and decides at every hour whether to buy, sell or hold, and how much, while trading fees eat into every order.

This is a university course project (Introduction to Reinforcement Learning, University of Debrecen, Fall 2026). It is **not** a trading strategy and **not** financial advice, and it promises no returns. The question it asks is how fees change what an agent learns to do, not whether it can make money.

*中文说明见 [README.zh.md](README.zh.md)。*

## Status

Working on macOS: the environment and its tests, the keyboard Play scene, the training build, short smoke training runs, the evaluation pipeline with baselines, the comparison configs and the series scripts. Next: the full training runs on the Windows PC ([docs/pc-training.md](docs/pc-training.md)). The Windows builds and `run_series.ps1` have not been run on a PC yet.

## Quick start (Mac)

Prerequisites: Unity Hub with Unity **6000.0.84f1**, and the `mlagents` conda environment from [Python environment](#python-environment). Close the Unity editor before running any command-line Unity step; the project cannot be open twice.

```bash
cd ZHENN_Ledger_Gym
UNITY=/Applications/Unity/Hub/Editor/6000.0.84f1/Unity.app/Contents/MacOS/Unity

# 1. Tests (exit code 0 means all passed)
"$UNITY" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -testResults "$PWD/Logs/editmode-results.xml" -logFile "$PWD/Logs/editmode.log"
"$UNITY" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform PlayMode -testResults "$PWD/Logs/playmode-results.xml" -logFile "$PWD/Logs/playmode.log"

# 2. Training build: Builds/mac/Gym.app
"$UNITY" -batchmode -nographics -projectPath "$PWD" -executeMethod Gym.Editor.BuildScript.BuildMacTraining -quit -logFile "$PWD/Logs/build-mac.log"

# 3. A 30k-step smoke training run (about 15 seconds on an M5)
conda activate mlagents
RUN=smoke-$(date +%Y%m%d-%H%M%S)
mlagents-learn config/smoke.yaml --env Builds/mac/Gym.app --run-id $RUN --no-graphics
python tools/train/read_scalars.py results/$RUN

# 4. Evaluate that model on the validation segment (results go to the git-ignored evaluations/smoke/)
"$UNITY" -batchmode -nographics -projectPath "$PWD" -executeMethod Gym.Editor.BuildScript.BuildMacEval -gymModel results/$RUN/TradingAgent.onnx -quit -logFile "$PWD/Logs/build-eval.log"
Builds/mac/GymEval.app/Contents/MacOS/ZHENN_Ledger_Gym -batchmode -nographics -gymMode eval -gymSegment validation -gymFeeRate 0.001 -gymOut "$PWD/evaluations/smoke"
python tools/eval/summarize.py evaluations/smoke/log.csv
```

To play by hand, open the project in Unity, open `Assets/Gym/Scenes/Play.unity` and press Play. Click into the Game view, then: `1`–`4` pick 10 / 25 / 50 / 100 %, `B` buys, `S` sells, `H` or Space holds (each key moves one candle), `P` toggles auto-play, `R` restarts.

## Training on the Windows PC

Long runs happen on a Windows PC: see [docs/pc-training.md](docs/pc-training.md) (English) or [docs/pc-training.zh.md](docs/pc-training.zh.md) (Chinese). In short: same Unity and Python versions, `BuildWindowsTraining`, a 100k-step speed test with `config/smoke-100k.yaml`, then the comparison series with `tools/train/run_series.ps1`.

## Repository layout

| Path | What is there |
| --- | --- |
| `Assets/Gym/Core/` | `Gym.Core`: the trading environment in plain C# (no UnityEngine): candles, account and fees, actions and mask, observation, reward, episode, splits, metrics, baselines, evaluation log |
| `Assets/Gym/Runtime/` | `Gym.Runtime`: the ML-Agents agent around `TradingEnv`, config loading, the Play scene views, the evaluation runner |
| `Assets/Gym/Editor/` | Build scripts, baseline evaluation, scene builder, Play checklist and snapshot tools |
| `Assets/Gym/Scenes/` | `Training` (16 agents), `Play` (keyboard), `Eval` (one agent plus the runner) |
| `Assets/Gym/Tests/` | EditMode tests (core and editor) and PlayMode tests |
| `Assets/StreamingAssets/Gym/` | `gym-config.json`, `symbols.json` and the data; shipped inside every build |
| `config/` | Training configs and the comparison variants ([config/README.md](config/README.md)) |
| `tools/data/` | The Binance Vision fetcher |
| `tools/train/` | Series runners, the config checker and the TensorBoard scalar reader |
| `tools/eval/` | `summarize.py` for evaluation logs |
| `evaluations/` | The append-only evaluation log and its per-run details (tracked) |
| `docs/` | PC training guide |
| `results/`, `Builds/`, `evaluations/smoke/` | Local output, ignored by Git |

## Data and licence

**Data: Binance Vision.** The environment replays BTC/USDT spot 1-hour candles from [Binance Vision](https://data.binance.vision), from 2017-08-17 04:00 UTC to 2026-08-31 23:00 UTC. The processed file is committed at `Assets/StreamingAssets/Gym/data/BTCUSDT-1h.csv`, next to a manifest with the SHA-256 of every source archive and of the CSV itself.

The data is licensed [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/) under the [Binance Vision Dataset Terms v1.0 (2026-08-26)](https://github.com/binance/binance-public-data/blob/master/TERMS_AND_CONDITIONS.md); see [`DATA-LICENSE.md`](Assets/StreamingAssets/Gym/data/DATA-LICENSE.md) for the attribution and the list of changes. This project is not affiliated with, sponsored or endorsed by Binance. The code has no licence file yet.

To regenerate the data (standard library only; raw archives are cached in `data/raw/`, which Git ignores):

```bash
python tools/data/fetch_binance_klines.py --self-test
python tools/data/fetch_binance_klines.py --symbol BTCUSDT
```

By default the script stops at 2026-08, the end of the test segment (pass `--end YYYY-MM` to add newer months), and drops the 43 candles of February 2018 that start at hh:28 instead of on the hour, forward-filling those hours (`--off-hour error` stops instead, `--off-hour floor` moves them to the hour). So the default command rebuilds exactly the committed file: the `csv_sha256` in the manifest is `4739c139dc501e38498589359db093394dcee86cabde5a6e37c12d726d084242`.

Segments (in `gym-config.json`): train 2017-08-17 to 2024-08-31, validation 2024-09-01 to 2025-08-31, test 2025-09-01 to 2026-08-31. In the test segment BTC fell 27.4 %, so holding cash beats buy-and-hold there.

## Evaluation log

Every evaluation is one full pass over the validation or test segment: start at the first candle in cash, no randomness, the same fills and fees as in training. Each one appends a row to `evaluations/log.csv` and writes a JSON file with all metrics and settings to `evaluations/runs/`. **The log is append-only**: rows are never edited or removed, and a log whose header differs from the current columns is refused. Running the same evaluation twice adds a second row.

Metrics: total return, maximum drawdown, annualised Sharpe ratio (hourly log returns, √8760), trades, rejected orders, turnover (traded value ÷ average equity), fees (USDT and % of the starting equity) and exposure (share of steps that end holding coin).

Baselines (buy-and-hold, always cash, and a random policy over seeds 0–99 reported as medians with the 5th and 95th percentile of the return) for fee rates 0, 0.1 % and 0.3 %:

```bash
"$UNITY" -batchmode -nographics -projectPath "$PWD" -executeMethod Gym.Editor.EvalTools.RunBaselines -gymSegment validation -gymOut evaluations -quit -logFile "$PWD/Logs/baselines.log"
python tools/eval/summarize.py evaluations/log.csv
```

Options: `-gymSegment validation|test`, `-gymFeeRates 0,0.001,0.003`, `-gymRandomSeeds 100`, `-gymOut <dir>`, `-gymConfig <file>`. A trained model is evaluated in its own build (`BuildMacEval` / `BuildWindowsEval` with `-gymModel`), which records the run id and the model's SHA-256 in every row; `-gymFixedFee` and `-gymSlippage` set the other costs.

## Versions (pinned on every machine)

| Component | Version |
| --- | --- |
| Unity Editor | 6000.0.84f1 (LTS) |
| ML-Agents Unity package | `com.unity.ml-agents` 4.0.3 |
| Python | 3.10.12 (Miniforge / conda) |
| `mlagents` Python package | 1.1.0 |
| PyTorch | 2.2.x |

Opening the project with a different Unity version, or pairing a different `mlagents` release, is the most common way ML-Agents setups break.

## Python environment

### macOS (Apple Silicon)

`grpcio` 1.48.2, which `mlagents` 1.1.0 pins, has no Apple Silicon wheel on PyPI or conda-forge. Use conda-forge's 1.48.1 and install the remaining dependencies explicitly:

```bash
conda create -n mlagents python=3.10.12
conda install -n mlagents "grpcio=1.48"
conda activate mlagents
pip install "torch~=2.2.1" "numpy>=1.23.5,<1.24" "protobuf>=3.6,<3.21" "onnx==1.15.0" \
  h5py "Pillow>=4.2.1" "pyyaml>=3.1.0" "six>=1.16" "attrs>=19.3.0" "huggingface-hub>=0.14" \
  "cattrs>=1.1.0,<1.7" cloudpickle "gym>=0.21.0" "pettingzoo==1.15.0" "filelock>=3.4.0" \
  absl-py markdown packaging "setuptools<70" tensorboard-data-server werkzeug
pip install --no-deps mlagents==1.1.0 mlagents-envs==1.1.0 tensorboard==2.18.0
```

`pip check` then reports one expected mismatch (TensorBoard asks for grpcio ≥ 1.48.2); it does not affect training.

### Windows

```bash
conda create -n mlagents python=3.10.12
conda activate mlagents
pip install torch~=2.2.1 --index-url https://download.pytorch.org/whl/cu121
pip install mlagents==1.1.0
```

(Not yet verified on the PC.)

## Reading the code

Open this folder in VS Code (*File → Open Folder*). Unity is configured to open scripts in VS Code; install the *Unity* extension (`visualstudiotoolsforunity.vstuc`) and a .NET SDK for IntelliSense. Start with `Assets/Gym/Core/TradingEnv.cs`; `AGENTS.md` lists the rules for anyone, human or AI, who changes the code.
