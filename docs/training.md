# Training

One smoke training run is step 3 of the README's [quick start](../README.md#quick-start-mac). This page covers a series of runs on the Mac, the two optional environment parameters `obs_set` and `risk_aversion`, and what the `Trading/*` curves mean.

Long runs happen on a Windows PC: see [pc-training.md](pc-training.md) (English) or [pc-training.zh.md](pc-training.zh.md) (Chinese). In short: same Unity and Python versions, `BuildWindowsTraining`, 100k-step speed tests on the CPU (`config/smoke-100k.yaml`) and on the graphics card (`config/smoke-100k-cuda.yaml`), then the comparison series with `scripts/train/run_series.ps1` on whichever was faster (`-Device cpu|cuda`).

## A series on the Mac

`scripts/train/run_series.sh` runs every config × every seed, one `mlagents-learn` run each; `--help` lists its options. With the training build from the quick start and the `mlagents` environment active, from the repository root:

```bash
scripts/train/run_series.sh --env unity/Builds/mac/Gym.app --seeds 1 --smoke config/ppo_base.yaml config/variants/fee-0.003.yaml
```

What each config changes, and how to check the configs after editing them: [config/README.md](../config/README.md).

## Observation set (`obs_set`)

Which numbers the agent sees is chosen by the environment parameter `obs_set` in the config's `environment_parameters`: 0 is `base` (the first round's 35 numbers), 1 is `long` (41: six long-horizon numbers added), 2 is `long-ta` (43: daily MACD and RSI added to `long`). The formulas are in [architecture.md](architecture.md#observation-action-reward). `config/variants/obs-long.yaml` and `config/variants/obs-long-ta.yaml` set it; `ppo_base.yaml` leaves it out, and then the player uses `-gymObs` or `observationSet` in `gym-config.json` (default `base`). Any other value stops the training player with exit code 1, and so does a set whose history length (2160 candles for `long` and `long-ta`) leaves the training segment too short or `playStart` too early. Every agent writes its set to the player log, for example `observations long (41 numbers, history 2160)`; check that line in a smoke run, because the Mac tests cannot send environment parameters. A model trained on one set only works with that set.

## Risk coefficient (`risk_aversion`)

The environment parameter `risk_aversion` is the reward's risk coefficient λ: every hour scores r − λ × min(r, 0)², so only losing hours pay the extra penalty (the formula is in [architecture.md](architecture.md#observation-action-reward)). `config/variants/risk-0.005.yaml`, `risk-0.01.yaml` and `risk-0.02.yaml` set it; `ppo_base.yaml` leaves it out, and then the player uses `-gymRiskAversion` or the agent's default 0, which is the first round's reward bit for bit. The agent reads it at the start of every episode, like the costs. A negative, NaN or infinite value stops the training player with exit code 1; `check_configs.py` is stricter and only accepts [0, 100] in a config. The Mac tests cannot send environment parameters, so check `Trading/RiskAversion` in a smoke run: it should be the config's value.

## Training curves (`Trading/*` in TensorBoard)

During training every agent reports eleven numbers at the end of each episode; TensorBoard shows their average over the episodes that ended in each summary period. They describe training episodes (720 steps from a random start, possibly holding coin at the start), so they are not the evaluation metrics of [evaluation.md](evaluation.md), even where the names match.

| Tag | Meaning |
| --- | --- |
| `Trading/Return` | Final equity ÷ starting equity − 1 |
| `Trading/Trades` | Filled orders |
| `Trading/Rejected` | Orders that were not filled: masked, too small (below the minimum order or one coin step), or eaten by the fixed fee |
| `Trading/FeesPaidPct` | Fees paid ÷ starting equity × 100 (a percentage) |
| `Trading/Exposure` | Share of steps that ended holding coin |
| `Trading/Turnover` | Traded value ÷ **starting** equity. The evaluation log's `turnover` divides by the **average** equity instead, so the two differ whenever equity moves |
| `Trading/RewardClips` | Steps whose r (the log-return part of the reward) was clipped to ±1; the risk penalty on top of it is not clipped |
| `Trading/FeeRate` | Fee rate the episode used, to check that the config's `fee_rate` arrived |
| `Trading/FixedFee` | Fixed fee per order the episode used |
| `Trading/RiskAversion` | Risk coefficient λ the episode used, to check that the config's `risk_aversion` arrived (0 when it is not set) |
| `Trading/RiskPenalty` | The episode's total risk penalty Σ λ × min(r, 0)², already taken out of `Environment/Cumulative Reward` (0 when λ = 0) |
