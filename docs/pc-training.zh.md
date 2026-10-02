# 在 Windows 电脑上训练（中文大白话版）

从一台什么都没装的 Windows 电脑，到跑完一组对比训练。除非特别说明，命令都在 **PowerShell** 里敲。

> **还没在 PC 上验证过。** 这一页是在 Mac 上写的。打包、冒烟、成批训练、评估这几步的 Mac 版命令在 Mac 上都跑通过，但下面这些 Windows 命令本身还没真跑过。照着做时哪里不一样，记下来、改这一页。

每一步都要记住的几条：

- Unity **必须是 6000.0.84f1**，`mlagents` **必须是 1.1.0**。版本不一样，ML-Agents 会出很难发现的毛病。
- 用命令行跑 Unity 之前，先把 Unity 编辑器关掉。同一个工程不能同时打开两次。
- **训练中途不要按 Ctrl+C。** 让它跑到 `max_steps` 自己停。实在要放弃一次训练，就别动它的 `results\<run-id>` 文件夹，换一个新的 run-id 重跑：用成批脚本时加 `-Prefix`（例如 `-Prefix rerun1`），run-id 就变成新的。
- `results\` 和 `Builds\` 只留在这台电脑上，不进 Git。`evaluations\log.csv` 进 Git，而且只追加、不改。**正式的评估流水只在这台 PC 上追加**（训练结果都在 PC 上）：追加前先 `git pull`（还没有连上 GitHub、仓库是拷过来的时候跳过 `git pull`），追加后马上提交。Mac 上的评估一律写进 `evaluations\smoke\`。
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
4. 照 README 的 Windows 一节建环境，装的是 **CUDA 版** PyTorch（能用显卡，也能用 CPU）：

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

放在 `C:\Gym` 这种**不会被 OneDrive 同步**的地方，不要放进「文档」「桌面」：很多 Windows 11 把这两个文件夹交给 OneDrive 同步，而 Unity 工程的 `Library\` 里有几万个小文件，同步盘会反复上传、还会锁住文件，打包和导入就会出莫名其妙的错。这和把 Unity 工程放在 iCloud、Dropbox 里是同一个坑。

```powershell
mkdir C:\Gym
cd C:\Gym
git clone <仓库地址> ZHENN_Ledger_Gym
cd ZHENN_Ledger_Gym
```

仓库推到 GitHub 之后才有地址。在那之前，从 Mac 拷整个文件夹到 `C:\Gym\ZHENN_Ledger_Gym`，但不要带 `Library\`、`Builds\`、`results\`、`Logs\` 这几个。后面的命令都在 `C:\Gym\ZHENN_Ledger_Gym` 里敲。

## 5. 用 Unity 打开一次

Unity Hub：*Projects → Add → Add project from disk*，选 `C:\Gym\ZHENN_Ledger_Gym`，用 6000.0.84f1 打开。第一次导入要好几分钟。等编辑器不转圈了，把它关掉。

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

退出码是 1 的话，看 `Logs\build-win.log` 的最后几行；如果是缺模块，那里会写出模块名，照第 2 步去装。

## 7. 测速度：CPU 和显卡各跑 10 万步

两次都跑，谁快就用谁。每次前后各记一下时间，所以 `mlagents-learn` 的输出照常一边跑一边显示：

```powershell
conda activate mlagents

# CPU
$t0 = Get-Date
mlagents-learn config\smoke-100k.yaml --env Builds\win\Gym.exe --run-id smoke-pc-100k-cpu --no-graphics
"CPU: {0:N0} steps/s" -f (100000 / ((Get-Date) - $t0).TotalSeconds)

# 显卡（第 3 步那行显示 False 的话跳过）
$t0 = Get-Date
mlagents-learn config\smoke-100k-cuda.yaml --env Builds\win\Gym.exe --run-id smoke-pc-100k-cuda --no-graphics
if ($LASTEXITCODE -ne 0) {
  "GPU: 显卡这次失败，用 CPU"   # 报错退出时不算速度
} else {
  "GPU: {0:N0} steps/s" -f (100000 / ((Get-Date) - $t0).TotalSeconds)
}

