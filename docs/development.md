# Development

Maintenance commands that change or check the Unity project itself. Close the Unity editor first and run from the repository root, with `UNITY` set as in the README's [quick start](../README.md#quick-start-mac). The rules for changing the code are in [AGENTS.md](../AGENTS.md).

## Regenerate scenes, the Play checklist and snapshots

```bash
"$UNITY" -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.GymSceneBuilder.BuildAll -quit -logFile "$PWD/unity/Logs/build-scenes.log"
"$UNITY" -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.PlayChecklist.Print -quit -logFile "$PWD/unity/Logs/checklist.log"
"$UNITY" -batchmode -projectPath "$PWD/unity" -executeMethod Gym.Editor.PlaySnapshot.Render -quit -logFile "$PWD/unity/Logs/snapshot.log"
"$UNITY" -batchmode -projectPath "$PWD/unity" -executeMethod Gym.Editor.WatchSnapshot.Render -gymModel "$PWD/results/<run-id>/TradingAgent.onnx" -logFile "$PWD/unity/Logs/watch-snapshot.log"
```

`GymSceneBuilder.BuildAll` rewrites all scenes, the Watch scene included; the Training scene usually comes back with the same content in a different order, which can be reverted with `git checkout`. The Watch scene is not added to the build list (`EditorBuildSettings.asset` keeps its three scenes); its agent is saved switched off and without a model, with the evaluation player's inference settings.

`PlayChecklist.Print` writes `unity/Logs/play-checklist.md` (the readings after the first three presses: 25 % and `B`, `H`, 50 % and `S`) and `unity/Logs/play-checklist-pnl.md` (what the pocket shows after the same presses: the cash change, the floating amount, and the unrealized and realized profit and loss, whose sum equals equity minus the starting equity).

`PlaySnapshot.Render` needs a graphics device, so it runs without `-nographics`. It writes seven 1600×900 images to `unity/Logs/`: `play-snapshot.png` (the default Chinese screen after 40 holds, a 25 % buy, 12 holds, a 50 % sell and 6 holds), `play-snapshot-en.png` (the same screen in English), `play-snapshot-buy.png` and `play-snapshot-sell.png` (right after the buy and the sell, with the floating amount and the avatar's motion half way), `play-snapshot-rejected.png` (a sell with no coin left, the avatar shaking), and two close-ups of the avatar: `play-snapshot-avatar.png` with the placeholder and `play-snapshot-avatar-picture.png` with a 3:2 test picture the tool writes to `unity/Temp/` (the real avatar folder is not touched). Everything on the Play screen, text included, is drawn by the scene's one camera, so the images show all of it.

`WatchSnapshot.Render` needs a graphics device and a trained model, and it enters Play mode, so it runs without `-nographics` and without `-quit`; it quits by itself (exit code 0 on success). It writes `watch-snapshot-no-model.png` (the hint shown before any model is chosen), then goes through the same steps as the **Gym > Watch a Model...** menu with the model given in `-gymModel` and writes `watch-snapshot.png` (the model trading, in English, after 300 steps on the next filled order, with the avatar half way through its motion) and `watch-snapshot-end.png` (the end of the validation segment, stopped on the last step). It remembers the model in a temporary record under `unity/Temp/`, so the model this project remembers for the Watch scene stays as it was.

The Play scene is laid out for a 16:9 Game view; a narrower window cuts off its sides. Its text uses a Chinese system font looked up by name when the scene runs (the list is in `unity/Assets/Gym/Runtime/Play/PlayFont.cs`); the repository holds no font file. If none of the fonts is installed, the scene falls back to Unity's default font and logs a warning, and Chinese text may show as boxes.

## Tests that need a trained model

The repository holds no model, so three tests are reported as ignored unless `GYM_WATCH_MODEL` gives the full path of an `.onnx` file: `WatchSetupTests.RememberingATrainedModel_…` (EditMode) and the two `WatchModelTests` (PlayMode), which watch the model over the whole validation segment twice and check that both runs take the same steps bit for bit. With `GYM_WATCH_EVAL_LOG` set to the `log.csv` that the evaluation player wrote for the same model (validation segment, fee rate 0.001), the PlayMode test also checks that the watch gives that row (all columns but the time and the run id) and the final equity in the run's details file. No `mlagents-learn` may be running: in the editor ML-Agents connects to a local trainer, and the test fails if it did.

```bash
export GYM_WATCH_MODEL="$PWD/results/<run-id>/TradingAgent.onnx"
export GYM_WATCH_EVAL_LOG="$PWD/evaluations/smoke/log.csv"   # optional
"$UNITY" -batchmode -nographics -projectPath "$PWD/unity" -runTests -testPlatform EditMode -testResults "$PWD/unity/Logs/editmode-results.xml" -logFile "$PWD/unity/Logs/editmode.log"
"$UNITY" -batchmode -nographics -projectPath "$PWD/unity" -runTests -testPlatform PlayMode -testResults "$PWD/unity/Logs/playmode-results.xml" -logFile "$PWD/unity/Logs/playmode.log"
```

The tests import the model as `Assets/Gym/Models/Imported/Watch-<folder>.onnx` (ignored by Git) and keep their record of it in a temporary file.

## After any Unity run

After any Unity run, check `git status`. Unity writes `unity/Assets/ML-Agents/` (ML-Agents timers) and `unity/ProjectSettings/SceneTemplateSettings.json` by itself; Git ignores both. It may also flip the `SENTIS_ANALYTICS_ENABLED` define in `unity/ProjectSettings/ProjectSettings.asset`; see rule 10 in [AGENTS.md](../AGENTS.md).
