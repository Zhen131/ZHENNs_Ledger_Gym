<#
.SYNOPSIS
Run a training series: every config x every seed, one mlagents-learn run each. Windows.
The macOS/Linux twin is run_series.sh with the same options.

.DESCRIPTION
Run ids:  [smoke-][Prefix-]<config file name>-s<seed>-<yyyyMMdd, UTC>
Results:  results\<run-id>\         (ML-Agents output, plus config-used.yaml)
          results\<run-id>.log      (everything mlagents-learn printed)
A run whose results\<run-id> already exists is skipped; --force is never used.
Seeds: mlagents-learn gets --seed <seed x 1000>, because ML-Agents gives environment k
the seed + k and seeds 1, 2, 3 ... would collide when -NumEnvs > 1. The run id keeps
the plain seed (-s3); results\<run-id>\seed-used.txt records what was passed.
One series uses one -NumEnvs value for every run, so the runs stay comparable.
-Smoke copies each config to results\_tmp\ with max_steps 5000 (or -SmokeSteps),
summary_freq 1000 and checkpoint_interval = max_steps; it only proves the script works.
Trading/* statistics appear only after the first episodes end (16 agents x 720 steps =
11,520 steps), so a 5000-step smoke run has none.

Option names map one to one onto run_series.sh:
  -Env <path>          --env <path>
  -Seeds 1,2,3,4,5     --seeds "1 2 3 4 5"
  -NumEnvs 1           --num-envs 1
  -Prefix NAME         --prefix NAME
  -Smoke               --smoke
  -SmokeSteps 5000     --smoke-steps 5000
  -DryRun              --dry-run
  -Configs a,b         a b   (positional in the .sh)

.EXAMPLE
conda activate mlagents
.\tools\train\run_series.ps1 -Env Builds\win\Gym.exe -Configs config\ppo_base.yaml,config\variants\fee-0.yaml
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][Alias('Env')][string]$EnvPath,
    [Parameter(Mandatory = $true)][string[]]$Configs,
    [int[]]$Seeds = @(1, 2, 3, 4, 5),
    [int]$NumEnvs = 1,
    [string]$Prefix = '',
    [switch]$Smoke,
    [int]$SmokeSteps = 5000,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

function Fail([string]$message) {
    Write-Error "error: $message" -ErrorAction Continue
    exit 2
}

if (-not (Test-Path -LiteralPath $EnvPath)) { Fail "environment build not found: $EnvPath" }
if ($NumEnvs -lt 1) { Fail "-NumEnvs must be one whole number >= 1 (got $NumEnvs)" }
if ($SmokeSteps -lt 1) { Fail '-SmokeSteps must be a whole number >= 1' }
if ($Seeds.Count -eq 0) { Fail '-Seeds is empty' }
if ($Prefix -notmatch '^[A-Za-z0-9._-]*$') { Fail "-Prefix may only use letters, digits, '.', '_' and '-'" }

$learn = if ($env:MLAGENTS_LEARN) { $env:MLAGENTS_LEARN } else { 'mlagents-learn' }
if (-not $DryRun -and -not (Get-Command $learn -ErrorAction SilentlyContinue)) {
    Fail "$learn not found; activate the mlagents environment or set MLAGENTS_LEARN"
}

$EnvPath = (Resolve-Path -LiteralPath $EnvPath).Path
$absConfigs = @()
foreach ($cfg in $Configs) {
    if (-not (Test-Path -LiteralPath $cfg -PathType Leaf)) { Fail "config not found: $cfg" }
    $absConfigs += (Resolve-Path -LiteralPath $cfg).Path
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $repo
New-Item -ItemType Directory -Force -Path 'results' | Out-Null
$date = (Get-Date).ToUniversalTime().ToString('yyyyMMdd')

$ran = 0; $skipped = 0; $failed = 0
$smokeNote = if ($Smoke) { ", smoke $SmokeSteps steps" } else { '' }
Write-Host "series: $($absConfigs.Count) config(s) x seeds [$($Seeds -join ' ')], -NumEnvs $NumEnvs, env $EnvPath$smokeNote"

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
        if ($Smoke) {
            New-Item -ItemType Directory -Force -Path 'results\_tmp' | Out-Null
            $used = Join-Path $repo "results\_tmp\$runId.yaml"
            $text = [IO.File]::ReadAllText($cfg)
            $text = $text -replace '(?m)^(\s*max_steps:).*$', "`${1} $SmokeSteps"
            $text = $text -replace '(?m)^(\s*summary_freq:).*$', '${1} 1000'
            $text = $text -replace '(?m)^(\s*checkpoint_interval:).*$', "`${1} $SmokeSteps"
            if (([regex]::Matches($text, "(?m)^\s*max_steps: $SmokeSteps\s*$")).Count -ne 1) { Fail "could not set max_steps in $used" }
            [IO.File]::WriteAllText($used, $text)
        }

        Write-Host "run   $runId"
        $learnSeed = [long]$seed * 1000
        $arguments = @($used, '--env', $EnvPath, '--run-id', $runId, '--seed', "$learnSeed", '--num-envs', "$NumEnvs", '--no-graphics')
        if ($DryRun) {
            Write-Host "      $learn $($arguments -join ' ')"
            continue
        }

        $log = "results\$runId.log"
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
            $seedNote = "seed (run id): $seed`n--seed passed to mlagents-learn: $learnSeed`n--num-envs: $NumEnvs (environment k gets $learnSeed + k)`n"
            [IO.File]::WriteAllText((Join-Path $repo "results\$runId\seed-used.txt"), $seedNote)
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
if ($failed -gt 0) { exit 1 }
exit 0
