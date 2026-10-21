# Training on the Windows PC

From a clean Windows machine to a finished comparison series. Every step runs in **PowerShell** unless it says otherwise.

> **Not yet verified on the PC.** Everything below was written on the Mac. The Mac equivalents of the build, smoke, series and evaluation steps have been run there; the Windows commands themselves have not. Note anything that differs and fix this page.

Rules that matter for every step:

- Use **exactly** Unity 6000.0.84f1 and `mlagents` 1.1.0. Other versions break ML-Agents in ways that are hard to see.
- Close the Unity editor before any command-line Unity step; the project cannot be open twice.
- **Do not press Ctrl+C during training.** Let a run reach its `max_steps`. If a run must be abandoned, leave its `results\<run-id>` folder alone and start a new run id: with the series script, add `-Prefix` (for example `-Prefix rerun1`) and the run ids change.
- `results\` and `unity\Builds\` stay on this machine; Git ignores them. `evaluations\log.csv` is tracked and append-only. **The official evaluation log is appended only on this PC** (the trained models live here): `git pull` before appending (skip `git pull` while the repository is a copy that is not connected to GitHub yet), commit right after. Evaluations on the Mac always go to `evaluations\smoke\`.
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
4. Create the environment as in the [Windows section of setup.md](setup.md#windows). This installs the **CUDA build** of PyTorch, which can use the graphics card as well as the CPU:

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

Put it somewhere **OneDrive does not sync**, such as `E:\project`, not under Documents or Desktop: many Windows 11 machines hand those two folders to OneDrive, and a Unity project's `Library\` holds tens of thousands of small files that a synced folder keeps uploading and locking, which makes imports and builds fail in strange ways. It is the same trap as keeping a Unity project in iCloud Drive or Dropbox.

```powershell
mkdir E:\project
cd E:\project
git clone <repository URL> ZHENNs_Ledger_Gym
cd ZHENNs_Ledger_Gym
```

The repository URL exists once the project is on GitHub. Until then, copy the folder from the Mac to `E:\project\ZHENNs_Ledger_Gym` without `unity\Library\`, `unity\Builds\`, `results\` and `unity\Logs\`. All later commands run in `E:\project\ZHENNs_Ledger_Gym`.

## 5. Open the project once

In Unity Hub: *Projects → Add → Add project from disk*, choose `E:\project\ZHENNs_Ledger_Gym\unity`, and open it with 6000.0.84f1. The first import takes several minutes. When the editor is idle, close it.

## 6. Build the Windows training player

`Unity.exe` is a window program, so PowerShell would not wait for it; `Start-Process -Wait` does.

```powershell
$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD\unity`"",
  '-executeMethod', 'Gym.Editor.BuildScript.BuildWindowsTraining', '-quit',
  '-logFile', "`"$PWD\unity\Logs\build-win.log`"")
$p.ExitCode   # 0 = success
Test-Path unity\Builds\win\Gym.exe
```

If the exit code is 1, the end of `unity\Logs\build-win.log` says why; a missing module is named there (install it as in step 2).

## 7. Speed test: 100k steps on the CPU and on the graphics card

Run both and train on the faster one. The time is taken before and after each run, so `mlagents-learn` prints its output as it goes:

```powershell
conda activate mlagents

# CPU
$t0 = Get-Date
mlagents-learn config\smoke-100k.yaml --env unity\Builds\win\Gym.exe --run-id smoke-pc-100k-cpu --no-graphics
"CPU: {0:N0} steps/s" -f (100000 / ((Get-Date) - $t0).TotalSeconds)

# Graphics card (skip if step 3 printed False)
$t0 = Get-Date
mlagents-learn config\smoke-100k-cuda.yaml --env unity\Builds\win\Gym.exe --run-id smoke-pc-100k-cuda --no-graphics
if ($LASTEXITCODE -ne 0) {
  "GPU: the graphics card failed this time; use the CPU"   # no speed for a failed run
} else {
  "GPU: {0:N0} steps/s" -f (100000 / ((Get-Date) - $t0).TotalSeconds)
}

