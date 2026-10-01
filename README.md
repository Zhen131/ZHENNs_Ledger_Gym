# ZHENN Ledger Gym

A 2D Unity ML-Agents environment where a reinforcement-learning agent walks through historical 1-hour BTC/USDT candles, one at a time, and decides at each step whether to buy, sell or hold, and how much. Trading fees are the obstacle it has to learn to live with.

This is a university course project (Introduction to Reinforcement Learning, University of Debrecen, Fall 2026). It is **not** a trading strategy and **not** financial advice. The question it asks is how fees change what an agent learns to do — not whether it can make money.

> Status: project scaffolding. The environment, agent and training configs are not written yet.

## Versions (pinned on every machine)

| Component | Version |
| --- | --- |
| Unity Editor | 6000.0.84f1 (LTS) |
| ML-Agents Unity package | `com.unity.ml-agents` 4.0.3 |
| Python | 3.10.12 (Miniforge / conda) |
| `mlagents` Python package | 1.1.0 |
| PyTorch | 2.2.x |

Opening the project with a different Unity version, or pairing a different `mlagents` release, is the most common way ML-Agents setups break.

## Workflow

Code and tests are written on a Mac; long training runs happen on a Windows PC. Both machines clone this repository and use the exact versions above.

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

## Reading the code

Open this folder in VS Code (*File → Open Folder*). The C# scripts that make up the environment and the agent live under `Assets/`. Unity is configured to open scripts in VS Code; install the *Unity* extension (`visualstudiotoolsforunity.vstuc`) and a .NET SDK for IntelliSense.
