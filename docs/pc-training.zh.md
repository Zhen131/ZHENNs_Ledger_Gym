# 在 Windows 电脑上训练（中文大白话版）

从一台什么都没装的 Windows 电脑，到跑完一组对比训练。除非特别说明，命令都在 **PowerShell** 里敲。

> **还没在 PC 上验证过。** 这一页是在 Mac 上写的。打包、冒烟、成批训练、评估这几步的 Mac 版命令在 Mac 上都跑通过，但下面这些 Windows 命令本身还没真跑过。照着做时哪里不一样，记下来、改这一页。

每一步都要记住的几条：

- Unity **必须是 6000.0.84f1**，`mlagents` **必须是 1.1.0**。版本不一样，ML-Agents 会出很难发现的毛病。
- 用命令行跑 Unity 之前，先把 Unity 编辑器关掉。同一个工程不能同时打开两次。
- **训练中途不要按 Ctrl+C。** 让它跑到 `max_steps` 自己停。实在要放弃一次训练，就别动它的 `results\<run-id>` 文件夹，换一个新的 run-id 重跑：用成批脚本时加 `-Prefix`（例如 `-Prefix rerun1`），run-id 就变成新的。
- `results\` 和 `unity\Builds\` 只留在这台电脑上，不进 Git。`evaluations\log.csv` 进 Git，而且只追加、不改。**正式的评估流水只在这台 PC 上追加**（训练结果都在 PC 上）：追加前先 `git pull`（还没有连上 GitHub、仓库是拷过来的时候跳过 `git pull`），追加后马上提交。Mac 上的评估一律写进 `evaluations\smoke\`。
- 长时间训练时别让电脑睡着（*设置 → 系统 → 电源*：插电时从不睡眠）。

## 1. 装 Git

从 <https://git-scm.com/download/win> 下载 Git for Windows，一路默认安装。检查：

```powershell
git --version
```

## 2. 装 Unity Hub 和 Unity 6000.0.84f1

1. 从 <https://unity.com/download> 装 Unity Hub，登录。
2. 装编辑器 **6000.0.84f1**（LTS）：在浏览器里打开 `unityhub://6000.0.84f1/78ab6fc243d5`；或者在 Hub 里 *Installs → Install Editor → Archive → download archive*，找 6000.0.84f1。
3. 不用额外勾模块：Windows 版编辑器自己就能打 Windows 包。第 6 步打包时如果报缺模块，再照报错里写的模块名，回 Hub 去装（*Installs →* 这个版本右边的齿轮 *→ Add modules*）。

装好后编辑器在 `C:\Program Files\Unity\Hub\Editor\6000.0.84f1\Editor\Unity.exe`。后面要反复用，先存进一个变量：

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.0.84f1\Editor\Unity.exe'
Test-Path $unity
```

（`Test-Path` 显示 `True` 就对了。）

## 3. 装 Miniforge，建 `mlagents` 环境

1. 从 <https://github.com/conda-forge/miniforge> 下载 Windows x86_64 安装包，选「Just Me」安装。
2. **先放开 PowerShell 运行脚本的开关。** Windows 有一个「允许运行哪些脚本」的开关（叫执行策略），家用版和专业版默认一个脚本都不让跑。下一步的 `conda init` 会往 PowerShell 每次打开时自动运行的脚本里写东西；不先放开，重开窗口时最上面会出一行红字（「因为在此系统上禁止运行脚本」），之后 `conda` 命令就找不到了。打开 PowerShell，运行：

   ```powershell
   Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
   ```

   问「是否要更改执行策略」时输入 `Y`、回车。这条命令只对你这个用户放开：本机写的脚本（比如 conda 的、仓库里的 `run_series.ps1`）可以跑，从网上下载、没有签名的脚本照样拦着。
3. 打开一次 **Miniforge Prompt**，运行 `conda init powershell`，然后关掉所有 PowerShell 窗口、重新开一个。**新窗口最上面不该有红字。** 有红字就回到第 2 步。
4. 照 [setup.md 的 Windows 一节](setup.md#windows)（英文）建环境，装的是 **CUDA 版** PyTorch（能用显卡，也能用 CPU）：

```powershell
conda create -n mlagents python=3.10.12
conda activate mlagents
pip install torch~=2.2.1 --index-url https://download.pytorch.org/whl/cu121
pip install mlagents==1.1.0
mlagents-learn --help
python -c "import torch; print(torch.cuda.is_available())"
```

最后一行显示 `True`，说明 PyTorch 找到了显卡；显示 `False` 就只能用 CPU，第 7 步显卡那次测速跳过。训练用 CPU 还是显卡，第 7 步各测一次再定。

## 4. 拿到仓库

放在 `E:\project` 这种**不会被 OneDrive 同步**的地方，不要放进「文档」「桌面」：很多 Windows 11 把这两个文件夹交给 OneDrive 同步，而 Unity 工程的 `Library\` 里有几万个小文件，同步盘会反复上传、还会锁住文件，打包和导入就会出莫名其妙的错。这和把 Unity 工程放在 iCloud、Dropbox 里是同一个坑。

```powershell
mkdir E:\project
cd E:\project
git clone <仓库地址> ZHENNs_Ledger_Gym
cd ZHENNs_Ledger_Gym
```

仓库推到 GitHub 之后才有地址。在那之前，从 Mac 拷整个文件夹到 `E:\project\ZHENNs_Ledger_Gym`，但不要带 `unity\Library\`、`unity\Builds\`、`results\`、`unity\Logs\` 这几个。后面的命令都在 `E:\project\ZHENNs_Ledger_Gym` 里敲。

## 5. 用 Unity 打开一次

Unity Hub：*Projects → Add → Add project from disk*，选 `E:\project\ZHENNs_Ledger_Gym\unity`，用 6000.0.84f1 打开。第一次导入要好几分钟。等编辑器不转圈了，把它关掉。

## 6. 打 Windows 训练包

`Unity.exe` 是窗口程序，PowerShell 默认不等它跑完就往下走，所以要用 `Start-Process -Wait`：

```powershell
$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD\unity`"",
  '-executeMethod', 'Gym.Editor.BuildScript.BuildWindowsTraining', '-quit',
  '-logFile', "`"$PWD\unity\Logs\build-win.log`"")
$p.ExitCode   # 0 表示成功
Test-Path unity\Builds\win\Gym.exe
```

退出码是 1 的话，看 `unity\Logs\build-win.log` 的最后几行；如果是缺模块，那里会写出模块名，照第 2 步去装。

## 7. 测速度：CPU 和显卡各跑 10 万步

两次都跑，谁快就用谁。每次前后各记一下时间，所以 `mlagents-learn` 的输出照常一边跑一边显示：

```powershell
conda activate mlagents

# CPU
$t0 = Get-Date
mlagents-learn config\smoke-100k.yaml --env unity\Builds\win\Gym.exe --run-id smoke-pc-100k-cpu --no-graphics
"CPU: {0:N0} steps/s" -f (100000 / ((Get-Date) - $t0).TotalSeconds)

# 显卡（第 3 步那行显示 False 的话跳过）
$t0 = Get-Date
mlagents-learn config\smoke-100k-cuda.yaml --env unity\Builds\win\Gym.exe --run-id smoke-pc-100k-cuda --no-graphics
if ($LASTEXITCODE -ne 0) {
  "GPU: 显卡这次失败，用 CPU"   # 报错退出时不算速度
} else {
  "GPU: {0:N0} steps/s" -f (100000 / ((Get-Date) - $t0).TotalSeconds)
}

