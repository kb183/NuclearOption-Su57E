"""Author fracture interiors, damage atlases and weapon geometry; no game tests.

Dependencies: Pillow, numpy and shapely. Local packages live in
Tools/AuthoringDeps (Unity clears Temp on editor restart).
Geometry uses the existing reflected OBJ source space.
"""
from pathlib import Path
import collections
import hashlib
import json
import math
import random
import sys
import uuid

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'Tools/AuthoringDeps'))
import numpy as np
from shapely import constrained_delaunay_triangles, make_valid
from shapely.geometry import Polygon
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageOps
from prepare_su57_damage_sections import read_mesh, clip, MeshBuilder, add, sub, mul, dot, cross, normalize, vec

SOURCE = ROOT / 'Assets/Blueprinter/Mods/SU57/Source'
REPORT = ROOT / 'Reference/SU57/DamageModel/v0.2.30'
PLANES = {0: [-6.30,-4.90,-4.60,-2.55,-1.15,-.85,-.70,.70,.85,1.15,2.55,4.60,4.90,6.30],
          1: [1.95,2.42], 2: [-6,-3.30,-2.50,.85,3.15,7.40]}


def write_json(name, value):
    path=SOURCE/name
    path.write_text(json.dumps(value,separators=(',',':')),encoding='utf-8')
    meta=path.with_name(path.name+'.meta')
    if not meta.exists():
        meta.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid5(uuid.NAMESPACE_URL,'su57/'+name).hex+
                        '\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')


def boundary_edges(mesh):
    points=[tuple(v[a] for a in 'xyz') for v in mesh['vertices']]
    keys=[tuple(round(c,4) for c in p) for p in points]
    counts=collections.Counter()
    lookup={}
    for i in range(0,len(mesh['triangles']),3):
        triangle=mesh['triangles'][i:i+3]
        for a,b in zip(triangle,triangle[1:]+triangle[:1]):
            if keys[a]==keys[b]: continue
            edge=tuple(sorted((keys[a],keys[b])))
            counts[edge]+=1;lookup[edge]=(points[a],points[b])
    return [lookup[e] for e,c in counts.items() if c==1]


def edge_chains(edges):
    # Welding ignores UV/normal splits. T-junctions are treated as separate
    # chains; their artificial closing segment remains inside the cut plane.
    graph=collections.defaultdict(set);points={}
    for a,b in edges:
        ka,kb=(tuple(round(c,4) for c in p) for p in (a,b))
        if ka==kb: continue
        graph[ka].add(kb);graph[kb].add(ka);points[ka]=a;points[kb]=b
    remaining={tuple(sorted((a,b))) for a,bs in graph.items() for b in bs}
    while remaining:
        ends=sorted(a for a in graph if sum(tuple(sorted((a,b))) in remaining for b in graph[a])==1)
        start=ends[0] if ends else min(remaining)[0]
        chain=[start];current=start
        while True:
            options=sorted(b for b in graph[current] if tuple(sorted((current,b))) in remaining)
            if not options: break
            nxt=options[0];remaining.remove(tuple(sorted((current,nxt))))
            if nxt==start: break
            chain.append(nxt);current=nxt
        if len(chain)>=3: yield [points[k] for k in chain]


