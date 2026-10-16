"""
Adds the pod you are standing in (and what you choose to keep of what is in it) to the T0 template, src/Base/T0.json.

Stand inside the pod and save the game. Then:

  python tools/add-pod.py <save.json>                    list the pod under the player and everything in it, numbered
  python tools/add-pod.py <save.json> --keep 1,3,4       add the pod, the foundations under it and those pieces
  python tools/add-pod.py <save.json> --keep all         add the pod, the foundations under it and everything listed
  python tools/add-pod.py <save.json> --keep none        add only the pod and the foundations under it

Positions are stored the way the template stores them: as offsets from its anchor foundation (the T0.json "anchor", in the save's own world axes), so the pod lands where it
stands in the base now. Crates keep their label ("text") and linked groups; what is in a crate is not stored (the Resupply mod fills them). A piece the template already has
(same group, same place) is not added twice, but takes the save's label, linked groups, panels and colour when those differ. Needs only Python 3.
"""
import json
import os
import re
import sys

args = sys.argv[1:]
if not args:
    sys.exit(__doc__)

SRC = args[0]
TEMPLATE = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'Base', 'T0.json')
keep = args[args.index('--keep') + 1] if '--keep' in args else None

POD_GROUPS = ('pod', 'podAngle', 'Pod4x', 'Pod9xA', 'Pod9xB', 'Pod9xC')
HALF = 4.1          # a pod is 8 m wide: what stands within this of its middle is in it
ABOVE, BELOW = 8.0, 1.0   # and from a little under its floor to the roof and what stands on it

text = open(SRC, encoding='utf-8-sig').read()
player = [float(v) for v in re.search(r'"playerPosition":"([^"]*)"', text).group(1).split(',')]
records = []
for m in re.findall(r'\{"id":\d+,"gId":"[^"]+"[^{}]*\}', text):
    try:
        records.append(json.loads(m))
    except ValueError:
        pass
placed = [r for r in records if 'pos' in r]


def pos(r):
    return [float(v) for v in r['pos'].split(',')]


pods = [r for r in placed if r['gId'] in POD_GROUPS]
if not pods:
    sys.exit('no pod in the save')
# the level the player is on: nearest across and, among pods stacked there, the one whose floor is just under the player (their feet are about 1 m above the floor)
pod = min(pods, key=lambda r: (pos(r)[0] - player[0]) ** 2 + (pos(r)[2] - player[2]) ** 2 + 4 * (pos(r)[1] + 1.0 - player[1]) ** 2)
px, py, pz = pos(pod)
gap = ((px - player[0]) ** 2 + (pz - player[2]) ** 2) ** 0.5
LEVEL = 6.0   # a level is 6 m high: a pod above this one has its floor 6 m higher
print('pod under the player: %s at %.2f, %.2f, %.2f (%.1f m from the player)' % (pod['gId'], px, py, pz, gap))
if gap > 5:
    print('  (more than 5 m away: stand inside the pod and save again if this is not the one)')

# only this level: nothing from a pod above or below. With a pod above, this level ends at its ceiling; with none, what stands on the roof (an antenna) counts too.
above_here = any(r is not pod and abs(pos(r)[0] - px) <= HALF and abs(pos(r)[2] - pz) <= HALF and abs(pos(r)[1] - (py + LEVEL)) < 1.0 for r in pods)
top = py + (LEVEL - 0.1 if above_here else ABOVE)
inside = [r for r in placed if r is not pod and r['gId'] not in POD_GROUPS and r['gId'] != 'Foundation'
          and abs(pos(r)[0] - px) <= HALF and abs(pos(r)[2] - pz) <= HALF and py - BELOW <= pos(r)[1] <= top]
inside.sort(key=lambda r: (r['gId'], pos(r)[1]))
under = [r for r in placed if r['gId'] == 'Foundation' and abs(pos(r)[0] - px) <= HALF and abs(pos(r)[2] - pz) <= HALF and abs(pos(r)[1] - (py - 2.5)) < 1.0]

template = json.load(open(TEMPLATE, encoding='utf-8'))
ax, ay, az = template['anchor']


def piece(r):
    x, y, z = pos(r)
    p = {'g': r['gId'], 'x': round(x - ax, 5), 'y': round(y - ay, 5), 'z': round(z - az, 5), 'rot': r.get('rot', '0,0,0,1')}
    if r.get('text'):
        p['text'] = r['text']
    if r.get('liGrps'):
        p['groups'] = r['liGrps']
    if r.get('pnls'):
        p['panels'] = r['pnls']
    if r.get('color'):
        p['color'] = r['color']
    return p


def find(p):
    return next((q for q in template['pieces'] if q['g'] == p['g'] and abs(q['x'] - p['x']) < 0.05 and abs(q['y'] - p['y']) < 0.05 and abs(q['z'] - p['z']) < 0.05), None)


FIELDS = ('text', 'groups', 'panels', 'color')   # what the save can change about a piece that is already there: its label, linked groups, panels and colour


def changed(p):
    q = find(p)
    return q is not None and any(q.get(f) != p.get(f) for f in FIELDS)


def have(p):
    return find(p) is not None and not changed(p)


print('in the pod (this level only%s):' % (', a pod above caps it' if above_here else ', roof included'))
for i, r in enumerate(inside, 1):
    p = piece(r)
    print('  %2d  %-22s %s%s%s' % (i, r['gId'], '(%.1f, %.1f, %.1f)' % (p['x'], p['y'], p['z']), '  label "%s"' % r['text'] if r.get('text') else '', '  [already in T0]' if have(p) else '  [in T0 with a different label; will be updated]' if changed(p) else ''))
if not inside:
    print('  nothing')
print('pod %s: %s; foundations under it: %d (%d already in T0)' % (pod['gId'], 'already in T0' if have(piece(pod)) else 'in T0, panels differ (--keep updates them)' if changed(piece(pod)) else 'not in T0 yet', len(under), sum(1 for r in under if have(piece(r)))))

if keep is None:
    sys.exit(0)

chosen = []
if keep.lower() == 'all':
    chosen = inside
elif keep.lower() != 'none':
    for n in keep.split(','):
        chosen.append(inside[int(n) - 1])

new = [piece(r) for r in [pod] + under + chosen]
# a piece that is there but differs in label (or linked groups, panels, colour) takes the save's values
updated = []
for p in new:
    if changed(p):
        q = find(p)
        for f in FIELDS:
            if f in p:
                q[f] = p[f]
            else:
                q.pop(f, None)
        updated.append('%s "%s"' % (p['g'], p.get('text', '')))
added = [p for p in new if find(p) is None]
order = {'Foundation': 0, 'pod': 1, 'OutsideLamp1': 3}
template['pieces'] = sorted(template['pieces'] + added, key=lambda p: (order.get(p['g'], 2), p['x'], p['z'], p['y']))
with open(TEMPLATE, 'w', encoding='utf-8', newline='\n') as f:
    json.dump(template, f, indent=1)
    f.write('\n')
print('added %d pieces to %s (%d were already there): %s' % (len(added), os.path.abspath(TEMPLATE), len(new) - len(added), ', '.join(p['g'] for p in added) or 'none'))
if updated:
    print('updated from the save: ' + ', '.join(updated))
