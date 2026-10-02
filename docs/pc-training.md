# Training on the Windows PC

From a clean Windows machine to a finished comparison series. Every step runs in **PowerShell** unless it says otherwise.

> **Not yet verified on the PC.** Everything below was written on the Mac. The Mac equivalents of the build, smoke, series and evaluation steps have been run there; the Windows commands themselves have not. Note anything that differs and fix this page.

Rules that matter for every step:

- Use **exactly** Unity 6000.0.84f1 and `mlagents` 1.1.0. Other versions break ML-Agents in ways that are hard to see.
- Close the Unity editor before any command-line Unity step; the project cannot be open twice.
- **Do not press Ctrl+C during training.** Let a run reach its `max_steps`. If a run must be abandoned, leave its `results\<run-id>` folder alone and start a new run id: with the series script, add `-Prefix` (for example `-Prefix rerun1`) and the run ids change.
- `results\` and `Builds\` stay on this machine; Git ignores them. `evaluations\log.csv` is tracked and append-only. **The official evaluation log is appended only on this PC** (the trained models live here): `git pull` before appending, commit right after. Evaluations on the Mac always go to `evaluations\smoke\`.
- Keep the PC awake during long runs (*Settings → System → Power*: never sleep while plugged in).

## 1. Git

Install Git for Windows from <https://git-scm.com/download/win> (defaults are fine). Check:

```powershell
git --version
```

## 2. Unity Hub and Unity 6000.0.84f1

1. Install Unity Hub from <https://unity.com/download> and sign in.
2. Install the editor **6000.0.84f1** (LTS): in a browser open `unityhub://6000.0.84f1/78ab6fc243d5`, or *Installs → Install Editor → Archive → download archive* and pick 6000.0.84f1.
3. No extra modules are needed: the Windows editor builds Windows players on its own. If the build in step 6 reports a missing module, install the module it names in the Hub (*Installs →* the gear next to this version *→ Add modules*).

The editor ends up in `C:\Program Files\Unity\Hub\Editor\6000.0.84f1\Editor\Unity.exe`. Keep its path in a variable for the later steps:

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.0.84f1\Editor\Unity.exe'
Test-Path $unity
```

## 3. Miniforge and the `mlagents` environment

1. Install Miniforge from <https://github.com/conda-forge/miniforge> (Windows x86_64 installer, "Just Me").
2. **First let PowerShell run local scripts.** Windows has a switch for which scripts may run (the execution policy), and on Home and Pro it allows none by default. The next step, `conda init`, adds code to the script PowerShell runs every time it opens; without this switch a new window starts with a red error ("running scripts is disabled on this system") and the `conda` command is missing. Open PowerShell and run:

   ```powershell
   Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
   ```

   Answer `Y` when it asks whether to change the policy. This only affects your user: scripts written on this machine (conda's, the repository's `run_series.ps1`) may run, while unsigned scripts downloaded from the internet are still blocked.
3. Open **Miniforge Prompt** once and run `conda init powershell`, then close every PowerShell window and open a new one. **The new window must not start with red text.** If it does, go back to step 2.
4. Create the environment as in the README's Windows section. This installs the **CUDA build** of PyTorch, which can use the graphics card as well as the CPU:

```powershell
conda create -n mlagents python=3.10.12
conda activate mlagents
pip install torch~=2.2.1 --index-url https://download.pytorch.org/whl/cu121
pip install mlagents==1.1.0
mlagents-learn --help
python -c "import torch; print(torch.cuda.is_available())"
```

`True` on the last line means PyTorch found the graphics card; `False` means CPU only, and the graphics-card speed test in step 7 is skipped. Which of the two trains is decided in step 7.

## 4. Get the repository

Put it somewhere **OneDrive does not sync**, such as `C:\Gym`, not under Documents or Desktop: many Windows 11 machines hand those two folders to OneDrive, and a Unity project's `Library\` holds tens of thousands of small files that a synced folder keeps uploading and locking, which makes imports and builds fail in strange ways. It is the same trap as keeping a Unity project in iCloud Drive or Dropbox.

```powershell
mkdir C:\Gym
cd C:\Gym
git clone <repository URL> ZHENN_Ledger_Gym
cd ZHENN_Ledger_Gym
```

The repository URL exists once the project is on GitHub. Until then, copy the folder from the Mac to `C:\Gym\ZHENN_Ledger_Gym` without `Library\`, `Builds\`, `results\` and `Logs\`. All later commands run in `C:\Gym\ZHENN_Ledger_Gym`.

## 5. Open the project once

In Unity Hub: *Projects → Add → Add project from disk*, choose `C:\Gym\ZHENN_Ledger_Gym`, and open it with 6000.0.84f1. The first import takes several minutes. When the editor is idle, close it.

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

If the exit code is 1, the end of `Logs\build-win.log` says why; a missing module is named there (install it as in step 2).

## 7. Speed test: 100k steps on the CPU and on the graphics card

Run both and train on the faster one. The time is taken before and after each run, so `mlagents-learn` prints its output as it goes:

```powershell
conda activate mlagents