python scripts\train\read_scalars.py results\smoke-pc-100k-cpu
```

第一次跑 `mlagents-learn` 时，Windows 防火墙可能弹窗问 **Python**（`python.exe`）要不要联网。点「允许」或「取消」都行：`mlagents-learn` 只是在本机的 5005 端口等训练包连上来，不走外网，本机内部的连接不受防火墙影响。

- 两个「每秒多少步」都记下来，**谁快就用谁**。显卡那次如果中途报错退出（ML-Agents 有个已知毛病，报错里会有 `Expected all tensors to be on the same device`），最后一行会打印「显卡这次失败，用 CPU」、不算速度：报错退出的那次用时很短，硬算出来的「每秒步数」会大得离谱，看着像显卡更快。这时就用 CPU。
- 用显卡更快的话，第 8 步成批训练加 `-Device cuda`；用 CPU 就什么都不用加。
- 200 万步要 `2000000 ÷ 每秒步数` 秒。作为参考，Mac（Apple M5、10 核，CPU）单环境大约每秒 2,000～2,900 步，200 万步要 12～16 分钟。**正式训练每组跑多少步、几个种子，就按快的那个速度定。**

`read_scalars.py` 应该列出 `Environment/Cumulative Reward`、`Policy/Entropy` 和一串 `Trading/…`，其中 `Trading/FeeRate` = 0.001。`Trading/…` 要等第一批局走完才有：16 个智能体 × 720 步 = 11,520 步。每条 `Trading/…` 是什么意思，见 [training.md](training.md#training-curves-trading-in-tensorboard)（英文）的「Training curves」表。

## 8. 跑对比组

第 3 步那条 `Set-ExecutionPolicy` 执行过的话，脚本直接就能跑。万一还是红字，看是哪一种：

- 「无法加载文件 …\run_series.ps1，因为在此系统上禁止运行脚本」：第 3 步那条没生效。
- 「无法加载文件 …\run_series.ps1。未对文件 …\run_series.ps1 进行数字签名」：仓库是用浏览器下载 zip 解压出来的。Windows 给下载来的每个文件都打了「来自网络」的标记，第 3 步放开的只是本机写的脚本，带这个标记的照样拦着。

第二种可以在仓库目录（`E:\project\ZHENNs_Ledger_Gym`）里先把标记去掉，以后新开的窗口也不用再管（`unity\Library\` 里文件多，要等一会儿）：

```powershell
Get-ChildItem -Recurse | Unblock-File
```

两种都可以用备用的办法：只对当前这个窗口放开，关掉窗口就恢复：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
```

先快速试一下脚本能不能用（每个只跑 15,000 步，刚好够第一批局结束、看到 `Trading/…`；run-id 前面带 `smoke-`）：

```powershell
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1 -Smoke -Configs config\ppo_base.yaml,config\variants\fee-0.003.yaml
```

再跑正式的，例如手续费对比、3 个种子（第 7 步显卡更快的话，末尾加 `-Device cuda`）：

```powershell
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1,2,3 -NumEnvs 1 -Configs config\ppo_base.yaml,config\variants\fee-0.yaml,config\variants\fee-0.003.yaml
```

| 参数 | 意思 | 不写时 |
| --- | --- | --- |
| `-Env` | 训练包 | 必须写 |
| `-Configs` | 配置文件，逗号隔开 | 必须写 |
| `-Seeds` | 种子，逗号隔开，不能是负数（实际传的是种子 × 1000） | `1,2,3,4,5` |
| `-NumEnvs` | 每次训练同时开几个游戏；一个系列只能用一个值 | `1` |
| `-Prefix` | 加在每个 run-id 前面的词；放弃一次训练、要重跑时就靠它换一个新 run-id | 不加 |
| `-Device` | `cpu` 或 `cuda`。只改 `results\_tmp\` 里复制出来的那份配置，`config\` 里的文件不动；实际用的写在 `config-used.yaml` 里 | `cpu` |
| `-Smoke` / `-SmokeSteps` | 在 `results\_tmp\` 里复制一份缩短版（15,000 步）再跑 | 关 |
| `-DryRun` | 只打印要跑的命令，什么文件都不写 | 关 |
| `-EnvArgs` | 传给训练包的参数，整串用引号包起来，例如 `-EnvArgs "-gymTrainSymbols BTCUSDT,ETHUSDT"`；写法和规矩见第 12 节 | 不传 |
| `-BasePort` | 这条流水线的端口起点（第 k 个环境用「起点 + k」），几条流水线同时跑时每条一个，见第 14.4 节 | 不传（`mlagents-learn` 用 5005） |

run-id 的格式是 `<配置名>-s<种子>-<年月日>`（UTC 日期）。`results\<run-id>` 已经存在的就跳过，绝不覆盖。每次训练留下 `results\<run-id>\`（里面有模型、`config-used.yaml`、`seed-used.txt`、`env-args.txt`，训练包里有 `build-info.json` 时还有它的副本，见第 12 节）和 `results\<run-id>.log`。每份对比配置改了什么，见 `config\README.md`；改过配置后用 `python scripts\train\check_configs.py` 检查。

脚本传给 mlagents-learn 的是 `--seed <种子 × 1000>`，并写进 `results\<run-id>\seed-used.txt`。原因：ML-Agents 给第 k 个环境的种子是「种子 + k」，种子 1、2、3 直接用的话，`-NumEnvs` 大于 1 时会撞号；乘 1000 就拉开了。每个智能体的随机起点都由这个种子推出来，所以配置、种子、`-NumEnvs` 都一样时，环境给的局完全一样。训练器那一侧也用了这个种子，但 PyTorch 不保证逐位相同，曲线可能还会有一点点差别。

每次训练结束，脚本会去 Player 日志里查有没有智能体「按时钟播种」。万一看到一大块红底的 `!!! WARNING: ... seeded from the clock`，`seed-used.txt` 末尾也会多一行 `WARNING: some agents seeded from the clock`：说明这次没用上种子、不能原样重跑。训练结果照样能用，但要记下来，告诉讨论会话。

## 9. 用 TensorBoard 看曲线

```powershell
tensorboard --logdir results
```

浏览器打开 <http://localhost:6006>。对比各组的 `Environment/Cumulative Reward`、`Policy/Entropy`，以及 `Trading/…` 那几条：成交笔数、换手、手续费、持币时间占比。

## 10. 评估

先 `git pull` 拿到最新的仓库（还没有连上 GitHub、仓库是拷过来的时候，跳过 `git pull`：没有远端，它只会报错），再把训练好的模型打进一个评估包，在验证段上跑。评估包单独放在 `unity\Builds\win-eval\`：训练包正在跑时会占着 `unity\Builds\win\` 里的 `UnityPlayer.dll` 等文件，两个包放一起就打不出来。

```powershell
git pull   # 还没有连上 GitHub、仓库是拷过来的时候跳过这一行

$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD\unity`"",
  '-executeMethod', 'Gym.Editor.BuildScript.BuildWindowsEval',
  '-gymModel', "`"$PWD\results\<run-id>\TradingAgent.onnx`"", '-quit',
  '-logFile', "`"$PWD\unity\Logs\build-eval.log`"")
$p.ExitCode   # 0 表示成功

$e = Start-Process -FilePath unity\Builds\win-eval\GymEval.exe -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-gymMode', 'eval', '-gymSegment', 'validation',
  '-gymFeeRate', '0.001', '-gymOut', "`"$PWD\evaluations`"",
  '-logFile', "`"$PWD\unity\Logs\eval.log`"")
$e.ExitCode   # 0 表示成功；不是 0 就看 unity\Logs\eval.log 的最后几行
```

- `-gymFeeRate` 用这个模型训练时的费率。测试段只留到最后出正式数字时用。
- **第二轮的模型**（`config\v2\` 训出来的）观测集是 `long` 或 `long-ta`，评估命令要加 `'-gymObs', 'long'`（或 `'long-ta'`），三个验证段都评；详见第 12 节。
- **第三轮的模型**（`config\v3\` 训出来的）是旋钮模式，评估命令还要加 `'-gymActionMode', 'dial'`；详见第 14.5 节。
- 评估包必须带 `-gymMode eval`、`-gymSegment`（`gym-config.json` 里的一个评估段：`validation-bear`、`validation-recovery`、`validation` 或 `test`）和 `-gymOut`，少一个、或者段名不在配置里，它就报错退出（退出码 1），什么都不写。段没有默认值，免得一不小心跑了测试段。费率写错（比如 `1.5`）也是几秒内退出码 1，`unity\Logs\eval.log` 里写着是哪个参数。
- 跑完马上提交，例如：

```powershell
git add evaluations
git commit -m "eval: <run-id> on the validation segment"
```

**对照组不用再跑。** 验证段、测试段的三档费率（0、0.1 %、0.3 %）对照组都已经在 Mac 上正式跑过、提交了；`validation-bear`、`validation-recovery` 两段的六个对照组和 `validation` 段的三条规则，也按这三档费率跑过、提交了。流水只追加、删不掉，重复跑只会多出重复的行。只有换了费率或者换了段，才需要再跑一次，命令是：

```powershell
$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD\unity`"",
  '-executeMethod', 'Gym.Editor.EvalTools.RunBaselines', '-gymSegment', 'validation',
  '-gymFeeRates', '<新费率>', '-gymOut', "`"$PWD\evaluations`"", '-quit', '-logFile', "`"$PWD\unity\Logs\baselines.log`"")
