#!/usr/bin/env bash
# Run a training series: every config x every seed, one mlagents-learn run each.
# macOS / Linux. The Windows twin is run_series.ps1 with the same options.
#
# Usage, with the mlagents environment active (or MLAGENTS_LEARN pointing at mlagents-learn):
#
#   scripts/train/run_series.sh --env unity/Builds/mac/Gym.app [--seeds "1 2 3 4 5"] [--num-envs 1]
#       [--prefix NAME] [--device cpu|cuda] [--smoke] [--smoke-steps 15000] [--dry-run]
#       [--env-args "-gymTrainSymbols BTCUSDT,ETHUSDT"] CONFIG.yaml [CONFIG.yaml ...]
#
# Run ids:   [smoke-][NAME-]<config file name>-s<seed>-<yyyyMMdd, UTC>
# Results:   results/<run-id>/                (ML-Agents output, plus config-used.yaml,
#                                              seed-used.txt, env-args.txt, build-info.json)
#            results/<run-id>.log             (everything mlagents-learn printed; a log left
#                                              by an attempt without results is overwritten)
# A run whose results/<run-id> already exists is skipped; --force is never used.
# Seeds are whole numbers >= 0. --dry-run prints the commands and writes nothing.
# Seeds: mlagents-learn gets --seed <seed x 1000>, because ML-Agents gives environment k
# the seed + k and seeds 1, 2, 3 ... would collide when --num-envs > 1. The run id
# keeps the plain seed (-s3); results/<run-id>/seed-used.txt records what was passed.
# One series uses one --num-envs value for every run, so the runs stay comparable.
# After each run the Player logs are searched for "(clock)": an agent that could not read
# the trainer's seed seeds itself from the clock and the run cannot be repeated. Such
# a run gets a loud warning and the line "WARNING: some agents seeded from the clock" in
# seed-used.txt.
# --smoke copies each config to results/_tmp/ with max_steps 15000 (or --smoke-steps),
# summary_freq 1000 and checkpoint_interval = max_steps; it only proves the script works.
# 15000 steps is just past the end of the first episodes (16 agents x 720 steps =
# 11,520 steps); Trading/* statistics only appear from then on.
# --device (default cpu) sets torch_settings.device. When a config says something else,
# it is copied to results/_tmp/ with the device changed; the files in config/ are never
# edited. config-used.yaml records the device each run really used.
# --env-args takes one quoted string of words for the player (the Unity build) and splits
# it at spaces (quotes inside are not understood). The words go after mlagents-learn's own
# --env-args, which is always the last option: mlagents-learn hands every word after it to
# the player, one argument each. The player only knows a name and its value as two words;
# "-gymObs long" in one piece would be ignored without an error. Every run writes the words
# to results/<run-id>/env-args.txt (an empty file without --env-args). Training refuses
# -gymSegment (it always uses the train segment); -logFile would hide the player logs that
# the "(clock)" check reads.
# The training build carries build-info.json in its StreamingAssets/Gym/ folder, not next to
# the executable: Gym.app/Contents/Resources/Data/StreamingAssets/Gym/ on macOS,
# Gym_Data/StreamingAssets/Gym/ next to Gym.exe or Gym.x86_64. If it is there, every run
# copies it to results/<run-id>/build-info.json.

set -u

usage() { sed -n '2,/^$/p' "$0" | sed 's/^# \{0,1\}//'; }

ENV_PATH=""
SEEDS="1 2 3 4 5"
NUM_ENVS=1
PREFIX=""
SMOKE=0
SMOKE_STEPS=15000
DEVICE=cpu
DRY_RUN=0
ENV_ARGS=""
CONFIGS=()

while [ $# -gt 0 ]; do
    case "$1" in
        --env) ENV_PATH="${2:-}"; shift 2 ;;
        --seeds) SEEDS="${2:-}"; shift 2 ;;
        --num-envs) NUM_ENVS="${2:-}"; shift 2 ;;
        --prefix) PREFIX="${2:-}"; shift 2 ;;
        --smoke) SMOKE=1; shift ;;
        --smoke-steps) SMOKE_STEPS="${2:-}"; shift 2 ;;
        --device) DEVICE="${2:-}"; shift 2 ;;
        --dry-run) DRY_RUN=1; shift ;;
        --env-args) ENV_ARGS="${2:-}"; shift 2 ;;
        -h|--help) usage; exit 0 ;;
        --*) echo "error: unknown option $1" >&2; usage >&2; exit 2 ;;
        *) CONFIGS+=("$1"); shift ;;
    esac
done

