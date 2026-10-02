# ZHENN Ledger Gym（中文说明）

一个 Unity ML-Agents 环境：强化学习智能体按小时一根一根地走过 BTC/USDT 的历史 K 线，每小时决定买、卖还是不动、动多少；每一笔成交都要付手续费。

这是一个大学课程项目（德布勒森大学「强化学习导论」，2026 年秋）。它**不是**交易策略，**不是**投资建议，也不承诺任何收益。它要回答的问题是：手续费怎样改变智能体学到的做法，而不是它能不能赚钱。

*English: [README.md](README.md).*

## 现在到哪了

Mac 上已经能用：环境和测试、键盘试玩场景、训练包、短的冒烟训练、带对照组的评估流程、对比组配置和成批训练脚本。下一步：在 Windows 电脑上做正式训练（见 [docs/pc-training.zh.md](docs/pc-training.zh.md)）。Windows 打包和 `run_series.ps1` 还没有在 PC 上跑过。

## Mac 快速开始

先要有：Unity Hub 和 Unity **6000.0.84f1**，以及下面「Python 环境」一节建好的 `mlagents` 环境。命令行跑 Unity 之前，先把 Unity 编辑器关掉，同一个工程不能同时打开两次。

```bash
cd ZHENN_Ledger_Gym
UNITY=/Applications/Unity/Hub/Editor/6000.0.84f1/Unity.app/Contents/MacOS/Unity

# 1. 跑测试（退出码 0 表示全过）
"$UNITY" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -testResults "$PWD/Logs/editmode-results.xml" -logFile "$PWD/Logs/editmode.log"
"$UNITY" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform PlayMode -testResults "$PWD/Logs/playmode-results.xml" -logFile "$PWD/Logs/playmode.log"

# 2. 打训练包：Builds/mac/Gym.app
"$UNITY" -batchmode -nographics -projectPath "$PWD" -executeMethod Gym.Editor.BuildScript.BuildMacTraining -quit -logFile "$PWD/Logs/build-mac.log"

# 3. 跑一次 3 万步的冒烟训练（M5 上大约 15 秒）
conda activate mlagents
RUN=smoke-$(date +%Y%m%d-%H%M%S)
mlagents-learn config/smoke.yaml --env Builds/mac/Gym.app --run-id $RUN --no-graphics
python tools/train/read_scalars.py results/$RUN

# 4. 用这个模型在验证段评估一次（结果写进被 Git 忽略的 evaluations/smoke/）
"$UNITY" -batchmode -nographics -projectPath "$PWD" -executeMethod Gym.Editor.BuildScript.BuildMacEval -gymModel results/$RUN/TradingAgent.onnx -quit -logFile "$PWD/Logs/build-eval.log"
Builds/mac/GymEval.app/Contents/MacOS/ZHENN_Ledger_Gym -batchmode -nographics -gymMode eval -gymSegment validation -gymFeeRate 0.001 -gymOut "$PWD/evaluations/smoke"
python tools/eval/summarize.py evaluations/smoke/log.csv
```

自己上手玩：用 Unity 打开工程，打开 `Assets/Gym/Scenes/Play.unity`，点 Play，先在 Game 窗口里点一下，然后：`1`～`4` 选 10 / 25 / 50 / 100 %，`B` 买、`S` 卖、`H` 或空格不动（每按一次走一根 K 线），`P` 开关自动播放，`R` 重开。

## 在 Windows 电脑上训练

长时间训练放在 Windows 电脑上，步骤见 [docs/pc-training.zh.md](docs/pc-training.zh.md)（英文版 [docs/pc-training.md](docs/pc-training.md)）。一句话：装同样版本的 Unity 和 Python 环境，用 `BuildWindowsTraining` 打包，先用 `config/smoke-100k.yaml` 跑 10 万步测速度，再用 `tools/train/run_series.ps1` 跑对比组。

## 目录

| 位置 | 里面是什么 |
| --- | --- |
| `Assets/Gym/Core/` | `Gym.Core`：纯 C# 的交易环境（不引用 Unity）：K 线、账户和手续费、动作和遮罩、观测、奖励、一局、分段、指标、对照组、评估流水 |
| `Assets/Gym/Runtime/` | `Gym.Runtime`：包在 `TradingEnv` 外面的 ML-Agents 智能体、读配置、试玩场景的看板和 K 线图、评估跑手 |
| `Assets/Gym/Editor/` | 打包脚本、对照组评估、场景生成、试玩核对表和截图工具 |
| `Assets/Gym/Scenes/` | `Training`（16 个智能体）、`Play`（键盘试玩）、`Eval`（1 个智能体加评估跑手） |
| `Assets/Gym/Tests/` | EditMode 测试（内核和编辑器）、PlayMode 测试 |
| `Assets/StreamingAssets/Gym/` | `gym-config.json`、`symbols.json` 和数据；每个包里都带一份 |
| `config/` | 训练配置和对比组（见 [config/README.md](config/README.md)） |
| `tools/data/` | 从 Binance Vision 下载数据的脚本 |
| `tools/train/` | 成批训练脚本、配置检查、读 TensorBoard 曲线 |
| `tools/eval/` | 汇总评估流水的 `summarize.py` |
| `evaluations/` | 只追加的评估流水和每次评估的明细（进 Git） |
| `docs/` | PC 训练指南 |
| `results/`、`Builds/`、`evaluations/smoke/` | 本机产物，不进 Git |

## 数据和许可声明