$p.ExitCode   # 0 表示成功；不是 0 就看 unity\Logs\baselines.log
```

看汇总表：

```powershell
python scripts\eval\summarize.py evaluations\log.csv
```

## 11. 东西都放在哪

| 什么 | 在哪 | 进不进 Git |
| --- | --- | --- |
| 训练产物、模型、TensorBoard 记录 | `results\<run-id>\` | 不进 |
| 一次训练的终端输出 | `results\<run-id>.log` | 不进 |
| 一次训练的留痕：传给训练包的参数、训练包的打包信息 | `results\<run-id>\env-args.txt`、`results\<run-id>\build-info.json` | 不进 |
| 打出来的包 | `unity\Builds\win\`（训练包）、`unity\Builds\win-eval\`（评估包） | 不进 |
| 评估流水和明细 | `evaluations\log.csv`、`evaluations\runs\` | 进（只追加，只在这台 PC 上追加） |
| 试验性的评估、Mac 上的评估 | `evaluations\smoke\` | 不进 |

要在 Mac 上看训练曲线，把 `results\<run-id>` 文件夹拷过去就行；每个文件夹里的 `TradingAgent.onnx` 就是模型。在 Mac 上评估的话，`-gymOut` 写 `evaluations/smoke`。

## 12. 第二轮：新参数和 `-EnvArgs`

第二轮的开关（换卷子、换币、换眼睛、换奖励、混训）都在 Mac 上做好、测过，但**训练包和评估包带这些参数的真跑只能在这台 PC 上做**，`run_series.ps1` 的 `-EnvArgs` 也还没在 Windows 上真跑过。所以先做完第 13 节的冒烟，再跑正式的。

### 12.1 新参数一览

| 参数 | 给谁 | 意思 | 不写时 |
| --- | --- | --- | --- |
| `-gymSegment <段名>` | 评估包、基准工具 | 考哪一段：`validation-bear`（跌段 2022 年）、`validation-recovery`（恢复段 2023-01～2024-08）、`validation`（涨段 2024-09～2025-08）、`test`（测试段）。**必填**：缺了、或者段名不在 `gym-config.json` 里，退出码 1，什么都不写。训练包只认 `train` | 没有默认值 |
| `-gymSymbol <币名>` | 评估包、基准工具、试玩、观战；训练包也认 | 当前币：`BTCUSDT`、`ETHUSDT`、`ADAUSDT`、`DOGEUSDT`、`XRPUSDT`。训练包不收 `DOGEUSDT`（只考不训，退出码 1） | 主币 `BTCUSDT` |
| `-gymObs base\|long\|long-ta` | 评估包、基准工具、试玩；训练包也认 | 观测集：35、41、43 个数。模型只能用它训练时的那一档 | `gym-config.json` 的 `observationSet`（`base`） |
| `-gymTrainSymbols A,B,…` | 训练包 | 混训清单，每局按这局的种子挑一只；名字要在配置的 `symbols` 里、不能有 `DOGEUSDT`、不能重复，错了退出码 1 | 只训当前币 |
| `-gymRiskAversion <λ>` | 训练包、评估包、基准工具 | 奖励的风险系数，有限的数、不小于 0；负数退出码 1。评估只量钱、不量奖励，所以它不改评估的数字 | 0 |
| 环境参数 `obs_set`（yaml 的 `environment_parameters`） | 训练 | 0 = `base`、1 = `long`、2 = `long-ta` | 用 `-gymObs` 或配置 |
| 环境参数 `risk_aversion`（同上） | 训练 | 风险系数 λ | 用 `-gymRiskAversion`，再没有是 0 |

### 12.2 训练时怎么传：观测集和风险系数写在 yaml，币写在 `-EnvArgs`

第二轮的配置在 `config\v2\`（每个文件的第一行写着它的基础，见 `config\README.md`）：

| 文件 | 是什么 |
| --- | --- |
| `ppo_v2.yaml` | 第二轮的基础：`ppo_base.yaml` 只改 `normalize: true`（第一轮胜出的设置） |
| `long.yaml`、`long-ta.yaml` | 基础 `ppo_v2`，`obs_set` 1、2 |
| `long-risk-0.005.yaml`、`long-risk-0.01.yaml`、`long-risk-0.02.yaml` | 基础 `long`，各加一个 `risk_aversion` |
| `smoke-long.yaml` | 基础 `long`，15,000 步的冒烟（第 13 节用） |

规矩：

- **观测集只走一条路：yaml 里的 `obs_set`。** 训练结果目录会自动存 `config-used.yaml`，留痕最省事。yaml 的 `obs_set` 和 `-EnvArgs` 里的 `-gymObs` 同时给时，**环境参数（yaml）赢**，两边写得不一样也不报错，所以 `-EnvArgs` 里别写 `-gymObs`。风险系数一样：yaml 的 `risk_aversion` 赢过 `-gymRiskAversion`。
- **混训的清单写在命令行 `-gymTrainSymbols`，不改 `gym-config.json`。** 配置里的 `trainSymbols` 写错时，连评估包也起不来；命令行写错只拦训练包。清单是 `BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT`，**不含 `DOGEUSDT`**（它是没见过的卷子，只考不训）。
- **训练的 `-EnvArgs` 不能带 `-gymSegment`**：训练模式下它只认 `train`，写别的段，训练包开工就退出码 1。也别带 `-logFile`：脚本要读 ML-Agents 写的训练包日志查「按时钟播种」，自己指定日志位置就查不到了。
- 整串用引号包起来。脚本按空格把它拆成一个个词，接在 `mlagents-learn` 命令的**最后**（`--env-args` 后面）：`--env-args` 后面的所有词都原样交给训练包，所以它必须在最后；`-gymTrainSymbols` 和清单必须是两个词，整串当一个词传过去，训练包会悄悄忽略、不报错。词里不能有空格。

完整的例子：第二轮主线和一个风险系数、四只币混训、3 个种子：

```powershell
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1,2,3 -Configs config\v2\long.yaml,config\v2\long-risk-0.01.yaml -EnvArgs "-gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT"
```

加 `-DryRun` 先看一眼，应该打印：

```text
env args: 2 word(s), passed last as --env-args: -gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT
build info: E:\project\ZHENNs_Ledger_Gym\unity\Builds\win\Gym_Data\StreamingAssets\Gym\build-info.json (copied to results\<run-id>\build-info.json)
run   long-s1-<日期>
      mlagents-learn E:\project\ZHENNs_Ledger_Gym\config\v2\long.yaml --env E:\project\ZHENNs_Ledger_Gym\unity\Builds\win\Gym.exe --run-id long-s1-<日期> --seed 1000 --num-envs 1 --no-graphics --env-args -gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT
