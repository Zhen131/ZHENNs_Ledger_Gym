# Models

`Imported/` is filled by `Gym.Editor.BuildScript.BuildMacEval`: it copies the ONNX passed with `-gymModel` to `Imported/<run-id>.onnx`, together with a copy of the Eval scene that uses it, and builds the evaluation player from there. Git ignores `Imported/`; trained models live under `results/` on the machine that trained them.
