# Development

Maintenance commands that change or check the Unity project itself. Close the Unity editor first and run from the repository root, with `UNITY` set as in the README's [quick start](../README.md#quick-start-mac). The rules for changing the code are in [AGENTS.md](../AGENTS.md).

## Regenerate scenes, the Play checklist and Play snapshots

```bash
"$UNITY" -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.GymSceneBuilder.BuildAll -quit -logFile "$PWD/unity/Logs/build-scenes.log"
"$UNITY" -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.PlayChecklist.Print -quit -logFile "$PWD/unity/Logs/checklist.log"
"$UNITY" -batchmode -projectPath "$PWD/unity" -executeMethod Gym.Editor.PlaySnapshot.Render -quit -logFile "$PWD/unity/Logs/snapshot.log"
```

`GymSceneBuilder.BuildAll` rewrites all scenes; the Training scene usually comes back with the same content in a different order, which can be reverted with `git checkout`.

`PlayChecklist.Print` writes `unity/Logs/play-checklist.md` (the readings after the first three presses: 25 % and `B`, `H`, 50 % and `S`) and `unity/Logs/play-checklist-pnl.md` (what the pocket shows after the same presses: the cash change, the floating amount, and the unrealized and realized profit and loss, whose sum equals equity minus the starting equity).

`PlaySnapshot.Render` needs a graphics device, so it runs without `-nographics`. It writes four 1600×900 images to `unity/Logs/`: `play-snapshot.png` (the default Chinese screen after 40 holds, a 25 % buy, 12 holds, a 50 % sell and 6 holds), `play-snapshot-en.png` (the same screen in English), `play-snapshot-buy.png` and `play-snapshot-sell.png` (right after the buy and the sell, with the floating amount half way). Everything on the Play screen, text included, is drawn by the scene's one camera, so the images show all of it.

The Play scene is laid out for a 16:9 Game view; a narrower window cuts off its sides. Its text uses a Chinese system font looked up by name when the scene runs (the list is in `unity/Assets/Gym/Runtime/Play/PlayFont.cs`); the repository holds no font file. If none of the fonts is installed, the scene falls back to Unity's default font and logs a warning, and Chinese text may show as boxes.

## After any Unity run

After any Unity run, check `git status`. Unity writes `unity/Assets/ML-Agents/` (ML-Agents timers) and `unity/ProjectSettings/SceneTemplateSettings.json` by itself; Git ignores both. It may also flip the `SENTIS_ANALYTICS_ENABLED` define in `unity/ProjectSettings/ProjectSettings.asset`; see rule 10 in [AGENTS.md](../AGENTS.md).
