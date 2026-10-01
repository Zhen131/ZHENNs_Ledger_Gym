# Training configs

All configs train the behavior `TradingAgent` on the CPU and pass the costs to the environment as `environment_parameters` (`fee_rate`, `fixed_fee`, `slippage`). The first line of every file says how it differs from `ppo_base.yaml`.

## Base and smoke runs

| File | What it is |
| --- | --- |
| `ppo_base.yaml` | PPO with the hyperparameters of ML-Agents' hybrid-action example FoodCollector (Release 23): batch 1024, buffer 10240, learning rate 3e-4 (linear), beta 0.005, epsilon 0.2, lambda 0.95, 3 epochs, 1 × 256 network without normalisation, gamma 0.99, time horizon 64, 2M steps, fee 0.1 % |
| `smoke.yaml` | Base with 30k steps (`summary_freq` 5000, `checkpoint_interval` 30000) |
| `smoke-fee0003.yaml` | `smoke.yaml` with `fee_rate` 0.003 and 15k steps: the first episodes end at 16 agents × 720 steps = 11,520 steps, and only then do the `Trading/*` tags (including `Trading/FeeRate`) appear |
| `smoke-100k.yaml` | Base with 100k steps, to measure the speed of a new machine |

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

Check the variants after editing any of them:

```bash
python tools/train/check_configs.py
```

It fails unless every single-change variant differs from the base in exactly one setting, every file has the `TradingAgent` behavior, the three cost parameters and the CPU device, and ML-Agents itself accepts the file.

Run a series (every config × every seed) with `tools/train/run_series.sh` on macOS or `tools/train/run_series.ps1` on Windows; see `docs/pc-training.md`.
