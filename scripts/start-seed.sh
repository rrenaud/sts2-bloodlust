#!/usr/bin/env bash
# Start a run on a seed, skipping character select. The Bloodlust mod picks the request up the next time the
# main menu appears: launches the game if it isn't running, otherwise just return to the main menu.
#   scripts/start-seed.sh SEED [CHARACTER] [ASCENSION] [--abandon]
#   CHARACTER: ironclad (default) | silent | defect | necrobinder | regent
#   --abandon: abandon a run in progress (recorded as abandoned) instead of refusing to start.
set -euo pipefail
abandon=false
args=()
for a in "$@"; do
  if [[ $a == --abandon ]]; then abandon=true; else args+=("$a"); fi
done
[[ ${#args[@]} -ge 1 ]] || { sed -n 2,7p "$0"; exit 1; }
seed=${args[0]}
character=${args[1]:-ironclad}
ascension=${args[2]:-0}
dir="${XDG_DATA_HOME:-$HOME/.local/share}/SlayTheSpire2"
mkdir -p "$dir"
printf '{"seed": "%s", "character": "%s", "ascension": %d, "abandon": %s}\n' \
  "$seed" "${character^^}" "$ascension" "$abandon" > "$dir/bloodlust_autostart.json"
if pgrep -x SlayTheSpire2 >/dev/null; then
  echo "Game is running: go to the main menu (Save & Quit) and the run starts."
else
  steam -applaunch 2868840 >/dev/null 2>&1 &
  disown
  echo "Launching; the run starts when the main menu loads."
fi
