# Models

`Imported/` is filled by `Gym.Editor.BuildScript.BuildMacEval`: it copies the ONNX passed with `-gymModel` to `Imported/<run-id>.onnx`, together with a copy of the Eval scene that uses it, and builds the evaluation player from there. The Unity menu **Gym > Watch a Model...** (and the Watch scene's tests and snapshots) copies the chosen ONNX to `Imported/Watch-<folder>.onnx`, named after the folder the file was in. Git ignores `Imported/`; trained models live under `results/` on the machine that trained them.
