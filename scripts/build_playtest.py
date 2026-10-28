#!/usr/bin/env python3
"""Build a playtest page from FightCapture's screenshots and the game's run-history file.

    scripts/build_playtest.py SCREENSHOT_DIR RUN_FILE OUT_DIR

SCREENSHOT_DIR is one run's folder under user://bloodlust_screenshots (PNGs + log.txt); RUN_FILE is that run's
saves/history/<id>.run. Writes OUT_DIR/index.html, OUT_DIR/log.txt and OUT_DIR/shots/*.webp (1280px wide).
"""
import json
import sys
from pathlib import Path

from PIL import Image

shots_dir, run_file, out = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
template = (Path(__file__).parent / 'playtest_template.html').read_text()
run = json.loads(run_file.read_text())


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
        for room in point.get('rooms', []):
            if room.get('room_type') in ('monster', 'elite', 'boss'):
                rooms[floor] = {'encounter': pretty(room['model_id']), 'type': room['room_type'],
                                'turns': room.get('turns_taken')}

(out / 'shots').mkdir(parents=True, exist_ok=True)
fights = {}
for line in (shots_dir / 'log.txt').read_text().splitlines():
    fields = line.split('\t')
    png = fields[0]
    # "HP 64/80", "Vigor 0", "Bloodlust stash 2": key on the last word before the value.
    info = {k.split()[-1]: v for k, v in (f.rsplit(' ', 1) for f in fields[1:] if ' ' in f)}
    _, floor_part, kind = png[:-4].split('_')
    floor = int(floor_part.removeprefix('floor'))
    webp = f'shots/{png[:-4]}.webp'
    image = Image.open(shots_dir / png).convert('RGB')
    image.resize((1280, round(image.height * 1280 / image.width)), Image.LANCZOS).save(out / webp, 'WEBP',
                                                                                         quality=78, method=6)
    hp, max_hp = info['HP'].split('/')
    fight = fights.setdefault(floor, {'floor': floor, **rooms.get(floor, {'encounter': '?', 'type': 'monster'})})
    fight[kind] = {'shot': webp, 'hp': int(hp), 'max_hp': int(max_hp), 'vigor': int(info['Vigor']),
                   'stash': int(info['stash'])}

data = {'seed': run['seed'], 'ascension': run['ascension'], 'win': run['win'], 'minutes': round(run['run_time'] / 60),
        'character': pretty(run['players'][0]['character']), 'fights': [fights[f] for f in sorted(fights)]}
(out / 'index.html').write_text(template.replace('/*DATA*/null', json.dumps(data)))
(out / 'log.txt').write_text((shots_dir / 'log.txt').read_text())
print(f"{len(data['fights'])} fights -> {out / 'index.html'}")