python tools\train\read_scalars.py results\smoke-pc-100k-cpu
```

第一次跑 `mlagents-learn` 时，Windows 防火墙可能弹窗问 **Python**（`python.exe`）要不要联网。点「允许」或「取消」都行：`mlagents-learn` 只是在本机的 5005 端口等训练包连上来，不走外网，本机内部的连接不受防火墙影响。

- 两个「每秒多少步」都记下来，**谁快就用谁**。显卡那次如果中途报错退出（ML-Agents 有个已知毛病，报错里会有 `Expected all tensors to be on the same device`），最后一行会打印「显卡这次失败，用 CPU」、不算速度：报错退出的那次用时很短，硬算出来的「每秒步数」会大得离谱，看着像显卡更快。这时就用 CPU。
- 用显卡更快的话，第 8 步成批训练加 `-Device cuda`；用 CPU 就什么都不用加。
- 200 万步要 `2000000 ÷ 每秒步数` 秒。作为参考，Mac（Apple M5、10 核，CPU）单环境大约每秒 2,000～2,900 步，200 万步要 12～16 分钟。**正式训练每组跑多少步、几个种子，就按快的那个速度定。**

`read_scalars.py` 应该列出 `Environment/Cumulative Reward`、`Policy/Entropy` 和一串 `Trading/…`，其中 `Trading/FeeRate` = 0.001。`Trading/…` 要等第一批局走完才有：16 个智能体 × 720 步 = 11,520 步。每条 `Trading/…` 是什么意思，见 README 的「训练曲线」一节。

## 8. 跑对比组

第 3 步那条 `Set-ExecutionPolicy` 执行过的话，脚本直接就能跑。万一还是红字，看是哪一种：

- 「无法加载文件 …\run_series.ps1，因为在此系统上禁止运行脚本」：第 3 步那条没生效。
- 「无法加载文件 …\run_series.ps1。未对文件 …\run_series.ps1 进行数字签名」：仓库是用浏览器下载 zip 解压出来的。Windows 给下载来的每个文件都打了「来自网络」的标记，第 3 步放开的只是本机写的脚本，带这个标记的照样拦着。

第二种可以在仓库目录（`C:\Gym\ZHENN_Ledger_Gym`）里先把标记去掉，以后新开的窗口也不用再管（`Library\` 里文件多，要等一会儿）：

```powershell
Get-ChildItem -Recurse | Unblock-File
```

两种都可以用备用的办法：只对当前这个窗口放开，关掉窗口就恢复：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
```

先快速试一下脚本能不能用（每个只跑 15,000 步，刚好够第一批局结束、看到 `Trading/…`；run-id 前面带 `smoke-`）：

```powershell
.\tools\train\run_series.ps1 -Env Builds\win\Gym.exe -Seeds 1 -Smoke -Configs config\ppo_base.yaml,config\variants\fee-0.003.yaml
```

再跑正式的，例如手续费对比、3 个种子（第 7 步显卡更快的话，末尾加 `-Device cuda`）：

```powershell
.\tools\train\run_series.ps1 -Env Builds\win\Gym.exe -Seeds 1,2,3 -NumEnvs 1 -Configs config\ppo_base.yaml,config\variants\fee-0.yaml,config\variants\fee-0.003.yaml
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

run-id 的格式是 `<配置名>-s<种子>-<年月日>`（UTC 日期）。`results\<run-id>` 已经存在的就跳过，绝不覆盖。每次训练留下 `results\<run-id>\`（里面有模型和 `config-used.yaml`）和 `results\<run-id>.log`。每份对比配置改了什么，见 `config\README.md`；改过配置后用 `python tools\train\check_configs.py` 检查。

脚本传给 mlagents-learn 的是 `--seed <种子 × 1000>`，并写进 `results\<run-id>\seed-used.txt`。原因：ML-Agents 给第 k 个环境的种子是「种子 + k」，种子 1、2、3 直接用的话，`-NumEnvs` 大于 1 时会撞号；乘 1000 就拉开了。每个智能体的随机起点都由这个种子推出来，所以配置、种子、`-NumEnvs` 都一样时，环境给的局完全一样。训练器那一侧也用了这个种子，但 PyTorch 不保证逐位相同，曲线可能还会有一点点差别。

每次训练结束，脚本会去 Player 日志里查有没有智能体「按时钟播种」。万一看到一大块红底的 `!!! WARNING: ... seeded from the clock`，`seed-used.txt` 末尾也会多一行 `WARNING: some agents seeded from the clock`：说明这次没用上种子、不能原样重跑。训练结果照样能用，但要记下来，告诉讨论会话（问题单 Q08）。

## 9. 用 TensorBoard 看曲线

```powershell
tensorboard --logdir results
```

浏览器打开 <http://localhost:6006>。对比各组的 `Environment/Cumulative Reward`、`Policy/Entropy`，以及 `Trading/…` 那几条：成交笔数、换手、手续费、持币时间占比。

## 10. 评估

先 `git pull` 拿到最新的仓库（还没有连上 GitHub、仓库是拷过来的时候，跳过 `git pull`：没有远端，它只会报错），再把训练好的模型打进一个评估包，在验证段上跑。评估包单独放在 `Builds\win-eval\`：训练包正在跑时会占着 `Builds\win\` 里的 `UnityPlayer.dll` 等文件，两个包放一起就打不出来。

```powershell
git pull   # 还没有连上 GitHub、仓库是拷过来的时候跳过这一行

