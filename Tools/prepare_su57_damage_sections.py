"""Author Su-57 rigid damage sections and low-poly collision shells, offline.

Preserves source UVs/normals and all triangles, clipping static triangles at
explicit aircraft-space region boundaries. Existing wing/nozzle selections
supply the moving regions. Does not launch Unity, build or test the aircraft.
"""
import collections
import hashlib
import json
import math
import colorsys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Assets/Blueprinter/Mods/SU57/Source"
REFERENCE = ROOT / "Reference/SU57/DamageModel/v0.2.30"


def add(a, b): return tuple(x+y for x,y in zip(a,b))
def sub(a, b): return tuple(x-y for x,y in zip(a,b))
def mul(a, t): return tuple(x*t for x in a)
def dot(a, b): return sum(x*y for x,y in zip(a,b))
def cross(a, b): return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def length2(a): return dot(a,a)
def normalize(a): return mul(a,1/math.sqrt(max(length2(a),1e-30)))
def vec(values): return dict(zip("xyz",values))


def node(axis, value, low, high): return (axis,value,low,high)
def sides(limit, center, left, right): return node(0,-limit,left,node(0,limit,center,right))
def side_name(name,side): return name+"_"+side


def fixed_tree():
    def wing(side):
        if side=="L": return node(0,-6.30,"Wingtip_L",node(0,-4.60,"WingOuter_L","WingInner_L"))
        return node(0,6.30,node(0,4.60,"WingInner_R","WingOuter_R"),"Wingtip_R")
    rear_center=node(2,-6.0,"Tail","FuselageRear")
    engines=sides(.70,rear_center,"Engine_L","Engine_R")
    central=node(2,-2.50,"FuselageRear","Center")
    left_mid=node(1,1.95,"Intake_L","Gearbay_L")
    right_mid=node(1,1.95,"Intake_R","Gearbay_R")
    middle=sides(1.15,central,left_mid,right_mid)
    fore=sides(.85,"FuselageForward","WingRoot_L","WingRoot_R")
    central_body=node(2,-3.30,engines,node(2,.85,middle,fore))
    with_wings=sides(2.55,central_body,wing("L"),wing("R"))
    cockpit=sides(.85,node(1,2.42,"FuselageForward","Cockpit"),"WingRoot_L","WingRoot_R")
    # The original glazing reaches Z=7.34; keep its underlying upper shell
    # within the cockpit owner before starting the detachable nose cone.
    return node(2,7.40,node(2,3.15,with_wings,cockpit),"Nose")


def read_mesh(path):
    vertices,normals,uv,faces=[],[],[],[]
    submesh=0;had_faces=False
    for line in path.read_text().splitlines():
        a=line.split()
        if not a: continue
        if a[0]=="v": vertices.append((-float(a[1]),float(a[2]),float(a[3])))
        elif a[0]=="vn": normals.append((-float(a[1]),float(a[2]),float(a[3])))
        elif a[0]=="vt": uv.append((float(a[1]),float(a[2])))
        elif a[0]=="g" and had_faces: submesh+=1;had_faces=False
        elif a[0]=="f":
            ids=[int(t.split("/")[0])-1 for t in a[1:]]
            for i in range(1,len(ids)-1): faces.append((submesh,(ids[0],ids[i+1],ids[i])))
            had_faces=True
    return vertices,normals,uv,faces


def clip(poly, axis, threshold, high):
    result=[]
    for a,b in zip(poly,poly[1:]+poly[:1]):
        da=a[0][axis]-threshold;db=b[0][axis]-threshold
        inside_a=da>=0 if high else da<=0
        inside_b=db>=0 if high else db<=0
        if inside_a: result.append(a)
        if inside_a!=inside_b:
            t=da/(da-db)
            result.append(tuple(add(x,mul(sub(y,x),t)) for x,y in zip(a,b)))
    return result


def partition(poly, tree, emit):
    if len(poly)<3: return
    if isinstance(tree,str): emit(tree,poly);return
    axis,value,low,high=tree
    coordinates=[p[0][axis] for p in poly]
    if max(coordinates)<=value: partition(poly,low,emit)
    elif min(coordinates)>=value: partition(poly,high,emit)
    else:
        partition(clip(poly,axis,value,False),low,emit)
        partition(clip(poly,axis,value,True),high,emit)


