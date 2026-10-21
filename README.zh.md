# ZHENN Ledger Gym（中文说明）

一个 Unity ML-Agents 环境：强化学习智能体按小时一根一根地走过 BTC/USDT 的历史 K 线，每小时决定买、卖还是不动、动多少；每一笔成交都要付手续费（这是第一、二轮的下单模式，现在仍是默认）。第三轮的旋钮模式里，智能体每天（每 24 根 K 线）只拧一次旋钮：币占总资产的目标比例 30 %、50 % 或 70 %，实际仓位偏离目标超过 5 个点时由调仓规则自动买卖调回去。另有四只币（ETH、ADA、DOGE、XRP）用来评估。智能体看到的是三档观测集里的一档：`base` 是第一轮的 35 个数（最近 32 根收盘价、仓位、浮盈、距上次交易多久；默认就是它），`long` 再加 6 个长周期的数（过去 1 天、1 周、30 天、90 天的收益率按波动率归一，过去 30 天的波动率，距过去 90 天最高点的回撤），`long-ta` 在 `long` 上再加按天算的 MACD 和 RSI，只当对照组。每小时的得分是资产的对数收益（乘 100、截到 ±1）；可选的风险系数（环境参数 `risk_aversion`，默认 0，此时得分不变）在每个亏损的小时再扣一笔「亏损的平方」的罚款。

这是一个大学课程项目（德布勒森大学「强化学习导论」，2026 年秋）。它**不是**交易策略，**不是**投资建议，也不承诺任何收益。它要回答的问题是：手续费怎样改变智能体学到的做法，而不是它能不能赚钱。

*English: [README.md](README.md).*

## 现在到哪了

Mac 上已经能用：环境和测试、键盘试玩场景、看训练好的模型交易的观战场景、训练包、短的冒烟训练、带对照组的评估流程、对比组配置和成批训练脚本。下一步：在 Windows 电脑上做正式训练（见 [docs/pc-training.zh.md](docs/pc-training.zh.md)）。Windows 打包和 `run_series.ps1` 还没有在 PC 上跑过。

## Mac 快速开始