# CPU
$t0 = Get-Date
mlagents-learn config\smoke-100k.yaml --env Builds\win\Gym.exe --run-id smoke-pc-100k-cpu --no-graphics
"CPU: {0:N0} steps/s" -f (100000 / ((Get-Date) - $t0).TotalSeconds)

# Graphics card (skip if step 3 printed False)
$t0 = Get-Date
mlagents-learn config\smoke-100k-cuda.yaml --env Builds\win\Gym.exe --run-id smoke-pc-100k-cuda --no-graphics
"GPU: {0:N0} steps/s" -f (100000 / ((Get-Date) - $t0).TotalSeconds)

python tools\train\read_scalars.py results\smoke-pc-100k-cpu
```

The first time `mlagents-learn` runs, Windows Firewall may ask whether **Python** (`python.exe`) may use the network. Allow or Cancel both work: `mlagents-learn` only waits on local port 5005 for the player to connect, nothing goes outside, and connections within the machine are not affected by the firewall.

- Write down both steps-per-second figures and **use the faster device**. If the graphics-card run stops with an error (a known ML-Agents problem; the message contains `Expected all tensors to be on the same device`), use the CPU.
- If the graphics card is faster, add `-Device cuda` to the series in step 8; for the CPU add nothing.
- 2M steps take `2000000 ÷ (steps/s)` seconds. For comparison, the Mac (Apple M5, 10 cores, CPU) did about 2,000–2,900 steps/s with one environment, so 2M steps take 12–16 minutes there. The faster speed decides how many steps and seeds each group gets in the real series.

`read_scalars.py` should list `Environment/Cumulative Reward`, `Policy/Entropy` and the `Trading/*` tags, with `Trading/FeeRate` = 0.001. `Trading/*` only appears once the first episodes have ended: 16 agents × 720 steps = 11,520 steps. The README's "Training curves" table explains each `Trading/*` tag.

## 8. The comparison series

After the `Set-ExecutionPolicy` of step 3 the script runs as is. If PowerShell still says scripts are disabled, allow them for this window only (fallback):

```powershell
Set-ExecutionPolicy -Scope Process Bypass
```

First a quick check that the script works (15,000 steps each, just enough for the `Trading/*` tags to appear; run ids start with `smoke-`):

```powershell
.\tools\train\run_series.ps1 -Env Builds\win\Gym.exe -Seeds 1 -Smoke -Configs config\ppo_base.yaml,config\variants\fee-0.003.yaml
```

Then the real series, for example the fee comparison with three seeds (add `-Device cuda` if the graphics card won in step 7):

```powershell
.\tools\train\run_series.ps1 -Env Builds\win\Gym.exe -Seeds 1,2,3 -NumEnvs 1 -Configs config\ppo_base.yaml,config\variants\fee-0.yaml,config\variants\fee-0.003.yaml
```

| Option | Meaning | Default |
| --- | --- | --- |
| `-Env` | the training build | required |
| `-Configs` | config files, comma-separated | required |
| `-Seeds` | seeds, comma-separated, not negative (passed as seed × 1000) | `1,2,3,4,5` |
| `-NumEnvs` | parallel players per run; one value for the whole series | `1` |
| `-Prefix` | extra word at the front of every run id; use it to get new run ids when a run has to be redone | none |
| `-Device` | `cpu` or `cuda`. Changes only the copy of each config in `results\_tmp\`, never the files in `config\`; `config-used.yaml` records what was used | `cpu` |
| `-Smoke` / `-SmokeSteps` | shortened copies in `results\_tmp\` (15,000 steps) | off |
| `-DryRun` | print the commands; write no files | off |

Run ids are `<config>-s<seed>-<yyyyMMdd>` (UTC date). A run whose `results\<run-id>` already exists is skipped, never overwritten. Each run leaves `results\<run-id>\` (with the model and `config-used.yaml`) and `results\<run-id>.log`. `config\README.md` explains what each variant changes; `python tools\train\check_configs.py` checks them.

The script passes `--seed <seed × 1000>` to mlagents-learn and writes it to `results\<run-id>\seed-used.txt`. ML-Agents gives environment k the seed + k, so plain seeds 1, 2, 3 would collide with `-NumEnvs` above 1; multiplying by 1000 keeps them apart. Every agent derives its random episode starts from that seed, so the same config, seed and `-NumEnvs` replay the same episodes. The trainer's own numbers are seeded as well, but PyTorch does not promise bit-identical results, so curves may still differ slightly.

After every run the script checks the Player logs for agents that seeded themselves from the clock. If a block of `!!! WARNING: ... seeded from the clock` appears (yellow on red), and `seed-used.txt` ends with `WARNING: some agents seeded from the clock`, that run did not use the seed and cannot be replayed exactly. Its results are still usable, but note it and report it in the discussion session (issue Q08).

## 9. TensorBoard

```powershell
tensorboard --logdir results
```

Open <http://localhost:6006>. Compare `Environment/Cumulative Reward`, `Policy/Entropy` and the `Trading/*` curves (trades, turnover, fees, exposure) between groups.

## 10. Evaluation

`git pull` first, then build an evaluation player with a trained model baked in and run it on the validation segment. It goes to its own folder, `Builds\win-eval\`: while the training player runs, it holds `UnityPlayer.dll` and the other files in `Builds\win\`, so a second player could not be built next to it.

```powershell
git pull

$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD`"",
  '-executeMethod', 'Gym.Editor.BuildScript.BuildWindowsEval',
  '-gymModel', "`"$PWD\results\<run-id>\TradingAgent.onnx`"", '-quit',
  '-logFile', "`"$PWD\Logs\build-eval.log`"")
$p.ExitCode   # 0 = success

$e = Start-Process -FilePath Builds\win-eval\GymEval.exe -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-gymMode', 'eval', '-gymSegment', 'validation',
  '-gymFeeRate', '0.001', '-gymOut', "`"$PWD\evaluations`"",
  '-logFile', "`"$PWD\Logs\eval.log`"")
$e.ExitCode   # 0 = success; otherwise read the end of Logs\eval.log
```

- Use the same `-gymFeeRate` the model was trained with. Use the test segment only for the final numbers.
- The evaluation player needs `-gymMode eval` and `-gymOut`; without either it exits with code 1 and writes nothing. A fee outside the valid range (for example `1.5`) also exits with code 1 within seconds, and `Logs\eval.log` names the argument.
- Commit right away, for example:

```powershell
git add evaluations
git commit -m "eval: <run-id> on the validation segment"
```

**The baselines do not need running again.** Buy-and-hold, cash and random for the validation and test segments at fees 0, 0.1 % and 0.3 % were run on the Mac and committed. The log is append-only, so running them again only adds duplicate rows that cannot be removed. Run them again only for a new fee rate or a new segment:

```powershell
$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD`"",
  '-executeMethod', 'Gym.Editor.EvalTools.RunBaselines', '-gymSegment', 'validation',
  '-gymFeeRates', '<new fee rate>', '-gymOut', 'evaluations', '-quit', '-logFile', "`"$PWD\Logs\baselines.log`"")
$p.ExitCode   # 0 = success; otherwise read Logs\baselines.log
```

The summary tables:

```powershell
python tools\eval\summarize.py evaluations\log.csv
```

## 11. What stays where

| What | Where | In Git? |
| --- | --- | --- |
| Training output, models, TensorBoard events | `results\<run-id>\` | no |
| Terminal output of a run | `results\<run-id>.log` | no |
| Players | `Builds\win\` (training), `Builds\win-eval\` (evaluation) | no |
| Evaluation log and details | `evaluations\log.csv`, `evaluations\runs\` | yes (append only, and only on this PC) |
| Trial evaluations, evaluations on the Mac | `evaluations\smoke\` | no |

To look at training curves on the Mac, copy the `results\<run-id>` folders across; `TradingAgent.onnx` inside each is the model. An evaluation on the Mac uses `-gymOut evaluations/smoke`.
