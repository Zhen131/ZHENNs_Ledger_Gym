<#
.SYNOPSIS
Run a training series: every config x every seed, one mlagents-learn run each. Windows.
The macOS/Linux twin is run_series.sh with the same options.

.DESCRIPTION
Run ids:  [smoke-][Prefix-]<config file name>-s<seed>-<yyyyMMdd, UTC>
Results:  results\<run-id>\         (ML-Agents output, plus config-used.yaml,
                                    seed-used.txt, env-args.txt, build-info.json)
          results\<run-id>.log      (everything mlagents-learn printed; a log left
                                    by an attempt without results is overwritten)
A run whose results\<run-id> already exists is skipped; --force is never used.
Seeds are whole numbers >= 0. -DryRun prints the commands and writes nothing.
Seeds: mlagents-learn gets --seed <seed x 1000>, because ML-Agents gives environment k
the seed + k and seeds 1, 2, 3 ... would collide when -NumEnvs > 1. The run id keeps
the plain seed (-s3); results\<run-id>\seed-used.txt records what was passed.
One series uses one -NumEnvs value for every run, so the runs stay comparable.
After each run the Player logs are searched for "(clock)": an agent that could not read
the trainer's seed seeds itself from the clock and the run cannot be repeated. Such
a run gets a loud warning and the line "WARNING: some agents seeded from the clock" in
seed-used.txt.
-Smoke copies each config to results\_tmp\ with max_steps 15000 (or -SmokeSteps),
summary_freq 1000 and checkpoint_interval = max_steps; it only proves the script works.
15000 steps is just past the end of the first episodes (16 agents x 720 steps =
11,520 steps); Trading/* statistics only appear from then on.
-Device (default cpu) sets torch_settings.device. When a config says something else,
it is copied to results\_tmp\ with the device changed; the files in config\ are never
edited. config-used.yaml records the device each run really used.
-EnvArgs takes one quoted string of words for the player (the Unity build) and splits
it at spaces (quotes inside are not understood). The words go after mlagents-learn's own
--env-args, which is always the last option: mlagents-learn hands every word after it to
the player, one argument each. The player only knows a name and its value as two words;
"-gymObs long" in one piece would be ignored without an error. Every run writes the words
to results\<run-id>\env-args.txt (an empty file without -EnvArgs). Training refuses
-gymSegment (it always uses the train segment); -logFile would hide the player logs that
the "(clock)" check reads.
The training build carries build-info.json in its StreamingAssets\Gym\ folder, not next to
the executable: Gym_Data\StreamingAssets\Gym\ next to Gym.exe on Windows,
Gym.app\Contents\Resources\Data\StreamingAssets\Gym\ on macOS. If it is there, every run
copies it to results\<run-id>\build-info.json.

Option names map one to one onto run_series.sh:
  -Env <path>          --env <path>
  -Seeds 1,2,3,4,5     --seeds "1 2 3 4 5"
  -NumEnvs 1           --num-envs 1
  -Prefix NAME         --prefix NAME
  -Device cpu|cuda     --device cpu|cuda
  -Smoke               --smoke
  -SmokeSteps 15000    --smoke-steps 15000
  -DryRun              --dry-run
  -EnvArgs "<words>"   --env-args "<words>"
  -Configs a,b         a b   (positional in the .sh)

.EXAMPLE
conda activate mlagents
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Configs config\ppo_base.yaml,config\variants\fee-0.yaml
.\scripts\train\run_series.ps1 -Env unity\Builds\win\Gym.exe -Seeds 1 -Configs config\v2\long.yaml -EnvArgs "-gymTrainSymbols BTCUSDT,ETHUSDT"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][Alias('Env')][string]$EnvPath,
    [Parameter(Mandatory = $true)][string[]]$Configs,
    [int[]]$Seeds = @(1, 2, 3, 4, 5),
    [int]$NumEnvs = 1,
    [string]$Prefix = '',
    [string]$Device = 'cpu',
    [switch]$Smoke,
    [int]$SmokeSteps = 15000,
    [switch]$DryRun,
    [string]$EnvArgs = ''
)

$ErrorActionPreference = 'Stop'

function Fail([string]$message) {
    Write-Error "error: $message" -ErrorAction Continue
    exit 2
}

# True when any agent of the run logged its master seed as "(clock)" instead of "(trainer)".
function Test-ClockSeeded([string]$runDir) {
    $logs = Get-ChildItem -Path (Join-Path $runDir 'run_logs') -Filter 'Player-*.log' -File -ErrorAction SilentlyContinue
    foreach ($log in $logs) {
        if (Select-String -LiteralPath $log.FullName -Pattern '(clock)' -SimpleMatch -Quiet) { return $true }
    }
    return $false
}

# Where the training build keeps build-info.json: BuildScript writes it into the build's StreamingAssets\Gym\, not next to the executable.
function Get-BuildInfoPath([string]$envPath) {
    if ($envPath -like '*.app') { return Join-Path $envPath 'Contents\Resources\Data\StreamingAssets\Gym\build-info.json' }
    $name = [IO.Path]::GetFileNameWithoutExtension($envPath)
    return Join-Path (Split-Path -Parent $envPath) "${name}_Data\StreamingAssets\Gym\build-info.json"
}

# One command on one line: a word with white space in it (or an empty one) is shown in double quotes, so one
# "-gymObs long" stands out from the two words -gymObs long.
function Format-Words([string[]]$words) {
    $shown = foreach ($word in $words) { if ($word -eq '' -or $word -match '\s') { '"' + $word + '"' } else { $word } }
    return ($shown -join ' ')
}

if (-not (Test-Path -LiteralPath $EnvPath)) { Fail "environment build not found: $EnvPath" }
if ($NumEnvs -lt 1) { Fail "-NumEnvs must be one whole number >= 1 (got $NumEnvs)" }
if ($SmokeSteps -lt 1) { Fail '-SmokeSteps must be a whole number >= 1' }
if ($Seeds.Count -eq 0) { Fail '-Seeds is empty' }
$negative = @($Seeds | Where-Object { $_ -lt 0 })
if ($negative.Count -gt 0) { Fail "seeds must be whole numbers >= 0 (got $($negative -join ', '))" }
if ($Prefix -notmatch '^[A-Za-z0-9._-]*$') { Fail "-Prefix may only use letters, digits, '.', '_' and '-'" }
if ($Device -cnotin @('cpu', 'cuda')) { Fail "-Device must be cpu or cuda (got '$Device')" }

$learn = if ($env:MLAGENTS_LEARN) { $env:MLAGENTS_LEARN } else { 'mlagents-learn' }
if (-not $DryRun -and -not (Get-Command $learn -ErrorAction SilentlyContinue)) {
    Fail "$learn not found; activate the mlagents environment or set MLAGENTS_LEARN"
}

$EnvPath = (Resolve-Path -LiteralPath $EnvPath).Path
# -EnvArgs is split at white space into single words (quotes are not understood). Passed as one argument, the player would see one unknown word and quietly ignore it.
$envWords = @($EnvArgs -split '\s+' | Where-Object { $_ -ne '' })
$buildInfo = Get-BuildInfoPath $EnvPath
$buildInfoMissing = ''
if (-not (Test-Path -LiteralPath $buildInfo -PathType Leaf)) { $buildInfoMissing = $buildInfo; $buildInfo = '' }
$absConfigs = @()
foreach ($cfg in $Configs) {
    if (-not (Test-Path -LiteralPath $cfg -PathType Leaf)) { Fail "config not found: $cfg" }
    $absConfigs += (Resolve-Path -LiteralPath $cfg).Path
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repo
if (-not $DryRun) { New-Item -ItemType Directory -Force -Path 'results' | Out-Null }
# Invariant culture: in some locales (Thai, for one) the default calendar is not Gregorian.
$date = (Get-Date).ToUniversalTime().ToString('yyyyMMdd', [Globalization.CultureInfo]::InvariantCulture)

$ran = 0; $skipped = 0; $failed = 0; $clocked = 0
$smokeNote = if ($Smoke) { ", smoke $SmokeSteps steps" } else { '' }
Write-Host "series: $($absConfigs.Count) config(s) x seeds [$($Seeds -join ' ')], -NumEnvs $NumEnvs, device $Device, env $EnvPath$smokeNote"
if ($envWords.Count -eq 0) {
    Write-Host 'env args: none (env-args.txt will be empty)'
} else {
    Write-Host "env args: $($envWords.Count) word(s), passed last as --env-args: $(Format-Words $envWords)"
}
if ($buildInfo) {
    Write-Host "build info: $buildInfo (copied to results\<run-id>\build-info.json)"
} else {
    Write-Host "build info: not found at $buildInfoMissing; rebuild the training player to get one (nothing copied)"
}

foreach ($cfg in $absConfigs) {
    $name = [IO.Path]::GetFileNameWithoutExtension($cfg)
    foreach ($seed in $Seeds) {
        $runId = "$name-s$seed-$date"
        if ($Prefix) { $runId = "$Prefix-$runId" }
        if ($Smoke) { $runId = "smoke-$runId" }
        if (Test-Path -LiteralPath "results\$runId") {
            Write-Host "skip  ${runId}: results\$runId already exists (never --force)"
            $skipped++
            continue
        }

        $used = $cfg
        $text = [IO.File]::ReadAllText($cfg)
        $configDevice = [regex]::Match($text, '(?m)^[ \t]*device:[ \t]*([^\s#]+)').Groups[1].Value
        if ($Smoke -or $configDevice -cne $Device) {
            $used = Join-Path $repo "results\_tmp\$runId.yaml"
            if ($Smoke) {
                $text = $text -replace '(?m)^(\s*max_steps:).*$', "`${1} $SmokeSteps"
                $text = $text -replace '(?m)^(\s*summary_freq:).*$', '${1} 1000'
                $text = $text -replace '(?m)^(\s*checkpoint_interval:).*$', "`${1} $SmokeSteps"
                if (([regex]::Matches($text, "(?m)^\s*max_steps: $SmokeSteps\s*$")).Count -ne 1) { Fail "could not set max_steps for $cfg" }
            }
            $text = $text -replace '(?m)^([ \t]*device:).*$', "`${1} $Device"
            if (([regex]::Matches($text, "(?m)^[ \t]*device: $Device\s*$")).Count -ne 1) { Fail "could not set torch_settings.device for $cfg" }
            if (-not $DryRun) {
                New-Item -ItemType Directory -Force -Path 'results\_tmp' | Out-Null
                [IO.File]::WriteAllText($used, $text)
            }
        }

        Write-Host "run   $runId"
        $learnSeed = [long]$seed * 1000
        $arguments = @($used, '--env', $EnvPath, '--run-id', $runId, '--seed', "$learnSeed", '--num-envs', "$NumEnvs", '--no-graphics')
        # --env-args must be last: mlagents-learn hands every word after it (other options too) to the player as given.
        if ($envWords.Count -gt 0) { $arguments += @('--env-args') + $envWords }
        if ($DryRun) {
            Write-Host "      $(Format-Words (@($learn) + $arguments))"
            continue
        }

        $log = "results\$runId.log"
        # Overwrite a log an earlier attempt left behind, as tee does in run_series.sh.
        if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log }
        # mlagents-learn writes its log to stderr; in Windows PowerShell 5.1 redirected
        # stderr turns into error records, so do not stop on them here.
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        & $learn @arguments 2>&1 | ForEach-Object {
            $line = "$_"
            Write-Host $line
            Add-Content -LiteralPath $log -Value $line -Encoding UTF8
        }
        $rc = $LASTEXITCODE
        $ErrorActionPreference = $previous

        if (Test-Path -LiteralPath "results\$runId" -PathType Container) {
            Copy-Item -LiteralPath $used -Destination "results\$runId\config-used.yaml"
            # The words passed to the player, on one line, separated by spaces; an empty file without -EnvArgs.
            $envNote = if ($envWords.Count -gt 0) { ($envWords -join ' ') + "`n" } else { '' }
            [IO.File]::WriteAllText((Join-Path $repo "results\$runId\env-args.txt"), $envNote)
            if ($buildInfo) { Copy-Item -LiteralPath $buildInfo -Destination "results\$runId\build-info.json" }
            $seedNote = "seed (run id): $seed`n--seed passed to mlagents-learn: $learnSeed`n--num-envs: $NumEnvs (environment k gets $learnSeed + k)`n"
            [IO.File]::WriteAllText((Join-Path $repo "results\$runId\seed-used.txt"), $seedNote)
            if (Test-ClockSeeded (Join-Path $repo "results\$runId")) {
                [IO.File]::AppendAllText((Join-Path $repo "results\$runId\seed-used.txt"), "WARNING: some agents seeded from the clock`n")
                $clocked++
                $bar = '!' * 77
                foreach ($line in @($bar,
                        "!!! WARNING: in $runId some agents seeded from the clock, not from --seed",
                        "!!! $learnSeed; this run cannot be repeated. See results\$runId\run_logs.",
                        $bar)) {
                    Write-Host $line -ForegroundColor Yellow -BackgroundColor DarkRed
                }
            }
        }
        if ($rc -eq 0) {
            $ran++
            Write-Host "done  $runId"
        } else {
            $failed++
            Write-Host "FAIL  $runId (exit $rc); see $log"
        }
    }
}

Write-Host "series finished: $ran ran, $skipped skipped, $failed failed"
if ($clocked -gt 0) {
    Write-Host "WARNING: $clocked run(s) had agents seeded from the clock; see the warnings above" -ForegroundColor Yellow -BackgroundColor DarkRed
}
if ($failed -gt 0) { exit 1 }
exit 0
