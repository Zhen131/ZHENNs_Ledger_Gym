# Architecture

How the code is layered and what happens in one step. Paths are relative to `unity/Assets/Gym/`.

## Three layers

- **Core** (`Core/`, assembly `Gym.Core`): the trading environment in plain C#, in four folders: `Market/`, `Accounting/`, `Env/` and `Evaluation/`. Its assembly definition (`Core/Gym.Core.asmdef`) has an empty `references` list and sets `noEngineReferences: true`, so the core cannot use the Unity engine.
- **Runtime** (`Runtime/`, assembly `Gym.Runtime`): connects the core to ML-Agents and to the scenes. It references `Gym.Core` and ML-Agents. Its agent, `TradingAgent`, is only a shell around the core's `TradingEnv`: it passes observations, the action mask and actions through, and adds the reward the core computed. In training it holds one `TradingEnv` per coin it trains on (one coin unless `trainSymbols` or `-gymTrainSymbols` lists more) and points `Env` at the episode's coin; evaluation, Play and Watch hold only the current coin's environment.
- **Editor tools** (`Editor/`, assembly `Gym.Editor`): building the players, running the baselines, generating the scenes, the Play checklist, the Play and Watch snapshots, and the **Gym > Watch a Model...** menu. It references `Gym.Core`, `Gym.Runtime`, ML-Agents and the inference engine, and is compiled for the Unity editor only.

Dependencies point one way: the editor tools use the runtime and the core, the runtime uses the core, and the core uses neither.

## Watching a model

The Watch scene (`Scenes/Watch.unity`) shows a trained model trading on the Play scene's screen. Its agent is saved switched off, set up like the evaluation player (Inference Only, deterministic, Burst; `BuildScript.UseEvaluationInference` sets both) but without a model, because ML-Agents throws as soon as an Inference Only agent without a model is switched on. The runtime does not reference the inference engine and cannot handle a model, so the editor tools put it on the agent: `WatchModelAttacher` registers itself as `WatchController.ModelSource` whenever the editor loads its scripts, and `WatchController` asks it for the model remembered in `WatchModelMemory` (a small file under `unity/UserSettings/`, per project). Before attaching, it checks that the model's vector observation input size (read with the inference engine, `Editor/ModelInputs.cs`) equals the size of the observation set the agent will use (`observationSet` in `gym-config.json`, or `-gymObs`); the menu refuses a model that does not fit, and the attacher does not attach it. With a model attached, the controller hands the segment and the costs to the agent through `TradingAgent.SegmentOverride` and `TradingAgent.CostOverride` (both empty in training and evaluation), switches off automatic stepping and switches the agent on. From there it steps the agent one `Academy.EnvironmentStep` at a time, as the evaluation runner does, on its own clock (`WatchPlayback`); at the end of the segment the views freeze, because the agent starts the next episode within the same step.

The Watch scene also shows dial-mode models: its mode is `actionMode` in `gym-config.json` or `-gymActionMode` when the editor starts (tests set `TradingAgent.ActionModeOverride`), and the model's action shape is checked against it like the observation size. In the dial mode the agent's last action is always recorded as a hold (the rule does the trading), so the readout's "Last action" row is rewritten as the target position and the episode's number of dial changes, for example `Target position   50% | changes: 3` (`HudReadout.LastActionRow`; `HudView.ShowsDialTarget` is switched on by the Watch scene only, so the Play scene and the order mode keep the row as it was, and the panel keeps its number of rows). The step counter, the avatar and the wallet still show the order mode's view: the step counter counts hours, the avatar makes the buying move whatever the rule did, and the wallet shows no floating amounts.

## One step

At decision index t the agent has seen the candles up to the close of t. The order it chooses fills at the open of candle t + 1, and the reward compares the equity at the close of t with the equity at the close of t + 1. Within the step, the observation and the action mask read nothing after the close of t; the open and the close of candle t + 1 are used only to fill the order and to compute the reward.