def cap_polygon(builder, chain, normal, axis):
    axes=[i for i in range(3) if i!=axis]
    flat=[(p[axes[0]],p[axes[1]]) for p in chain]
    poly=make_valid(Polygon(flat))
    polygons=[poly] if poly.geom_type=='Polygon' else [g for g in getattr(poly,'geoms',[]) if g.geom_type=='Polygon']
    emitted=0
    for poly in polygons:
        # LOD2 wing-root slivers are smaller than the old 8 cm2 cutoff.
        # Preserve those valid contours instead of leaving narrow open seams.
        if poly.area<.0000001: continue
        # Recess the structural face 4 mm into its own section. The side strip
        # connects the cut edge to that face; exterior UVs remain untouched.
        coord=sum(p[axis] for p in chain)/len(chain)
        def lift(q, recess):
            p=[0.,0.,0.];p[axis]=coord-normal[axis]*recess
            p[axes[0]],p[axes[1]]=q
            return tuple(p)
        for triangle in constrained_delaunay_triangles(poly).geoms:
            corners=list(triangle.exterior.coords)[:3]
            positions=[lift(q,.004) for q in corners]
            if dot(cross(sub(positions[1],positions[0]),sub(positions[2],positions[0])),normal)<0: positions.reverse()
            builder.emit([(p,normal,(p[axes[0]]*.6,p[axes[1]]*.6)) for p in positions])
        for ring in [poly.exterior,*poly.interiors]:
            coords=list(ring.coords)
            for a,b in zip(coords,coords[1:]):
                p=[lift(a,0),lift(b,0),lift(b,.004),lift(a,.004)]
                n=normalize(cross(sub(p[1],p[0]),sub(p[2],p[0])))
                builder.emit([(q,n,(q[axes[0]]*.6,q[axes[1]]*.6)) for q in p])
        emitted+=1
    return emitted