```

命令以 `--env-args -gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT` 结尾、清单外面没有引号，就对了。脚本打印命令时，带空格的词会加上双引号，所以要是看到 `"-gymTrainSymbols BTCUSDT,…"` 这样带引号的一整个词，说明没拆开，停下来。

### 12.3 每次训练留下什么

| 文件 | 内容 |
| --- | --- |
| `results\<run-id>\config-used.yaml` | 这次实际用的配置，`obs_set`、`risk_aversion`、`device` 都在里面 |
| `results\<run-id>\seed-used.txt` | 传给 `mlagents-learn` 的种子 |
| `results\<run-id>\env-args.txt` | 传给训练包的词，一行、空格隔开；没给 `-EnvArgs` 时是空文件 |
| `results\<run-id>\build-info.json` | 训练包的打包信息：打包时间 `built_at_utc`、Unity 版本 `unity_version`、包里那份 `gym-config.json` 的 SHA-256 `gym_config_sha256`。从训练包里复制来：它在 `unity\Builds\win\Gym_Data\StreamingAssets\Gym\build-info.json`，**不在 `Gym.exe` 旁边**。包是这一版之前打的就没有它，脚本开头会写 `build info: not found at …`，重打训练包（第 6 步）就有了 |
| `results\<run-id>\run_logs\Player-*.log` | 训练包自己的日志：每个智能体一行 `observations …` 和一行 `training symbols …`，整个包一行 `training player: …`（观测集、当前币、清单、分段日期、它实际读的 `gym-config.json` 的 SHA-256，应该和 `build-info.json` 里的一样） |

### 12.4 评估第二轮的模型

- **观测集必须和模型一致。** `long` 档训出来的模型，评估命令要加 `'-gymObs', 'long'`（`long-ta` 同理）。对不上时评估包退出码 1、什么都不写，日志里写着两边的个数，例如 `the model takes 35 observations but the observation set long has 41`。评估包打包时把模型的输入个数写进 `build-info.json` 的 `vector_observation_size`，开工时拿它比。
- 三个验证段都要评：`validation-bear`、`validation-recovery`、`validation`。**测试段 `test` 整轮最后只考一次**，挑好终版模型之后。
- 换币评估加 `'-gymSymbol', 'ETHUSDT'` 这样的参数；流水的 `symbol` 列就是这只币。
- `BuildWindowsEval` 的输出位置是写死的 `unity\Builds\win-eval\GymEval.exe`：**重打会覆盖上一个评估包。** 要留着上一个，先把整个 `unity\Builds\win-eval\` 拷走。拿不准包里是哪个模型，看 `unity\Builds\win-eval\GymEval_Data\StreamingAssets\Gym\build-info.json` 的 `run_id`。

## 13. 第二轮冒烟：Mac 上没真跑过的几件事

按顺序做，前面的产出后面要用。每一件写了命令和「应该看到什么」；哪一件对不上，记下原文、停下来，告诉讨论会话。冒烟的评估一律写进 `evaluations\smoke\`（不进 Git），**不写正式流水 `evaluations\log.csv`**。下面的 `<日期>` 换成脚本打印的 run-id 里的日期（UTC）。

### 冒烟 0：先只打印命令

```powershell
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1 -DryRun -Configs config\v2\smoke-long.yaml -EnvArgs "-gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT"
```

应该看到：`env args: 2 word(s), …`；`run   smoke-long-s1-<日期>` 下面那行命令以 `--no-graphics --env-args -gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT` 结尾，清单外面没有引号（见 12.2）。`build info: not found at …` 在重打训练包之前是正常的。要是 PowerShell 报 `-Env` 有歧义（`parameter name 'Env' is ambiguous`），或者清单带着引号，停下来。

### 冒烟 1：重打训练包，看 `build-info.json`

照第 6 步重打训练包，然后：

```powershell
Get-Content unity\Builds\win\Gym_Data\StreamingAssets\Gym\build-info.json
(Get-FileHash unity\Builds\win\Gym_Data\StreamingAssets\Gym\gym-config.json -Algorithm SHA256).Hash.ToLower()
```

应该看到：`build-info.json` 有 `built_at_utc`（刚才的时间）、`unity_version`（`6000.0.84f1`）、`gym_config_sha256` 三项，最后一项和第二行算出来的一样。再跑一次冒烟 0，这回应该是 `build info: …\Gym_Data\StreamingAssets\Gym\build-info.json (copied to …)`。

### 冒烟 2：观测集和混训清单到了没有

```powershell
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1 -Configs config\v2\smoke-long.yaml -EnvArgs "-gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT"
$run = 'smoke-long-s1-<日期>'
Get-Content results\$run\env-args.txt
Get-Content results\$run\build-info.json
Select-String -Path results\$run\run_logs\Player-*.log -Pattern 'observation|training symbols|training player' | Select-Object -First 6
python scripts\train\read_scalars.py results\$run | Select-String 'Trading/'
```

应该看到：

- `env-args.txt` 是一行 `-gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT`；`build-info.json` 和冒烟 1 的一样。
- **观测集看每个智能体初始化的那一行**：`…: index …, mode Training, segment train 2017-08-17..2021-12-31, observations long (41 numbers, history 2160), master seed … (trainer)`，前面还有一行 `the trainer's obs_set chooses the observation set long (the config and command line said base)`。**不要看** `config …, mode Train, observations base` 那一行：训练器选的档不写回配置，那一行照样写 `base`。
- 每个智能体一行 `training symbols BTCUSDT (train 2017-08-17..2021-12-31), ETHUSDT (train 2017-08-17..2021-12-31), ADAUSDT (train 2018-04-17..2021-12-31), XRPUSDT (train 2018-05-04..2021-12-31); each episode picks one from its episode seed`。
- 整个包一行 `training player: observations long (41 numbers), current symbol BTCUSDT, training symbols …, segment train 2017-08-17..2021-12-31, gym-config.json sha256 <和 build-info.json 的一样> (…)`。
- `Trading/RiskAversion` 和 `Trading/RiskPenalty` 都是 0（这份配置没有风险系数），`Trading/FeeRate` 是 0.001，`Trading/SymbolShare/BTCUSDT`、`…/ETHUSDT`、`…/ADAUSDT`、`…/XRPUSDT` 四条都有。15,000 步只走完大约 16 局，每个点只有一两局，在 0、0.5、1 之间跳是正常的；占比准不准看冒烟 4。
- 没有红底的 `!!! WARNING ... seeded from the clock`。

### 冒烟 3：风险系数到了没有

```powershell
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1 -Smoke -Configs config\v2\long-risk-0.01.yaml
python scripts\train\read_scalars.py results\smoke-long-risk-0.01-s1-<日期> | Select-String 'Trading/Risk'
```

应该看到：`Trading/RiskAversion` 是 0.01（TensorBoard 存的是单精度，可能显示成 `0.009999999776482582`），`Trading/RiskPenalty` 大于 0。冒烟 2 那次两条都是 0：两次一比，就知道配置里的 `risk_aversion` 真的到了训练包。

### 冒烟 4：混训的速度和内存

16 个智能体在混训时各建 4 个环境（一共 64 个），K 线是共用的，内存应该不会多很多；量一下。两次都是 10 万步，一次单币、一次四币，`-Prefix` 让两次的 run-id 分开（不加的话会和冒烟 2 的 `smoke-long-s1-<日期>` 撞名、被跳过）：

```powershell
$t0 = Get-Date
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1 -Smoke -SmokeSteps 100000 -Prefix speed1 -Configs config\v2\long.yaml
"one coin: {0:N0} steps/s" -f (100000 / ((Get-Date) - $t0).TotalSeconds)

$t0 = Get-Date
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1 -Smoke -SmokeSteps 100000 -Prefix speed4 -Configs config\v2\long.yaml -EnvArgs "-gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT"
"four coins: {0:N0} steps/s" -f (100000 / ((Get-Date) - $t0).TotalSeconds)
```

每次跑到一半，另开一个 PowerShell 窗口看训练包占多少内存：

```powershell
Get-Process Gym | Select-Object Id, @{ n = 'MB'; e = { [int]($_.WorkingSet64 / 1MB) } }
```

应该看到：两个「每秒多少步」、两个内存数，都记进 PC 训练日志。四币那次在 TensorBoard（第 9 步）里把左边的 *Smoothing* 拉到 0.95 左右，`Trading/SymbolShare/*` 四条都在 0.25 上下（大约 130 局）；单币那次只有一条 `Trading/SymbolShare/BTCUSDT`，一直是 1。

### 冒烟 5：训练包不收 DOGE

不经过 `mlagents-learn`，直接起训练包，当前币给 `DOGEUSDT`：