That is one step in the **order mode** (the default, rounds one and two). In the **dial mode** (round three) one step is one decision that walks `decisionHours` candles (24 by default, one decision a day): the agent picks a target position at the close of t, and then every one of those candles goes through the same hourly step, with the order chosen by the rebalancing rule instead of the agent (see [Action](#observation-action-reward)). When fewer candles are left in the episode or the segment, the last decision walks what is left and the episode ends.

A training episode starts at a random candle of the training segment of the episode's coin (the current symbol, or one picked from the training list, see [Seeds](#seeds)), with probability 0.5 already holding a random part of its equity in coin, and lasts 720 steps (`episodeLength` and `randomInitialPositionShare` in `gym-config.json`). In the dial mode it lasts `dialEpisodeLength` candles (2160, 90 daily decisions; allowed 720–2160), and an episode that starts with coin holds one of the levels, picked at random (the third random number of the start, so the order mode's random numbers are unchanged). An evaluation episode starts in cash at the first candle of the segment, has no randomness, and runs to the end of the segment. Neither starts earlier than the **history length** of the observation set, the number of earlier candles its numbers need: candle 32 of the data for `base`, candle 2160 (90 days) for `long` and `long-ta` (below). The same history length decides whether a training segment is long enough for an episode and whether `playStart` is late enough. When an episode ends, the agent ends it as interrupted: a time limit, not a terminal state.

## Observation, action, reward

**Observation:** one of three observation sets (`Core/Env/ObservationSet.cs`, `Core/Env/ObservationBuilder.cs`). `base` is the first round's 35 numbers and the default; `long` adds six long-horizon numbers; `long-ta` adds daily MACD and RSI to `long`, as a control group. Every set starts with the same 35 numbers, so the **price window** (the closes in numbers 0 to 31) is 32 candles in all three; the **history length** is only how far back the numbers reach.

| Set | Numbers | History length | Environment parameter `obs_set` |
| --- | --- | --- | --- |
| `base` | 35 (0–34) | 32 | 0 |
| `long` | 41 (0–40) | 2160 | 1 |
| `long-ta` | 43 (0–42) | 2160 | 2 |

With c(t) the close of candle t, the decision index t; nothing reads a candle after t:

- numbers 0 to 31: tanh(10 × (c(t − i) ÷ c(t) − 1)) for i = 1 … 32;
- number 32: the share of the equity held in coin;
- number 33: tanh(10 × the return of the position if it were sold at c(t), fees included);
- number 34: steps since the last trade ÷ 720, at most 1.

`long` adds, with σ the population standard deviation of the 720 hourly log returns ln(c(i) ÷ c(i − 1)) for i = t − 719 … t (raised to 1e-6 when smaller):

- numbers 35 to 38: for h = 24, 168, 720 and 2160 hours, z = ln(c(t) ÷ c(t − h)) ÷ (σ × √h) and the number is tanh(z ÷ 3);
- number 39: tanh(ln(σ ÷ 0.0065)); 0.0065 is the median of BTC's 720-hour volatility over the first round's training segment (2017-08 to 2024-08), one ruler for every coin;
- number 40: the drawdown from the highest close of the last 90 days, (max c(t − 2159 … t) − c(t)) ÷ max, in [0, 1).

`long-ta` adds two numbers computed on one close a day, d(k) = c(t − 24k) for k = 0 … 89:

- number 41: MACD. EMA12 and EMA26 run over d from the oldest (k = 89) to the newest (k = 0), both started at the oldest sample, smoothing 2 ÷ (N + 1); the number is tanh(20 × (EMA12 − EMA26) ÷ c(t));
- number 42: RSI(14) of the last 14 daily changes d(k) − d(k + 1), k = 0 … 13: G and L are the simple averages of the gains and of the absolute losses; RSI = 100 when L = 0 and G > 0, 50 when both are 0, otherwise 100 − 100 ÷ (1 + G ÷ L); the number is (RSI − 50) ÷ 50.

The set is chosen once per agent, in this order: the trainer's environment parameter `obs_set` (read with the same function as the costs, after the agent has connected to the trainer and before it builds its environment), then `-gymObs base|long|long-ta` on the command line, then `observationSet` in `gym-config.json` (default `base`). When `obs_set` chooses a different set than the command line or the config, the agent redoes the segment-length and `playStart` checks with its history length and stops the player with exit code 1 if they fail. Tests and the Watch scene can set `TradingAgent.ObservationSetOverride` instead; in training and evaluation it is empty.

**Action:** two action modes (`Core/Env/ActionMode.cs`, `Core/Env/ActionCodec.cs`):

| Mode | `action_mode` | Shape | One step |
| --- | --- | --- | --- |
| orders (default) | 0 | 1 continuous number + one discrete branch of 3 | one candle: hold, buy or sell a fraction |
| dial | 1 | 0 continuous numbers + one discrete branch with a choice per level (3) | `decisionHours` candles (24) at one target position |

*Order mode.* One discrete choice of three, hold (0), buy (1) or sell (2), and one continuous number a. The order uses the fraction clamp((a + 1) ÷ 2, 0, 1) of the cash (buy) or of the coin (sell) (`Core/Env/TradeAction.cs`). Buy and sell are masked by two rules: buy needs cash ≥ minimum order × (1 + fee rate) × (1 + slippage) + fixed fee; sell needs coin worth at least the minimum order at the close of t, after slippage. A masked choice that is sent anyway, for example from the keyboard in the Play scene, is booked as a rejected order.

*Dial mode.* The agent only picks a level k; the target position (the coin's share of the equity) is `dialLevels[k]`, 0.3, 0.5 or 0.7 (`TradingEnv.StepDial`). Every level is always allowed (the mask is all on). For each of the next `decisionHours` candles i, at the close of i, the **rebalancing rule** (`Core/Env/Rebalancer.cs`) chooses the order: with p the coin's share of the equity and b the band (`rebalanceBand`, 0.05), it buys when target − p > b, with the fraction min(1, (target − p) × equity ÷ cash) of the cash (1 when there is no cash), sells when p − target > b, with the fraction min(1, (p − target) ÷ p) of the coin, and otherwise holds; an order the mask does not allow becomes a hold. That order goes to the order mode's step, so fills, fees, the hourly reward and its clipping are the same code. It is the rule the round-two rule baselines have always used (`Baselines.RuleOrder` now forwards to it with their band 0.10). The environment counts the decisions, the candles the last decision walked, and the **level changes**: the decisions whose target differs from the one before (the first decision of an episode is not a change).

The mode is chosen once per agent, in this order: the trainer's environment parameter `action_mode` (read with the same function as the costs), then `-gymActionMode orders|dial`, then `actionMode` in `gym-config.json` (default `orders`); `decision_hours` and `rebalance_band` likewise override `-gymDecisionHours` / `decisionHours` and `-gymBand` / `rebalanceBand`. When the trainer's mode or observation set differs from the command line and the config, the agent redoes the segment-length and `playStart` checks with that mode's episode length and that set's history length and stops the player with exit code 1 if they fail. Tests and the Watch scene can set `TradingAgent.ActionModeOverride`; in training and evaluation it is empty. The Play scene does not support the dial mode yet and stops with a clear error.

**Reward:** each step scores r − λ × min(r, 0)², where r = clamp(100 × ln(E₁ ÷ E₀), −1, 1), with E₀ and E₁ the equity before and after the step (r = −1 when E₁ ≤ 0), and λ ≥ 0 is the risk coefficient (`Core/Env/RewardFunction.cs`). Fees are already out of the equity, so there is no separate fee penalty. The penalty λ × min(r, 0)² only touches losing steps: a gain keeps its score, a loss of r costs λr² more, and a step whose r was clipped to −1 scores −1 − λ. A step counts as clipped only when r was clipped, whatever the penalty. With λ = 0, the default, every score is bit for bit the first round's r. In the dial mode a step's score is the sum of the scores of the candles it walked, each clipped as above; the reward formula itself is unchanged.

λ is chosen at the start of every episode, together with the costs and in the same order: the trainer's environment parameter `risk_aversion` (read with the same function as the costs), then `-gymRiskAversion` on the command line, then the agent's Inspector default (`defaultRiskAversion`, 0 in the prefab). A negative, NaN or infinite value is refused: `-gymRiskAversion` as a config error when the player loads, the environment parameter or the Inspector default by stopping the player with exit code 1. Training, evaluation, Play and Watch pass it to `TradingEnv`'s `Reset`; the baselines do not (λ = 0), because the reward changes no evaluation metric. `TradingEnv` keeps the episode's λ, the sum of its penalties and the number of steps with a penalty above 0; `RewardSum` is the sum of the scores.

The agent prefab `Prefabs/TradingAgent.prefab` declares the order mode's shape to ML-Agents: behavior name `TradingAgent`, 35 observations without stacking, 1 continuous action plus one discrete branch of 3, and a decision every step. The prefab keeps 35; in `Initialize`, which ML-Agents calls before it creates the agent's sensors, the agent sets the vector observation size to the size of its observation set (41 for `long`, 43 for `long-ta`). The action shape cannot be changed there, because ML-Agents builds the actuators and the policy from it before it calls `Initialize`; so the agent chooses its action mode in `OnEnable`, before calling `base.OnEnable()`, and in the dial mode sets its own `ActionSpec` to 0 continuous actions and one branch of 3. The prefab keeps the order mode's shape.

A trained model only fits the set it was trained on. The runtime does not reference the inference engine, so `BuildScript.BuildEval` reads the model's input size in the editor and writes it to `build-info.json` (`vector_observation_size`); the evaluation player compares it with the size of its set before the first step and exits with code 1, writing nothing, when they differ (`EvalRunner.CheckModelInput`).

## Seeds

- With a trainer attached, each agent's master seed comes from the seed `mlagents-learn` sends, mixed with the agent's index, and the agent's log line ends in `(trainer)`. Without a trainer, or when that seed cannot be read, the master seed comes from the clock and the log line ends in `(clock)`; the series scripts search the player logs for `(clock)` (`Runtime/Agents/MasterSeedChooser.cs`, `Runtime/Agents/TradingAgent.cs`).
- The master seed starts a random sequence that gives every training episode its own seed; the episode seed picks the start candle and the initial coin holding.
- With several training coins, the episode seed also picks the coin: `SymbolPicker.Pick(seed, count)` is `new System.Random(SeedMixer.Mix(seed, SymbolPicker.StreamIndex)).Next(count)`, and 0 without any random number when there is one coin (`Core/Env/SymbolPicker.cs`). It takes nothing more from the master seed's sequence, so the episode seeds are the same with or without the list, and with one coin every episode is the same as before. The two-argument `Mix` scrambles the seed again: the start candle uses `new System.Random(SeedMixer.Mix(seed))`, and because `System.Random` is linear in its seed, a coin from `Mix(seed)` XOR a constant would depend on the start candle and the initial holding. A test checks that each of four coins gets 20–30 % of 10,000 seeds overall and within each quarter of the start candles and each initial holding.
- Every seed passes through `SeedMixer` (SplitMix64) before it reaches `System.Random`, because `System.Random` with consecutive seeds gives shifted copies of one sequence (`Core/Env/SeedMixer.cs`).

## Where to start reading

Start with `Core/Env/TradingEnv.cs`: one episode, with the step described above. It calls `ObservationBuilder`, `ActionCodec` and `RewardFunction` in the same folder and the `Account` in `Core/Accounting/`. Then `Runtime/Agents/TradingAgent.cs` shows how ML-Agents drives it.
