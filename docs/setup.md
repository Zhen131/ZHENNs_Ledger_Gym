# Setup

What a machine needs before the README's [quick start](../README.md#quick-start-mac): the pinned versions, the Python environment, and an editor for the code.

## Versions (pinned on every machine)

| Component | Version |
| --- | --- |
| Unity Editor | 6000.0.84f1 (LTS) |
| ML-Agents Unity package | `com.unity.ml-agents` 4.0.3 (pulls `com.unity.ai.inference` 2.6.1) |
| Unity Test Framework | `com.unity.test-framework` 1.6.0 |
| Python | 3.10.12 (Miniforge / conda), in the conda environment `mlagents` |
| `mlagents` / `mlagents-envs` Python packages | 1.1.0 |
| PyTorch | 2.2.x |

Opening the project with a different Unity version, or pairing a different `mlagents` release, is the most common way ML-Agents setups break.

## Python environment

### macOS (Apple Silicon)

`grpcio` 1.48.2, which `mlagents` 1.1.0 pins, has no Apple Silicon wheel on PyPI or conda-forge. Use conda-forge's 1.48.1 and install the remaining dependencies explicitly:

```bash
conda create -n mlagents python=3.10.12
conda install -n mlagents "grpcio=1.48"
conda activate mlagents
pip install "torch~=2.2.1" "numpy>=1.23.5,<1.24" "protobuf>=3.6,<3.21" "onnx==1.15.0" \
  h5py "Pillow>=4.2.1" "pyyaml>=3.1.0" "six>=1.16" "attrs>=19.3.0" "huggingface-hub>=0.14" \
  "cattrs>=1.1.0,<1.7" cloudpickle "gym>=0.21.0" "pettingzoo==1.15.0" "filelock>=3.4.0" \
  absl-py markdown packaging "setuptools<70" tensorboard-data-server werkzeug
pip install --no-deps mlagents==1.1.0 mlagents-envs==1.1.0 tensorboard==2.18.0
```

`pip check` then reports one expected mismatch (TensorBoard asks for grpcio ≥ 1.48.2); it does not affect training.

### Windows

```bash
conda create -n mlagents python=3.10.12
conda activate mlagents
pip install torch~=2.2.1 --index-url https://download.pytorch.org/whl/cu121
pip install mlagents==1.1.0
```

(Not yet verified on the PC.)

The [PC training guide](pc-training.md) walks through the Windows installation step by step.

### Activating the environment

`conda activate mlagents` (in the quick start and above) works only in a terminal whose shell conda has set up: run `conda init zsh` (or `conda init bash`) once, then open a new terminal. Otherwise it fails, for example with `CondaError: Run 'conda init' before 'conda activate'`.

## Reading the code

Open the `unity/` folder in VS Code (*File → Open Folder*). Unity is configured to open scripts in VS Code; install the *Unity* extension (`visualstudiotoolsforunity.vstuc`) and a .NET SDK for IntelliSense. Where to start reading is in [architecture.md](architecture.md#where-to-start-reading).
