# 在 Windows 电脑上训练（中文大白话版）

从一台什么都没装的 Windows 电脑，到跑完一组对比训练。除非特别说明，命令都在 **PowerShell** 里敲。

> **还没在 PC 上验证过。** 这一页是在 Mac 上写的。打包、冒烟、成批训练、评估这几步的 Mac 版命令在 Mac 上都跑通过，但下面这些 Windows 命令本身还没真跑过。照着做时哪里不一样，记下来、改这一页。

每一步都要记住的几条：

- Unity **必须是 6000.0.84f1**，`mlagents` **必须是 1.1.0**。版本不一样，ML-Agents 会出很难发现的毛病。
- 用命令行跑 Unity 之前，先把 Unity 编辑器关掉。同一个工程不能同时打开两次。
- **训练中途不要按 Ctrl+C。** 让它跑到 `max_steps` 自己停。实在要放弃一次训练，就别动它的 `results\<run-id>` 文件夹，换一个新的 run-id 重跑。
- `results\` 和 `Builds\` 只留在这台电脑上，不进 Git。`evaluations\log.csv` 进 Git，而且只追加、不改。
- 长时间训练时别让电脑睡着（*设置 → 系统 → 电源*：插电时从不睡眠）。

## 1. 装 Git

从 <https://git-scm.com/download/win> 下载 Git for Windows，一路默认安装。检查：

```powershell
git --version
```

## 2. 装 Unity Hub 和 Unity 6000.0.84f1

1. 从 <https://unity.com/download> 装 Unity Hub，登录。
2. 装编辑器 **6000.0.84f1**（LTS）：在浏览器里打开 `unityhub://6000.0.84f1/78ab6fc243d5`；或者在 Hub 里 *Installs → Install Editor → Archive → download archive*，找 6000.0.84f1。
3. 模块只勾 **Windows Build Support (Mono)**，别的都不用。

装好后编辑器在 `C:\Program Files\Unity\Hub\Editor\6000.0.84f1\Editor\Unity.exe`。后面要反复用，先存进一个变量：

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.0.84f1\Editor\Unity.exe'
Test-Path $unity
```

（`Test-Path` 显示 `True` 就对了。）

## 3. 装 Miniforge，建 `mlagents` 环境

1. 从 <https://github.com/conda-forge/miniforge> 下载 Windows x86_64 安装包，选「Just Me」安装。
2. 打开一次 **Miniforge Prompt**，运行 `conda init powershell`，然后关掉、重新开一个 PowerShell 窗口。
3. 照 README 的 Windows 一节建环境：

```powershell
conda create -n mlagents python=3.10.12
conda activate mlagents
pip install torch~=2.2.1 --index-url https://download.pytorch.org/whl/cu121
pip install mlagents==1.1.0
mlagents-learn --help
```

训练用 CPU（每份配置里都写了 `torch_settings.device: cpu`），所以装的是带 CUDA 的 PyTorch 也没关系，只是用不上。

## 4. 拿到仓库

```powershell
cd $HOME\Documents
git clone <仓库地址> ZHENN_Ledger_Gym
cd ZHENN_Ledger_Gym
```

仓库推到 GitHub 之后才有地址。在那之前，从 Mac 拷整个文件夹过来，但不要带 `Library\`、`Builds\`、`results\`、`Logs\` 这几个。

## 5. 用 Unity 打开一次

Unity Hub：*Projects → Add → Add project from disk*，选这个文件夹，用 6000.0.84f1 打开。第一次导入要好几分钟。等编辑器不转圈了，把它关掉。

## 6. 打 Windows 训练包

`Unity.exe` 是窗口程序，PowerShell 默认不等它跑完就往下走，所以要用 `Start-Process -Wait`：

```powershell
$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD`"",
  '-executeMethod', 'Gym.Editor.BuildScript.BuildWindowsTraining', '-quit',
  '-logFile', "`"$PWD\Logs\build-win.log`"")
$p.ExitCode   # 0 表示成功
Test-Path Builds\win\Gym.exe
```

退出码是 1 的话，看 `Logs\build-win.log` 的最后几行；如果是缺 *Windows Build Support (Mono)* 模块，那里会直接写出来。

第一次运行 `Gym.exe` 时，Windows 防火墙可能问要不要允许联网：在「专用网络」上允许（ML-Agents 通过本机的 5005 以上的端口和游戏通信）。

## 7. 测速度：跑 10 万步

```powershell
conda activate mlagents
$t = Measure-Command { mlagents-learn config\smoke-100k.yaml --env Builds\win\Gym.exe --run-id smoke-pc-100k --no-graphics }
"{0:N0} steps/s" -f (100000 / $t.TotalSeconds)
python tools\train\read_scalars.py results\smoke-pc-100k
```

把「每秒多少步」记下来。200 万步要 `2000000 ÷ 每秒步数` 秒。作为参考，Mac（Apple M5、10 核）单环境大约每秒 2,000～2,900 步，200 万步要 12～16 分钟。**正式训练每组跑多少步、几个种子，就按这个速度定。**

`read_scalars.py` 应该列出 `Environment/Cumulative Reward`、`Policy/Entropy` 和一串 `Trading/…`，其中 `Trading/FeeRate` = 0.001。`Trading/…` 要等第一批局走完才有：16 个智能体 × 720 步 = 11,520 步。

## 8. 跑对比组

第一次运行脚本时 PowerShell 可能不让跑，只对当前窗口放开：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
```

