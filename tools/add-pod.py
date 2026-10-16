"""
Adds the pod you are standing in (and what you choose to keep of what is in it) to the T0 template, src/Base/T0.json.

Stand inside the pod and save the game. Then:

  python tools/add-pod.py <save.json>                    list the pod under the player and everything in it, numbered
  python tools/add-pod.py <save.json> --keep 1,3,4       add the pod, the foundations under it and those pieces
  python tools/add-pod.py <save.json> --keep all         add the pod, the foundations under it and everything listed
  python tools/add-pod.py <save.json> --keep none        add only the pod and the foundations under it
  --ahead N                                              also the N pods in a row in front of the player (the way they face), listed and added together

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
HALF = 4.1          # a pod is 8 m wide: what stands within this of its middle is in it (a 2x2 pod, Pod4x, is 16 m wide, a 3x3 pod 24)
HALVES = {'Pod4x': 8.1, 'Pod9xA': 12.1, 'Pod9xB': 12.1, 'Pod9xC': 12.1}


def half(r):
    return HALVES.get(r['gId'], HALF)

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
def contains(r):
    return abs(pos(r)[0] - player[0]) <= half(r) and abs(pos(r)[2] - player[2]) <= half(r)


# the pod the player is inside (a 2x2 pod is 16 m wide, so its middle can be 8 m away); else the nearest middle
inside_pods = [r for r in pods if contains(r)] or pods
pod = min(inside_pods, key=lambda r: (pos(r)[0] - player[0]) ** 2 + (pos(r)[2] - player[2]) ** 2 + 4 * (pos(r)[1] + 1.0 - player[1]) ** 2)
px, py, pz = pos(pod)
gap = ((px - player[0]) ** 2 + (pz - player[2]) ** 2) ** 0.5
LEVEL = 6.0   # a level is 6 m high: a pod above this one has its floor 6 m higher
print('player at %.1f, %.1f, %.1f' % tuple(player))
print('pod under the player: %s at %.2f, %.2f, %.2f (%.1f m from its middle)' % (pod['gId'], px, py, pz, gap))
if not contains(pod) and gap > 5:
    print('  (more than 5 m away: stand inside the pod and save again if this is not the one)')

# Pods in front of the player in a row (--ahead N): the way the player faces (from playerRotation), snapped to one of the four directions, 8 m apart.
ahead = int(args[args.index('--ahead') + 1]) if '--ahead' in args else 0
chain = [pod]
if ahead:
    qy, qw = [float(v) for v in re.search(r'"playerRotation":"[^,]*,([^,]*),[^,]*,([^"]*)"', text).groups()]
    import math
    yaw = math.degrees(2 * math.atan2(qy, qw))
    fx, fz = math.sin(math.radians(yaw)), math.cos(math.radians(yaw))
    step = (8.0 * round(fx), 8.0 * round(fz)) if abs(fx) >= abs(fz) else (0.0, 8.0 * round(fz))
    if abs(fx) >= abs(fz):
        step = (8.0 if fx > 0 else -8.0, 0.0)
    else:
        step = (0.0, 8.0 if fz > 0 else -8.0)
    print('facing %s' % ('+x' if step[0] > 0 else '-x' if step[0] < 0 else '+z' if step[1] > 0 else '-z'))
    for n in range(1, ahead + 1):
        last = chain[-1]
        lx, ly, lz = pos(last)
        cand = []
        for r in pods:
            if r in chain:
                continue
            rx, ry, rz = pos(r)
            along = (rx - lx) * (1 if step[0] > 0 else -1 if step[0] < 0 else 0) + (rz - lz) * (1 if step[1] > 0 else -1 if step[1] < 0 else 0)
            lateral = abs((rz - lz) if step[0] else (rx - lx))
            if abs(ry - ly) < 1.0 and along > 0 and lateral < 2.5 and along <= half(last) + half(r) + 1.0:
                cand.append((along, r))
        if not cand:
            print('  no pod %d ahead of the player' % n)
            break
        chain.append(min(cand, key=lambda c: c[0])[1])


def level(pod):
    """Only this level: nothing from a pod above or below. With a pod above, the level ends at its ceiling; with none, what stands on the roof (an antenna) counts too."""
    qx, qy, qz = pos(pod)
    above = any(r is not pod and abs(pos(r)[0] - qx) <= half(pod) and abs(pos(r)[2] - qz) <= half(pod) and abs(pos(r)[1] - (qy + LEVEL)) < 1.0 for r in pods)
    top = qy + (LEVEL - 0.1 if above else ABOVE)
    things = [r for r in placed if r is not pod and r['gId'] not in POD_GROUPS and r['gId'] != 'Foundation'
              and abs(pos(r)[0] - qx) <= half(pod) and abs(pos(r)[2] - qz) <= half(pod) and qy - BELOW <= pos(r)[1] <= top]
    things.sort(key=lambda r: (r['gId'], pos(r)[1]))
    floor = [r for r in placed if r['gId'] == 'Foundation' and abs(pos(r)[0] - qx) <= half(pod) and abs(pos(r)[2] - qz) <= half(pod) and abs(pos(r)[1] - (qy - 2.5)) < 1.0]
    return above, things, floor


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


inside, under, owner = [], [], {}
for pod_i, pod_r in enumerate(chain, 1):
    above_here, things, floor = level(pod_r)
    under += floor
    print('pod %d of %d, %s at %.1f, %.1f (this level only%s): %s; foundations under it: %d (%d already in T0)' % (
        pod_i, len(chain), pod_r['gId'], pos(pod_r)[0], pos(pod_r)[2], ', a pod above caps it' if above_here else ', roof included',
        'already in T0' if have(piece(pod_r)) else 'in T0, panels differ (--keep updates them)' if changed(piece(pod_r)) else 'not in T0 yet', len(floor), sum(1 for r in floor if have(piece(r)))))
    if not things:
        print('       nothing in it')
    for r in things:
        inside.append(r)
        p = piece(r)
        print('  %3d  %-22s %s%s%s' % (len(inside), r['gId'], '(%.1f, %.1f, %.1f)' % (p['x'], p['y'], p['z']), '  label "%s"' % r['text'] if r.get('text') else '', '  [already in T0]' if have(p) else '  [in T0 with a different label; will be updated]' if changed(p) else ''))

if keep is None:
    sys.exit(0)

chosen = []
if keep.lower() == 'all':
    chosen = inside
elif keep.lower() != 'none':
    for n in keep.split(','):
        chosen.append(inside[int(n) - 1])

new = [piece(r) for r in chain + under + chosen]
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
order = {'Foundation': 0, 'pod': 1, 'podAngle': 1, 'Pod4x': 1, 'Pod9xA': 1, 'Pod9xB': 1, 'Pod9xC': 1, 'OutsideLamp1': 3}
template['pieces'] = sorted(template['pieces'] + added, key=lambda p: (order.get(p['g'], 2), p['x'], p['z'], p['y']))
with open(TEMPLATE, 'w', encoding='utf-8', newline='\n') as f:
    json.dump(template, f, indent=1)
    f.write('\n')
print('added %d pieces to %s (%d were already there): %s' % (len(added), os.path.abspath(TEMPLATE), len(new) - len(added), ', '.join(p['g'] for p in added) or 'none'))
if updated:
    print('updated from the save: ' + ', '.join(updated))