fail() { echo "error: $*" >&2; exit 2; }
smoke_text() {
    sed -E -e "s/^([[:space:]]*max_steps:).*/\1 $SMOKE_STEPS/" \
           -e "s/^([[:space:]]*summary_freq:).*/\1 1000/" \
           -e "s/^([[:space:]]*checkpoint_interval:).*/\1 $SMOKE_STEPS/" "$1"
}
# 这次运行实际用的配置：--smoke 时缩短，torch_settings.device 取自 --device。
used_text() {
    if [ "$SMOKE" -eq 1 ]; then smoke_text "$1"; else cat "$1"; fi |
        sed -E "s/^([[:space:]]*device:).*/\1 $DEVICE/"
}
config_device() { sed -nE 's/^[[:space:]]*device:[[:space:]]*([^[:space:]#]+).*/\1/p' "$1"; }
is_int() { case "$1" in ''|*[!0-9]*) return 1 ;; *) return 0 ;; esac; }
abs_path() { (cd "$(dirname "$1")" && printf '%s/%s\n' "$(pwd)" "$(basename "$1")"); }
# 这次运行里任何一个 Agent 把 master seed 记成 "(clock)" 而不是 "(trainer)" 时为真。
clock_seeded() { grep -qsF '(clock)' "$1"/run_logs/Player-*.log; }
# 训练包里 build-info.json 的位置：BuildScript 把它写在包里的 StreamingAssets/Gym/，不在可执行文件旁边。
build_info_path() {
    case "$1" in
        *.app) printf '%s/Contents/Resources/Data/StreamingAssets/Gym/build-info.json\n' "$1" ;;
        *) local base; base="$(basename "$1")"
           printf '%s/%s_Data/StreamingAssets/Gym/build-info.json\n' "$(dirname "$1")" "${base%.*}" ;;
    esac
}
# 打印一条命令：含空白的词（或空词）加双引号，这样一整个 "-gymObs long" 和分开的两个词 -gymObs long 看得出区别。
show_words() {
    local out="" word
    for word in "$@"; do
        case "$word" in
            ''|*[[:space:]]*) out="$out \"$word\"" ;;
            *) out="$out $word" ;;
        esac
    done
    printf '%s\n' "${out# }"
}

[ -n "$ENV_PATH" ] || fail "--env <path to the training build> is required"
[ -e "$ENV_PATH" ] || fail "environment build not found: $ENV_PATH"
[ ${#CONFIGS[@]} -gt 0 ] || fail "give at least one config file"
is_int "$NUM_ENVS" && [ "$NUM_ENVS" -ge 1 ] || fail "--num-envs must be one whole number >= 1 (got '$NUM_ENVS')"
is_int "$SMOKE_STEPS" && [ "$SMOKE_STEPS" -ge 1 ] || fail "--smoke-steps must be a whole number >= 1"
for seed in $SEEDS; do is_int "$seed" || fail "seeds must be whole numbers (got '$seed')"; done
[ -n "$SEEDS" ] || fail "--seeds is empty"
case "$PREFIX" in *[!A-Za-z0-9._-]*) fail "--prefix may only use letters, digits, '.', '_' and '-'" ;; esac
case "$DEVICE" in cpu|cuda) ;; *) fail "--device must be cpu or cuda (got '$DEVICE')" ;; esac

LEARN="${MLAGENTS_LEARN:-mlagents-learn}"
if [ "$DRY_RUN" -eq 0 ] && ! command -v "$LEARN" >/dev/null 2>&1; then
    fail "$LEARN not found; activate the mlagents environment or set MLAGENTS_LEARN"
fi

ENV_PATH="$(abs_path "$ENV_PATH")"
# --env-args 按空白拆成一个个词（不认引号、不展开通配符）。整串当一个参数传过去，Unity 只会收到一个认不出的词、悄悄忽略。
ENV_WORDS=()
set -f
for word in $ENV_ARGS; do ENV_WORDS+=("$word"); done
set +f
BUILD_INFO="$(build_info_path "$ENV_PATH")"
[ -f "$BUILD_INFO" ] || { BUILD_INFO_MISSING="$BUILD_INFO"; BUILD_INFO=""; }
ABS_CONFIGS=()
for cfg in "${CONFIGS[@]}"; do
    [ -f "$cfg" ] || fail "config not found: $cfg"
    ABS_CONFIGS+=("$(abs_path "$cfg")")
done

REPO="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO" || exit 2
[ "$DRY_RUN" -eq 1 ] || mkdir -p results
DATE="$(LC_ALL=C date -u +%Y%m%d)"

