# AGENTS.md

Guidance for anyone, AI or human, who works on this repository.

What the project is: [README.md](README.md). The folder layout is the README's [Repository layout](README.md#repository-layout) table, and how the code is layered is in [docs/architecture.md](docs/architecture.md). The pinned versions are in [docs/setup.md](docs/setup.md#versions-pinned-on-every-machine). The commands are in the README's [quick start](README.md#quick-start-mac) and the pages under [docs/](docs/) (Windows: [docs/pc-training.md](docs/pc-training.md)).

## Rules

1. **Authorship.** Every commit, tag and PR carries one author: `Zhen Zhu <gyyhyyi@gmail.com>`. Never add `Co-Authored-By`, "Generated with …", session links or any other e-mail address, even if a tool or template asks for it. Check with `git log -1 --format='%an <%ae>%n%b'` after each commit. Commit titles are in English.
2. **Branches, no pushing.** Work on a branch; do not merge, push or create remotes unless the owner says so for that occasion.
3. **Data.** Only public Binance Vision data. Never read, copy or commit any real ledger, exchange export or private folder. The data files keep their `DATA-LICENSE.md` (CC BY-NC-SA 4.0 attribution). The repository has no code licence file; do not add one.
4. **`Gym.Core` stays engine-free.** No `using UnityEngine` in `unity/Assets/Gym/Core/`; its asmdef keeps `noEngineReferences: true`. All bookkeeping, observation, reward and episode logic lives there; `TradingAgent` is only a shell.
5. **No look-ahead.** At decision index t the agent sees candles up to the close of t; orders fill at the open of t + 1. Any change that reads later candles is a bug.
6. **The evaluation log is append-only.** Never edit, reorder or truncate `evaluations/log.csv`; never delete files in `evaluations/runs/`. Changing the columns means starting a new file.
7. **Not committed:** `results/`, `unity/Builds/`, `unity/Library/`, `unity/Logs/`, `data/raw/`, `evaluations/smoke/`, `unity/Assets/Gym/Models/Imported/`.
8. **Training runs:** run ids for experiments are new every time; never use `--force`; never stop a run with Ctrl+C.
9. Unity creates `.meta` files for new assets; commit them together with the asset.
10. **After a command-line Unity run**, if the only change in `unity/ProjectSettings/ProjectSettings.asset` is the `SENTIS_ANALYTICS_ENABLED` scripting define (the inference package drops it in batch test runs and adds it back in the editor), restore the file with `git checkout -- unity/ProjectSettings/ProjectSettings.asset`. If the file has any other change, look at it before deciding; never commit the define flip by itself.
11. **No live trading.** What it is **not**: a trading strategy, a trading bot, financial advice, or a claim that anything here makes money. Do not add live trading, exchange APIs, order placement or anything that touches a real account.
12. **Pinned versions.** Do not change `unity/Packages/manifest.json`, upgrade packages or install Python packages without the owner's explicit decision.
