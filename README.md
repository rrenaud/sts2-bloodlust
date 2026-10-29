# Bloodlust

A Slay the Spire 2 mod that adds one relic.

**Bloodlust** (Rare, any character)
> Overkill damage you deal is stored. At the start of your next combat, gain that much Vigor (max 15).

- **Playtest 2** (cap 15): [Ironclad A10 on seed `BL00D8901`](https://rrenaud.github.io/sts2-bloodlust/playtest2/),
  lost to the Act 1 boss, Soul Fysh, on floor 17.
- **Playtest 1** (cap 20): [Ironclad A10 win on seed `BL00D7150`](https://rrenaud.github.io/sts2-bloodlust/playtest/),
  with a screenshot at the start and end of all 24 fights and how much each fight banked (use ← → to flip through them).

> Bloodlust gave the end of each battle real tension and tradeoffs. It generated meaningful choices.
>
> — rrenaud, after the first playtest

## How it works

- Only overkill counts: the damage past an enemy's remaining HP on the hit that kills it. Overkill from your
  pets (Osty) counts; damage to your own side, or another player's kills in co-op, does not.
- Overkill adds up over the whole fight, capped at 20, and the relic's counter shows the running total.
- On turn 1 of your next combat you gain that much Vigor (the same timing as Akabeko) and the counter resets.
- The stored amount is saved with the run, so it survives save & quit between fights.
- There is no custom art yet: it borrows Red Skull's icon, which is confusing if you also own Red Skull.

In the first playtest, with a cap of 20, the cap was hit in 17 of 24 fights, from Act 2 on almost every fight, so the cap is now 15.

## Getting it on floor 1

Start a single-player run on seed **`BL00D7150`** (zeros, not O's) and pick **Small Capsule** from Neow; its relic
reward is Bloodlust. The seed search below found this for every character at Ascension 0, 10 and 20, and the first
playtest confirmed it in game. Other mods that add relics change the result.

Other seeds where Small Capsule gives Bloodlust: `BL00D8901`, `BL00D11148`, `BL00D11180`, `BL00D19563`.

## Install

Built against **STS2 v0.111.0**, **.NET 9** and **BaseLib 3.4.7** (subscribe to BaseLib on the Workshop).

```bash
dotnet build -c Release    # builds and copies Bloodlust.dll + Bloodlust.json into the game's mods/Bloodlust/
```

Set `-p:Sts2Dir=...` if the game is not at `~/.local/share/Steam/steamapps/common/Slay the Spire 2`, or
`-p:SkipModInstall=true` to only build. Enable Bloodlust and BaseLib in the Mods menu and restart. Launch with
`steam -applaunch 2868840` so the Workshop BaseLib loads.

## Development tools

**Start a seed.** `scripts/start-seed.sh SEED [character] [ascension] [--abandon]` writes a one-shot request that the
mod picks up the next time the main menu appears, and starts that run directly, skipping character select. It
launches the game if it isn't running. With a run in progress it does nothing unless `--abandon` is given, which
abandons that run the way the main menu does.

**Fight screenshots.** The mod saves a screenshot when your first turn of each fight starts and when the fight ends,
to `~/.local/share/SlayTheSpire2/bloodlust_screenshots/<run start>/`, with a `log.txt` of HP, Vigor and the
stored amount for each one. `scripts/build_playtest.py` turns a run's folders (one per session, if you Save & Quit) plus its
run-history file into a page like the playtests above.

**Seed search.** `tests/` is a console app that runs the installed game's own Neow option generator, relic
grab-bag population and `RelicReward` against your profile's unlocks, without opening the game:

```bash
dotnet run -c Release --project tests -- [--seed BL00D7150] [--ascension 10] [--count 5] [--progress path/to/progress.save]
```

## Code

- `Relics/Bloodlust.cs`: the relic (`AfterDamageGiven` collects `DamageResult.OverkillDamage`,
  `AfterSideTurnStart` on turn 1 applies `VigorPower`, `[SavedProperty]` keeps the stash).
- `FightCapture.cs`: fight start/end screenshots.
- `AutoStart.cs`: the start-a-seed mode.
