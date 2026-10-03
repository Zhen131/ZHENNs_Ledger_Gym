# Development

Maintenance commands that change or check the Unity project itself. Close the Unity editor first and run from the repository root, with `UNITY` set as in the README's [quick start](../README.md#quick-start-mac). The rules for changing the code are in [AGENTS.md](../AGENTS.md).

## Regenerate scenes, the Play checklist and a chart snapshot

```bash
"$UNITY" -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.GymSceneBuilder.BuildAll -quit -logFile "$PWD/unity/Logs/build-scenes.log"
"$UNITY" -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.PlayChecklist.Print -quit -logFile "$PWD/unity/Logs/checklist.log"
"$UNITY" -batchmode -projectPath "$PWD/unity" -executeMethod Gym.Editor.PlaySnapshot.Render -quit -logFile "$PWD/unity/Logs/snapshot.log"
```

`GymSceneBuilder.BuildAll` rewrites all scenes; the Training scene usually comes back with the same content in a different order, which can be reverted with `git checkout`.

## After any Unity run

After any Unity run, check `git status`. Unity writes `unity/Assets/ML-Agents/` (ML-Agents timers) and `unity/ProjectSettings/SceneTemplateSettings.json` by itself; Git ignores both. It may also flip the `SENTIS_ANALYTICS_ENABLED` define in `unity/ProjectSettings/ProjectSettings.asset`; see rule 10 in [AGENTS.md](../AGENTS.md).
