"""Extract slim side-bay doors from the existing forward-root lower skin.

No suspended pod, overlapping roof, or extra convex collider is added.
The footprint follows the outer intake shoulder and stops behind LEVCON.
"""
from prepare_su57_damage_sections import MeshBuilder, add, sub, mul, dot, cross, normalize, vec

SIDE_X = 2.58
SIDE_Z = .10
SIDE_Y = 2.10
SKEW = .11
HALF_WIDTH = .18
REAR_Z = -1.38
FRONT_Z = 1.67


def clip_plane(poly, normal, distance, high=False):
    output = []
    for a, b in zip(poly, poly[1:] + poly[:1]):
        da, db = dot(a[0], normal)-distance, dot(b[0], normal)-distance
        inside_a, inside_b = (da >= 0, db >= 0) if high else (da <= 0, db <= 0)
        if inside_a:
            output.append(a)
        if inside_a != inside_b:
            t = da/(da-db)
            output.append(tuple(add(x, mul(sub(y, x), t)) for x, y in zip(a, b)))
    return output


def author_side_bays(data, write_json):
    definitions = {s['name']: s for s in data['sections']}
    entries = []
    for side in ['L', 'R']:
        for kind, material, bone in [('SideInterior', 'hardware', ''), ('SideDoorInner', 'hardware', 'Side_'+side), ('Side', 'skin', 'Side_'+side)]:
            entries.append(dict(name=kind+'_'+side, partPath='fuselage_F/wingRoot_'+side, bone=bone, material=material))
    lods = []
    counts = []
    for lod in data['lods']:
        all_faces = []
        for mesh in lod['meshes']:
            if definitions[mesh['name']]['bone'] or definitions[mesh['name']]['material'] != 'skin':
                continue
            positions = [tuple(p[a] for a in 'xyz') for p in mesh['vertices']]
            for i in range(0, len(mesh['triangles']), 3):
                all_faces.append([positions[j] for j in mesh['triangles'][i:i+3]])

        def roof(p):
            hits = []
            x, _, z = p
            for a, b, c in all_faces:
                det=(b[2]-c[2])*(a[0]-c[0])+(c[0]-b[0])*(a[2]-c[2])
                if abs(det)<1e-10:
                    continue
                u=((b[2]-c[2])*(x-c[0])+(c[0]-b[0])*(z-c[2]))/det
                v=((c[2]-a[2])*(x-c[0])+(a[0]-c[0])*(z-c[2]))/det
                if min(u,v,1-u-v)>=-1e-5:
                    hits.append(u*a[1]+v*b[1]+(1-u-v)*c[1])
            # Keep hardware inside the existing upper/lower skins, including
            # the thin tapered ends. These are art dimensions, not measurements
            # of the real aircraft or evidence of store clearance in the game.
            top=max(hits) if hits else p[1]+.03
            return (p[0], max(p[1]+.013, min(p[1]+.30, top-.012)), p[2])

        doors = {side: MeshBuilder() for side in ['L','R']}
        for mesh in lod['meshes']:
            definition = definitions[mesh['name']]
            if definition['bone'] or definition['material'] != 'skin':
                continue
            builder=MeshBuilder()
            positions=[tuple(p[a] for a in 'xyz') for p in mesh['vertices']]
            normals=[tuple(p[a] for a in 'xyz') for p in mesh['normals']]
            uvs=[tuple(p[a] for a in 'xy') for p in mesh['uv']]
            for i in range(0,len(mesh['triangles']),3):
                ids=mesh['triangles'][i:i+3]
                poly=[(positions[j],normals[j],uvs[j]) for j in ids]
                normal=cross(sub(poly[1][0],poly[0][0]),sub(poly[2][0],poly[0][0]))
                center=mul(tuple(map(sum, zip(*(p[0] for p in poly)))), 1/3)
                if normal[1]>=-1e-9 or center[1]>2.04 or abs(center[0])<1.9:
                    builder.emit(poly)
                    continue
                side,sign=('L',-1) if center[0]<0 else ('R',1)
                # Narrow tapered footprint. X shifts outwards toward the rear
                # along the source shoulder, rather than crossing LEVCON.
                planes=[((sign,0,SKEW),SIDE_X+HALF_WIDTH),
                        ((-sign,0,-SKEW),-SIDE_X+HALF_WIDTH),
                        ((0,0,1),FRONT_Z),((0,0,-1),-REAR_Z),
                        ((sign,0,SKEW+.36),SIDE_X+HALF_WIDTH+.36*1.27),
                        ((-sign,0,-SKEW+.36),-SIDE_X+HALF_WIDTH+.36*1.27)]
                inside=poly
                for n,d in planes:
                    builder.emit(clip_plane(inside,n,d,True))
                    inside=clip_plane(inside,n,d)
                    if len(inside)<3:
                        break
                if len(inside)>=3:
                    doors[side].emit(inside)
            mesh.update(builder.export(mesh['name']))

        meshes=[]
        for side,door in doors.items():
            if not door.triangles:
                raise ValueError('No existing lower shoulder skin for side door '+side)
            inner, interior = MeshBuilder(),MeshBuilder()
            for i in range(0,len(door.triangles),3):
                ids=door.triangles[i:i+3]
                inner.emit([(add(door.vertices[j],(0,.012,0)),mul(door.normals[j],-1),door.uv[j]) for j in reversed(ids)])
                interior.emit([(roof(door.vertices[j]),(0,-1,0),door.uv[j]) for j in ids])
            edges={}
            for i in range(0,len(door.triangles),3):
                ids=door.triangles[i:i+3]
                for a,b in zip(ids,ids[1:]+ids[:1]):
                    key=tuple(sorted(tuple(round(c,7) for c in door.vertices[j]) for j in [a,b]))
                    if key in edges:edges[key]=None
                    else:edges[key]=(a,b)
            for edge in edges.values():
                if edge is None:continue
                a,b=(door.vertices[j] for j in edge)
                normal=normalize(cross(sub(b,a),(0,1,0)))
                uv=[door.uv[j] for j in edge]
                inner.emit([(a,normal,uv[0]),(b,normal,uv[1]),(add(b,(0,.012,0)),normal,uv[1]),(add(a,(0,.012,0)),normal,uv[0])])
                interior.emit([(add(a,(0,.018,0)),normal,uv[0]),(roof(a),normal,uv[0]),(roof(b),normal,uv[1]),(add(b,(0,.018,0)),normal,uv[1])])
            meshes.extend([door.export('Side_'+side),inner.export('SideDoorInner_'+side),interior.export('SideInterior_'+side)])
        lods.append({'meshes':meshes})
        counts.append({side:len(door.triangles)//3 for side,door in doors.items()})
    result=dict(schemaVersion=1, coordinateSpace='reflected source; builder adds ModelOffset',sections=entries,lods=lods)
    result['authoring']={'kind':'existing lower shoulder skin extracted as tapered doors; no added fairing volume',
        'centerX':SIDE_X-SKEW*SIDE_Z,'centerZ':SIDE_Z,'stationRootY':SIDE_Y-2.2,
        'width':HALF_WIDTH*2,'rearZ':REAR_Z,'frontZ':FRONT_Z,'skew':SKEW,
        'hingeX':SIDE_X+HALF_WIDTH-SKEW*SIDE_Z,'hingeSourceY':1.98,
        'doorTriangles':counts,'addedStructuralColliders':0,
        'status':'source geometry authoring only; loaded store fit and operation await user acceptance'}
    write_json('SU57_DamageSections.json',data)
    write_json('SU57_SideBays.json',result)
    return result
