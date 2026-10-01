# Training on the Windows PC

From a clean Windows machine to a finished comparison series. Every step runs in **PowerShell** unless it says otherwise.

> **Not yet verified on the PC.** Everything below was written on the Mac. The Mac equivalents of the build, smoke, series and evaluation steps have been run there; the Windows commands themselves have not. Note anything that differs and fix this page.

Rules that matter for every step:

- Use **exactly** Unity 6000.0.84f1 and `mlagents` 1.1.0. Other versions break ML-Agents in ways that are hard to see.
- Close the Unity editor before any command-line Unity step; the project cannot be open twice.
- **Do not press Ctrl+C during training.** Let a run reach its `max_steps`. If a run must be abandoned, leave its `results\<run-id>` folder alone and start a new run id.
- `results\` and `Builds\` stay on this machine; Git ignores them. `evaluations\log.csv` is tracked and append-only.
- Keep the PC awake during long runs (*Settings → System → Power*: never sleep while plugged in).

## 1. Git

Install Git for Windows from <https://git-scm.com/download/win> (defaults are fine). Check:

```powershell
git --version
```

## 2. Unity Hub and Unity 6000.0.84f1

1. Install Unity Hub from <https://unity.com/download> and sign in.
2. Install the editor **6000.0.84f1** (LTS): in a browser open `unityhub://6000.0.84f1/78ab6fc243d5`, or *Installs → Install Editor → Archive → download archive* and pick 6000.0.84f1.
3. Tick the module **Windows Build Support (Mono)**. Nothing else is needed.

The editor ends up in `C:\Program Files\Unity\Hub\Editor\6000.0.84f1\Editor\Unity.exe`. Keep its path in a variable for the later steps:

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.0.84f1\Editor\Unity.exe'
Test-Path $unity
```

## 3. Miniforge and the `mlagents` environment

1. Install Miniforge from <https://github.com/conda-forge/miniforge> (Windows x86_64 installer, "Just Me").
2. Open **Miniforge Prompt** once and run `conda init powershell`, then open a new PowerShell window.
3. Create the environment as in the README's Windows section:

```powershell
conda create -n mlagents python=3.10.12
conda activate mlagents
pip install torch~=2.2.1 --index-url https://download.pytorch.org/whl/cu121
pip install mlagents==1.1.0
mlagents-learn --help
```

Training runs on the CPU (`torch_settings.device: cpu` in every config), so the CUDA build of PyTorch is installed but not used.

## 4. Get the repository

```powershell
cd $HOME\Documents
git clone <repository URL> ZHENN_Ledger_Gym
cd ZHENN_Ledger_Gym
```

The repository URL exists once the project is on GitHub. Until then, copy the folder from the Mac without `Library\`, `Builds\`, `results\` and `Logs\`.

## 5. Open the project once

In Unity Hub: *Projects → Add → Add project from disk*, choose the folder, and open it with 6000.0.84f1. The first import takes several minutes. When the editor is idle, close it.

## 6. Build the Windows training player

`Unity.exe` is a window program, so PowerShell would not wait for it; `Start-Process -Wait` does.

```powershell
$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD`"",
  '-executeMethod', 'Gym.Editor.BuildScript.BuildWindowsTraining', '-quit',
  '-logFile', "`"$PWD\Logs\build-win.log`"")
