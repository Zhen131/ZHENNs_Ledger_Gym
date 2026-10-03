# Evaluation log

Commands run from the repository root, with `UNITY` set as in the README's [quick start](../README.md#quick-start-mac).

Every evaluation is one full pass over the validation or test segment: start at the first candle in cash, no randomness, the same fills and fees as in training. Each one appends a row to `evaluations/log.csv` and writes a JSON file with all metrics and settings to `evaluations/runs/`. **The log is append-only**: rows are never edited or removed, and a log whose header differs from the current columns is refused. Running the same evaluation twice adds a second row.

Metrics: total return, maximum drawdown, annualised Sharpe ratio (hourly log returns, √8760), trades, rejected orders, turnover (traded value ÷ average equity), fees (USDT and % of the starting equity) and exposure (share of steps that end holding coin).

Baselines (buy-and-hold, always cash, and a random policy over seeds 0–99 reported as medians with the 5th and 95th percentile of the return) for fee rates 0, 0.1 % and 0.3 %:

```bash
# Appends to the tracked evaluations/log.csv; run once per segment and fee set
"$UNITY" -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.EvalTools.RunBaselines -gymSegment validation -gymOut "$PWD/evaluations" -quit -logFile "$PWD/unity/Logs/baselines.log"
python scripts/eval/summarize.py evaluations/log.csv
```

Options: `-gymSegment validation|test`, `-gymFeeRates 0,0.001,0.003`, `-gymRandomSeeds 100`, `-gymOut <dir>`, `-gymConfig <file>`. A trained model is evaluated in its own build (`BuildMacEval` / `BuildWindowsEval` with `-gymModel`), which records the run id and the model's SHA-256 in every row; `-gymFixedFee` and `-gymSlippage` set the other costs. The evaluation player refuses to start (exit code 1, nothing written) without `-gymMode eval`, `-gymSegment validation|test` and `-gymOut <dir>`; it has no default segment, so the test segment is never evaluated by accident.

`BuildMacEval` signs `GymEval.app` again after adding `build-info.json`, so a fresh build passes `codesign --verify --deep`. ML-Agents, however, writes its timer file into the app (`Contents/ML-Agents/Timers/`) every time the player runs, which breaks the signature again. It still runs on this Mac; to give the app to another Mac, copy it before its first run, or sign it again first: `codesign --force --deep -s - unity/Builds/mac/GymEval.app`.