```powershell
$p = Start-Process -FilePath unity\Builds\win\Gym.exe -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-gymSymbol', 'DOGEUSDT', '-logFile', "`"$PWD\unity\Logs\train-doge.log`"")
$null = $p.Handle   # 先取一下，Windows PowerShell 之后才读得到退出码
if (-not $p.WaitForExit(120000)) { Stop-Process -Id $p.Id; 'did not stop within 2 minutes' }
$p.ExitCode   # 应该是 1
Select-String -Path unity\Logs\train-doge.log -Pattern 'evalOnly'
```

应该看到：退出码 1，日志里有 `DOGEUSDT is evalOnly (only evaluated, never trained on), so a training agent cannot use it as the current symbol; …`。两分钟还没退出就是不对。

### 冒烟 6：评估包的维数核对

用第一轮的 35 维模型打评估包（照第 10 步的打包命令，模型换成这个），再拿 `long` 档去评它：

```powershell
$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD\unity`"",
  '-executeMethod', 'Gym.Editor.BuildScript.BuildWindowsEval',
  '-gymModel', "`"$PWD\results\normalize-on-s3-20261019\TradingAgent.onnx`"", '-quit',
  '-logFile', "`"$PWD\unity\Logs\build-eval.log`"")
$p.ExitCode   # 0
Get-Content unity\Builds\win-eval\GymEval_Data\StreamingAssets\Gym\build-info.json

New-Item -ItemType Directory -Force evaluations\smoke\c16-obs-long | Out-Null
$e = Start-Process -FilePath unity\Builds\win-eval\GymEval.exe -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-gymMode', 'eval', '-gymSegment', 'validation', '-gymObs', 'long',
  '-gymFeeRate', '0.001', '-gymOut', "`"$PWD\evaluations\smoke\c16-obs-long`"",
  '-logFile', "`"$PWD\unity\Logs\eval-obs-long.log`"")
$e.ExitCode   # 应该是 1
(Get-ChildItem evaluations\smoke\c16-obs-long).Count   # 应该是 0
Select-String -Path unity\Logs\eval-obs-long.log -Pattern 'observations but'
```

应该看到：`build-info.json` 里有 `"run_id": "normalize-on-s3-20261019"` 和 `"vector_observation_size": 35`；评估退出码 1，`c16-obs-long` 里什么都没有，日志里有 `the model takes 35 observations but the observation set long has 41`。

### 冒烟 7：观战和评估逐列相同

还是冒烟 6 的评估包（第一轮的模型，`base` 档）。先正常评一次 `validation`，再带着模型跑观战的 PlayMode 测试（没设这两个环境变量时它们被忽略）。跑的时候不要开着 `mlagents-learn`，Unity 编辑器也要关着：

```powershell
$e = Start-Process -FilePath unity\Builds\win-eval\GymEval.exe -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-gymMode', 'eval', '-gymSegment', 'validation',
  '-gymFeeRate', '0.001', '-gymOut', "`"$PWD\evaluations\smoke\c16-watch`"",
  '-logFile', "`"$PWD\unity\Logs\eval-watch.log`"")
$e.ExitCode   # 0
Import-Csv evaluations\smoke\c16-watch\log.csv | Format-List kind, model_run_id, symbol, segment, notes

$env:GYM_WATCH_MODEL = "$PWD\results\normalize-on-s3-20261019\TradingAgent.onnx"
$env:GYM_WATCH_EVAL_LOG = "$PWD\evaluations\smoke\c16-watch\log.csv"
$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD\unity`"", '-runTests', '-testPlatform', 'PlayMode',
  '-testFilter', 'Gym.Tests.PlayMode.WatchModelTests',
  '-testResults', "`"$PWD\unity\Logs\watch-model-results.xml`"", '-logFile', "`"$PWD\unity\Logs\watch-model.log`"")
$p.ExitCode   # 0
Select-String -Path unity\Logs\watch-model-results.xml -Pattern '<test-run'
Select-String -Path unity\Logs\watch-model.log -Pattern 'RESULT:'
Remove-Item Env:GYM_WATCH_MODEL, Env:GYM_WATCH_EVAL_LOG
```

**只认 24 批之后的评估包写的流水**：`GYM_WATCH_EVAL_LOG` 只能指向这样的评估包刚写的 `log.csv`（它的 `build-info.json` 有 `"action_shape_recorded": true`）。24 批起，agent 行的 `notes` 末尾多了 `; mode=orders`，观战那一行也是；拿老评估包写的流水（或老的 `evaluations\log.csv`）去比，只会在 `notes` 上不同，这是预期，不是错（`24B` 第 2 步）。第三轮再做这一项时，先照第 10 步用这一版重打冒烟 6 的评估包。

应该看到：评估那行 `kind` 是 `agent`、`segment` 是 `validation`、`notes` 以 `obs=base; mode=orders` 结尾（24 批之前的包写的是以 `obs=base` 结尾）；测试退出码 0，`<test-run` 那行 `total="2" passed="2"`、`skipped="0"`；日志里有 `RESULT: the watch row equals the evaluation row in every column but timestamp_utc and model_run_id, and the final equity is the same`。跑完 `git status` 若只有 `unity\ProjectSettings\ProjectSettings.asset` 里 `SENTIS_ANALYTICS_ENABLED` 那一处变了，用 `git checkout -- unity/ProjectSettings/ProjectSettings.asset` 还原。

### 冒烟 8：评估包认新段名，不认不存在的段名

把冒烟 2 训出来的 15,000 步模型（`long` 档）打成评估包。**这会覆盖冒烟 6 的评估包**（输出位置是写死的）：

```powershell
$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD\unity`"",
  '-executeMethod', 'Gym.Editor.BuildScript.BuildWindowsEval',
  '-gymModel', "`"$PWD\results\smoke-long-s1-<日期>\TradingAgent.onnx`"", '-quit',
  '-logFile', "`"$PWD\unity\Logs\build-eval.log`"")
$p.ExitCode   # 0
Get-Content unity\Builds\win-eval\GymEval_Data\StreamingAssets\Gym\build-info.json   # "vector_observation_size": 41

$e = Start-Process -FilePath unity\Builds\win-eval\GymEval.exe -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-gymMode', 'eval', '-gymSegment', 'validation-bear', '-gymObs', 'long',
  '-gymFeeRate', '0.001', '-gymOut', "`"$PWD\evaluations\smoke`"",
  '-logFile', "`"$PWD\unity\Logs\eval-bear.log`"")
$e.ExitCode   # 0
Import-Csv evaluations\smoke\log.csv | Select-Object -Last 1 | Format-List kind, model_run_id, symbol, segment, segment_start, segment_end, notes

New-Item -ItemType Directory -Force evaluations\smoke\c16-bad-segment | Out-Null
$e = Start-Process -FilePath unity\Builds\win-eval\GymEval.exe -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-gymMode', 'eval', '-gymSegment', 'validation-nope', '-gymObs', 'long',
  '-gymFeeRate', '0.001', '-gymOut', "`"$PWD\evaluations\smoke\c16-bad-segment`"",
  '-logFile', "`"$PWD\unity\Logs\eval-bad-segment.log`"")
$e.ExitCode   # 应该是 1
(Get-ChildItem evaluations\smoke\c16-bad-segment).Count   # 应该是 0
Select-String -Path unity\Logs\eval-bad-segment.log -Pattern 'not an evaluation segment'
```

应该看到：`build-info.json` 的 `run_id` 是 `smoke-long-s1-<日期>`、`vector_observation_size` 是 41；第一次评估退出码 0，最后一行 `model_run_id` 是这个模型、`segment` 是 `validation-bear`、`2022-01-01` 到 `2022-12-31`、`notes` 以 `obs=long` 结尾；第二次退出码 1、`c16-bad-segment` 里什么都没有，日志里有 `-gymSegment validation-nope is not an evaluation segment; use …`（后面列出四个段名）。

### 冒烟 9：评估包换币

还是冒烟 8 的评估包：

```powershell
$e = Start-Process -FilePath unity\Builds\win-eval\GymEval.exe -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-gymMode', 'eval', '-gymSegment', 'validation-bear', '-gymObs', 'long',
  '-gymSymbol', 'ETHUSDT', '-gymFeeRate', '0.001', '-gymOut', "`"$PWD\evaluations\smoke`"",
  '-logFile', "`"$PWD\unity\Logs\eval-eth.log`"")
$e.ExitCode   # 0
Import-Csv evaluations\smoke\log.csv | Select-Object -Last 1 | Format-List symbol, segment, notes
```

应该看到：退出码 0，最后一行的 `symbol` 是 `ETHUSDT`。

### 冒烟 10：评估包不收负的风险系数