def definitions(wing):
    entries=[]
    def section(name,path,bone="",material="skin"):
        entries.append({"name":name,"partPath":path,"bone":bone,"material":material})
    for name,path in [("Center",""),("FuselageForward","fuselage_F"),("FuselageRear","fuselage_R"),("Cockpit","fuselage_F/cockpit"),("Nose","fuselage_F/cockpit/nose"),("Tail","tail")]: section(name,path)
    section("CanopyGlass","fuselage_F/cockpit",material="glass")
    for side in ("L","R"):
        wing_path="gearbay_"+side+"/wing1_"+side
        engine="fuselage_R/engine_"+side
        for name,path in [("WingRoot","fuselage_F/wingRoot_"+side),("Gearbay","gearbay_"+side),("Intake","gearbay_"+side+"/intake_"+side),("Engine",engine),("WingInner",wing_path),("WingOuter",wing_path+"/wing2_"+side),("Wingtip",wing_path+"/wing2_"+side+"/wingtip_"+side)]: section(side_name(name,side),path)
        section("Nozzle_"+side,engine+"/nozzle_"+side,"nozzle_"+side)
        for b in wing["bones"]:
            if not b["name"].endswith("_"+side): continue
            kind=b["kind"]
            if kind=="LeadingEdge":
                section("LeadingInner_"+side,wing_path+"/wing1_F"+side,b["name"])
                section("LeadingOuter_"+side,wing_path+"/wing2_"+side+"/wing2_F"+side,b["name"])
                continue
            path={"Levcon":"fuselage_F/wingRoot_"+side,"VerticalTail":engine+"/tail_"+side,"HorizontalTail":engine+"/tail_"+side+"/elevator_"+side,"Flaperon":wing_path+"/flap_"+side,"Aileron":wing_path+"/wing2_"+side+"/aileron_"+side}[kind]
            section(b["name"],path,b["name"])
    return entries


class MeshBuilder:
    def __init__(self): self.vertices=[];self.normals=[];self.uv=[];self.triangles=[];self.lookup={}
    def emit(self,poly):
        for i in range(1,len(poly)-1):
            triangle=[poly[0],poly[i],poly[i+1]]
            if length2(cross(sub(triangle[1][0],triangle[0][0]),sub(triangle[2][0],triangle[0][0])))<1e-16: continue
            for p,n,uv in triangle:
                key=tuple(round(v,8) for v in p+n+uv)
                index=self.lookup.get(key)
                if index is None:
                    index=len(self.vertices);self.lookup[key]=index
                    self.vertices.append(p);self.normals.append(normalize(n));self.uv.append(uv)
                self.triangles.append(index)
    def export(self,name):
        return {"name":name,"vertices":[vec(v) for v in self.vertices],"normals":[vec(v) for v in self.normals],"uv":[dict(zip("xy",v)) for v in self.uv],"triangles":self.triangles}


def convex_shell(source_points, min_thickness=.04):
    points=sorted(set(tuple(round(v,6) for v in p) for p in source_points))
    # Thin panels need a volumetric collision shell even when source faces
    # are mathematically coplanar. Only deficient coordinate extents expand.
    for axis in range(3):
        low=min(p[axis] for p in points);high=max(p[axis] for p in points)
        if high-low<min_thickness:
            mid=(low+high)/2
            points=[tuple(mid+sign*min_thickness/2 if j==axis else p[j] for j in range(3)) for p in points for sign in (-1,1)]
    directions=[(x,y,z) for x in (-1,0,1) for y in (-1,0,1) for z in (-1,0,1) if x or y or z]
    for i in range(48):
        y=1-2*(i+.5)/48;r=math.sqrt(1-y*y);angle=i*math.pi*(3-math.sqrt(5))
        directions.append((r*math.cos(angle),y,r*math.sin(angle)))
    points=sorted(set(max(points,key=lambda p:dot(p,d)) for d in directions))
    a=0;b=max(range(len(points)),key=lambda i:length2(sub(points[i],points[a])))
    c=max(range(len(points)),key=lambda i:length2(cross(sub(points[i],points[a]),sub(points[b],points[a]))))
    normal=cross(sub(points[b],points[a]),sub(points[c],points[a]))
    d=max(range(len(points)),key=lambda i:abs(dot(normal,sub(points[i],points[a]))))
    if abs(dot(normal,sub(points[d],points[a])))<1e-10: raise ValueError("Cannot author volumetric shell")
    interior=mul(add(add(points[a],points[b]),add(points[c],points[d])),.25)
    def face(a,b,c):
        return (a,c,b) if dot(cross(sub(points[b],points[a]),sub(points[c],points[a])),sub(interior,points[a]))>0 else (a,b,c)
    faces=[face(a,b,c),face(a,d,b),face(a,c,d),face(b,d,c)]
    for i,p in enumerate(points):
        if i in (a,b,c,d): continue
        visible=[f for f in faces if dot(cross(sub(points[f[1]],points[f[0]]),sub(points[f[2]],points[f[0]])),sub(p,points[f[0]]))>1e-9]
        edges=collections.Counter(tuple(sorted(e)) for f in visible for e in [(f[0],f[1]),(f[1],f[2]),(f[2],f[0])])
        faces=[f for f in faces if f not in visible]
        faces.extend(face(x,y,i) for (x,y),count in edges.items() if count==1)
    used=sorted(set(i for f in faces for i in f));remap={v:i for i,v in enumerate(used)}
    return {"vertices":[vec(points[i]) for i in used],"triangles":[remap[i] for f in faces for i in f]}