先快速试一下脚本能不能用（每个只跑 15,000 步，刚好够第一批局结束、看到 `Trading/…`；run-id 前面带 `smoke-`）：

```powershell
.\tools\train\run_series.ps1 -Env Builds\win\Gym.exe -Seeds 1 -Smoke -Configs config\ppo_base.yaml,config\variants\fee-0.003.yaml
```

再跑正式的，例如手续费对比、3 个种子：

```powershell
.\tools\train\run_series.ps1 -Env Builds\win\Gym.exe -Seeds 1,2,3 -NumEnvs 1 -Configs config\ppo_base.yaml,config\variants\fee-0.yaml,config\variants\fee-0.003.yaml
```

| 参数 | 意思 | 不写时 |
| --- | --- | --- |
| `-Env` | 训练包 | 必须写 |
| `-Configs` | 配置文件，逗号隔开 | 必须写 |
| `-Seeds` | 种子，逗号隔开（实际传的是种子 × 1000） | `1,2,3,4,5` |
| `-NumEnvs` | 每次训练同时开几个游戏；一个系列只能用一个值 | `1` |
| `-Prefix` | 加在每个 run-id 前面的词 | 不加 |
| `-Smoke` / `-SmokeSteps` | 在 `results\_tmp\` 里复制一份缩短版（15,000 步）再跑 | 关 |
| `-DryRun` | 只打印要跑的命令，不真跑 | 关 |

run-id 的格式是 `<配置名>-s<种子>-<年月日>`（UTC 日期）。`results\<run-id>` 已经存在的就跳过，绝不覆盖。每次训练留下 `results\<run-id>\`（里面有模型和 `config-used.yaml`）和 `results\<run-id>.log`。每份对比配置改了什么，见 `config\README.md`；改过配置后用 `python tools\train\check_configs.py` 检查。

脚本传给 mlagents-learn 的是 `--seed <种子 × 1000>`，并写进 `results\<run-id>\seed-used.txt`。原因：ML-Agents 给第 k 个环境的种子是「种子 + k」，种子 1、2、3 直接用的话，`-NumEnvs` 大于 1 时会撞号；乘 1000 就拉开了。每个智能体的随机起点都由这个种子推出来，所以配置、种子、`-NumEnvs` 都一样时，环境给的局完全一样。训练器那一侧也用了这个种子，但 PyTorch 不保证逐位相同，曲线可能还会有一点点差别。

## 9. 用 TensorBoard 看曲线

```powershell
tensorboard --logdir results
```

浏览器打开 <http://localhost:6006>。对比各组的 `Environment/Cumulative Reward`、`Policy/Entropy`，以及 `Trading/…` 那几条：成交笔数、换手、手续费、持币时间占比。

## 10. 评估

先把训练好的模型打进一个评估包，再在验证段上跑：

```powershell
$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD`"",
  '-executeMethod', 'Gym.Editor.BuildScript.BuildWindowsEval',
  '-gymModel', "`"$PWD\results\<run-id>\TradingAgent.onnx`"", '-quit',
  '-logFile', "`"$PWD\Logs\build-eval.log`"")
$p.ExitCode

$e = Start-Process -FilePath Builds\win\GymEval.exe -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-gymMode', 'eval', '-gymSegment', 'validation',
  '-gymFeeRate', '0.001', '-gymOut', "`"$PWD\evaluations`"")
$e.ExitCode
```

`-gymFeeRate` 用这个模型训练时的费率。测试段只留到最后出正式数字时用。对照组（每跑一次就追加几行）：

```powershell
$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD`"",
  '-executeMethod', 'Gym.Editor.EvalTools.RunBaselines', '-gymSegment', 'validation',
  '-gymOut', 'evaluations', '-quit', '-logFile', "`"$PWD\Logs\baselines.log`"")
python tools\eval\summarize.py evaluations\log.csv
```

## 11. 东西都放在哪

| 什么 | 在哪 | 进不进 Git |
| --- | --- | --- |
| 训练产物、模型、TensorBoard 记录 | `results\<run-id>\` | 不进 |
| 一次训练的终端输出 | `results\<run-id>.log` | 不进 |
| 打出来的包 | `Builds\win\` | 不进 |
| 评估流水和明细 | `evaluations\log.csv`、`evaluations\runs\` | 进（只追加） |
| 试验性的评估 | `evaluations\smoke\` | 不进 |

要在 Mac 上分析结果，把 `results\<run-id>` 文件夹拷过去就行；每个文件夹里的 `TradingAgent.onnx` 就是模型。
