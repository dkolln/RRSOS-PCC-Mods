"""
Captures what was added to the base after T0 into src/Base/Growth.json: pieces that appear when the group that makes them possible is unlocked.

Run: python tools/capture-growth.py <save.json> [--out FILE]

It compares the save with the T0 template (src/Base/T0.json) with the same anchor (the foundation under the outdoor lamp). For now it captures one kind of change:

  Pod panels. A window is not a piece of its own: it is a panel type on a pod. A pod whose panels differ from T0 becomes an entry with "from" (its T0 panels, which the pod must
  still have for the change to be made, so a pod the player changed themselves is left alone), "panels" (what it should become) and "needs" (the group that unlocks the new
  panel type; see PANEL_NEEDS).

Other new pieces (heaters, vegetubes, drills, ...) are listed but not captured yet.
"""
import json
import os
import re
import sys
from decimal import Decimal

# panel type -> the group that has to be unlocked before the base may use it. 3 = the living compartment window.
PANEL_NEEDS = {3: 'window'}

HERE = os.path.dirname(os.path.abspath(__file__))
args = sys.argv[1:]
if not args:
    sys.exit(__doc__)
SRC = args[0]
OUT = os.path.join(HERE, '..', 'src', 'Base', 'Growth.json')
if '--out' in args:
    OUT = args[args.index('--out') + 1]
T0 = json.load(open(os.path.join(HERE, '..', 'src', 'Base', 'T0.json'), encoding='utf-8'))


def vec(s):
    return [Decimal(v) for v in s.split(',')]


text = open(SRC, encoding='utf-8-sig').read()
records = []
for m in re.findall(r'\{"id":\d+,"gId":"[^"]+"[^{}]*\}', text):
    try:
        records.append(json.loads(m))
    except ValueError:
        pass
placed = [r for r in records if 'pos' in r and r['gId'] != 'EscapePod']

lamps = [r for r in placed if r['gId'] == 'OutsideLamp1']
if not lamps:
    # the Base mod's upgrade rules swap the one outdoor lamp for a beacon labelled Base
    lamps = [r for r in placed if r['gId'] == 'Beacon' and r.get('text', '').strip().lower() == 'base']
if len(lamps) != 1:
    sys.exit('expected exactly one outdoor lamp (or one beacon labelled Base), found %d' % len(lamps))
lamp = vec(lamps[0]['pos'])
foundations = [r for r in placed if r['gId'] == 'Foundation']
anchor = vec(min(foundations, key=lambda r: sum((a - b) ** 2 for a, b in zip(vec(r['pos']), lamp)))['pos'])
t0_anchor = [Decimal(str(v)) for v in T0['anchor']]
if anchor != t0_anchor:
    sys.exit('the save\'s anchor %s is not the T0 anchor %s' % ([str(v) for v in anchor], [str(v) for v in t0_anchor]))


def offset(r):
    p = vec(r['pos'])
    return [p[i] - anchor[i] for i in range(3)]


def near(a, b):
    return all(abs(Decimal(str(a[i])) - b[i]) < Decimal('0.05') for i in range(3))


entries = []
matched = set()
for piece in T0['pieces']:
    if piece['g'] != 'pod':
        continue
    here = [r for r in placed if r['gId'] == 'pod' and near([piece['x'], piece['y'], piece['z']], offset(r))]
    if len(here) != 1:
        print('NOT FOUND in the save: pod at', piece['x'], piece['z'])
        continue
    r = here[0]
    matched.add(r['id'])
    if r.get('pnls', '') == piece.get('panels', ''):
        continue

    old = [int(v) for v in piece['panels'].split(',')]
    new = [int(v) for v in r['pnls'].split(',')]
    changed = sorted({n for o, n in zip(old, new) if o != n})
    unknown = [c for c in changed if c not in PANEL_NEEDS]
    if unknown:
        print('SKIPPED pod at', piece['x'], piece['z'], ': panel types', unknown, 'have no entry in PANEL_NEEDS (which group unlocks them?)')
        continue
    needs = sorted({PANEL_NEEDS[c] for c in changed})
    if len(needs) != 1:
        print('SKIPPED pod at', piece['x'], piece['z'], ': needs several groups', needs)
        continue

    entries.append({
        'g': 'pod', 'x': piece['x'], 'y': piece['y'], 'z': piece['z'], 'rot': piece['rot'],
        'from': piece['panels'], 'panels': r['pnls'], 'needs': needs[0],
    })
    print('pod at x %s z %s: panels %s -> %s (needs %s)' % (piece['x'], piece['z'], piece['panels'], r['pnls'], needs[0]))

t0_counts = {}
for piece in T0['pieces']:
    t0_counts[piece['g']] = t0_counts.get(piece['g'], 0) + 1
extra = {}
for r in placed:
    if r['id'] in matched:
        continue
    extra[r['gId']] = extra.get(r['gId'], 0) + 1
extra = {g: n - t0_counts.get(g, 0) for g, n in extra.items() if n > t0_counts.get(g, 0)}
if extra:
    print('Placed since T0, not captured yet:', ', '.join('%d x %s' % (n, g) for g, n in sorted(extra.items())))

growth = {'name': 'Growth', 'capturedFrom': os.path.basename(SRC), 'anchor': [float(v) for v in anchor], 'pieces': entries}
with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
    json.dump(growth, f, indent=1)
    f.write('\n')
print('%d entries written to %s' % (len(entries), os.path.abspath(OUT)))