def draw_authoring_layout(data,shells):
    image=Image.new("RGB",(2000,1600),"#0d1726");draw=ImageDraw.Draw(image,"RGBA")
    font=ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf",27)
    small=ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf",19)
    draw.text((35,22),"Su-57 0.2.30 | Original visible sections / separated collision cells",font=font,fill="white")
    draw.text((35,65),"37 visible sections / 34 native structural owners | Source geometry only; Unity/game acceptance pending",font=small,fill="#bfd0e5")
    paths=sorted(s["partPath"] for s in shells)
    colors={p:tuple(int(c*255) for c in colorsys.hsv_to_rgb((i*.618034)%1,.46,.92)) for i,p in enumerate(paths)}
    definitions={s["name"]:s for s in data["sections"]}
    body=data["lods"][0]["meshes"]
    points=[v for m in body for v in m["vertices"]]
    bounds={a:(min(v[a] for v in points),max(v[a] for v in points)) for a in "xyz"}
    for column,meshes in enumerate([body,shells]):
        for row,(axes,label,top,bottom) in enumerate([(("x","z"),"TOP",115,1090),(("z","y"),"SIDE",1120,1535)]):
            left=25+column*990;right=left+960
            draw.rounded_rectangle((left,top,right,bottom),radius=15,fill="#142238",outline="#28415e",width=2)
            draw.text((left+20,top+15),("VISIBLE PARTS" if column==0 else "CONVEX COLLISION SHELLS")+" | "+label,font=small,fill="white")
            ax,ay=axes
            scale=min((right-left-80)/(bounds[ax][1]-bounds[ax][0]),(bottom-top-85)/(bounds[ay][1]-bounds[ay][0]))
            cx=sum(bounds[ax])/2;cy=sum(bounds[ay])/2
            def project(v): return ((left+right)/2+(v[ax]-cx)*scale,(top+bottom)/2+20-(v[ay]-cy)*scale)
            polygons=[]
            for m in meshes:
                path=definitions[m["name"]]["partPath"] if column==0 else m["partPath"]
                vertices=m["vertices"];tri=m["triangles"]
                for t in range(0,len(tri),3):
                    v=[vertices[i] for i in tri[t:t+3]]
                    depth=sum(p["y" if row==0 else "x"] for p in v)/3
                    polygons.append((depth,[project(p) for p in v],colors[path]))
            for _,polygon,color in sorted(polygons,key=lambda p:p[0]):
                draw.polygon(polygon,fill=color+(255,))
                if column==1: draw.line(polygon+[polygon[0]],fill=(10,24,40,95),width=1)
    draw.text((35,1560),"Original hull planes inset 6 mm; overlapping hulls split with 24 mm clearance. Visible source surfaces retain their positions.",font=small,fill="#bfd0e5")
    image.save(REFERENCE/"geometry-layout.png")