先要有：Unity Hub 和 Unity **6000.0.84f1**，以及照 [docs/setup.md](docs/setup.md#python-environment)（英文）建好的 `mlagents` 环境。命令行跑 Unity 之前，先把 Unity 编辑器关掉，同一个工程不能同时打开两次。下面每条命令都在克隆下来的仓库根目录里敲（就是放这份 README 的那个文件夹）。Windows 上的命令见 [docs/pc-training.zh.md](docs/pc-training.zh.md)。

```bash
UNITY=/Applications/Unity/Hub/Editor/6000.0.84f1/Unity.app/Contents/MacOS/Unity

# 1. 跑测试（退出码 0 表示全过）
"$UNITY" -batchmode -nographics -projectPath "$PWD/unity" -runTests -testPlatform EditMode -testResults "$PWD/unity/Logs/editmode-results.xml" -logFile "$PWD/unity/Logs/editmode.log"
"$UNITY" -batchmode -nographics -projectPath "$PWD/unity" -runTests -testPlatform PlayMode -testResults "$PWD/unity/Logs/playmode-results.xml" -logFile "$PWD/unity/Logs/playmode.log"

# 2. 打训练包：unity/Builds/mac/Gym.app
"$UNITY" -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.BuildScript.BuildMacTraining -quit -logFile "$PWD/unity/Logs/build-mac.log"

# 3. 跑一次 3 万步的冒烟训练（M5 上大约 15 秒）
conda activate mlagents
RUN=smoke-$(date +%Y%m%d-%H%M%S)
mlagents-learn config/smoke.yaml --env unity/Builds/mac/Gym.app --run-id $RUN --no-graphics
python scripts/train/read_scalars.py results/$RUN

# 4. 用这个模型在验证段评估一次（结果写进被 Git 忽略的 evaluations/smoke/）
"$UNITY" -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.BuildScript.BuildMacEval -gymModel "$PWD/results/$RUN/TradingAgent.onnx" -quit -logFile "$PWD/unity/Logs/build-eval.log"
unity/Builds/mac/GymEval.app/Contents/MacOS/ZHENN_Ledger_Gym -batchmode -nographics -gymMode eval -gymSegment validation -gymFeeRate 0.001 -gymOut "$PWD/evaluations/smoke"
python scripts/eval/summarize.py evaluations/smoke/log.csv
```

自己上手玩：用 Unity 打开工程（`unity/` 文件夹），打开 `Assets/Gym/Scenes/Play.unity`，把 Game 窗口的比例设成 16:9，点 Play，先在 Game 窗口里点一下，然后：`1`～`4` 选 10 / 25 / 50 / 100 %，`B` 买、`S` 卖、`H` 或空格不动（每按一次走一根 K 线），`P` 开关自动播放，`R` 重开，`L` 或左上角的按钮把画面上的字在中文（默认）和英文之间切换。左上角是读数面板；K 线图右边是价格刻度，每个 UTC 0 点下面标着日期；左下角的口袋上写着现金，旁边是未实现盈亏和已实现盈亏，买入成交时口袋上方飘起红色的 `-$金额`，卖出成交时绿色的 `+$金额` 掉进口袋。画面限在每秒 30 帧（场景里的 `FrameRateLimiter`，想更顺滑就在那里改成 60）。最新那根 K 线上站着一个头像框小人，每走一步动一下：不动就跳一下；买入成交时跳起来变大一点，头上冒出 `买入 25%`；卖出成交时跳起来往左歪一下，头上冒出 `卖出 50%`；被拒时不跳、左右晃，头上是灰色的 `被拒`。脚下前几根 K 线比最新那根高时，小人升到它们上方，用一根细杆指着最新那根。

看训练好的模型自己交易：点 Unity 菜单 **Gym > Watch a Model...**，选一个 `.onnx` 文件（训练完在 `results/<run-id>/TradingAgent.onnx`）。程序把它导入到 `unity/Assets/Gym/Models/Imported/`（不进 Git），记在这个工程自己的 `unity/UserSettings/Gym/watch-model.json` 里，然后打开 `Assets/Gym/Scenes/Watch.unity` 进入播放；下次直接打开 `Watch.unity` 点 Play，看的还是这个模型。观战画面和试玩画面是同一套，只是换成模型来下单：`P` 暂停和继续，暂停时 `N` 走一步，`[`、`]` 在每秒 1、2、5、10、20、50、100、200、500 步里调慢调快（开始是每秒 5 步），`R` 回到段首重来，`L` 切换语言。默认看验证段、从段首开始、用 agent 上的默认费用（费率 0.1 %、固定费 0、滑点 0）；段和三项费用是 `WatchController` 在 Inspector 里的字段。测试段留到最后出正式数字时再看。走到段尾画面就停在最后一步，按 `R` 重来。观战走的就是评估包那条路：推理设置一样（只推理、确定性、Burst），一步一步推的走法也一样；用冒烟训练出的模型实测，评估流水那一行和最终总资产逐位相同。正式数字仍以评估包为准。没选模型时，画面上会说去点哪个菜单。观战用的观测集跟 `gym-config.json` 的 `observationSet`（默认 `base`）或命令行 `-gymObs` 走：要看 `long` 或 `long-ta` 训练出的模型，先把 `observationSet` 改成对应的档（或者启动编辑器时带 `-gymObs`）；档不对时观战菜单会拒绝这个模型，说维数对不上。观战只在 Unity 编辑器里用，不进打包清单。

想给小人换头像：把 `avatar.png`（或 `avatar.jpg`、`avatar.jpeg`）放进 `unity/Assets/StreamingAssets/Gym/avatar/`，建议 256 × 256 像素左右的正方形图，别的形状会按原比例缩进框里。没有图、或者文件打不开（会打一条警告），就显示灰色的占位小人。详见 [avatar/README.md](unity/Assets/StreamingAssets/Gym/avatar/README.md)。

## 目录

| 位置 | 里面是什么 |
| --- | --- |
| `unity/` | Unity 工程；用 Unity 打开的就是这个文件夹 |
| `unity/Assets/Gym/Core/` | `Gym.Core`：纯 C# 的交易环境（不引用 Unity），一个概念一个文件夹、一个命名空间：`Market/`（K 线、币种规则、分段：`CandleSeries`、`SymbolRules`、`SegmentSpec`、`SplitValidator`）、`Accounting/`（账户和手续费：`Account`、`CostModel`）、`Env/`（动作和遮罩、两种动作模式和调仓规则、观测集、奖励、一局、种子、混训时一局用哪只币：`TradingEnv`、`TradeAction`、`ActionCodec`、`ActionMode`、`DialSettings`、`Rebalancer`、`ObservationSet`、`ObservationBuilder`、`RewardFunction`、`SeedMixer`、`SymbolPicker`）、`Evaluation/`（指标、对照组、评估流水：`Metrics`、`Baselines`、`EvaluationLog`、`JsonWriter`） |
| `unity/Assets/Gym/Runtime/` | `Gym.Runtime`：`Agents/`（包在 `TradingEnv` 外面的 ML-Agents 智能体 `TradingAgent`，以及 `EpisodeStats`）、`Configuration/`（读配置：`GymConfigLoader`、`GymDataCache`）、`Evaluation/`（评估跑手 `EvalRunner`）、`Play/`（试玩场景：`PlayController`；画面组件 `HudView`、`CandleChartView`、`WalletView`、`AvatarView`、`PlayLanguageSwitch`、`FrameRateLimiter`；文字表 `PlayText` 和按名字找系统字体的 `PlayFont`；头像图 `AvatarPictureLoader` 和占位图 `AvatarPlaceholder`；纯函数 `HudReadout`、`PriceScale`、`TimeAxis`、`MoneyText`、`FloatingAmount`、`AvatarAnimation`、`AvatarPose`、`AvatarHint`；画图的小工具 `WorldText`、`ShapeLayer`、`ColoredMeshBuilder`、`PocketShape`、`PlayLayout`、`PlayPalette`）、`Watch/`（观战场景：`WatchController`、它的播放逻辑 `WatchPlayback` 和它推着走的 `AgentWatchTarget`；提示 `WatchNotice`；记住的模型 `WatchModelMemory`） |
| `unity/Assets/Gym/Editor/` | `BuildScript`（打训练包和评估包）、`EvalTools`（对照组评估）、`GymSceneBuilder`（场景生成）、`PlayChecklist` 和 `PlaySnapshot`（试玩核对表和截图工具）、`WatchSetup`（菜单 **Gym > Watch a Model...**）、`WatchModelAttacher`（把记住的模型挂到观战的智能体上）、`WatchSnapshot`（观战截图） |
| `unity/Assets/Gym/Scenes/` | `Training`（16 个智能体）、`Play`（键盘试玩）、`Watch`（训练好的模型来交易；只在编辑器里用，不进打包清单）、`Eval`（1 个智能体加评估跑手） |
| `unity/Assets/Gym/Prefabs/` | `TradingAgent.prefab`（里面写的是 `base` 的 35 个观测和下单模式的动作，智能体开场时改成自己那一档观测的大小，旋钮模式下再把动作形状改成旋钮的）；它的观测和动作见 [docs/architecture.md](docs/architecture.md)（英文） |
| `unity/Assets/Gym/Materials/` | `CandleChart.mat`，试玩场景里 K 线图用的材质 |
| `unity/Assets/Gym/Models/` | 打评估包时、以及用 **Gym > Watch a Model...** 选模型时导入的模型放进 `Imported/`，不进 Git（见 [Models/README.md](unity/Assets/Gym/Models/README.md)） |
| `unity/Assets/Gym/Tests/` | EditMode 测试在 `Core/`（内核，按同样的四个文件夹摆）、`Runtime/`、`Editor/`（智能体预制体、场景和对照组工具 `EvalTools`）；PlayMode 测试在 `PlayMode/` |
| `unity/Assets/StreamingAssets/Gym/` | `gym-config.json`、`symbols.json` 和数据（BTCUSDT、ETHUSDT、ADAUSDT、DOGEUSDT、XRPUSDT 各一份 `data/<币名>-1h.csv`，各自旁边一份清单，加上 `DATA-LICENSE.md`）；放头像图的 `avatar/`；每个包里都带一份 |
| `unity/Packages/` | `manifest.json` 和 `packages-lock.json`：Unity 包和它们的版本 |
| `unity/ProjectSettings/` | Unity 的工程设置，编辑器版本记在 `ProjectVersion.txt` 里 |
| `config/` | 训练配置（`ppo_base.yaml`、几份冒烟配置）、对比组（`variants/`）和第二轮的配置（`v2/`）；见 [config/README.md](config/README.md) |
| `scripts/data/` | 从 Binance Vision 下载数据的脚本（`fetch_binance_klines.py`），和算几只币收益率相关性的脚本（`correlation.py`） |
| `scripts/train/` | 成批训练脚本（`run_series.sh`、`run_series.ps1`）、配置检查（`check_configs.py`）、读 TensorBoard 曲线（`read_scalars.py`） |
| `scripts/eval/` | 汇总评估流水的 `summarize.py` |
| `evaluations/` | 只追加的评估流水（`log.csv`）和每次评估的明细（`runs/*.json`），进 Git |
| `docs/` | 下面列的这些文档 |
| `results/`、`data/raw/`、`unity/Builds/`、`evaluations/smoke/`、`unity/UserSettings/` | 本机产物和本机设置（包括观战记住的模型），不进 Git |

## 文档

除了 PC 训练指南有中文版，其余都是英文。

- [docs/setup.md](docs/setup.md)：版本、Python 环境（macOS 和 Windows）、用 VS Code 看代码
- [docs/architecture.md](docs/architecture.md)：三层结构、一步怎么走、三档观测集和它们的公式、两种动作模式（下单和旋钮）和调仓规则、奖励和它的风险系数、种子、从哪个文件开始读
- [docs/data.md](docs/data.md)：五只币的数据、许可、怎么重新生成、缺口怎么补、各币和 BTC 的相关性、分段（训练段 2017-08-17～2021-12-31；验证段三段：validation-bear 2022 年、validation-recovery 2023-01～2024-08、validation 2024-09～2025-08；测试段 2025-09～2026-08）
- [docs/evaluation.md](docs/evaluation.md)：评估流水、指标、评估段、用 `-gymSymbol` 选币、用 `-gymObs` 选观测集、用 `-gymActionMode` 选动作模式、模型输入维数的核对，六个对照组（买入持有、一直拿现金、随机，加三条简单规则：`momentum_90d` 过去 90 天涨才持有、`sma_cross` 1 周均线在 1 月均线之上才持有、`vol_target` 按波动率调仓），和第三轮的两个旋钮对手（`constant_mix_50` 永远 50 %、`dial_momentum_90d` 过去 90 天涨了拧到 70 %、否则 30 %）
- [docs/training.md](docs/training.md)：在 Mac 上跑成批训练、观测集 `obs_set`、风险系数 `risk_aversion`、动作模式 `action_mode` 和 `decision_hours`、`rebalance_band`、混着几只币训练（`trainSymbols`、`-gymTrainSymbols`），以及 `Trading/*` 曲线的意思
- [docs/development.md](docs/development.md)：重建场景、试玩核对表和截图、要训练好的模型才能跑的测试；Unity 会自己写哪些文件
- [docs/pc-training.zh.md](docs/pc-training.zh.md)（[英文版](docs/pc-training.md)）：在 Windows 电脑上训练
- [config/README.md](config/README.md)：训练配置和对比组
- [AGENTS.md](AGENTS.md)：改代码之前要读的规矩（人和 AI 都一样）

## 数据和许可声明

环境回放 [Binance Vision](https://data.binance.vision) 的现货 1 小时 K 线：BTC/USDT（主币），以及 ETH、ADA、DOGE、XRP 对 USDT（用 `-gymSymbol` 选）。训练用当前币（`-gymSymbol` 选中的那只，不写时是 BTC）；混训（BTC、ETH、ADA、XRP 每局一只）见训练清单（命令行 `-gymTrainSymbols` 或 `gym-config.json` 的 `trainSymbols`，见 [docs/training.md](docs/training.md)，英文）；DOGE 只考不训。数据按 [CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/) 许可；署名和改动清单见 [`DATA-LICENSE.md`](unity/Assets/StreamingAssets/Gym/data/DATA-LICENSE.md)。本项目与 Binance 没有任何关联，也没有得到它的赞助或认可。代码暂时没有许可证文件。数据来源、条款、怎么重新生成、缺口怎么补，见 [docs/data.md](docs/data.md)（英文）。