```powershell
New-Item -ItemType Directory -Force evaluations\smoke\c16-bad-risk | Out-Null
$e = Start-Process -FilePath unity\Builds\win-eval\GymEval.exe -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-gymMode', 'eval', '-gymSegment', 'validation-bear', '-gymObs', 'long',
  '-gymRiskAversion', '-1', '-gymFeeRate', '0.001', '-gymOut', "`"$PWD\evaluations\smoke\c16-bad-risk`"",
  '-logFile', "`"$PWD\unity\Logs\eval-bad-risk.log`"")
$e.ExitCode   # 应该是 1
(Get-ChildItem evaluations\smoke\c16-bad-risk).Count   # 应该是 0
Select-String -Path unity\Logs\eval-bad-risk.log -Pattern 'RiskAversion'
```

应该看到：退出码 1，`c16-bad-risk` 里什么都没有，日志里有 `-gymRiskAversion -1 is out of range: …`。

冒烟 0 到冒烟 10 都对上了，再按 12.2 的例子跑正式的第二轮。

## 14. 第三轮：仓位旋钮

第三轮的模型不再下单，只拧「目标仓位」旋钮：30%、50%、70% 三档，每 24 根小时 K 线（一天）选一次，买卖由规则代劳（实际仓位偏离目标超过带宽就买或卖回去）。旋钮模式的环境、对手、评估包的动作形状核对、算分脚本都在 Mac 上做好、测过，但**训练包和评估包带旋钮模式的真跑只能在这台 PC 上做**，`run_series.ps1` 的 `-BasePort` 也还没在 Windows 上真跑过。所以先做完第 15 节的冒烟，再跑正式的。

### 14.1 新参数一览

| 参数 | 给谁 | 意思 | 不写时 |
| --- | --- | --- | --- |
| 环境参数 `action_mode`（yaml 的 `environment_parameters`） | 训练 | 0 = 下单模式（第一、二轮）、1 = 旋钮模式 | 用 `-gymActionMode`，再没有用 `gym-config.json` 的 `actionMode`（`orders`） |
| 环境参数 `decision_hours`（同上） | 训练 | 旋钮模式每次决策走几根 K 线。`check_configs.py` 只收 24（`22A` 第二节第 5 条） | 用 `-gymDecisionHours`，再没有用 `decisionHours`（24） |
| 环境参数 `rebalance_band`（同上） | 训练 | 调仓带宽：实际仓位偏离目标超过它才下单。`check_configs.py` 只收 0.02～0.10 | 用 `-gymBand`，再没有用 `rebalanceBand`（0.05） |
| `-gymActionMode orders\|dial` | 评估包、观战（启动编辑器时带）；训练包也认 | 动作模式。模型只能用它训练时的那一种。试玩场景不支持 `dial`，会报错退出 | `gym-config.json` 的 `actionMode`（`orders`） |
| `-gymDecisionHours <n>` | 评估包、观战；训练包也认 | 每次决策走几根，整数 ≥ 1 | `decisionHours`（24） |
| `-gymBand <x>` | 评估包、观战；训练包也认 | 调仓带宽，(0, 0.5) 之内 | `rebalanceBand`（0.05） |
| `-BasePort <n>`（`run_series.ps1`） | 训练脚本 | 这一条流水线的端口起点：第 k 个环境用「起点 + k」。整数，1024 到 65535 − `-NumEnvs`，不对就报错退出 | 不传，`mlagents-learn` 用它自己的 5005 |

档位（`dialLevels` 0.3、0.5、0.7）和旋钮模式每局的长度（`dialEpisodeLength` 2160 根，也就是 90 次决策）只在 `gym-config.json` 里，不走命令行。规矩和第二轮的观测集一样：**训练时这三样只写在 yaml 里**，`-EnvArgs` 里别写 `-gymActionMode`、`-gymDecisionHours`、`-gymBand`（环境参数赢，两边写得不一样也不报错）。

### 14.2 `config\v3\` 的三份配置

| 文件 | 基础（第一行写着） | 和基础差什么 | 用来做什么 |
| --- | --- | --- | --- |
| `dial.yaml` | `config\v2\long.yaml` | `action_mode: 1` | 主线，两条线都用它：BTC 单币线、四币混训线 |
| `dial-base.yaml` | `config\v3\dial.yaml` | `obs_set: 0`（`base`，35 个数） | `base` 对照，只跑 BTC 单币线 |
| `smoke-dial.yaml` | `config\v3\dial.yaml`（冒烟配置，不和基础比） | `max_steps` 15000、`checkpoint_interval` 15000、`summary_freq` 1000 | 第 15 节的冒烟。一步是一次决策（24 根），第一批局在 16 个智能体 × 90 次决策 = 1,440 步结束，`Trading/…` 从 2,000 步起才有 |

`decision_hours`、`rebalance_band` 三份都没写，用的是 `gym-config.json` 的 24 和 0.05。**没有 `dial-mix.yaml`**：混训线用的就是 `dial.yaml`，币写在 `-EnvArgs`，和第二轮一样；两条线的设置因此一字不差（`22A` 第二节第 9 条）。

`max_steps` 现在仍是 200 万。旋钮模式一步是一天，200 万步约等于把训练段过 1,250 遍；正式跑多少步，按冒烟 14 的测速定（`22A` 第三节第 2 条，倾向 50 万步起）。**要改步数先问讨论会话**：直接改 `dial.yaml` 的 `max_steps`，它就和基础 `long.yaml` 差两项，`check_configs.py` 会判 FAIL。

### 14.3 两条线和对照怎么起

| 线 | 配置 | 种子 | `-Prefix` | `-EnvArgs` | run-id |
| --- | --- | --- | --- | --- | --- |
| BTC 单币（主线） | `config\v3\dial.yaml` | 1～5 | 不加 | 不传 | `dial-s<种子>-<日期>` |
| 四币混训（主线） | `config\v3\dial.yaml` | 1～5 | `mix` | `"-gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT"` | `mix-dial-s<种子>-<日期>` |
| `base` 对照 | `config\v3\dial-base.yaml` | 1～3 | 不加 | 不传 | `dial-base-s<种子>-<日期>` |

一条流水线一次只跑一个训练；几条流水线同时跑见 14.4。一次只开一条时，三条命令依次是：

```powershell
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1,2,3,4,5 -Configs config\v3\dial.yaml
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1,2,3,4,5 -Prefix mix -Configs config\v3\dial.yaml -EnvArgs "-gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT"
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1,2,3 -Configs config\v3\dial-base.yaml
```

`-Prefix` 在第三轮**只用来分「线」**（混训线 `mix`）。算分脚本 `score_round3.py` 把 run-id 去掉 `-s<种子>-<日期>` 当成「配置」：`dial`、`mix-dial`、`dial-base` 正好是三个配置。别的前缀会凭空多出配置，把同一个配置的种子拆开，配置得分和「几个种子过门槛」就都算错了，而且不会报错。

### 14.4 几条流水线同时跑

第二轮看到 CPU 只用了三成左右，所以第三轮几条流水线同时跑（`22A` 第二节第 10 条）。**起步建议 3 条**，最后开几条由 Windows 派活会话按冒烟 14 的测速定。

规矩：

- **一条流水线一个终端、一个 `-BasePort`**，相邻的至少隔 `-NumEnvs` 个数，例如 5005、5105、5205。两条用了同一个端口，后起的那条会直接报 `UnityWorkerInUseException` 退出，不会悄悄串线。
- **流水线之间靠种子或线分开，不靠 `-Prefix`。** 三条流水线最简单是一条线一条：BTC 单币、混训、`base` 对照各一条。流水线比线多时，把一条线按种子拆开，例如一条跑 `-Seeds 1,2`、另一条跑 `-Seeds 3,4,5`。**同一条线的同一个种子不许在两条流水线里同时跑**（同一个 run-id 同时起两次，后起的那次 `mlagents-learn` 会报 `Previous data from this run ID was found`）。
- 每条流水线的输出各写各的 `results\series-<名>.out`（`*>` 写出来的是 UTF-16，`Select-String` 照样能搜）。
- 每个终端先设 `$env:CUDA_VISIBLE_DEVICES = '-1'`：配置里是 `device: cpu`，训练用不到显卡，设了就不会给显卡添负担。
- 临时配置 `results\_tmp\<run-id>.yaml` 按 run-id 起名，几条流水线不会写到同一个文件，不用另外分目录。

三条流水线，一条线一条（三个 PowerShell 窗口，都先 `conda activate mlagents`、`cd E:\project\ZHENNs_Ledger_Gym`）：