python scripts\train\read_scalars.py results\smoke-pc-100k-cpu
```

The first time `mlagents-learn` runs, Windows Firewall may ask whether **Python** (`python.exe`) may use the network. Allow or Cancel both work: `mlagents-learn` only waits on local port 5005 for the player to connect, nothing goes outside, and connections within the machine are not affected by the firewall.

- Write down both steps-per-second figures and **use the faster device**. If the graphics-card run stops with an error (a known ML-Agents problem; the message contains `Expected all tensors to be on the same device`), the last line prints "the graphics card failed this time; use the CPU" instead of a speed: a run that stopped early takes little time, so a steps-per-second figure would come out far too high and make the graphics card look faster. Use the CPU then.
- If the graphics card is faster, add `-Device cuda` to the series in step 8; for the CPU add nothing.
- 2M steps take `2000000 ÷ (steps/s)` seconds. For comparison, the Mac (Apple M5, 10 cores, CPU) did about 2,000–2,900 steps/s with one environment, so 2M steps take 12–16 minutes there. The faster speed decides how many steps and seeds each group gets in the real series.

`read_scalars.py` should list `Environment/Cumulative Reward`, `Policy/Entropy` and the `Trading/*` tags, with `Trading/FeeRate` = 0.001. `Trading/*` only appears once the first episodes have ended: 16 agents × 720 steps = 11,520 steps. The "Training curves" table in [training.md](training.md#training-curves-trading-in-tensorboard) explains each `Trading/*` tag.

## 8. The comparison series

After the `Set-ExecutionPolicy` of step 3 the script runs as is. If PowerShell still prints red text, check which of the two it is:

- "…\run_series.ps1 cannot be loaded because running scripts is disabled on this system": the command of step 3 did not take effect.
- "…\run_series.ps1 cannot be loaded. The file …\run_series.ps1 is not digitally signed": the repository was unpacked from a zip downloaded in a browser. Windows marks every downloaded file as coming from the internet, and step 3 only allows scripts written on this machine, so marked ones are still blocked.

For the second one, remove the mark once in the repository folder (`E:\project\ZHENNs_Ledger_Gym`); new windows then need nothing more (it takes a while because of the many files in `unity\Library\`):

```powershell
Get-ChildItem -Recurse | Unblock-File
```

Either one can also use the fallback, which allows scripts for this window only and ends when the window closes:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
```

First a quick check that the script works (15,000 steps each, just enough for the `Trading/*` tags to appear; run ids start with `smoke-`):

```powershell
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1 -Smoke -Configs config\ppo_base.yaml,config\variants\fee-0.003.yaml
```

Then the real series, for example the fee comparison with three seeds (add `-Device cuda` if the graphics card won in step 7):

```powershell
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1,2,3 -NumEnvs 1 -Configs config\ppo_base.yaml,config\variants\fee-0.yaml,config\variants\fee-0.003.yaml
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
| `-EnvArgs` | words for the training player, as one quoted string, for example `-EnvArgs "-gymTrainSymbols BTCUSDT,ETHUSDT"`; how and why in section 12 | none |
| `-BasePort` | the first port of this series (environment k uses that port + k); one per series when several run side by side, see 14.4 | not passed (`mlagents-learn` uses 5005) |

Run ids are `<config>-s<seed>-<yyyyMMdd>` (UTC date). A run whose `results\<run-id>` already exists is skipped, never overwritten. Each run leaves `results\<run-id>\` (with the model, `config-used.yaml`, `seed-used.txt`, `env-args.txt` and, when the training build has one, a copy of its `build-info.json`; see section 12) and `results\<run-id>.log`. `config\README.md` explains what each variant changes; `python scripts\train\check_configs.py` checks them.

The script passes `--seed <seed × 1000>` to mlagents-learn and writes it to `results\<run-id>\seed-used.txt`. ML-Agents gives environment k the seed + k, so plain seeds 1, 2, 3 would collide with `-NumEnvs` above 1; multiplying by 1000 keeps them apart. Every agent derives its random episode starts from that seed, so the same config, seed and `-NumEnvs` replay the same episodes. The trainer's own numbers are seeded as well, but PyTorch does not promise bit-identical results, so curves may still differ slightly.

After every run the script checks the Player logs for agents that seeded themselves from the clock. If a block of `!!! WARNING: ... seeded from the clock` appears (yellow on red), and `seed-used.txt` ends with `WARNING: some agents seeded from the clock`, that run did not use the seed and cannot be replayed exactly. Its results are still usable, but note it and report it in the discussion session.

## 9. TensorBoard

```powershell
tensorboard --logdir results
```

Open <http://localhost:6006>. Compare `Environment/Cumulative Reward`, `Policy/Entropy` and the `Trading/*` curves (trades, turnover, fees, exposure) between groups.

## 10. Evaluation

`git pull` first (skip `git pull` while the repository is a copy that is not connected to GitHub yet: without a remote it only fails), then build an evaluation player with a trained model baked in and run it on the validation segment. It goes to its own folder, `unity\Builds\win-eval\`: while the training player runs, it holds `UnityPlayer.dll` and the other files in `unity\Builds\win\`, so a second player could not be built next to it.

```powershell
git pull   # skip this line while the repository is a copy not connected to GitHub yet

$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD\unity`"",
  '-executeMethod', 'Gym.Editor.BuildScript.BuildWindowsEval',
  '-gymModel', "`"$PWD\results\<run-id>\TradingAgent.onnx`"", '-quit',
  '-logFile', "`"$PWD\unity\Logs\build-eval.log`"")
$p.ExitCode   # 0 = success

$e = Start-Process -FilePath unity\Builds\win-eval\GymEval.exe -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-gymMode', 'eval', '-gymSegment', 'validation',
  '-gymFeeRate', '0.001', '-gymOut', "`"$PWD\evaluations`"",
  '-logFile', "`"$PWD\unity\Logs\eval.log`"")
$e.ExitCode   # 0 = success; otherwise read the end of unity\Logs\eval.log
```

- Use the same `-gymFeeRate` the model was trained with. Use the test segment only for the final numbers.
- **Round-two models** (trained from `config\v2\`) use the `long` or `long-ta` observation set: add `'-gymObs', 'long'` (or `'long-ta'`) to the evaluation command, and evaluate all three validation segments; see section 12.
- **Round-three models** (trained from `config\v3\`) are dial-mode models: also add `'-gymActionMode', 'dial'`; see 14.5.
- The evaluation player needs `-gymMode eval`, `-gymSegment` (an evaluation segment from `gym-config.json`: `validation-bear`, `validation-recovery`, `validation` or `test`) and `-gymOut`; without any one of them, or with a segment name that is not in the configuration, it exits with code 1 and writes nothing. There is no default segment, so the test segment is never evaluated by accident. A fee outside the valid range (for example `1.5`) also exits with code 1 within seconds, and `unity\Logs\eval.log` names the argument.
- Commit right away, for example:

```powershell
git add evaluations
git commit -m "eval: <run-id> on the validation segment"
```

**The baselines do not need running again.** Buy-and-hold, cash and random for the validation and test segments at fees 0, 0.1 % and 0.3 % were run on the Mac and committed, and so were all six baselines on `validation-bear` and `validation-recovery` and the three rules on `validation` at the same fees. The log is append-only, so running them again only adds duplicate rows that cannot be removed. Run them again only for a new fee rate or a new segment:

```powershell
$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD\unity`"",
  '-executeMethod', 'Gym.Editor.EvalTools.RunBaselines', '-gymSegment', 'validation',
  '-gymFeeRates', '<new fee rate>', '-gymOut', "`"$PWD\evaluations`"", '-quit', '-logFile', "`"$PWD\unity\Logs\baselines.log`"")
$p.ExitCode   # 0 = success; otherwise read unity\Logs\baselines.log
```

The summary tables:

```powershell
python scripts\eval\summarize.py evaluations\log.csv
```

## 11. What stays where

| What | Where | In Git? |
| --- | --- | --- |
| Training output, models, TensorBoard events | `results\<run-id>\` | no |
| Terminal output of a run | `results\<run-id>.log` | no |
| What a run was given: the words for the player and the training build's details | `results\<run-id>\env-args.txt`, `results\<run-id>\build-info.json` | no |
| Players | `unity\Builds\win\` (training), `unity\Builds\win-eval\` (evaluation) | no |
| Evaluation log and details | `evaluations\log.csv`, `evaluations\runs\` | yes (append only, and only on this PC) |
| Trial evaluations, evaluations on the Mac | `evaluations\smoke\` | no |

To look at training curves on the Mac, copy the `results\<run-id>` folders across; `TradingAgent.onnx` inside each is the model. An evaluation on the Mac uses `-gymOut evaluations/smoke`.

## 12. Round two: the new options and `-EnvArgs`

Round two's switches (new segments, more coins, the long observation sets, the risk coefficient, training on several coins) were built and tested on the Mac, but **the training and evaluation players can only be run with them on this PC**, and the `-EnvArgs` of `run_series.ps1` has not run on Windows yet. So do the smoke checks of section 13 first, then the real series.

### 12.1 The new options

| Option | For | Meaning | Default |
| --- | --- | --- | --- |
| `-gymSegment <name>` | evaluation player, baseline tool | the segment to evaluate: `validation-bear` (the fall of 2022), `validation-recovery` (2023-01 to 2024-08), `validation` (the rise of 2024-09 to 2025-08), `test`. **Required**: without it, or with a name that is not in `gym-config.json`, exit code 1 and nothing written. The training player only takes `train` | none |
| `-gymSymbol <name>` | evaluation player, baseline tool, Play, Watch; the training player too | the current coin: `BTCUSDT`, `ETHUSDT`, `ADAUSDT`, `DOGEUSDT`, `XRPUSDT`. The training player refuses `DOGEUSDT` (evaluated only, exit code 1) | the main coin `BTCUSDT` |
| `-gymObs base\|long\|long-ta` | evaluation player, baseline tool, Play; the training player too | the observation set: 35, 41 or 43 numbers. A model only works with the set it was trained on | `observationSet` in `gym-config.json` (`base`) |
| `-gymTrainSymbols A,B,…` | training player | train on several coins, one per episode, picked from the episode's seed; every name must be in the config's `symbols`, not `DOGEUSDT`, no repeats, or exit code 1 | the current coin only |
| `-gymRiskAversion <λ>` | training player, evaluation player, baseline tool | the reward's risk coefficient, finite and not negative; a negative value exits with code 1. An evaluation measures the equity, not the reward, so it changes no evaluation number | 0 |
| environment parameter `obs_set` (in the yaml's `environment_parameters`) | training | 0 = `base`, 1 = `long`, 2 = `long-ta` | `-gymObs` or the config |
| environment parameter `risk_aversion` (likewise) | training | the risk coefficient λ | `-gymRiskAversion`, else 0 |

### 12.2 Training: the observation set and the risk coefficient in the yaml, the coins in `-EnvArgs`

Round two's configs are in `config\v2\` (the first line of each names its base; see `config\README.md`):

| File | What it is |
| --- | --- |
| `ppo_v2.yaml` | round two's base: `ppo_base.yaml` with `normalize: true` (the setting that won round one) |
| `long.yaml`, `long-ta.yaml` | base `ppo_v2`, `obs_set` 1 and 2 |
| `long-risk-0.005.yaml`, `long-risk-0.01.yaml`, `long-risk-0.02.yaml` | base `long`, each with one `risk_aversion` |
| `smoke-long.yaml` | base `long`, a 15,000-step smoke run (used in section 13) |

Rules:

- **Choose the observation set one way only: `obs_set` in the yaml.** Every run keeps its `config-used.yaml`, so that is recorded for free. When the yaml's `obs_set` and a `-gymObs` in `-EnvArgs` are both given, **the environment parameter (the yaml) wins**, without an error if they disagree, so do not put `-gymObs` in `-EnvArgs`. The same goes for the risk coefficient: the yaml's `risk_aversion` wins over `-gymRiskAversion`.
- **Give the coins on the command line, `-gymTrainSymbols`, and leave `gym-config.json` alone.** A mistake in the config's `trainSymbols` stops the evaluation player as well; a mistake on the command line only stops the training player. The list is `BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT`, **without `DOGEUSDT`** (the coin the agent never sees, evaluated only).
- **No `-gymSegment` in the training `-EnvArgs`**: training only takes `train`, and any other segment stops the training player with exit code 1 at the start. No `-logFile` either: the script reads the player logs ML-Agents writes to check for agents seeded from the clock, and a log file of your own would hide them.
- Quote the whole string. The script splits it at spaces and puts the words **last** on the `mlagents-learn` command line, after `--env-args`: `mlagents-learn` hands every word after `--env-args` to the player as given, so it has to be last, and `-gymTrainSymbols` and the list have to be two words; passed as one word, the player ignores it without an error. A word cannot contain a space.

A complete example: the main line and one risk coefficient, on four coins, with three seeds:

```powershell
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1,2,3 -Configs config\v2\long.yaml,config\v2\long-risk-0.01.yaml -EnvArgs "-gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT"
```

With `-DryRun` first, it should print:

```text
env args: 2 word(s), passed last as --env-args: -gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT
build info: E:\project\ZHENNs_Ledger_Gym\unity\Builds\win\Gym_Data\StreamingAssets\Gym\build-info.json (copied to results\<run-id>\build-info.json)
run   long-s1-<date>
      mlagents-learn E:\project\ZHENNs_Ledger_Gym\config\v2\long.yaml --env E:\project\ZHENNs_Ledger_Gym\unity\Builds\win\Gym.exe --run-id long-s1-<date> --seed 1000 --num-envs 1 --no-graphics --env-args -gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT
```

The command must end with `--env-args -gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT`, with no quotes around the list. The script shows a word that contains a space in double quotes, so a single quoted word like `"-gymTrainSymbols BTCUSDT,…"` means the string was not split: stop there.

### 12.3 What every run records

| File | Contents |
| --- | --- |
| `results\<run-id>\config-used.yaml` | the config the run really used, with `obs_set`, `risk_aversion` and `device` |
| `results\<run-id>\seed-used.txt` | the seed passed to `mlagents-learn` |
| `results\<run-id>\env-args.txt` | the words passed to the player, on one line; an empty file without `-EnvArgs` |
| `results\<run-id>\build-info.json` | the training build's details: build time `built_at_utc`, Unity version `unity_version`, SHA-256 of the build's `gym-config.json` `gym_config_sha256`. It is copied from the build, where it lives in `unity\Builds\win\Gym_Data\StreamingAssets\Gym\build-info.json`, **not next to `Gym.exe`**. A build made before this version has none; the script then starts with `build info: not found at …`, and rebuilding the training player (step 6) adds it |
| `results\<run-id>\run_logs\Player-*.log` | the player's own log: one `observations …` line and one `training symbols …` line per agent, and one `training player: …` line (observation set, current coin, list, segment dates and the SHA-256 of the `gym-config.json` it read, which should equal the one in `build-info.json`) |

### 12.4 Evaluating round-two models

- **The observation set must match the model.** For a model trained on `long`, add `'-gymObs', 'long'` to the evaluation command (`long-ta` likewise). When they do not match, the evaluation player exits with code 1 and writes nothing, and its log gives both sizes, for example `the model takes 35 observations but the observation set long has 41`. The evaluation build records the model's input size in `build-info.json` as `vector_observation_size` and compares it at the start.
- Evaluate all three validation segments: `validation-bear`, `validation-recovery`, `validation`. **The test segment `test` is evaluated once, at the very end of the round**, after the final model has been chosen.
- For another coin add `'-gymSymbol', 'ETHUSDT'` and the like; the log's `symbol` column is that coin.
- `BuildWindowsEval` always writes to `unity\Builds\win-eval\GymEval.exe`: **a new evaluation build replaces the previous one.** To keep the previous one, copy the whole `unity\Builds\win-eval\` away first. To see which model a build holds, read `run_id` in `unity\Builds\win-eval\GymEval_Data\StreamingAssets\Gym\build-info.json`.

## 13. Round two smoke checks: what the Mac could not run

Do them in order; later ones use what earlier ones made. Each has its commands and what you should see; when one does not match, note the exact text, stop and report it in the discussion session. Smoke evaluations always go to `evaluations\smoke\` (not in Git), **never to the official log `evaluations\log.csv`**. Replace `<date>` with the date (UTC) in the run id the script printed.

### Smoke 0: print the command only

```powershell
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1 -DryRun -Configs config\v2\smoke-long.yaml -EnvArgs "-gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT"
```

You should see `env args: 2 word(s), …`, and under `run   smoke-long-s1-<date>` a command ending with `--no-graphics --env-args -gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT`, no quotes around the list (see 12.2). `build info: not found at …` is expected before the training player is rebuilt. If PowerShell says `-Env` is ambiguous (`parameter name 'Env' is ambiguous`), or the list is in quotes, stop.

### Smoke 1: rebuild the training player and read its `build-info.json`

Rebuild the training player as in step 6, then:

```powershell
Get-Content unity\Builds\win\Gym_Data\StreamingAssets\Gym\build-info.json
(Get-FileHash unity\Builds\win\Gym_Data\StreamingAssets\Gym\gym-config.json -Algorithm SHA256).Hash.ToLower()
```

You should see `built_at_utc` (just now), `unity_version` (`6000.0.84f1`) and `gym_config_sha256`, the last equal to the hash on the second line. Run smoke 0 again: it should now say `build info: …\Gym_Data\StreamingAssets\Gym\build-info.json (copied to …)`.

### Smoke 2: did the observation set and the coin list arrive?

```powershell
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1 -Configs config\v2\smoke-long.yaml -EnvArgs "-gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT"
$run = 'smoke-long-s1-<date>'
Get-Content results\$run\env-args.txt
Get-Content results\$run\build-info.json
Select-String -Path results\$run\run_logs\Player-*.log -Pattern 'observation|training symbols|training player' | Select-Object -First 6
python scripts\train\read_scalars.py results\$run | Select-String 'Trading/'
```

You should see:

- `env-args.txt` holds the one line `-gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT`; `build-info.json` is the one from smoke 1.
- **Read the observation set from each agent's start line**: `…: index …, mode Training, segment train 2017-08-17..2021-12-31, observations long (41 numbers, history 2160), master seed … (trainer)`, after a line `the trainer's obs_set chooses the observation set long (the config and command line said base)`. **Not** from the line `config …, mode Train, observations base`: the set the trainer chooses is not written back into the config, so that line still says `base`.
- One line per agent `training symbols BTCUSDT (train 2017-08-17..2021-12-31), ETHUSDT (train 2017-08-17..2021-12-31), ADAUSDT (train 2018-04-17..2021-12-31), XRPUSDT (train 2018-05-04..2021-12-31); each episode picks one from its episode seed`.
- One line for the player `training player: observations long (41 numbers), current symbol BTCUSDT, training symbols …, segment train 2017-08-17..2021-12-31, gym-config.json sha256 <the one in build-info.json> (…)`.
- `Trading/RiskAversion` and `Trading/RiskPenalty` are 0 (this config has no risk coefficient), `Trading/FeeRate` is 0.001, and all four `Trading/SymbolShare/BTCUSDT`, `…/ETHUSDT`, `…/ADAUSDT`, `…/XRPUSDT` are there. 15,000 steps finish only about 16 episodes, one or two per point, so values jumping between 0, 0.5 and 1 are normal; smoke 4 checks the shares.
- No red `!!! WARNING ... seeded from the clock`.

### Smoke 3: did the risk coefficient arrive?

```powershell
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1 -Smoke -Configs config\v2\long-risk-0.01.yaml
python scripts\train\read_scalars.py results\smoke-long-risk-0.01-s1-<date> | Select-String 'Trading/Risk'
```

You should see `Trading/RiskAversion` at 0.01 (TensorBoard keeps single precision, so it may read `0.009999999776482582`) and `Trading/RiskPenalty` above 0. In smoke 2 both were 0: together the two runs show that the config's `risk_aversion` really reached the player.

### Smoke 4: speed and memory when training on four coins

With four coins every one of the 16 agents builds 4 environments (64 in all); the candles are shared, so memory should not grow much. Measure it: 100k steps on one coin and on four, with `-Prefix` to keep the run ids apart (without it both would be `smoke-long-s1-<date>` like smoke 2, and be skipped):

```powershell
$t0 = Get-Date
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1 -Smoke -SmokeSteps 100000 -Prefix speed1 -Configs config\v2\long.yaml
"one coin: {0:N0} steps/s" -f (100000 / ((Get-Date) - $t0).TotalSeconds)

$t0 = Get-Date
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1 -Smoke -SmokeSteps 100000 -Prefix speed4 -Configs config\v2\long.yaml -EnvArgs "-gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT"
"four coins: {0:N0} steps/s" -f (100000 / ((Get-Date) - $t0).TotalSeconds)
```

Halfway through each run, look at the player's memory from a second PowerShell window:

```powershell
Get-Process Gym | Select-Object Id, @{ n = 'MB'; e = { [int]($_.WorkingSet64 / 1MB) } }
```

You should get two steps-per-second figures and two memory figures; note all four in the PC training log. For the four-coin run, set *Smoothing* on the left of TensorBoard (step 9) to about 0.95: the four `Trading/SymbolShare/*` curves sit around 0.25 (about 130 episodes). The one-coin run has a single `Trading/SymbolShare/BTCUSDT` at 1.

### Smoke 5: the training player refuses DOGE

Start the training player on its own, without `mlagents-learn`, with `DOGEUSDT` as the current coin:

```powershell
$p = Start-Process -FilePath unity\Builds\win\Gym.exe -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-gymSymbol', 'DOGEUSDT', '-logFile', "`"$PWD\unity\Logs\train-doge.log`"")
$null = $p.Handle   # read it once, or Windows PowerShell cannot read the exit code later
if (-not $p.WaitForExit(120000)) { Stop-Process -Id $p.Id; 'did not stop within 2 minutes' }
$p.ExitCode   # should be 1
Select-String -Path unity\Logs\train-doge.log -Pattern 'evalOnly'
```

You should see exit code 1 and `DOGEUSDT is evalOnly (only evaluated, never trained on), so a training agent cannot use it as the current symbol; …` in the log. Still running after two minutes is wrong.

### Smoke 6: the evaluation player checks the model's input size

Build an evaluation player with round one's 35-input model (the build command of step 10 with this model), then evaluate it with the `long` set:

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
$e.ExitCode   # should be 1
(Get-ChildItem evaluations\smoke\c16-obs-long).Count   # should be 0
Select-String -Path unity\Logs\eval-obs-long.log -Pattern 'observations but'
```

You should see `"run_id": "normalize-on-s3-20261019"` and `"vector_observation_size": 35` in `build-info.json`; the evaluation exits with code 1, `c16-obs-long` stays empty, and the log says `the model takes 35 observations but the observation set long has 41`.

### Smoke 7: watching equals evaluating, column by column

Still the evaluation build of smoke 6 (round one's model, `base`). Evaluate `validation` normally, then run the Watch PlayMode tests with the model (without these two environment variables they are ignored). No `mlagents-learn` may be running, and the Unity editor must be closed:

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

**Only a log written by an evaluation build from batch 24 on will do**: `GYM_WATCH_EVAL_LOG` must point to the `log.csv` such a build has just written (its `build-info.json` has `"action_shape_recorded": true`). Since batch 24 the agent rows' `notes` end with `; mode=orders`, and so does the watch's row; compared with a log from an older build (or the old `evaluations\log.csv`) they differ in `notes` only, which is expected, not an error (`24B` step 2). When doing this check again in round three, first rebuild smoke 6's evaluation build with this version, as in step 10.

You should see an evaluation row with `kind` `agent`, `segment` `validation` and `notes` ending in `obs=base; mode=orders` (a build from before batch 24 writes `obs=base` at the end); the tests exit with code 0, the `<test-run` line says `total="2" passed="2"` and `skipped="0"`, and the log has `RESULT: the watch row equals the evaluation row in every column but timestamp_utc and model_run_id, and the final equity is the same`. If `git status` afterwards shows only the `SENTIS_ANALYTICS_ENABLED` change in `unity\ProjectSettings\ProjectSettings.asset`, restore it with `git checkout -- unity/ProjectSettings/ProjectSettings.asset`.

### Smoke 8: the evaluation player takes the new segment names and refuses an unknown one

Build an evaluation player with the 15,000-step model of smoke 2 (`long`). **This replaces the evaluation build of smoke 6** (the output path is fixed):

```powershell
$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD\unity`"",
  '-executeMethod', 'Gym.Editor.BuildScript.BuildWindowsEval',
  '-gymModel', "`"$PWD\results\smoke-long-s1-<date>\TradingAgent.onnx`"", '-quit',
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
$e.ExitCode   # should be 1
(Get-ChildItem evaluations\smoke\c16-bad-segment).Count   # should be 0
Select-String -Path unity\Logs\eval-bad-segment.log -Pattern 'not an evaluation segment'
```

You should see `run_id` `smoke-long-s1-<date>` and `vector_observation_size` 41 in `build-info.json`; the first evaluation exits with code 0 and its row has this model's `model_run_id`, `segment` `validation-bear` from `2022-01-01` to `2022-12-31` and `notes` ending in `obs=long`; the second exits with code 1, `c16-bad-segment` stays empty, and the log says `-gymSegment validation-nope is not an evaluation segment; use …` followed by the four names.

### Smoke 9: the evaluation player on another coin

Still the evaluation build of smoke 8:

```powershell
$e = Start-Process -FilePath unity\Builds\win-eval\GymEval.exe -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-gymMode', 'eval', '-gymSegment', 'validation-bear', '-gymObs', 'long',
  '-gymSymbol', 'ETHUSDT', '-gymFeeRate', '0.001', '-gymOut', "`"$PWD\evaluations\smoke`"",
  '-logFile', "`"$PWD\unity\Logs\eval-eth.log`"")
$e.ExitCode   # 0
Import-Csv evaluations\smoke\log.csv | Select-Object -Last 1 | Format-List symbol, segment, notes
```

You should see exit code 0 and `symbol` `ETHUSDT` in the last row.

### Smoke 10: the evaluation player refuses a negative risk coefficient

```powershell
New-Item -ItemType Directory -Force evaluations\smoke\c16-bad-risk | Out-Null
$e = Start-Process -FilePath unity\Builds\win-eval\GymEval.exe -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-gymMode', 'eval', '-gymSegment', 'validation-bear', '-gymObs', 'long',
  '-gymRiskAversion', '-1', '-gymFeeRate', '0.001', '-gymOut', "`"$PWD\evaluations\smoke\c16-bad-risk`"",
  '-logFile', "`"$PWD\unity\Logs\eval-bad-risk.log`"")
$e.ExitCode   # should be 1
(Get-ChildItem evaluations\smoke\c16-bad-risk).Count   # should be 0
Select-String -Path unity\Logs\eval-bad-risk.log -Pattern 'RiskAversion'
```

You should see exit code 1, an empty `c16-bad-risk`, and `-gymRiskAversion -1 is out of range: …` in the log.

When smoke 0 to smoke 10 all match, run round two for real as in the example of 12.2.

## 14. Round three: the position dial

Round three's model no longer places orders. It only turns a dial for the target position: 30 %, 50 % or 70 %, chosen once every 24 hourly candles (once a day), while a rule does the buying and selling (it trades back whenever the actual position is more than the band away from the target). The dial mode's environment, the opponents, the evaluation player's action-shape check and the scoring script were built and tested on the Mac, but **real runs of the training and evaluation players in the dial mode can only happen on this PC**, and `run_series.ps1 -BasePort` has not run on Windows yet either. So do the smoke checks of section 15 first, then the real runs.

### 14.1 The new options

| Option | For | Meaning | When left out |
| --- | --- | --- | --- |
| Environment parameter `action_mode` (in the yaml's `environment_parameters`) | training | 0 = the order mode (rounds one and two), 1 = the dial mode | `-gymActionMode`, then `actionMode` in `gym-config.json` (`orders`) |
| Environment parameter `decision_hours` (likewise) | training | candles per decision in the dial mode. `check_configs.py` only accepts 24 (`22A` section two, item 5) | `-gymDecisionHours`, then `decisionHours` (24) |
| Environment parameter `rebalance_band` (likewise) | training | the rebalancing band: the rule only trades when the position is more than this away from the target. `check_configs.py` only accepts 0.02 to 0.10 | `-gymBand`, then `rebalanceBand` (0.05) |
| `-gymActionMode orders\|dial` | evaluation player, Watch (when starting the editor); the training player takes it too | the action mode. A model only works in the mode it was trained in. The Play scene does not support `dial` and stops with an error | `actionMode` in `gym-config.json` (`orders`) |
| `-gymDecisionHours <n>` | evaluation player, Watch; the training player takes it too | candles per decision, a whole number ≥ 1 | `decisionHours` (24) |
| `-gymBand <x>` | evaluation player, Watch; the training player takes it too | the rebalancing band, inside (0, 0.5) | `rebalanceBand` (0.05) |
| `-BasePort <n>` (`run_series.ps1`) | the series script | the first port of this series: environment k uses that port + k. A whole number from 1024 to 65535 − `-NumEnvs`; anything else stops the script with an error | not passed, `mlagents-learn` uses its own 5005 |

The levels (`dialLevels` 0.3, 0.5, 0.7) and the dial mode's episode length (`dialEpisodeLength` 2160 candles, that is 90 decisions) only come from `gym-config.json`, never from the command line. The rule is the same as for round two's observation set: **in training these three settings go in the yaml only**; do not put `-gymActionMode`, `-gymDecisionHours` or `-gymBand` in `-EnvArgs` (the environment parameter wins, and a disagreement gives no error).

### 14.2 The three configs in `config\v3\`

| File | Base (first line) | Changed from its base | What for |
| --- | --- | --- | --- |
| `dial.yaml` | `config\v2\long.yaml` | `action_mode: 1` | The main line, used by both lines of training: BTC only, and the four coins |
| `dial-base.yaml` | `config\v3\dial.yaml` | `obs_set: 0` (`base`, 35 numbers) | The `base` control group, on the BTC-only line only |
| `smoke-dial.yaml` | `config\v3\dial.yaml` (a smoke config, not compared) | `max_steps` 15000, `checkpoint_interval` 15000, `summary_freq` 1000 | The smoke checks of section 15. One step is one decision (24 candles), so the first episodes end at 16 agents × 90 decisions = 1,440 steps, and `Trading/…` appears from 2,000 steps on |

None of the three sets `decision_hours` or `rebalance_band`, so `gym-config.json`'s 24 and 0.05 apply. **There is no `dial-mix.yaml`**: the four-coin line trains `dial.yaml` itself, with the coins in `-EnvArgs`, as in round two, so both lines use exactly the same settings (`22A` section two, item 9).

`max_steps` is still 2 million. In the dial mode a step is a day, so 2 million steps go through the training segment about 1,250 times; how many steps to train is decided from the speed of smoke 14 (`22A` section three, item 2; the leaning is to start at 500k). **Ask the discussion session before changing the steps**: changing `max_steps` in `dial.yaml` makes it differ from its base `long.yaml` in two settings, and `check_configs.py` fails it.

### 14.3 Starting the two lines and the control group

| Line | Config | Seeds | `-Prefix` | `-EnvArgs` | Run id |
| --- | --- | --- | --- | --- | --- |
| BTC only (main line) | `config\v3\dial.yaml` | 1 to 5 | none | none | `dial-s<seed>-<date>` |
| Four coins (main line) | `config\v3\dial.yaml` | 1 to 5 | `mix` | `"-gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT"` | `mix-dial-s<seed>-<date>` |
| `base` control group | `config\v3\dial-base.yaml` | 1 to 3 | none | none | `dial-base-s<seed>-<date>` |

A series runs one training at a time; for several series side by side see 14.4. With one series at a time, the three commands are:

```powershell
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1,2,3,4,5 -Configs config\v3\dial.yaml
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1,2,3,4,5 -Prefix mix -Configs config\v3\dial.yaml -EnvArgs "-gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT"
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1,2,3 -Configs config\v3\dial-base.yaml
```

In round three **`-Prefix` only names a line** (`mix` for the four coins). The scoring script `score_round3.py` takes the run id without `-s<seed>-<date>` as the configuration: `dial`, `mix-dial` and `dial-base` are exactly three configurations. Any other prefix would make up configurations and split the seeds of one between them, so the configuration scores and the "seeds past both thresholds" would be wrong, without any error.

### 14.4 Several series side by side

Round two used only about a third of the CPU, so round three runs several series at the same time (`22A` section two, item 10). **Start with 3**; how many in the end is decided by the Windows dispatch session from the speed of smoke 14.

Rules:

- **One terminal and one `-BasePort` per series**, at least `-NumEnvs` apart, for example 5005, 5105, 5205. When two series use the same port, the later one stops at once with `UnityWorkerInUseException`; they never get mixed up silently.
- **Split the series by seeds or by line, never by `-Prefix`.** With three series the simplest is one line each: BTC only, four coins, the `base` control group. With more series than lines, split a line by seeds, for example one series `-Seeds 1,2` and another `-Seeds 3,4,5`. **The same seed of the same line must not run in two series at once** (the same run id started twice: the later `mlagents-learn` stops with `Previous data from this run ID was found`).
- Each series writes its own output to `results\series-<name>.out` (`*>` writes UTF-16; `Select-String` reads it all the same).
- In each terminal first set `$env:CUDA_VISIBLE_DEVICES = '-1'`: the configs say `device: cpu`, the training never needs the graphics card, and then it takes nothing from it.
- The temporary configs `results\_tmp\<run-id>.yaml` are named by run id, so two series never write the same file; no separate folders are needed.

Three series, one line each (three PowerShell windows, each after `conda activate mlagents` and `cd E:\project\ZHENNs_Ledger_Gym`):

```powershell
# window one: BTC only
$env:CUDA_VISIBLE_DEVICES = '-1'
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1,2,3,4,5 -BasePort 5005 -Configs config\v3\dial.yaml *> results\series-dial.out

# window two: four coins
$env:CUDA_VISIBLE_DEVICES = '-1'
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1,2,3,4,5 -BasePort 5105 -Prefix mix -Configs config\v3\dial.yaml -EnvArgs "-gymTrainSymbols BTCUSDT,ETHUSDT,ADAUSDT,XRPUSDT" *> results\series-mix-dial.out

# window three: the base control group
$env:CUDA_VISIBLE_DEVICES = '-1'
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1,2,3 -BasePort 5205 -Configs config\v3\dial-base.yaml *> results\series-dial-base.out
```

Whether the machine is busy, and whether the series keep out of each other's way:

```powershell
# every training player's command line has --mlagents-port <port>, each series inside its own range
Get-CimInstance Win32_Process -Filter "Name='Gym.exe'" | Select-Object ProcessId, CommandLine | Format-List
# the ports the trainers (python.exe) wait on
Get-NetTCPConnection -State Listen -LocalPort 5005,5105,5205 | Select-Object LocalPort, OwningProcess
# total CPU use, every 5 seconds, 6 times; the Performance tab of Task Manager works too
Get-Counter '\Processor(_Total)\% Processor Time' -SampleInterval 5 -MaxSamples 6
# memory of all training players together
"{0:N0} MB" -f ((Get-Process Gym | Measure-Object WorkingSet64 -Sum).Sum / 1MB)
```

**Do not look for the port in the Player logs**: the player only writes it there when it cannot reach the trainer. Each run records its port in the `base-port: …` line of `results\<run-id>\seed-used.txt` (`base-port: default …` without `-BasePort`).

### 14.5 Evaluating round-three models

- A dial-mode model needs `'-gymActionMode', 'dial'` in the evaluation command, and its training's observation set: `'-gymObs', 'long'` for the `dial` and `mix-dial` models, `base` for the `dial-base` models (`base` is what you get without `-gymObs`). A model trained with another band or another number of candles per decision needs the same `-gymBand` or `-gymDecisionHours`.
- With the wrong action mode the evaluation player exits with code 1 and writes nothing; the log gives both shapes, for example `the model's actions are 1 continuous + [3] but the action mode dial with 3 levels needs 0 continuous + [3]` (smoke 13).
- Evaluate all three validation segments, `validation-bear`, `validation-recovery` and `validation`, at a fee of 0.001. **The test segment `test` is used once, at the end of the round**, after the final model is chosen; the report says "tuned on the validation segments, the test segment used for the third time" (`22A` section two, item 11).
- An example (the model `dial-s1-<date>`, the bear segment):

```powershell
$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD\unity`"",
  '-executeMethod', 'Gym.Editor.BuildScript.BuildWindowsEval',
  '-gymModel', "`"$PWD\results\dial-s1-<date>\TradingAgent.onnx`"", '-quit',
  '-logFile', "`"$PWD\unity\Logs\build-eval.log`"")
$p.ExitCode   # 0

$e = Start-Process -FilePath unity\Builds\win-eval\GymEval.exe -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-gymMode', 'eval', '-gymSegment', 'validation-bear',
  '-gymActionMode', 'dial', '-gymObs', 'long',
  '-gymFeeRate', '0.001', '-gymOut', "`"$PWD\evaluations`"",
  '-logFile', "`"$PWD\unity\Logs\eval.log`"")
$e.ExitCode   # 0
```

- The row's `notes` end with `; mode=dial; band=0.05; hours=24; changes=<dial changes>`.
- With all three segments in the log, score:

```powershell
python scripts\eval\score_round3.py evaluations\log.csv                          # every model
python scripts\eval\score_round3.py evaluations\log.csv --runs dial-,mix-dial-    # only round three's run ids
```

The two thresholds (bear-segment drawdown ≤ 0.4132, `validation` return ≥ 0.3901, both after rounding to four decimals), the mean three-segment Calmar score, the configuration scores and "did not learn" (never changed level on the three segments) are explained in [evaluation.md](evaluation.md); the final model is the highest-scoring one among all models past both thresholds.

## 15. Round three smoke checks: what the Mac could not run

As in section 13: in order, since later checks use what earlier ones made; when one does not match, write down what you saw, stop and tell the discussion session. Smoke evaluations always go to `evaluations\smoke\`, **never to the official log**. Replace `<date>` with the date in the run id the script prints (UTC). First rebuild the training player as in step 6 (only this version knows the dial mode), and look at a `-DryRun` first as in smoke 0.

### Smoke 11: did the three environment parameters arrive?

First the dial mode's smoke config (its name already starts with `smoke`, so no `-Smoke`):

```powershell
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1 -Configs config\v3\smoke-dial.yaml
$run = 'smoke-dial-s1-<date>'
Select-String -Path results\$run\run_logs\Player-*.log -Pattern 'actions dial|chooses the action mode' | Select-Object -First 4
python scripts\train\read_scalars.py results\$run | Select-String 'Trading/Decisions|Trading/DialChanges'
```

You should see:

- for every agent, `the trainer's action_mode chooses the action mode dial (the config and command line said orders)`;
- every agent's start line: `…: index …, mode Training, segment train 2017-08-17..2021-12-31, observations long (41 numbers, history 2160), actions dial (levels 0.3, 0.5, 0.7, decision hours 24, band 0.05, episodes of 2160 candles), master seed … (trainer)`;
- both `Trading/Decisions` and `Trading/DialChanges` (smoke 12 reads their values).

This run only proves the path of `action_mode`: `decision_hours` and `rebalance_band` were `gym-config.json`'s defaults, and their environment parameters never travelled. So run once more with a **temporary config** (in `results\_tmp\`, not in Git) that sets `decision_hours: 24` and `rebalance_band: 0.03`:

```powershell
New-Item -ItemType Directory -Force results\_tmp | Out-Null
$text = [IO.File]::ReadAllText("$PWD\config\v3\smoke-dial.yaml")
$text = $text.Replace('  action_mode: 1', "  action_mode: 1`n  decision_hours: 24`n  rebalance_band: 0.03")
[IO.File]::WriteAllText("$PWD\results\_tmp\smoke-dial-band003.yaml", $text)
Select-String -Path results\_tmp\smoke-dial-band003.yaml -Pattern 'action_mode|decision_hours|rebalance_band'   # three lines

.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1 -Configs results\_tmp\smoke-dial-band003.yaml
Select-String -Path results\smoke-dial-band003-s1-<date>\run_logs\Player-*.log -Pattern 'actions dial' | Select-Object -First 1
```

You should see `actions dial (levels 0.3, 0.5, 0.7, decision hours 24, band 0.03, episodes of 2160 candles)`: the band is 0.03, not 0.05. That proves all three travel from the yaml to the player (`22A` section two, item 6 lets the band be tried between 2 and 10 points; that is the path it will take).

### Smoke 12: one step walks 24 candles, and the trainer took "0 continuous + one branch of 3"

Still the first run of smoke 11 (`smoke-dial-s1-<date>`):

```powershell
python scripts\train\read_scalars.py results\$run | Select-String 'Trading/Decisions'
Select-String -Path results\$run.log -Pattern 'Error|Exception|action spec|ActionSpec'
Select-String -Path results\$run.log -Pattern 'Step: 15000'
```

You should see:

- `Trading/Decisions` equal to **`dialEpisodeLength ÷ decisionHours`**, now 2160 ÷ 24 = **90** (if `gym-config.json` changes, so does this);
- nothing found by the second line: the trainer gave no error about the action shape;
- the `Step: 15000` line, and the script's last line `series finished: 1 ran, 0 skipped, 0 failed`.

### Smoke 13: the evaluation player takes a dial model and refuses an order model

First build the evaluation player with smoke 11's dial model (`long`):

```powershell
$p = Start-Process -FilePath $unity -Wait -PassThru -ArgumentList @(
  '-batchmode', '-nographics', '-projectPath', "`"$PWD\unity`"",
  '-executeMethod', 'Gym.Editor.BuildScript.BuildWindowsEval',
  '-gymModel', "`"$PWD\results\smoke-dial-s1-<date>\TradingAgent.onnx`"", '-quit',
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

You should see `"continuous_actions": 0`, `"discrete_branches": [3]`, `"action_shape_recorded": true` and `"vector_observation_size": 41` in `build-info.json`; the evaluation exits with code 0, and the last row has `model_run_id` `smoke-dial-s1-<date>` and `notes` ending in `; mode=dial; band=0.05; hours=24; changes=<k>`.

Then build round two's final model (the order mode, `base`, kept in the repository) and evaluate it in the dial mode. **This overwrites the evaluation build above**:

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
$e.ExitCode   # should be 1
(Get-ChildItem evaluations\smoke\c25-orders-as-dial).Count   # should be 0
Select-String -Path unity\Logs\eval-orders-as-dial.log -Pattern "the model's actions are"
```

You should see exit code 1, an empty `c25-orders-as-dial`, and `the model's actions are 1 continuous + [3] but the action mode dial with 3 levels needs 0 continuous + [3]` in the log, with both shapes.

### Smoke 14: two series side by side, 100k steps each

Two PowerShell windows, started **at the same time**, with different seeds and `-BasePort` values (seeds 2 and 3, so that neither collides with smoke 11's `smoke-dial-s1-<date>` and gets skipped):

```powershell
# window one
$env:CUDA_VISIBLE_DEVICES = '-1'
$t0 = Get-Date
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 2 -Smoke -SmokeSteps 100000 -BasePort 5005 -Configs config\v3\dial.yaml *> results\series-c25-p1.out
"pipeline 1: {0:N0} steps/s" -f (100000 / ((Get-Date) - $t0).TotalSeconds)

# window two
$env:CUDA_VISIBLE_DEVICES = '-1'
$t0 = Get-Date
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 3 -Smoke -SmokeSteps 100000 -BasePort 5105 -Configs config\v3\dial.yaml *> results\series-c25-p2.out
"pipeline 2: {0:N0} steps/s" -f (100000 / ((Get-Date) - $t0).TotalSeconds)
```

While both run, look at the ports from a third window (the commands of 14.4):

```powershell
Get-CimInstance Win32_Process -Filter "Name='Gym.exe'" | Select-Object ProcessId, CommandLine | Format-List
Get-NetTCPConnection -State Listen -LocalPort 5005,5105 | Select-Object LocalPort, OwningProcess
Get-Counter '\Processor(_Total)\% Processor Time' -SampleInterval 5 -MaxSamples 6
```

You should see:

- two `Gym.exe`, one with `--mlagents-port 5005` on its command line and one with `--mlagents-port 5105`, and both ports listening. **Not the Player logs** (they only show a port when the connection fails);
- both `.out` files ending in `series finished: 1 ran, 0 skipped, 0 failed`, neither with `UnityWorkerInUseException` nor a red `!!! WARNING`;
- `base-port: 5005 (…)` in `results\smoke-dial-s2-<date>\seed-used.txt` and `base-port: 5105 (…)` in that of `smoke-dial-s3-<date>`;
- note both steps-per-second figures and the CPU use in the PC training log: they decide how many series to run. If you are unsure about three, start a third window at the same time (seed 4, `-BasePort 5205`) and see how much each series slows down and whether the CPU passes 90 %.

### Smoke 15: `score_round3.py` runs on the log

```powershell
python scripts\eval\score_round3.py evaluations\log.csv
python scripts\eval\score_round3.py evaluations\smoke\log.csv
python -m unittest discover -s scripts\eval
```

You should see exit code 0 for the first two. On the official log, table 3 has both opponents past both thresholds: `constant_mix_50` with drawdown 0.4132 and return 0.3901 (exactly the thresholds, because the thresholds are its results), and `dial_momentum_90d`; on the smoke log, smoke 13's `smoke-dial-s1-<date>` is in table 1 with one segment, marked `missing segment` and not ranked. The unit tests end with `OK`.

### Smoke 16: watching a dial model in the editor

The Watch scene's mode comes from `-gymActionMode` and `-gymObs` given when the editor starts, otherwise from `actionMode` and `observationSet` in `gym-config.json` (now `orders`, and `base` without `observationSet`). `gym-config.json` is in Git and goes into the training and evaluation builds, so **prefer the command line** and leave the file alone.

1. Close the Unity editor and open it again with the switches:

   ```powershell
   & $unity -projectPath "$PWD\unity" -gymActionMode dial -gymObs long
   ```

   (If you must edit the file instead: in `unity\Assets\StreamingAssets\Gym\gym-config.json` change `"actionMode": "orders"` to `"dial"` and add a line `"observationSet": "long",` below it, save, then open the editor.)
2. Menu **Gym > Watch a Model...**, choose `results\smoke-dial-s1-<date>\TradingAgent.onnx` (or a real dial model). The readout's "Last action" row now reads like `Target position   50% | changes: 3` (in Chinese 「目标仓位　50%｜换档 3 次」), with the same number of rows; in the Console the agent's start line has `actions dial (…)`. An order-mode model is refused here, the error gives both shapes and says to change `actionMode` (or `-gymActionMode`).
3. Close the editor when done. If you edited the file, restore it with `git checkout -- unity/Assets/StreamingAssets/Gym/gym-config.json`. `git status` must be clean (only the `SENTIS_ANALYTICS_ENABLED` change in `ProjectSettings.asset` is restored as in smoke 7 of section 13), **before the next build**: otherwise the next training or evaluation build carries the edited config.

Three things on the screen are not adapted to the dial mode (known, not fixed this round): the "step" row counts hours, so it jumps by 24 per step; the avatar makes the buying move even when the rule sells; and the wallet shows no floating amounts. Read the numbers from the "Target position" row and from the log.

When smoke 11 to smoke 16 all match, run round three for real as in 14.3 and 14.4.