$p.ExitCode   # 0 = success
Test-Path Builds\win\Gym.exe
```

If the exit code is 1, the end of `Logs\build-win.log` says why; a missing *Windows Build Support (Mono)* module is named there.

The first time `Gym.exe` runs, Windows Firewall may ask about network access: allow it on private networks (ML-Agents talks to the player on localhost ports 5005 and up).

## 7. Speed test: 100k steps

```powershell
conda activate mlagents
$t = Measure-Command { mlagents-learn config\smoke-100k.yaml --env Builds\win\Gym.exe --run-id smoke-pc-100k --no-graphics }
"{0:N0} steps/s" -f (100000 / $t.TotalSeconds)
python tools\train\read_scalars.py results\smoke-pc-100k
```

Write down the steps per second. 2M steps take `2000000 ÷ (steps/s)` seconds. For comparison, the Mac (Apple M5, 10 cores) did about 2,000–2,900 steps/s with one environment, so 2M steps take 12–16 minutes there. The speed decides how many steps and seeds each group gets in the real series.

`read_scalars.py` should list `Environment/Cumulative Reward`, `Policy/Entropy` and the `Trading/*` tags, with `Trading/FeeRate` = 0.001. `Trading/*` only appears once the first episodes have ended: 16 agents × 720 steps = 11,520 steps.

## 8. The comparison series

PowerShell may refuse to run scripts the first time; allow them for this window only:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
```

First a quick check that the script works (15,000 steps each, just enough for the `Trading/*` tags to appear; run ids start with `smoke-`):

```powershell
.\tools\train\run_series.ps1 -Env Builds\win\Gym.exe -Seeds 1 -Smoke -Configs config\ppo_base.yaml,config\variants\fee-0.003.yaml
```

Then the real series, for example the fee comparison with three seeds:

```powershell
.\tools\train\run_series.ps1 -Env Builds\win\Gym.exe -Seeds 1,2,3 -NumEnvs 1 -Configs config\ppo_base.yaml,config\variants\fee-0.yaml,config\variants\fee-0.003.yaml
```

| Option | Meaning | Default |
| --- | --- | --- |
| `-Env` | the training build | required |
| `-Configs` | config files, comma-separated | required |
| `-Seeds` | seeds, comma-separated (passed as seed × 1000) | `1,2,3,4,5` |
| `-NumEnvs` | parallel players per run; one value for the whole series | `1` |
| `-Prefix` | extra word at the front of every run id | none |
| `-Smoke` / `-SmokeSteps` | shortened copies in `results\_tmp\` (15,000 steps) | off |
| `-DryRun` | print the commands without running them | off |

Run ids are `<config>-s<seed>-<yyyyMMdd>` (UTC date). A run whose `results\<run-id>` already exists is skipped, never overwritten. Each run leaves `results\<run-id>\` (with the model and `config-used.yaml`) and `results\<run-id>.log`. `config\README.md` explains what each variant changes; `python tools\train\check_configs.py` checks them.

The script passes `--seed <seed × 1000>` to mlagents-learn and writes it to `results\<run-id>\seed-used.txt`. ML-Agents gives environment k the seed + k, so plain seeds 1, 2, 3 would collide with `-NumEnvs` above 1; multiplying by 1000 keeps them apart. Every agent derives its random episode starts from that seed, so the same config, seed and `-NumEnvs` replay the same episodes. The trainer's own numbers are seeded as well, but PyTorch does not promise bit-identical results, so curves may still differ slightly.

## 9. TensorBoard

```powershell
tensorboard --logdir results
```

Open <http://localhost:6006>. Compare `Environment/Cumulative Reward`, `Policy/Entropy` and the `Trading/*` curves (trades, turnover, fees, exposure) between groups.

## 10. Evaluation

Build an evaluation player with a trained model baked in, then run it on the validation segment:

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

Use the same `-gymFeeRate` the model was trained with. Use the test segment only for the final numbers. Baselines for comparison (each run appends rows):

```powershell
$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD`"",
  '-executeMethod', 'Gym.Editor.EvalTools.RunBaselines', '-gymSegment', 'validation',
  '-gymOut', 'evaluations', '-quit', '-logFile', "`"$PWD\Logs\baselines.log`"")
python tools\eval\summarize.py evaluations\log.csv
```

## 11. What stays where

| What | Where | In Git? |
| --- | --- | --- |
| Training output, models, TensorBoard events | `results\<run-id>\` | no |
| Terminal output of a run | `results\<run-id>.log` | no |
| Players | `Builds\win\` | no |
| Evaluation log and details | `evaluations\log.csv`, `evaluations\runs\` | yes (append only) |
| Smoke evaluations | `evaluations\smoke\` | no |

To analyse results on the Mac, copy the `results\<run-id>` folders across; `TradingAgent.onnx` inside each is the model.
