"""
Captures the owner's T0 base from a save into src/Base/T0.json, the template the Base mod builds.

T0 = as much of the base as can be built before the first drill, heater or vegetube, with only what is unlocked at the very start.

Run: python tools/capture-t0.py <save.json> [--out FILE]

Every placed object is stored as an offset from the FOUNDATION under the outdoor lamp (the anchor), in the save's own world axes (north is +x, the lamp points north),
so moving the anchor moves the whole base. Left out: the escape pod and anything without a position (items inside inventories). Crates keep their label ("text") and the
groups the game linked to them; what is inside them is not stored: the Base mod stocks a crate from its label.
Pieces are ordered foundations, pods, everything else, the lamp last. Needs only Python 3.
"""
import json
import os
import re
import sys
from decimal import Decimal

args = sys.argv[1:]
if not args:
    sys.exit(__doc__)
SRC = args[0]
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'Base', 'T0.json')
if '--out' in args:
    OUT = args[args.index('--out') + 1]

text = open(SRC, encoding='utf-8-sig').read()
records = []
for m in re.findall(r'\{"id":\d+,"gId":"[^"]+"[^{}]*\}', text):
    try:
        records.append(json.loads(m))
    except ValueError:
        pass
placed = [r for r in records if 'pos' in r and r['gId'] != 'EscapePod']


def vec(s):
    return [Decimal(v) for v in s.split(',')]


lamps = [r for r in placed if r['gId'] == 'OutsideLamp1']
if len(lamps) != 1:
    sys.exit('expected exactly one outdoor lamp, found %d' % len(lamps))
lamp = vec(lamps[0]['pos'])
foundations = [r for r in placed if r['gId'] == 'Foundation']
anchor_rec = min(foundations, key=lambda r: sum((a - b) ** 2 for a, b in zip(vec(r['pos']), lamp)))
anchor = vec(anchor_rec['pos'])
print('anchor foundation', [str(v) for v in anchor], 'lamp', [str(v) for v in lamp])

order = {'Foundation': 0, 'pod': 1, 'OutsideLamp1': 3}
pieces = []
for r in placed:
    p = vec(r['pos'])
    piece = {
        'g': r['gId'],
        'x': float(round(p[0] - anchor[0], 5)),
        'y': float(round(p[1] - anchor[1], 5)),
        'z': float(round(p[2] - anchor[2], 5)),
        'rot': r.get('rot', '0,0,0,1'),
    }
    if r.get('text'):
        piece['text'] = r['text']
    if r.get('liGrps'):
        piece['groups'] = r['liGrps']
    if r.get('pnls'):
        piece['panels'] = r['pnls']
    if r.get('color'):
        piece['color'] = r['color']
    pieces.append(piece)
pieces.sort(key=lambda p: (order.get(p['g'], 2), p['x'], p['z'], p['y']))

template = {'name': 'T0', 'capturedFrom': os.path.basename(SRC), 'anchor': [float(v) for v in anchor], 'pieces': pieces}
os.makedirs(os.path.dirname(os.path.abspath(OUT)), exist_ok=True)
with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
    json.dump(template, f, indent=1)
    f.write('\n')

# Round trip: the template plus the anchor must give back every position in the save.
worst = Decimal(0)
for piece, r in zip(sorted(placed, key=lambda r: 0), placed):
    pass
by_key = {}
for r in placed:
    by_key.setdefault(r['gId'], []).append(vec(r['pos']))
checked = 0
for piece in pieces:
    world = [anchor[0] + Decimal(str(piece['x'])), anchor[1] + Decimal(str(piece['y'])), anchor[2] + Decimal(str(piece['z']))]
    best = min(max(abs(a - b) for a, b in zip(world, c)) for c in by_key[piece['g']])
    worst = max(worst, best)
    checked += 1
print('%d pieces written to %s; worst round-trip error %s m' % (checked, os.path.abspath(OUT), worst))