def main():
    REFERENCE.mkdir(parents=True,exist_ok=True)
    wing=json.loads((SOURCE/"SU57_WingSkin.json").read_text())
    nozzle=json.loads((SOURCE/"SU57_NozzleSkin.json").read_text())
    defs=definitions(wing);by_name={d["name"]:d for d in defs}
    data={"schemaVersion":1,"coordinateSpace":"LoadMesh source space: X reflected; ModelOffset applied by builder","sections":defs,"lods":[]}
    collision_points=collections.defaultdict(list);summary=[]
    for lod,(w,n) in enumerate(zip(wing["lods"],nozzle["lods"])):
        path=SOURCE/w["meshFile"];vertices,normals,uv,faces=read_mesh(path)
        ownership={}
        for s in w["surfaces"]:
            for i in s["vertices"]: ownership[i]=s["name"]
        for key,side in [("left","L"),("right","R")]:
            for i in n[key]: ownership[i]="Nozzle_"+side
        builders={s["name"]:MeshBuilder() for s in defs}
        mixed=collections.Counter()
        for material,face in faces:
            poly=[(vertices[i],normals[i],uv[i]) for i in face]
            if material==0:
                builders["CanopyGlass"].emit(poly);continue
            votes=collections.Counter(ownership.get(i,"body") for i in face)
            owner=votes.most_common(1)[0][0]
            if len(votes)>1: mixed[" | ".join(sorted(votes))]+=1
            if owner=="body": partition(poly,fixed_tree(),lambda name,p:builders[name].emit(p))
            elif owner.startswith("frontwing2_"):
                side=owner[-1]
                branch=node(0,-4.90,"LeadingOuter_L","LeadingInner_L") if side=="L" else node(0,4.90,"LeadingInner_R","LeadingOuter_R")
                partition(poly,branch,lambda name,p:builders[name].emit(p))
            else: builders[owner].emit(poly)
        meshes=[builder.export(name) for name,builder in builders.items() if builder.triangles]
        data["lods"].append({"meshFile":w["meshFile"],"sourceSha256":hashlib.sha256(path.read_bytes()).hexdigest(),"sourceVertexCount":len(vertices),"sourceTriangleCount":len(faces),"meshes":meshes})
        for name,builder in builders.items():
            if lod==0 and builder.triangles: collision_points[by_name[name]["partPath"]].extend(builder.vertices)
        summary.append({"lod":lod,"sourceTriangles":len(faces),"authoredTriangles":sum(len(b.triangles)//3 for b in builders.values()),"sections":[{"name":name,"partPath":by_name[name]["partPath"],"vertices":len(b.vertices),"triangles":len(b.triangles)//3} for name,b in builders.items()],"mixedSourceTriangles":dict(mixed)})
        print("LOD",lod,"source",len(faces),"authored",summary[-1]["authoredTriangles"],"sections",len(meshes))
    shells=[]
    for path,points in sorted(collision_points.items()):
        shells.append({"partPath":path,"name":"SU57_"+(path.replace("/","_") or "Center")+"_col",**convex_shell(points)})
    from su57_collision_cells import separate_shells
    shells=separate_shells(shells)
    (SOURCE/"SU57_DamageSections.json").write_text(json.dumps(data,separators=(",",":")),encoding="utf-8")
    (SOURCE/"SU57_CollisionSections.json").write_text(json.dumps({"schemaVersion":1,"coordinateSpace":data["coordinateSpace"],"originalPlaneInset":.006,"overlapSplitGap":.024,"shells":shells},indent=2),encoding="utf-8")
    summary={"date":"2026-11-07","status":"authored source data; Unity cooking/build/game acceptance not run","originalPlaneInset":.006,"overlapSplitGap":.024,"lods":summary,"colliders":[{"partPath":s["partPath"],"vertices":len(s["vertices"]),"triangles":len(s["triangles"])//3} for s in shells],"limitations":["Collision cells are inset approximate volumes, not exact source surface hitboxes or measured real-aircraft structure","6 mm face insets and 24 mm overlap splits describe authored rest pose only; attached self-contact filtering also covers physical articulation","Fracture caps and conformal side doors are produced by the subsequent detail authoring pass","No Unity cooking, runtime physics, store clearance, multiplayer or flight acceptance has been run"]}
    (REFERENCE/"geometry-authoring.json").write_text(json.dumps(summary,indent=2),encoding="utf-8")
    draw_authoring_layout(data,shells)
    print("Prepared",len(shells),"Su-57 collider shells; source assets only")


if __name__=="__main__": main()