def author_caps(data):
    defs={s['name']:s for s in data['sections']};lods=[];counts=[]
    for level,lod in enumerate(data['lods']):
        meshes=[];stats=[];closed=[]
        for mesh in lod['meshes']:
            definition=defs[mesh['name']]
            if definition['material']=='glass' or mesh['name'].startswith('Nozzle'): continue
            points=np.array([[v[a] for a in 'xyz'] for v in mesh['vertices']])
            center=points.mean(axis=0);edges=boundary_edges(mesh)
            if not edges:closed.append(mesh['name'])
            builder=MeshBuilder();polygons=0
            if not definition['bone']:
                for axis,planes in PLANES.items():
                    for plane in planes:
                        on_plane=[e for e in edges if all(abs(p[axis]-plane)<.00012 for p in e)]
                        normal=[0.,0.,0.];normal[axis]=1. if center[axis]<plane else -1.
                        for chain in edge_chains(on_plane): polygons+=cap_polygon(builder,chain,tuple(normal),axis)
            else:
                # Moving panels may have non-planar saw-tooth boundaries from
                # triangle selection. Project each closed root contour to its
                # dominant plane; omit strongly curved inlet/outlet contours.
                for chain in edge_chains(edges):
                    p=np.array(chain);_,singular,basis=np.linalg.svd(p-p.mean(axis=0),full_matrices=False)
                    normal=basis[-1];axis=int(np.argmax(abs(normal)))
                    if abs(normal[axis])<.92 or np.ptp(p[:,axis])>.12: continue
                    normal=[0.,0.,0.];normal[axis]=1. if center[axis]<p[:,axis].mean() else -1.
                    polygons+=cap_polygon(builder,chain,tuple(normal),axis)
            if builder.triangles:
                meshes.append(builder.export(mesh['name']))
                stats.append({'name':mesh['name'],'faces':len(builder.triangles)//3,'contours':polygons})
        lods.append({'meshes':meshes});counts.append({'lod':level,'caps':stats,'noOpenSourceBoundary':closed})
        print('Fracture interiors LOD',level,len(meshes),'sections',sum(m['faces'] for m in stats),'triangles')
    write_json('SU57_FractureCaps.json',{'schemaVersion':1,'lods':lods})
    return counts


def author_side_bays(data):
    from su57_side_bays import author_side_bays as extract_side_bays
    return extract_side_bays(data,write_json)


def author_weapon_doors(side_bays):
    lods=[]
    files=['RU_SU_57_closed_-3781013679314548165.obj','02_RU_SU_57_closed_-6969719833142860783.obj']
    for level,file in enumerate([files[0],files[1],files[1]]):
        v,n,uv,faces=read_mesh(SOURCE/file)
        builders={name:MeshBuilder() for name in ['MainFront_L','MainFront_R','MainRear_L','MainRear_R']}
        for _,ids in faces:
            center=tuple(sum(v[i][a] for i in ids)/3 for a in range(3))
            # Exactly the former fixed-cover selection; gear panels remain
            # in the existing, accepted native LandingGear pipeline.
            if center[2]>3 or abs(center[0])>1.4: continue
            poly=[(v[i],n[i],uv[i]) for i in ids]
            for front,high in [('MainFront',True),('MainRear',False)]:
                zpoly=clip(poly,2,-2.85,high)
                for side,right in [('L',False),('R',True)]:
                    builders[front+'_'+side].emit(clip(zpoly,0,0,right))
        meshes=[b.export(name) for name,b in builders.items()]
        meshes.extend(m for m in side_bays['lods'][level]['meshes'] if m['name'] in ('Side_L','Side_R'))
        lods.append({'meshFile':file,'sourceSha256':hashlib.sha256((SOURCE/file).read_bytes()).hexdigest(),'meshes':meshes})
    write_json('SU57_WeaponDoors.json',{'schemaVersion':1,'lods':lods})
    return [{'name':m['name'],'triangles':len(m['triangles'])//3} for m in lods[0]['meshes']]


def author_hardpoints(data):
    anchors=[]
    def underside(x,z):
        hits=[]
        for mesh in data['lods'][0]['meshes']:
            if not mesh['name'].startswith(('WingInner','WingOuter')):continue
            vertices=mesh['vertices'];tri=mesh['triangles']
            for t in range(0,len(tri),3):
                a,b,c=(vertices[i] for i in tri[t:t+3])
                det=(b['z']-c['z'])*(a['x']-c['x'])+(c['x']-b['x'])*(a['z']-c['z'])
                if abs(det)<1e-10:continue
                u=((b['z']-c['z'])*(x-c['x'])+(c['x']-b['x'])*(z-c['z']))/det
                v=((c['z']-a['z'])*(x-c['x'])+(a['x']-c['x'])*(z-c['z']))/det
                if u>=-1e-6 and v>=-1e-6 and u+v<=1+1e-6:
                    hits.append(u*a['y']+v*b['y']+(1-u-v)*c['y']-2.2)
        if not hits:raise ValueError('Authored station has no fixed wing support: '+str((x,z)))
        return min(hits)
    def add_anchor(group,index,name,part,position,doors=None):
        anchors.append(dict(setIndex=group,index=index,name=name,partPath=part,position=vec(position),
                            euler=vec((0,0,0)),doors=doors or []))
    # Aircraft-local coordinates measured from the authored model, not claims
    # of real Su-57 attachment dimensions. No physical AeroPart is repositioned.
    add_anchor(0,0,'Cannon','fuselage_F',(.86,.18,5.35))
    add_anchor(1,0,'MainFront','fuselage_F',(0,-.53,-.15),['MainFront_L','MainFront_R'])
    add_anchor(2,0,'MainRear','fuselage_R',(0,-.33,-5.40),['MainRear_L','MainRear_R'])
    for index,side in enumerate(['L','R']):
        sign=-1 if side=='L' else 1
        add_anchor(3,index,'Side_'+side,'fuselage_F/wingRoot_'+side,(sign*(2.58-.11*.10),-.10,.10),['Side_'+side])
        anchors[-1]['euler']=vec((0,-sign*math.degrees(math.atan(.11)),0))
        for group,name,x,z,part in [(4,'Inner',3.72,-2.40,'gearbay_'+side+'/wing1_'+side),
                                  (5,'Outer',5.38,-3.65,'gearbay_'+side+'/wing1_'+side+'/wing2_'+side)]:
            support=underside(sign*x,z)
            add_anchor(group,index,name+'_'+side,part,(sign*x,support-.34,z))
            anchors[-1]['supportY']=support+.015
    doors=[]
    for name,part,x,y,z in [('MainFront','fuselage_F',.51,-.48,-.25),('MainRear','fuselage_R',.51,-.275,-5.64)]:
        for side in ['L','R']:
            sign=-1 if side=='L' else 1
            doors.append(dict(name=name+'_'+side,partPath=part,position=vec((sign*x,y,z)),hingeAngle=sign*95,openSpeed=6,closeSpeed=4))
    for side in ['L','R']:
        sign=-1 if side=='L' else 1
        doors.append(dict(name='Side_'+side,partPath='fuselage_F/wingRoot_'+side,
                          position=vec((sign*(2.76-.11*.10),-.22,.10)),euler=vec((0,-sign*math.degrees(math.atan(.11)),0)),hingeAngle=sign*105,openSpeed=7,closeSpeed=4))
    write_json('SU57_Hardpoints.json',{'schemaVersion':1,'coordinateSpace':'aircraft root local, ModelOffset already applied',
               'anchors':anchors,'doors':doors,'reservedDisabledSet':6,
               'notes':['Indices 0..5 retained; index 6 reserved empty for legacy tail-hook loadouts',
                        'Side doors extracted from existing outer intake shoulder skin; hanging fairings and collision extensions removed',
                        'Coordinates are initial art placement; loaded-store clearance awaits user acceptance']})
    return anchors


def author_damage_textures(data):
    size=2048;rng=random.Random(570228)
    base=Image.open(SOURCE/'SU-57_SU57_1_BaseMap_4131054348559121509.png').convert('RGBA').resize((size,size),Image.Resampling.LANCZOS)
    mask=Image.new('L',(size,size));draw=ImageDraw.Draw(mask)
    for mesh in data['lods'][0]['meshes']:
        if mesh['name']=='CanopyGlass': continue
        uv=mesh['uv'];indices=mesh['triangles']
        for i in range(0,len(indices),3):
            draw.polygon([(uv[j]['x']*(size-1),(1-uv[j]['y'])*(size-1)) for j in indices[i:i+3]],fill=255)
    occupied=np.asarray(mask)>0
    locations=np.argwhere(occupied)
    scorch=Image.new('L',(size,size));chips=Image.new('L',(size,size));holes=Image.new('L',(size,size));scratches=Image.new('L',(size,size))
    sd,cd,hd,xd=(ImageDraw.Draw(im) for im in [scorch,chips,holes,scratches])
    for hit in range(300):
        y,x=map(int,locations[rng.randrange(len(locations))]);radius=rng.uniform(5,24)
        polygon=[]
        for j in range(15):
            angle=j*math.tau/15;r=radius*rng.uniform(.65,1.2)
            polygon.append((x+math.cos(angle)*r,y+math.sin(angle)*r*.7))
        sd.polygon(polygon,fill=rng.randint(170,255))
        inner=[(x+(a-x)*.40,y+(b-y)*.40) for a,b in polygon];cd.polygon(inner,fill=rng.randint(150,255))
        if hit%17==0:
            hd.polygon([(x+(a-x)*.12,y+(b-y)*.12) for a,b in polygon],fill=255)
        for _ in range(rng.randint(1,4)):
            dx,dy=rng.uniform(-radius,radius),rng.uniform(-radius,radius)
            xd.line((x+dx,y+dy,x+dx+rng.uniform(5,28),y+dy+rng.uniform(-4,4)),fill=rng.randint(80,210),width=1)
    # Larger irregular heat/abrasion patches complement the small impact marks.
    for _ in range(35):
        y,x=map(int,locations[rng.randrange(len(locations))]);radius=rng.uniform(25,65)
        polygon=[]
        for j in range(19):
            angle=j*math.tau/19;r=radius*rng.uniform(.55,1.2)
            polygon.append((x+math.cos(angle)*r,y+math.sin(angle)*r*.6))
        sd.polygon(polygon,fill=rng.randint(150,230))
    noise=np.random.default_rng(570228).random((size,size))
    burn=np.asarray(scorch.filter(ImageFilter.GaussianBlur(7)),dtype=np.float32)/255*occupied
    chip=np.asarray(chips,dtype=np.float32)/255*occupied
    scratch=np.asarray(scratches,dtype=np.float32)/255*occupied
    damage=np.asarray(base,dtype=np.float32).copy()
    soot=np.stack([25+noise*12,26+noise*11,28+noise*10],axis=-1)
    damage[:,:,:3]=damage[:,:,:3]*(1-burn[:,:,None]*.91)+soot*burn[:,:,None]*.91
    exposed=(chip*.8+scratch*.5).clip(0,1)
    metal=np.stack([97+noise*30,91+noise*29,80+noise*26],axis=-1)
    damage[:,:,:3]=damage[:,:,:3]*(1-exposed[:,:,None])+metal*exposed[:,:,None]
    hole=np.asarray(holes)>100
    damage[:,:,3]=np.where(hole & occupied,0,255)
    Image.fromarray(damage.clip(0,255).astype('uint8')).save(SOURCE/'SU57_DamageBasecolor.png')
    height=chip*.10-burn*.025+scratch*.04
    gy,gx=np.gradient(height);nx=-gx*3;ny=gy*3;nz=np.ones_like(nx)
    norm=np.sqrt(nx*nx+ny*ny+nz*nz)
    normal=np.stack([nx/norm*.5+.5,ny/norm*.5+.5,nz/norm*.5+.5],axis=-1)*255
    Image.fromarray(normal.clip(0,255).astype('uint8')).save(SOURCE/'SU57_DamageNormal.png')
    Image.new('RGB',(16,16),(128,128,255)).save(SOURCE/'SU57_SkinNormal.png')
    Image.new('RGB',(16,16),(255,255,255)).save(SOURCE/'SU57_SkinAO.png')
    # Metallic remains linear R/A, explicitly kept apart from the float
    # _Metallic of URP Lit. Native _Metallic is a texture property.
    metallic=Image.open(SOURCE/'SU57_MetallicSmoothness.png').convert('RGBA')
    r,g,b,a=metallic.split();Image.merge('RGBA',(r,r,r,a)).save(SOURCE/'SU57_SkinMetallic.png')
    cap=Image.new('RGB',(256,256));arr=np.random.default_rng(5728).integers(0,17,(256,256),dtype='uint8')
    cap=Image.fromarray(np.stack([arr+62,arr+58,arr+49],axis=-1))
    capdraw=ImageDraw.Draw(cap)
    for y in range(8,256,24):
        for x in range(0,256,28):
            capdraw.regular_polygon((x+(14 if (y//24)%2 else 0),y,11),6,rotation=30,outline=(30,29,26),width=2)
    cap.save(SOURCE/'SU57_FractureInterior.png')
    # Preserve stable Unity import identities; builder adjusts normal import.
    template=(SOURCE/'SU57_MetallicSmoothness.png.meta').read_text()
    for name in ['SU57_DamageBasecolor','SU57_DamageNormal','SU57_SkinNormal','SU57_SkinAO','SU57_SkinMetallic','SU57_FractureInterior']:
        meta=SOURCE/(name+'.png.meta')
        if not meta.exists():
            import re
            txt=re.sub(r'guid: [0-9a-f]+','guid: '+uuid.uuid5(uuid.NAMESPACE_URL,'su57/'+name).hex,template,count=1)
            if name=='SU57_DamageBasecolor' or name=='SU57_FractureInterior':txt=txt.replace('sRGBTexture: 0','sRGBTexture: 1')
            meta.write_text(txt)
    sheet=Image.new('RGB',(1800,650),'#142033');d=ImageDraw.Draw(sheet)
    for i,(label,panel) in enumerate([('Su-57 base UV',base),('Authored damage / alpha',Image.open(SOURCE/'SU57_DamageBasecolor.png')),('Damage tangent normal',Image.open(SOURCE/'SU57_DamageNormal.png'))]):
        d.text((i*600+12,12),label,fill='white');sheet.paste(panel.convert('RGB').resize((600,600)),(i*600,40))
    sheet.save(REPORT/'damage-textures.png')
    return {'resolution':size,'seed':570228,'hitPatterns':300,'transparentPixels':int((hole & occupied).sum()),
            'normalConvention':'RGB tangent-space +Y; Unity NormalMap importer repacks for native sampling',
            'metallicInput':'linear replicated R metal, A retained smoothness; native visual response awaits acceptance'}


def draw_weapons(data,anchors):
    image=Image.new('RGB',(1300,1450),'#142033');d=ImageDraw.Draw(image)
    font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',22)
    def project(p):return (650+p[0]*65,720-p[2]*65)
    faces=[]
    for mesh in data['lods'][0]['meshes']:
        v=mesh['vertices'];t=mesh['triangles']
        for i in range(0,len(t),3):
            p=[tuple(v[j][a] for a in 'xyz') for j in t[i:i+3]]
            faces.append((sum(q[1] for q in p),p))
    for _,p in sorted(faces,reverse=True): d.polygon([project(q) for q in p],fill='#677d91',outline='#52697d')
    for a in anchors:
        pos=project(tuple(a['position'][axis] for axis in 'xyz'))
        d.ellipse((pos[0]-7,pos[1]-7,pos[0]+7,pos[1]+7),fill='#ffb653')
        d.text((pos[0]+10,pos[1]-16),a['name'],fill='white',font=font)
    d.text((25,20),'Su-57 0.2.30 | authored weapon stations | underside projection',font=font,fill='white')
    d.text((25,1405),'Initial geometry placement; loaded-store and launch clearance awaits user acceptance.',font=font,fill='#bdcada')
    image.save(REPORT/'weapon-stations.png')


def draw_side_bays(bays):
    image=Image.new('RGB',(1600,1050),'#101d2c');draw=ImageDraw.Draw(image)
    font=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',23)
    draw.text((25,20),'Su-57 0.2.30 | side doors cut from original lower shoulder skin',fill='white',font=font)
    draw.text((25,60),'No hanging pods; original exterior positions, normals and UVs retained.',fill='#b5c8dd',font=font)
    data=json.loads((SOURCE/'SU57_DamageSections.json').read_text())
    for column,(axes,label) in enumerate([(('x','z'),'UNDERSIDE'),(('z','y'),'LOWER RIGHT SHOULDER')]):
        left=20+column*800
        draw.rounded_rectangle((left,110,left+760,950),radius=16,fill='#192c40',outline='#35506c')
        draw.text((left+20,130),label,fill='white',font=font)
        all_meshes=[(m,False) for m in data['lods'][0]['meshes']]+[(m,True) for m in bays['lods'][0]['meshes'] if m['name']=='Side_R']
        polys=[]
        for mesh,moving in all_meshes:
            ps=mesh['vertices'];t=mesh['triangles']
            for i in range(0,len(t),3):
                points=[ps[j] for j in t[i:i+3]]
                if any(p['x']<1.8 or p['x']>3.45 or p['z']< -1.8 or p['z']>2.3 for p in points):continue
                if column==0:
                    projected=[(left+60+(p['x']-1.8)*380,880-(p['z']+1.8)*170) for p in points]
                    depth=-sum(p['y'] for p in points)
                else:
                    projected=[(left+55+(p['z']+1.8)*160,810-(p['y']-1.5)*500) for p in points]
                    depth=sum(p['x'] for p in points)
                polys.append((depth,projected,'#efb15e' if moving else '#607c91'))
        for _,poly,color in sorted(polys,key=lambda q:q[0]):draw.polygon(poly,fill=color,outline='#30465b')
    draw.text((25,985),'Orange = extracted door. Source projection only; in-game store clearance and taxi acceptance pending.',fill='#b5c8dd',font=font)
    image.save(REPORT/'side-bays.png')


def main():
    REPORT.mkdir(parents=True,exist_ok=True)
    data=json.loads((SOURCE/'SU57_DamageSections.json').read_text())
    # Include original painted UVs before moving the side panels out of the
    # body data; the doors continue using the same damage/livery atlas.
    textures=author_damage_textures(data)
    side_bays=author_side_bays(data);caps=author_caps(data)
    doors=author_weapon_doors(side_bays);anchors=author_hardpoints(data)
    draw_weapons(data,anchors)
    draw_side_bays(side_bays)
    (REPORT/'authoring.json').write_text(json.dumps({'version':'0.2.30','caps':caps,'doorsLOD0':doors,'textures':textures,
            'sideBays':side_bays['authoring'],
            'status':'authored source assets only; no Unity build or game validation'},indent=2),encoding='utf-8')
    print('Authored fracture, texture and weapon source resources to',SOURCE)


if __name__=='__main__':main()
