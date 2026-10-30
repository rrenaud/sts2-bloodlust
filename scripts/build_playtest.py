#!/usr/bin/env python3
"""Build a playtest page from FightCapture's screenshots and the game's run-history file.

    scripts/build_playtest.py OUT_DIR RUN_FILE SCREENSHOT_DIR... --number N --cap C [--quote TEXT]

SCREENSHOT_DIR is a run's folder under user://bloodlust_screenshots (PNGs + log.txt); older versions split a run into
one folder per Save & Quit session, so several can be passed, in order. RUN_FILE is that run's saves/history/<id>.run. Writes
OUT_DIR/index.html, OUT_DIR/log.txt and OUT_DIR/shots/f<floor>_<kind>.webp (1280px wide); <kind> is start, end or cards.
"""
import argparse
import html
import json
from pathlib import Path

from PIL import Image

parser = argparse.ArgumentParser()
parser.add_argument('out', type=Path)
parser.add_argument('run_file', type=Path)
parser.add_argument('shot_dirs', type=Path, nargs='+')
parser.add_argument('--number', required=True, help='playtest number, for the title')
parser.add_argument('--cap', type=int, required=True, help="the relic's Vigor cap during this run")
parser.add_argument('--quote', help="the player's note, shown under the header")
args = parser.parse_args()
out = args.out
template = (Path(__file__).parent / 'playtest_template.html').read_text()
run = json.loads(args.run_file.read_text())


def pretty(model_id):
    words = model_id.split('.', 1)[1].split('_')
    while words and words[-1] in ('WEAK', 'NORMAL', 'ELITE', 'BOSS'):
        words.pop()
    return ' '.join(w.capitalize() for w in words)


# Combat rooms by floor, from the run history (floors count from 1 across acts).
rooms = {}
floor = 0
for act in run['map_point_history']:
    for point in act:
        floor += 1
        stats = point.get('player_stats') or [{}]
        for room in point.get('rooms', []):
            if room.get('room_type') in ('monster', 'elite', 'boss'):
                cards = [{'name': pretty(c['card']['id']) + '+' * c['card'].get('current_upgrade_level', 0),
                          'picked': c['was_picked']} for c in stats[0].get('card_choices', [])]
                rooms[floor] = {'encounter': pretty(room['model_id']), 'type': room['room_type'],
                                'turns': room.get('turns_taken'), 'card_choices': cards}

(out / 'shots').mkdir(parents=True, exist_ok=True)
fights = {}
log = []
for shots_dir in args.shot_dirs:
    for line in (shots_dir / 'log.txt').read_text().splitlines():
        fields = line.split('\t')
        png = fields[0]
        # "HP 64/80", "Vigor 0", "Bloodlust stash 2": key on the last word before the value.
        info = {k.split()[-1]: v for k, v in (f.rsplit(' ', 1) for f in fields[1:] if ' ' in f)}
        _, floor_part, kind = png[:-4].split('_')
        floor = int(floor_part.removeprefix('floor'))
        fight = fights.setdefault(floor, {'floor': floor, **rooms.get(floor, {'encounter': '?', 'type': 'monster'})})
        # Resuming a run retakes the fight's start shot; keep the first.
        if kind in fight:
            continue
        log.append(line)
        webp = f'shots/f{floor:02}_{kind}.webp'
        image = Image.open(shots_dir / png).convert('RGB')
        image.resize((1280, round(image.height * 1280 / image.width)), Image.LANCZOS).save(out / webp, 'WEBP',
                                                                                             quality=78, method=6)
        hp, max_hp = info['HP'].split('/')
        fight['cards_shot'] = fight.get('cards_shot') or kind == 'cards'
        fight[kind] = {'shot': webp, 'hp': int(hp), 'max_hp': int(max_hp), 'vigor': int(info['Vigor']),
                       'stash': int(info['stash'])}

data = {'seed': run['seed'], 'ascension': run['ascension'], 'win': run['win'], 'minutes': round(run['run_time'] / 60),
        'character': pretty(run['players'][0]['character']), 'fights': [fights[f] for f in sorted(fights)]}
quote = (f'<blockquote>\n    <p>{html.escape(args.quote)}</p>\n    <cite>rrenaud, after the run</cite>\n  </blockquote>\n'
         if args.quote else '')
page = (template.replace('{{NUMBER}}', html.escape(args.number)).replace('{{CAP}}', str(args.cap))
        .replace('<!--QUOTE-->', quote).replace('/*DATA*/null', json.dumps(data)))
(out / 'index.html').write_text(page)
(out / 'log.txt').write_text(''.join(l + '\n' for l in log))
print(f"{len(data['fights'])} fights -> {out / 'index.html'}")
