#!/usr/bin/env bash
# Run a training series: every config x every seed, one mlagents-learn run each.
# macOS / Linux. The Windows twin is run_series.ps1 with the same options.
#
# Usage, with the mlagents environment active (or MLAGENTS_LEARN pointing at mlagents-learn):
#
#   tools/train/run_series.sh --env Builds/mac/Gym.app [--seeds "1 2 3 4 5"] [--num-envs 1]
#       [--prefix NAME] [--smoke] [--smoke-steps 15000] [--dry-run] CONFIG.yaml [CONFIG.yaml ...]
#
# Run ids:   [smoke-][NAME-]<config file name>-s<seed>-<yyyyMMdd, UTC>
# Results:   results/<run-id>/                (ML-Agents output, plus config-used.yaml)
#            results/<run-id>.log             (everything mlagents-learn printed; a log left
#                                              by an attempt without results is overwritten)
# A run whose results/<run-id> already exists is skipped; --force is never used.
# Seeds are whole numbers >= 0. --dry-run prints the commands and writes nothing.
# Seeds: mlagents-learn gets --seed <seed x 1000>, because ML-Agents gives environment k
# the seed + k and seeds 1, 2, 3 ... would collide when --num-envs > 1 (Q03). The run id
# keeps the plain seed (-s3); results/<run-id>/seed-used.txt records what was passed.
# One series uses one --num-envs value for every run, so the runs stay comparable.
# After each run the Player logs are searched for "(clock)": an agent that could not read
# the trainer's seed seeds itself from the clock and the run cannot be repeated (Q08). Such
# a run gets a loud warning and the line "WARNING: some agents seeded from the clock" in
# seed-used.txt.
# --smoke copies each config to results/_tmp/ with max_steps 15000 (or --smoke-steps),
# summary_freq 1000 and checkpoint_interval = max_steps; it only proves the script works.
# 15000 steps is just past the end of the first episodes (16 agents x 720 steps =
# 11,520 steps); Trading/* statistics only appear from then on (Q06).

set -u

usage() { sed -n '2,/^$/p' "$0" | sed 's/^# \{0,1\}//'; }

ENV_PATH=""
SEEDS="1 2 3 4 5"
NUM_ENVS=1
PREFIX=""
SMOKE=0
SMOKE_STEPS=15000
DRY_RUN=0
CONFIGS=()

while [ $# -gt 0 ]; do
    case "$1" in
        --env) ENV_PATH="${2:-}"; shift 2 ;;
        --seeds) SEEDS="${2:-}"; shift 2 ;;
        --num-envs) NUM_ENVS="${2:-}"; shift 2 ;;
        --prefix) PREFIX="${2:-}"; shift 2 ;;
        --smoke) SMOKE=1; shift ;;
        --smoke-steps) SMOKE_STEPS="${2:-}"; shift 2 ;;
        --dry-run) DRY_RUN=1; shift ;;
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
is_int() { case "$1" in ''|*[!0-9]*) return 1 ;; *) return 0 ;; esac; }
abs_path() { (cd "$(dirname "$1")" && printf '%s/%s\n' "$(pwd)" "$(basename "$1")"); }
# True when any agent of the run logged its master seed as "(clock)" instead of "(trainer)" (Q08).
clock_seeded() { grep -qsF '(clock)' "$1"/run_logs/Player-*.log; }

[ -n "$ENV_PATH" ] || fail "--env <path to the training build> is required"
[ -e "$ENV_PATH" ] || fail "environment build not found: $ENV_PATH"
[ ${#CONFIGS[@]} -gt 0 ] || fail "give at least one config file"
is_int "$NUM_ENVS" && [ "$NUM_ENVS" -ge 1 ] || fail "--num-envs must be one whole number >= 1 (got '$NUM_ENVS')"
is_int "$SMOKE_STEPS" && [ "$SMOKE_STEPS" -ge 1 ] || fail "--smoke-steps must be a whole number >= 1"
for seed in $SEEDS; do is_int "$seed" || fail "seeds must be whole numbers (got '$seed')"; done
[ -n "$SEEDS" ] || fail "--seeds is empty"
case "$PREFIX" in *[!A-Za-z0-9._-]*) fail "--prefix may only use letters, digits, '.', '_' and '-'" ;; esac

LEARN="${MLAGENTS_LEARN:-mlagents-learn}"
if [ "$DRY_RUN" -eq 0 ] && ! command -v "$LEARN" >/dev/null 2>&1; then
    fail "$LEARN not found; activate the mlagents environment or set MLAGENTS_LEARN"
fi

ENV_PATH="$(abs_path "$ENV_PATH")"
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
echo "series: ${#ABS_CONFIGS[@]} config(s) x seeds [$SEEDS], --num-envs $NUM_ENVS, env $ENV_PATH$( [ "$SMOKE" -eq 1 ] && echo ", smoke $SMOKE_STEPS steps")"

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
        if [ "$SMOKE" -eq 1 ]; then
            used="$REPO/results/_tmp/$run_id.yaml"
            [ "$(smoke_text "$cfg" | grep -cE "^[[:space:]]*max_steps: $SMOKE_STEPS\$")" -eq 1 ] || fail "could not set max_steps for $cfg"
            if [ "$DRY_RUN" -eq 0 ]; then
                mkdir -p results/_tmp
                smoke_text "$cfg" > "$used"
            fi
        fi

        echo "run   $run_id"
        learn_seed=$((seed * 1000))
        cmd=("$LEARN" "$used" --env "$ENV_PATH" --run-id "$run_id" --seed "$learn_seed" --num-envs "$NUM_ENVS" --no-graphics)
        if [ "$DRY_RUN" -eq 1 ]; then
            echo "      ${cmd[*]}"
            continue
        fi

        "${cmd[@]}" 2>&1 | tee "results/$run_id.log"
        rc=${PIPESTATUS[0]}
        if [ -d "results/$run_id" ]; then
            cp "$used" "results/$run_id/config-used.yaml"
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
                    echo "!!! $learn_seed; this run cannot be repeated. See results/$run_id/run_logs (Q08)."
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
[ "$clocked" -eq 0 ] || echo "WARNING: $clocked run(s) had agents seeded from the clock; see the warnings above (Q08)" >&2
[ "$failed" -eq 0 ]