**数据来源：Binance Vision。** 环境回放 [Binance Vision](https://data.binance.vision) 的 BTC/USDT 现货 1 小时 K 线，从 2017-08-17 04:00 到 2026-08-31 23:00（UTC）。处理好的文件就在仓库里：`Assets/StreamingAssets/Gym/data/BTCUSDT-1h.csv`，旁边的清单记着每个原始压缩包和这个 CSV 的 SHA-256。

数据按 [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/) 许可，依据 [Binance Vision 数据条款 v1.0（2026-08-26）](https://github.com/binance/binance-public-data/blob/master/TERMS_AND_CONDITIONS.md)；署名和改动清单见 [`DATA-LICENSE.md`](Assets/StreamingAssets/Gym/data/DATA-LICENSE.md)。本项目与 Binance 没有任何关联，也没有得到它的赞助或认可。代码暂时没有许可证文件。

重新生成数据（只用 Python 标准库；原始压缩包缓存在被 Git 忽略的 `data/raw/`）：

```bash
python tools/data/fetch_binance_klines.py --self-test
python tools/data/fetch_binance_klines.py --symbol BTCUSDT
```

脚本默认只取到 2026-08（测试段的最后一个月；要加新月份就传 `--end YYYY-MM`），并且把 2018 年 2 月那 43 根开在 hh:28、不在整点的 K 线扔掉，那几个小时按前一根收盘价补平（`--off-hour error` 改成报错停下，`--off-hour floor` 改成挪到整点）。所以不带别的参数，生成的就是仓库里这一份：清单里的 `csv_sha256` 是 `4739c139dc501e38498589359db093394dcee86cabde5a6e37c12d726d084242`。

如果原始压缩包是重新下载的（换了一台机器，或者 `data/raw/` 是空的），CSV 不变，清单只有 `downloaded_at_utc`（压缩包存到本机的时间）会变，这一处改动不用提交。生成结果里如果出现仓库这份数据没有的错位 K 线或超过 24 小时的缺口，脚本会打一行 `!!! WARNING`（仓库这份有 43 根错位 K 线、一段从 2018-02-08 01:00 起的 75 小时缺口），以后用 `--end` 加月份时，新冒出来的一长段平线就不会悄悄混进去。

三段（在 `gym-config.json` 里）：训练 2017-08-17～2024-08-31，验证 2024-09-01～2025-08-31，测试 2025-09-01～2026-08-31。测试段里 BTC 跌了 27.4 %，所以在那一段「一直拿现金」赢了「买入持有」。

## 评估流水

每次评估都是在验证段或测试段上从头走到尾：从第一根 K 线开始、全拿现金、没有随机，成交和手续费的规则和训练时一模一样。每次评估往 `evaluations/log.csv` 追加一行，再往 `evaluations/runs/` 写一份带全部指标和设置的 JSON。**流水只追加**：已有的行永远不改不删；表头和现在的列对不上就拒绝写入。同一个评估跑两次，就会多两行。

指标：总收益、最大回撤、年化夏普比率（按小时对数收益，乘 √8760）、成交笔数、被拒笔数、换手（成交额 ÷ 平均权益）、手续费（USDT 和占期初权益的百分比）、持币时间占比（走完一步后手里还有币的步数占比）。

对照组（买入持有、一直拿现金、随机策略；随机策略用种子 0～99，报中位数和收益的第 5、第 95 百分位），三档费率 0、0.1 %、0.3 %：

```bash
"$UNITY" -batchmode -nographics -projectPath "$PWD" -executeMethod Gym.Editor.EvalTools.RunBaselines -gymSegment validation -gymOut evaluations -quit -logFile "$PWD/Logs/baselines.log"
python tools/eval/summarize.py evaluations/log.csv
```

可选参数：`-gymSegment validation|test`、`-gymFeeRates 0,0.001,0.003`、`-gymRandomSeeds 100`、`-gymOut <目录>`、`-gymConfig <文件>`。训练好的模型用单独的评估包评估（`BuildMacEval` / `BuildWindowsEval` 加 `-gymModel`），每一行都会记下 run-id 和模型的 SHA-256；`-gymFixedFee`、`-gymSlippage` 设定另外两项费用。

## 版本（每台机器都必须一样）

| 组件 | 版本 |
| --- | --- |
| Unity 编辑器 | 6000.0.84f1（LTS） |
| ML-Agents Unity 包 | `com.unity.ml-agents` 4.0.3 |
| Python | 3.10.12（Miniforge / conda） |
| `mlagents` Python 包 | 1.1.0 |
| PyTorch | 2.2.x |

用别的 Unity 版本打开工程，或者换了 `mlagents` 的版本，是 ML-Agents 最常见的出毛病方式。

## Python 环境

### macOS（Apple Silicon）

`mlagents` 1.1.0 钉死的 `grpcio` 1.48.2 在 PyPI 和 conda-forge 上都没有 Apple Silicon 版。改用 conda-forge 的 1.48.1，其余依赖一个个装：

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

之后 `pip check` 会报一处意料之中的不匹配（TensorBoard 要 grpcio ≥ 1.48.2），不影响训练。

### Windows

```bash
conda create -n mlagents python=3.10.12
conda activate mlagents
pip install torch~=2.2.1 --index-url https://download.pytorch.org/whl/cu121
pip install mlagents==1.1.0
```

（还没在 PC 上验证。）

## 读代码

用 VS Code 打开这个文件夹（*File → Open Folder*）。Unity 已经设成用 VS Code 打开脚本；装上 *Unity* 扩展（`visualstudiotoolsforunity.vstuc`）和 .NET SDK 就有代码提示。从 `Assets/Gym/Core/TradingEnv.cs` 看起；改代码之前先读 `AGENTS.md` 里的规矩（人和 AI 都一样）。
