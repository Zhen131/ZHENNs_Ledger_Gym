# Architecture

How the code is layered and what happens in one step. Paths are relative to `unity/Assets/Gym/`.

## Three layers

- **Core** (`Core/`, assembly `Gym.Core`): the trading environment in plain C#, in four folders: `Market/`, `Accounting/`, `Env/` and `Evaluation/`. Its assembly definition (`Core/Gym.Core.asmdef`) has an empty `references` list and sets `noEngineReferences: true`, so the core cannot use the Unity engine.
- **Runtime** (`Runtime/`, assembly `Gym.Runtime`): connects the core to ML-Agents and to the scenes. It references `Gym.Core` and ML-Agents. Its agent, `TradingAgent`, is only a shell around the core's `TradingEnv`: it passes observations, the action mask and actions through, and adds the reward the core computed.
- **Editor tools** (`Editor/`, assembly `Gym.Editor`): building the players, running the baselines, generating the scenes, the Play checklist, the Play and Watch snapshots, and the **Gym > Watch a Model...** menu. It references `Gym.Core`, `Gym.Runtime`, ML-Agents and the inference engine, and is compiled for the Unity editor only.

Dependencies point one way: the editor tools use the runtime and the core, the runtime uses the core, and the core uses neither.

## Watching a model

The Watch scene (`Scenes/Watch.unity`) shows a trained model trading on the Play scene's screen. Its agent is saved switched off, set up like the evaluation player (Inference Only, deterministic, Burst; `BuildScript.UseEvaluationInference` sets both) but without a model, because ML-Agents throws as soon as an Inference Only agent without a model is switched on. The runtime does not reference the inference engine and cannot handle a model, so the editor tools put it on the agent: `WatchModelAttacher` registers itself as `WatchController.ModelSource` whenever the editor loads its scripts, and `WatchController` asks it for the model remembered in `WatchModelMemory` (a small file under `unity/UserSettings/`, per project). With a model attached, the controller hands the segment and the costs to the agent through `TradingAgent.SegmentOverride` and `TradingAgent.CostOverride` (both empty in training and evaluation), switches off automatic stepping and switches the agent on. From there it steps the agent one `Academy.EnvironmentStep` at a time, as the evaluation runner does, on its own clock (`WatchPlayback`); at the end of the segment the views freeze, because the agent starts the next episode within the same step.

## One step

At decision index t the agent has seen the candles up to the close of t. The order it chooses fills at the open of candle t + 1, and the reward compares the equity at the close of t with the equity at the close of t + 1. Within the step, the observation and the action mask read nothing after the close of t; the open and the close of candle t + 1 are used only to fill the order and to compute the reward.

A training episode starts at a random candle, with probability 0.5 already holding a random part of its equity in coin, and lasts 720 steps (`episodeLength` and `randomInitialPositionShare` in `gym-config.json`). An evaluation episode starts in cash at the first candle of the segment (no earlier than candle 32 of the data, because the observation looks back 32 candles), has no randomness, and runs to the end of the segment. When an episode ends, the agent ends it as interrupted: a time limit, not a terminal state.

## Observation, action, reward

**Observation:** 35 numbers (`Core/Env/ObservationBuilder.cs`). With c(t) the close of candle t:

- numbers 0 to 31: tanh(10 × (c(t − i) ÷ c(t) − 1)) for i = 1 … 32;
- number 32: the share of the equity held in coin;
- number 33: tanh(10 × the return of the position if it were sold at c(t), fees included);
- number 34: steps since the last trade ÷ 720, at most 1.

**Action:** one discrete choice of three, hold (0), buy (1) or sell (2), and one continuous number a. The order uses the fraction clamp((a + 1) ÷ 2, 0, 1) of the cash (buy) or of the coin (sell) (`Core/Env/ActionCodec.cs`, `Core/Env/TradeAction.cs`). Buy and sell are masked by two rules: buy needs cash ≥ minimum order × (1 + fee rate) × (1 + slippage) + fixed fee; sell needs coin worth at least the minimum order at the close of t, after slippage. A masked choice that is sent anyway, for example from the keyboard in the Play scene, is booked as a rejected order.

**Reward:** r = clamp(100 × ln(E₁ ÷ E₀), −1, 1), with E₀ and E₁ the equity before and after the step (`Core/Env/RewardFunction.cs`). Fees are already out of the equity, so there is no separate fee penalty.

The agent prefab `Prefabs/TradingAgent.prefab` declares the same shape to ML-Agents: behavior name `TradingAgent`, 35 observations without stacking, 1 continuous action plus one discrete branch of 3, and a decision every step.

## Seeds

- With a trainer attached, each agent's master seed comes from the seed `mlagents-learn` sends, mixed with the agent's index, and the agent's log line ends in `(trainer)`. Without a trainer, or when that seed cannot be read, the master seed comes from the clock and the log line ends in `(clock)`; the series scripts search the player logs for `(clock)` (`Runtime/Agents/MasterSeedChooser.cs`, `Runtime/Agents/TradingAgent.cs`).
- The master seed starts a random sequence that gives every training episode its own seed; the episode seed picks the start candle and the initial coin holding.
- Every seed passes through `SeedMixer` (SplitMix64) before it reaches `System.Random`, because `System.Random` with consecutive seeds gives shifted copies of one sequence (`Core/Env/SeedMixer.cs`).

## Where to start reading

Start with `Core/Env/TradingEnv.cs`: one episode, with the step described above. It calls `ObservationBuilder`, `ActionCodec` and `RewardFunction` in the same folder and the `Account` in `Core/Accounting/`. Then `Runtime/Agents/TradingAgent.cs` shows how ML-Agents drives it.