ran=0; skipped=0; failed=0; clocked=0
echo "series: ${#ABS_CONFIGS[@]} config(s) x seeds [$SEEDS], --num-envs $NUM_ENVS, device $DEVICE, env $ENV_PATH$( [ "$SMOKE" -eq 1 ] && echo ", smoke $SMOKE_STEPS steps")"
if [ ${#ENV_WORDS[@]} -eq 0 ]; then
    echo "env args: none (env-args.txt will be empty)"
else
    echo "env args: ${#ENV_WORDS[@]} word(s), passed last as --env-args: $(show_words "${ENV_WORDS[@]}")"
fi
if [ -n "$BUILD_INFO" ]; then
    echo "build info: $BUILD_INFO (copied to results/<run-id>/build-info.json)"
else
    echo "build info: not found at $BUILD_INFO_MISSING; rebuild the training player to get one (nothing copied)"
fi

for cfg in "${ABS_CONFIGS[@]}"; do
    name="$(basename "$cfg" .yaml)"
    for seed in $SEEDS; do
        run_id="${PREFIX:+$PREFIX-}$name-s$seed-$DATE"
        [ "$SMOKE" -eq 1 ] && run_id="smoke-$run_id"
        if [ -e "results/$run_id" ]; then
            echo "skip  $run_id: results/$run_id already exists (never --force)"
            skipped=$((skipped + 1))
            continue
        fi

        used="$cfg"
        if [ "$SMOKE" -eq 1 ] || [ "$(config_device "$cfg")" != "$DEVICE" ]; then
            used="$REPO/results/_tmp/$run_id.yaml"
            [ "$SMOKE" -eq 0 ] || [ "$(used_text "$cfg" | grep -cE "^[[:space:]]*max_steps: $SMOKE_STEPS\$")" -eq 1 ] ||
                fail "could not set max_steps for $cfg"
            [ "$(used_text "$cfg" | grep -cE "^[[:space:]]*device: $DEVICE\$")" -eq 1 ] || fail "could not set torch_settings.device for $cfg"
            if [ "$DRY_RUN" -eq 0 ]; then
                mkdir -p results/_tmp
                used_text "$cfg" > "$used"
            fi
        fi

        echo "run   $run_id"
        learn_seed=$((seed * 1000))
        cmd=("$LEARN" "$used" --env "$ENV_PATH" --run-id "$run_id" --seed "$learn_seed" --num-envs "$NUM_ENVS" --no-graphics)
        # --env-args 必须放在最后：mlagents-learn 把它后面的所有词（包括别的选项）都原样交给 Unity。
        [ ${#ENV_WORDS[@]} -eq 0 ] || cmd+=(--env-args "${ENV_WORDS[@]}")
        if [ "$DRY_RUN" -eq 1 ]; then
            echo "      $(show_words "${cmd[@]}")"
            continue
        fi

        "${cmd[@]}" 2>&1 | tee "results/$run_id.log"
        rc=${PIPESTATUS[0]}
        if [ -d "results/$run_id" ]; then
            cp "$used" "results/$run_id/config-used.yaml"
            # 传给 Unity 的词，一行、空格隔开；没给 --env-args 时是空文件。
            if [ ${#ENV_WORDS[@]} -eq 0 ]; then
                : > "results/$run_id/env-args.txt"
            else
                printf '%s\n' "${ENV_WORDS[*]}" > "results/$run_id/env-args.txt"
            fi
            [ -z "$BUILD_INFO" ] || cp "$BUILD_INFO" "results/$run_id/build-info.json"
            {
                echo "seed (run id): $seed"
                echo "--seed passed to mlagents-learn: $learn_seed"
                echo "--num-envs: $NUM_ENVS (environment k gets $learn_seed + k)"
            } > "results/$run_id/seed-used.txt"
            if clock_seeded "results/$run_id"; then
                echo "WARNING: some agents seeded from the clock" >> "results/$run_id/seed-used.txt"
                clocked=$((clocked + 1))
                {
                    echo "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!"
                    echo "!!! WARNING: in $run_id some agents seeded from the clock, not from --seed"
                    echo "!!! $learn_seed; this run cannot be repeated. See results/$run_id/run_logs."
                    echo "!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!"
                } >&2
            fi
        fi
        if [ "$rc" -eq 0 ]; then
            ran=$((ran + 1))
            echo "done  $run_id"
        else
            failed=$((failed + 1))
            echo "FAIL  $run_id (exit $rc); see results/$run_id.log" >&2
        fi
    done
done

echo "series finished: $ran ran, $skipped skipped, $failed failed"
[ "$clocked" -eq 0 ] || echo "WARNING: $clocked run(s) had agents seeded from the clock; see the warnings above" >&2
[ "$failed" -eq 0 ]
