# Training

One smoke training run is step 3 of the README's [quick start](../README.md#quick-start-mac). This page covers a series of runs on the Mac and what the `Trading/*` curves mean.

Long runs happen on a Windows PC: see [pc-training.md](pc-training.md) (English) or [pc-training.zh.md](pc-training.zh.md) (Chinese). In short: same Unity and Python versions, `BuildWindowsTraining`, 100k-step speed tests on the CPU (`config/smoke-100k.yaml`) and on the graphics card (`config/smoke-100k-cuda.yaml`), then the comparison series with `scripts/train/run_series.ps1` on whichever was faster (`-Device cpu|cuda`).

## A series on the Mac

`scripts/train/run_series.sh` runs every config × every seed, one `mlagents-learn` run each; `--help` lists its options. With the training build from the quick start and the `mlagents` environment active, from the repository root:

```bash
scripts/train/run_series.sh --env unity/Builds/mac/Gym.app --seeds 1 --smoke config/ppo_base.yaml config/variants/fee-0.003.yaml
```

What each config changes, and how to check the configs after editing them: [config/README.md](../config/README.md).

## Observation set (`obs_set`)

Which numbers the agent sees is chosen by the environment parameter `obs_set` in the config's `environment_parameters`: 0 is `base` (the first round's 35 numbers), 1 is `long` (41: six long-horizon numbers added), 2 is `long-ta` (43: daily MACD and RSI added to `long`). The formulas are in [architecture.md](architecture.md#observation-action-reward). `config/variants/obs-long.yaml` and `config/variants/obs-long-ta.yaml` set it; `ppo_base.yaml` leaves it out, and then the player uses `-gymObs` or `observationSet` in `gym-config.json` (default `base`). Any other value stops the training player with exit code 1, and so does a set whose history length (2160 candles for `long` and `long-ta`) leaves the training segment too short or `playStart` too early. Every agent writes its set to the player log, for example `observations long (41 numbers, history 2160)`; check that line in a smoke run, because the Mac tests cannot send environment parameters. A model trained on one set only works with that set.

## Training curves (`Trading/*` in TensorBoard)

During training every agent reports nine numbers at the end of each episode; TensorBoard shows their average over the episodes that ended in each summary period. They describe training episodes (720 steps from a random start, possibly holding coin at the start), so they are not the evaluation metrics of [evaluation.md](evaluation.md), even where the names match.

| Tag | Meaning |
| --- | --- |
| `Trading/Return` | Final equity ÷ starting equity − 1 |
| `Trading/Trades` | Filled orders |
| `Trading/Rejected` | Orders that were not filled: masked, too small (below the minimum order or one coin step), or eaten by the fixed fee |
| `Trading/FeesPaidPct` | Fees paid ÷ starting equity × 100 (a percentage) |
| `Trading/Exposure` | Share of steps that ended holding coin |
| `Trading/Turnover` | Traded value ÷ **starting** equity. The evaluation log's `turnover` divides by the **average** equity instead, so the two differ whenever equity moves |
| `Trading/RewardClips` | Steps whose reward was clipped to ±1 |
| `Trading/FeeRate` | Fee rate the episode used, to check that the config's `fee_rate` arrived |
| `Trading/FixedFee` | Fixed fee per order the episode used |