```powershell
# 窗口一：BTC 单币
$env:CUDA_VISIBLE_DEVICES = '-1'
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1,2,3,4,5 -BasePort 5005 -Configs config\v3\dial.yaml *> results\series-dial.out

# 窗口二：四币混训
$env:CUDA_VISIBLE_DEVICES = '-1'
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1,2,3,4,5 -BasePort 5105 -Prefix mix -Configs config\v3\dial.yaml -EnvArgs "-gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT" *> results\series-mix-dial.out

# 窗口三：base 对照
$env:CUDA_VISIBLE_DEVICES = '-1'
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1,2,3 -BasePort 5205 -Configs config\v3\dial-base.yaml *> results\series-dial-base.out
```

看机器跑满了没有、几条有没有互相干扰：

```powershell
# 每个训练包的命令行里有 --mlagents-port <端口>，三条流水线的端口各在自己的区间里
Get-CimInstance Win32_Process -Filter "Name='Gym.exe'" | Select-Object ProcessId, CommandLine | Format-List
# 训练器（python.exe）在哪几个端口上等训练包
Get-NetTCPConnection -State Listen -LocalPort 5005,5105,5205 | Select-Object LocalPort, OwningProcess
# CPU 一共用了多少（每 5 秒一次，取 6 次）；任务管理器的「性能」页也行
Get-Counter '\Processor(_Total)\% Processor Time' -SampleInterval 5 -MaxSamples 6
# 训练包一共占多少内存
"{0:N0} MB" -f ((Get-Process Gym | Measure-Object WorkingSet64 -Sum).Sum / 1MB)
```

端口**不要去 Player 日志里找**：训练包只在连不上训练器时才把端口写进日志。每次训练的端口记在 `results\<run-id>\seed-used.txt` 的 `base-port: …` 那一行（没给 `-BasePort` 时是 `base-port: default …`）。

### 14.5 评估第三轮的模型

- 旋钮模式的模型，评估命令要加 `'-gymActionMode', 'dial'`；观测集照训练时的：`dial`、`mix-dial` 的模型加 `'-gymObs', 'long'`，`dial-base` 的模型用 `base`（不写 `-gymObs` 就是 `base`）。训练时改过带宽或决策根数的，评估也要带一样的 `-gymBand`、`-gymDecisionHours`。
- 动作模式对不上时评估包退出码 1、什么都不写，日志里写着两边的形状，例如 `the model's actions are 1 continuous + [3] but the action mode dial with 3 levels needs 0 continuous + [3]`（冒烟 13）。
- 三个验证段都评：`validation-bear`、`validation-recovery`、`validation`，费率 0.001。**测试段 `test` 整轮最后只考一次**，挑好终版之后；报告里写「验证段调参、测试段第三次」（`22A` 第二节第 11 条）。
- 例子（`dial-s1-<日期>` 的模型，跌段）：

```powershell
$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD\unity`"",
  '-executeMethod', 'Gym.Editor.BuildScript.BuildWindowsEval',
  '-gymModel', "`"$PWD\results\dial-s1-<日期>\TradingAgent.onnx`"", '-quit',
  '-logFile', "`"$PWD\unity\Logs\build-eval.log`"")
$p.ExitCode   # 0

$e = Start-Process -FilePath unity\Builds\win-eval\GymEval.exe -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-gymMode', 'eval', '-gymSegment', 'validation-bear',
  '-gymActionMode', 'dial', '-gymObs', 'long',
  '-gymFeeRate', '0.001', '-gymOut', "`"$PWD\evaluations`"",
  '-logFile', "`"$PWD\unity\Logs\eval.log`"")
$e.ExitCode   # 0
```

- 流水那一行的 `notes` 以 `; mode=dial; band=0.05; hours=24; changes=<换档次数>` 结尾。
- 三段都评完，算分：

```powershell
python scripts\eval\score_round3.py evaluations\log.csv                          # 所有模型
python scripts\eval\score_round3.py evaluations\log.csv --runs dial-,mix-dial-    # 只看第三轮的 run-id
```

两道门槛（跌段回撤 ≤ 0.4132、涨段收益 ≥ 0.3901，先四舍五入到 4 位小数再比）、三段卡玛平均、配置得分、「没学会」（三段都从没换过档）的算法见 [evaluation.md](evaluation.md)（英文）；终版在所有过了两道门槛的模型里挑得分最高的。

## 15. 第三轮冒烟：Mac 上没真跑过的几件事

和第 13 节一样：按顺序做，前面的产出后面要用；哪一件对不上，记下原文、停下来，告诉讨论会话。冒烟的评估一律写进 `evaluations\smoke\`，**不写正式流水**。`<日期>` 换成脚本打印的 run-id 里的日期（UTC）。做之前先照第 6 步重打训练包（这一版的训练包才认旋钮模式），再照冒烟 0 先 `-DryRun` 看一眼。

### 冒烟 11：三个环境参数到了没有

先跑旋钮模式的冒烟配置（它的名字本来就以 `smoke` 开头，不用再加 `-Smoke`）：

```powershell
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1 -Configs config\v3\smoke-dial.yaml
$run = 'smoke-dial-s1-<日期>'
Select-String -Path results\$run\run_logs\Player-*.log -Pattern 'actions dial|chooses the action mode' | Select-Object -First 4
python scripts\train\read_scalars.py results\$run | Select-String 'Trading/Decisions|Trading/DialChanges'
```

应该看到：

- 每个智能体一行 `the trainer's action_mode chooses the action mode dial (the config and command line said orders)`。
- 每个智能体初始化那一行：`…: index …, mode Training, segment train 2017-08-17..2021-12-31, observations long (41 numbers, history 2160), actions dial (levels 0.3, 0.5, 0.7, decision hours 24, band 0.05, episodes of 2160 candles), master seed … (trainer)`。
- `Trading/Decisions` 和 `Trading/DialChanges` 两条都有（冒烟 12 看它们的数）。

这一次只证明了 `action_mode` 这一路：`decision_hours`、`rebalance_band` 用的是 `gym-config.json` 的缺省值，环境参数那一路没走过。所以再用一份**临时配置**（放在 `results\_tmp\`，不进 Git）带上 `decision_hours: 24`、`rebalance_band: 0.03` 跑一次：

```powershell
New-Item -ItemType Directory -Force results\_tmp | Out-Null
$text = [IO.File]::ReadAllText("$PWD\config\v3\smoke-dial.yaml")
$text = $text.Replace('  action_mode: 1', "  action_mode: 1`n  decision_hours: 24`n  rebalance_band: 0.03")
[IO.File]::WriteAllText("$PWD\results\_tmp\smoke-dial-band003.yaml", $text)
Select-String -Path results\_tmp\smoke-dial-band003.yaml -Pattern 'action_mode|decision_hours|rebalance_band'   # 三行

.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1 -Configs results\_tmp\smoke-dial-band003.yaml
Select-String -Path results\smoke-dial-band003-s1-<日期>\run_logs\Player-*.log -Pattern 'actions dial' | Select-Object -First 1
```

应该看到：那一行写的是 `actions dial (levels 0.3, 0.5, 0.7, decision hours 24, band 0.03, episodes of 2160 candles)`，`band` 是 0.03 而不是 0.05。这就证明了三样都能从 yaml 送到训练包（`22A` 第二节第 6 条允许在 2～10 个点之间试带宽，到时候走的就是这条路）。

### 冒烟 12：一步走 24 根，训练器收下了「0 个连续 + 1 个 3 档分支」

还是冒烟 11 的第一次（`smoke-dial-s1-<日期>`）：

```powershell
python scripts\train\read_scalars.py results\$run | Select-String 'Trading/Decisions'
Select-String -Path results\$run.log -Pattern 'Error|Exception|action spec|ActionSpec'
Select-String -Path results\$run.log -Pattern 'Step: 15000'
```

应该看到：

- `Trading/Decisions` 是 **`dialEpisodeLength ÷ decisionHours`**，现在是 2160 ÷ 24 = **90**（`gym-config.json` 改了，期望跟着变）。
- 第二行什么都搜不到：训练器没有因为动作形状报错。
- 有 `Step: 15000` 那一行，脚本最后是 `series finished: 1 ran, 0 skipped, 0 failed`。

### 冒烟 13：评估包收旋钮模型、拒下单模型

先拿冒烟 11 训出来的旋钮模型（`long` 档）打评估包：

```powershell
$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD\unity`"",
  '-executeMethod', 'Gym.Editor.BuildScript.BuildWindowsEval',
  '-gymModel', "`"$PWD\results\smoke-dial-s1-<日期>\TradingAgent.onnx`"", '-quit',
  '-logFile', "`"$PWD\unity\Logs\build-eval.log`"")
$p.ExitCode   # 0
Get-Content unity\Builds\win-eval\GymEval_Data\StreamingAssets\Gym\build-info.json

$e = Start-Process -FilePath unity\Builds\win-eval\GymEval.exe -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-gymMode', 'eval', '-gymSegment', 'validation-bear',
  '-gymActionMode', 'dial', '-gymObs', 'long',
  '-gymFeeRate', '0.001', '-gymOut', "`"$PWD\evaluations\smoke`"",
  '-logFile', "`"$PWD\unity\Logs\eval-dial.log`"")
$e.ExitCode   # 0
Import-Csv evaluations\smoke\log.csv | Select-Object -Last 1 | Format-List kind, model_run_id, segment, notes
```

