# Training configs

All configs train the behavior `TradingAgent` on the CPU (the one exception is `smoke-100k-cuda.yaml`, below) and pass the costs to the environment as `environment_parameters` (`fee_rate`, `fixed_fee`, `slippage`); the two `obs-*` variants also pass the observation set (`obs_set`), and the three `risk-*` variants the reward's risk coefficient (`risk_aversion`). The first line of every file says how it differs from `ppo_base.yaml`.

What the `Trading/*` curves in TensorBoard mean is in the [Training curves](../docs/training.md#training-curves-trading-in-tensorboard) table of `docs/training.md`. Note that `Trading/Turnover` divides the traded value by the **starting** equity of the episode, while `turnover` in the evaluation log divides by the **average** equity.

## Base and smoke runs

| File | What it is |
| --- | --- |
| `ppo_base.yaml` | PPO with the hyperparameters of ML-Agents' hybrid-action example FoodCollector (Release 23): batch 1024, buffer 10240, learning rate 3e-4 (linear), beta 0.005, epsilon 0.2, lambda 0.95, 3 epochs, 1 × 256 network without normalisation, gamma 0.99, time horizon 64, 2M steps, fee 0.1 % |
| `ppo_base-curve.yaml` | Base with `checkpoint_interval` 200000 and `keep_checkpoints` 11: the same 2M-step training keeps the ten 200k-step checkpoints (200k, 400k, … 2M steps) plus the one written at the end, so each can be evaluated on the validation segment for the learning curve (steps → return) |
| `smoke.yaml` | Base with 30k steps (`summary_freq` 5000, `checkpoint_interval` 30000) |
| `smoke-fee0003.yaml` | `smoke.yaml` with `fee_rate` 0.003 and 15k steps: the first episodes end at 16 agents × 720 steps = 11,520 steps, and only then do the `Trading/*` tags (including `Trading/FeeRate`) appear |
| `smoke-100k.yaml` | Base with 100k steps, to measure the speed of a new machine |
| `smoke-100k-cuda.yaml` | `smoke-100k.yaml` on the graphics card (`torch_settings.device: cuda`). The PC runs both speed tests and trains on whichever is faster; the series scripts then take `--device cuda` / `-Device cuda`, which changes only their copy of each config |

## Comparison variants (`variants/`)

Each variant changes one thing, so a difference in the results can be traced to it. The two marked *multiple* change several settings on purpose.

| File | Changed from the base | Why compare |
| --- | --- | --- |
| `beta-1e-3.yaml` | `beta` 0.001 | Weaker push to explore; watch `Policy/Entropy` |
| `beta-1e-2.yaml` | `beta` 0.01 | Stronger push to explore; watch `Policy/Entropy` |
| `gamma-0.9.yaml` | `gamma` 0.9 | Short-sighted versus far-sighted |
| `normalize-on.yaml` | `normalize: true` | Does automatic observation scaling help? |
| `width-64.yaml` | `hidden_units` 64 | Network size |
| `fee-0.yaml` | `fee_rate` 0 | **The central comparison**: how fees change the number of trades and the turnover |
| `fee-0.003.yaml` | `fee_rate` 0.003 | **The central comparison**, with triple fees |
| `sac-base.yaml` | *multiple*: SAC with FoodCollector's SAC hyperparameters (learning rate 3e-4 constant, batch 256, buffer 2048, `buffer_init_steps` 0, `tau` 0.005, `steps_per_update` 10, `init_entcoef` 0.05, `reward_signal_steps_per_update` 10); network and environment as the base | Algorithm comparison |
| `teacher-style.yaml` | *multiple*: `normalize: true`, 3 × 256 network, `gamma` 0.9, learning rate 1e-4, batch 512, buffer 10000 | The course example's defaults |
| `obs-long.yaml` | `obs_set` 1: the `long` observation set (41 numbers: the base 35 plus 1-day, 1-week, 30-day and 90-day returns scaled by volatility, the 30-day volatility and the drawdown from the 90-day high) | Does a longer view stop the agent from simply holding? |
| `obs-long-ta.yaml` | `obs_set` 2: the `long-ta` observation set (43 numbers: `long` plus daily MACD and RSI(14)) | Control group for `long`: do the classic indicators add anything? |
| `risk-0.005.yaml` | `risk_aversion` 0.005: every hour scores r − 0.005 × min(r, 0)², a penalty on the squared loss only | Does a small penalty on losses make the agent cut them instead of simply holding? |
| `risk-0.01.yaml` | `risk_aversion` 0.01 | The same, twice as strong |
| `risk-0.02.yaml` | `risk_aversion` 0.02 | The same, four times as strong |

Check the variants after editing any of them:

```bash
python scripts/train/check_configs.py
```

It fails unless every single-change variant differs from the base in exactly one setting, every file (smoke configs included) has the `TradingAgent` behavior, the three cost parameters inside the ranges the environment accepts (`fee_rate` in [0, 1), `slippage` in [0, 0.1), `fixed_fee` ≥ 0), `obs_set` (optional) 0, 1 or 2, `risk_aversion` (optional) a number in [0, 100], and the CPU device (cuda in `smoke-100k-cuda.yaml`), and ML-Agents itself accepts the file. Adding `obs_set` or `risk_aversion` counts as the one setting a variant changes; `ppo_base.yaml` has neither, which means the observation set of `gym-config.json` (`base`) and λ = 0. The range [0, 100] for `risk_aversion` is stricter on purpose than the player, which accepts any finite value ≥ 0: above 100 the penalty dwarfs the return, so it is almost certainly a typo. What the numbers of each set are and how the risk coefficient enters the reward: [docs/architecture.md](../docs/architecture.md#observation-action-reward).

Run a series (every config × every seed) with `scripts/train/run_series.sh` on macOS or `scripts/train/run_series.ps1` on Windows; see [docs/training.md](../docs/training.md) and [docs/pc-training.md](../docs/pc-training.md).
