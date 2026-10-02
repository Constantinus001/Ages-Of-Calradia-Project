"""Read-only comparison of actual diagnostic segment endpoints and heights."""
import collections
import csv
import json
import math
import pathlib
import sys


def audit(folder):
    paths = {r['segment']: r for r in csv.DictReader((folder / 'coastline-paths.csv').open())}
    samples = collections.defaultdict(list)
    for row in csv.DictReader((folder / 'coastline-surfaces.csv').open()):
        try:
            point = tuple(float(row[k]) for k in ('x', 'y', 'rawZ'))
            if all(math.isfinite(v) for v in point):
                samples[row['segment']].append(point)
        except ValueError:
            pass
    tolerance = .00015
    buckets = collections.defaultdict(list)
    nodes = []
    for sid, row in paths.items():
        if row['accepted'] != 'True':
            continue
        nonland = row['leftNativeTerrain'] if row['leftLand'] == 'False' else row['rightNativeTerrain']
        kind = 'inland' if row['leftLand'] == row['rightLand'] else 'sea' if nonland == 'CoastalSea' else 'unknown'
        for prefix in ('first', 'second'):
            xy = (float(row[prefix+'X']), float(row[prefix+'Y']))
            key = tuple(math.floor(v / tolerance) for v in xy)
            found = [i for dx in (-1, 0, 1) for dy in (-1, 0, 1)
                     for i in buckets[key[0]+dx, key[1]+dy] if math.dist(xy, nodes[i]['xy']) <= tolerance]
            if len(found) > 1:
                raise ValueError('Ambiguous endpoint cluster')
            if found:
                i = found[0]
            else:
                i = len(nodes)
                nodes.append({'xy': xy, 'ends': []})
                buckets[key].append(i)
            heights = [p[2] for p in samples[sid] if math.dist(xy, p[:2]) <= tolerance]
            nodes[i]['ends'].append({'segment': sid, 'kind': kind, 'z': sum(heights)/len(heights) if heights else None})
    mismatches = []
    open_ends = []
    missing_heights = 0
    for node in nodes:
        ends = node['ends']
        if all(e['kind'] == 'inland' for e in ends):
            continue
        if len(ends) == 1:
            open_ends.append(node)
        missing_heights += sum(e['z'] is None for e in ends)
        values = [e['z'] for e in ends if e['z'] is not None]
        if len(values) > 1 and max(values)-min(values) > .01:
            node['heightGap'] = max(values)-min(values)
            mismatches.append(node)
    return dict(capture=folder.name, candidates=len(paths),
                accepted=sum(r['accepted'] == 'True' for r in paths.values()),
                openCoastEndpoints=len(open_ends), heightMismatchNodes=len(mismatches),
                missingEndpointHeights=missing_heights, mismatches=mismatches), paths


if __name__ == '__main__':
    before, before_paths = audit(pathlib.Path(sys.argv[1]))
    after, after_paths = audit(pathlib.Path(sys.argv[2]))
    # Segment ids are comparable only when the actual candidate XY/order match.
    if before_paths.keys() != after_paths.keys():
        raise ValueError('Candidate ids changed; cannot compare by id')
    for key in before_paths:
        for field in ('firstX', 'firstY', 'secondX', 'secondY'):
            if before_paths[key][field] != after_paths[key][field]:
                raise ValueError('Candidate geometry changed; cannot compare by id')
    restored = [r for sid, r in after_paths.items()
                if before_paths[sid]['accepted'] == 'False' and r['accepted'] == 'True']
    lost = [sid for sid, r in after_paths.items()
            if before_paths[sid]['accepted'] == 'True' and r['accepted'] != 'True']
    result = dict(before=before, after=after, restored=restored, lostPreviouslyAccepted=lost)
    pathlib.Path(sys.argv[3]).write_text(json.dumps(result, indent=2))
    for record in (before, after):
        print(json.dumps({k: v for k, v in record.items() if k != 'mismatches'}))
    print('Restored:', ','.join(r['segment'] for r in restored), 'Lost:', lost)
