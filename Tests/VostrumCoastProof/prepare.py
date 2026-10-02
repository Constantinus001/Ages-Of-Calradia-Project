import csv,json,math,pathlib,collections
root=pathlib.Path(__file__).resolve().parents[2]
capture=root/'output/diagnostics/stripes-20260908/capture-20260909-162030-656/coastline-paths.csv'
out=root/'output/diagnostics/vostrum-coast-proof'
rows=list(csv.DictReader(capture.open()))
pairs=[]
for r in rows:
    a=(float(r['firstX']),float(r['firstY']));b=(float(r['secondX']),float(r['secondY']))
    if math.dist(((a[0]+b[0])/2,(a[1]+b[1])/2),(683.695,291.421))>=35:continue
    nonland=r['leftNativeTerrain'] if r['leftLand']=='False' else r['rightNativeTerrain']
    assert r['accepted']=='True' and r['leftLand']!=r['rightLand'] and nonland=='CoastalSea'
    pairs.append((a,b))
# The capture serializes equivalent float endpoints with slightly different
# decimal precision. Weld only sub-0.0001 differences; never bridge coast gaps.
canonical={};clusters=[];max_displacement=0
for p in sorted(set(p for pair in pairs for p in pair)):
    matches=[group for group in clusters if math.dist(p,group[0])<=.0001]
    assert len(matches)<=1, 'Ambiguous endpoint cluster'
    if matches:
        group=matches[0]
        assert all(math.dist(p,q)<=.0001 for q in group), 'Transitive weld exceeds tolerance'
        canonical[p]=group[0];group.append(p)
        max_displacement=max(max_displacement,math.dist(p,group[0]))
    else:
        canonical[p]=p;clusters.append([p])
graph=collections.defaultdict(list)
for original_a,original_b in pairs:
    a,b=canonical[original_a],canonical[original_b]
    assert a!=b and b not in graph[a], 'Collapsed or duplicate source edge'
    graph[a].append(b);graph[b].append(a)
ends=[p for p,n in graph.items() if len(n)==1]
assert len(ends)==2 and all(len(n)<=2 for n in graph.values())
path=[min(ends)];previous=None
while True:
    choices=[p for p in graph[path[-1]] if p!=previous]
    if not choices:break
    previous=path[-1];path.append(choices[0]);assert len(path)<=len(graph)
assert len(path)==len(graph)==len(pairs)+1
# Preserve the captured centerline. A Chaikin pass at width 1.6 folded an
# inner edge at x~713; do not silently accept that candidate or narrow it.
# Compute joins before subdivision so refinement cannot change the outline.
points=path
def normal(a,b):
    d=math.dist(a,b);return (-(b[1]-a[1])/d,(b[0]-a[0])/d)
left=[];right=[];max_miter=0
for i,p in enumerate(points):
    n1=normal(points[max(0,i-1)],p) if i else normal(p,points[1])
    n2=normal(p,points[i+1]) if i+1<len(points) else n1
    s=(n1[0]+n2[0],n1[1]+n2[1]);length=math.hypot(*s);n=(s[0]/length,s[1]/length)
    m=1/(n[0]*n2[0]+n[1]*n2[1]);assert m<=2;max_miter=max(max_miter,m)
    left.append((p[0]+n[0]*.8*m,p[1]+n[1]*.8*m));right.append((p[0]-n[0]*.8*m,p[1]-n[1]*.8*m))

coarse_left,coarse_right=left,right
left=[left[0]];right=[right[0]];points=[path[0]]
for i in range(len(path)-1):
    count=math.ceil(max(math.dist(coarse_left[i],coarse_left[i+1]),math.dist(coarse_right[i],coarse_right[i+1])))
    for step in range(1,count+1):
        t=step/count
        for source,dest in ((coarse_left,left),(coarse_right,right),(path,points)):
            a,b=source[i],source[i+1]
            dest.append((a[0]+(b[0]-a[0])*t,a[1]+(b[1]-a[1])*t))
def cross(a,b,c):return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
for i in range(len(points)-1):
    assert cross(left[i],right[i],left[i+1])*cross(right[i],right[i+1],left[i+1])>0, ('Folded strip',i,points[i:i+2],left[i:i+2],right[i:i+2])
outline=left+list(reversed(right))
for i,(a,b) in enumerate(zip(outline,outline[1:]+outline[:1])):
    for j in range(i+2,len(outline)):
        if i==0 and j==len(outline)-1:continue
        c,d=outline[j],outline[(j+1)%len(outline)]
        assert not(cross(a,b,c)*cross(a,b,d)<0 and cross(c,d,a)*cross(c,d,b)<0),'Crossed ribbon'
plan=dict(sourcePairs=[[a[0],a[1],b[0],b[1]] for a,b in pairs],left=left,right=right,
          sourceSegments=len(pairs),crossSections=len(points),maxMiter=max_miter,clearance=.15,
          endpointWeldTolerance=.0001,maxEndpointDisplacement=max_displacement,
          endpointWelds=[group for group in clusters if len(group)>1],
          notes='Vostrum Naval DLC proof only; height must be sampled in the live scene; no water pass validated')
out.mkdir(parents=True,exist_ok=True)
(out/'vostrum-plan.json').write_text(json.dumps(plan,indent=2))
x0=min(p[0] for p in outline)-2;y0=min(p[1] for p in outline)-2
width=max(p[0] for p in outline)-x0+2;height=max(p[1] for p in outline)-y0+2
def coords(p):return ' '.join(f'{x:.5f},{y:.5f}' for x,y in p)
svg=f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="{x0} {y0} {width} {height}">
<rect x="{x0}" y="{y0}" width="{width}" height="{height}" fill="#132131"/>
<polygon points="{coords(outline)}" fill="#d8a6ed"/>
<polyline points="{coords(path)}" fill="none" stroke="#f4d479" stroke-width=".12"/>
</svg>'''
(out/'plan-view.svg').write_text(svg)
print(json.dumps({k:plan[k] for k in ('sourceSegments','crossSections','maxMiter')}))
print('PASS: confirmed sea only; one open chain; anchored ends; consistent winding; no ribbon boundary intersections.')