$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD`"",
  '-executeMethod', 'Gym.Editor.BuildScript.BuildWindowsEval',
  '-gymModel', "`"$PWD\results\<run-id>\TradingAgent.onnx`"", '-quit',
  '-logFile', "`"$PWD\Logs\build-eval.log`"")
$p.ExitCode   # 0 表示成功

$e = Start-Process -FilePath Builds\win-eval\GymEval.exe -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-gymMode', 'eval', '-gymSegment', 'validation',
  '-gymFeeRate', '0.001', '-gymOut', "`"$PWD\evaluations`"",
  '-logFile', "`"$PWD\Logs\eval.log`"")
$e.ExitCode   # 0 表示成功；不是 0 就看 Logs\eval.log 的最后几行
```

- `-gymFeeRate` 用这个模型训练时的费率。测试段只留到最后出正式数字时用。
- 评估包必须带 `-gymMode eval`、`-gymSegment`（`validation` 或 `test`）和 `-gymOut`，少一个它就报错退出（退出码 1），什么都不写。段没有默认值，免得一不小心跑了测试段。费率写错（比如 `1.5`）也是几秒内退出码 1，`Logs\eval.log` 里写着是哪个参数。
- 跑完马上提交，例如：

```powershell
git add evaluations
git commit -m "eval: <run-id> on the validation segment"
```

**对照组不用再跑。** 验证段、测试段的三档费率（0、0.1 %、0.3 %）对照组都已经在 Mac 上正式跑过、提交了。流水只追加、删不掉，重复跑只会多出重复的行。只有换了费率或者换了段，才需要再跑一次，命令是：

```powershell
$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD`"",
  '-executeMethod', 'Gym.Editor.EvalTools.RunBaselines', '-gymSegment', 'validation',
  '-gymFeeRates', '<新费率>', '-gymOut', 'evaluations', '-quit', '-logFile', "`"$PWD\Logs\baselines.log`"")
$p.ExitCode   # 0 表示成功；不是 0 就看 Logs\baselines.log
```

看汇总表：

```powershell
python tools\eval\summarize.py evaluations\log.csv
```

## 11. 东西都放在哪

| 什么 | 在哪 | 进不进 Git |
| --- | --- | --- |
| 训练产物、模型、TensorBoard 记录 | `results\<run-id>\` | 不进 |
| 一次训练的终端输出 | `results\<run-id>.log` | 不进 |
| 打出来的包 | `Builds\win\`（训练包）、`Builds\win-eval\`（评估包） | 不进 |
| 评估流水和明细 | `evaluations\log.csv`、`evaluations\runs\` | 进（只追加，只在这台 PC 上追加） |
| 试验性的评估、Mac 上的评估 | `evaluations\smoke\` | 不进 |

要在 Mac 上看训练曲线，把 `results\<run-id>` 文件夹拷过去就行；每个文件夹里的 `TradingAgent.onnx` 就是模型。在 Mac 上评估的话，`-gymOut` 写 `evaluations/smoke`。