应该看到：`build-info.json` 里 `"continuous_actions": 0`、`"discrete_branches": [3]`、`"action_shape_recorded": true`、`"vector_observation_size": 41`；评估退出码 0，最后一行 `model_run_id` 是 `smoke-dial-s1-<日期>`、`notes` 以 `; mode=dial; band=0.05; hours=24; changes=<k>` 结尾。

再拿第二轮的终版（下单模式，`base` 档，仓库里就有）打评估包，用旋钮模式去评它，**这会覆盖上面那个评估包**：

```powershell
$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD\unity`"",
  '-executeMethod', 'Gym.Editor.BuildScript.BuildWindowsEval',
  '-gymModel', "`"$PWD\models\round2\ppo_v2-s3-20261020\TradingAgent.onnx`"", '-quit',
  '-logFile', "`"$PWD\unity\Logs\build-eval.log`"")
$p.ExitCode   # 0

New-Item -ItemType Directory -Force evaluations\smoke\c25-orders-as-dial | Out-Null
$e = Start-Process -FilePath unity\Builds\win-eval\GymEval.exe -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-gymMode', 'eval', '-gymSegment', 'validation-bear', '-gymActionMode', 'dial',
  '-gymFeeRate', '0.001', '-gymOut', "`"$PWD\evaluations\smoke\c25-orders-as-dial`"",
  '-logFile', "`"$PWD\unity\Logs\eval-orders-as-dial.log`"")
$e.ExitCode   # 应该是 1
(Get-ChildItem evaluations\smoke\c25-orders-as-dial).Count   # 应该是 0
Select-String -Path unity\Logs\eval-orders-as-dial.log -Pattern "the model's actions are"
```

应该看到：退出码 1，`c25-orders-as-dial` 里什么都没有，日志里有 `the model's actions are 1 continuous + [3] but the action mode dial with 3 levels needs 0 continuous + [3]`，两边的形状都在。

### 冒烟 14：两条流水线同时各跑 10 万步

两个 PowerShell 窗口，**同时**起（两条用不同的种子和 `-BasePort`；种子用 2 和 3，免得和冒烟 11 的 `smoke-dial-s1-<日期>` 撞名被跳过）：

```powershell
# 窗口一
$env:CUDA_VISIBLE_DEVICES = '-1'
$t0 = Get-Date
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 2 -Smoke -SmokeSteps 100000 -BasePort 5005 -Configs config\v3\dial.yaml *> results\series-c25-p1.out
"pipeline 1: {0:N0} steps/s" -f (100000 / ((Get-Date) - $t0).TotalSeconds)

# 窗口二
$env:CUDA_VISIBLE_DEVICES = '-1'
$t0 = Get-Date
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 3 -Smoke -SmokeSteps 100000 -BasePort 5105 -Configs config\v3\dial.yaml *> results\series-c25-p2.out
"pipeline 2: {0:N0} steps/s" -f (100000 / ((Get-Date) - $t0).TotalSeconds)
```

两条都在跑时，在第三个窗口看端口（14.4 的命令）：

```powershell
Get-CimInstance Win32_Process -Filter "Name='Gym.exe'" | Select-Object ProcessId, CommandLine | Format-List
Get-NetTCPConnection -State Listen -LocalPort 5005,5105 | Select-Object LocalPort, OwningProcess
Get-Counter '\Processor(_Total)\% Processor Time' -SampleInterval 5 -MaxSamples 6
```

应该看到：

- 两个 `Gym.exe`，命令行里一个有 `--mlagents-port 5005`、一个有 `--mlagents-port 5105`；两个端口都在监听。**不看 Player 日志**（它只在连不上时才写端口）。
- 两个 `.out` 最后都是 `series finished: 1 ran, 0 skipped, 0 failed`，都没有 `UnityWorkerInUseException`、没有红底的 `!!! WARNING`。
- `results\smoke-dial-s2-<日期>\seed-used.txt` 有 `base-port: 5005 (…)`，`smoke-dial-s3-<日期>` 的有 `base-port: 5105 (…)`。
- 记下两个「每秒多少步」和 CPU 利用率，写进 PC 训练日志：这是定并行条数的依据。拿不准三条行不行，再加第三个窗口（种子 4、`-BasePort 5205`）同时跑一次，看每条的速度掉了多少、CPU 有没有到九成以上。

### 冒烟 15：`score_round3.py` 在流水上跑得通

```powershell
python scripts\eval\score_round3.py evaluations\log.csv
python scripts\eval\score_round3.py evaluations\smoke\log.csv
python -m unittest discover -s scripts\eval
```

应该看到：前两条退出码 0。正式流水那次，表三里两个对手两道门槛都过：`constant_mix_50` 回撤 0.4132、收益 0.3901（正好等于门槛，因为门槛就是它的成绩），`dial_momentum_90d` 也过；冒烟流水那次，冒烟 13 的 `smoke-dial-s1-<日期>` 列在表一里、只有一段，标着 `missing segment`、不排名。单元测试最后一行是 `OK`。

### 冒烟 16：在编辑器里观战旋钮模型

观战用哪种模式，看编辑器启动时的 `-gymActionMode`、`-gymObs`，没有就看 `gym-config.json` 的 `actionMode`、`observationSet`（现在是 `orders`，`observationSet` 没写就是 `base`）。`gym-config.json` 进 Git、还会打进训练包和评估包，所以**优先用命令行**，别改这个文件。

1. 关掉 Unity 编辑器，带参数重新打开它：

   ```powershell
   & $unity -projectPath "$PWD\unity" -gymActionMode dial -gymObs long
   ```

   （要是非改文件不可：把 `unity\Assets\StreamingAssets\Gym\gym-config.json` 的 `"actionMode": "orders"` 改成 `"dial"`，在它下面加一行 `"observationSet": "long",`，存盘后再开编辑器。）
2. 菜单 **Gym > Watch a Model...**，选 `results\smoke-dial-s1-<日期>\TradingAgent.onnx`（或正式训出来的旋钮模型）。读数面板上原来「上一步」那一行变成「目标仓位　50%｜换档 3 次」这样（英文是 `Target position`、`50% | changes: 3`），面板行数不变；Console 里那个智能体的初始化行有 `actions dial (…)`。下单模式的模型在这里会被拒，报错里有两边的形状，提示去改 `actionMode`（或 `-gymActionMode`）。
3. 看完关掉编辑器。改过文件的话 `git checkout -- unity/Assets/StreamingAssets/Gym/gym-config.json` 还原。`git status` 要干净（只有 `ProjectSettings.asset` 里 `SENTIS_ANALYTICS_ENABLED` 那一处的话照第 13 节冒烟 7 的办法还原），**再打包**：否则下一个训练包、评估包就带着改过的配置。

旋钮模式下画面还有三处没改（已知，本轮不修）：「步数」那一行按小时数走，每一步跳 24；规则卖出时小人也做买入的动作；钱袋不飘字。看数以读数面板上的「目标仓位」那一行和流水为准。

冒烟 11 到冒烟 16 都对上了，再按 14.3、14.4 跑正式的第三轮。
