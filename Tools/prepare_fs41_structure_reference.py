"""Create offline reference tables and collider projections, never game tests."""
import collections
import csv
import json
import re
from pathlib import Path

import UnityPy
from PIL import Image, ImageDraw, ImageFont
from UnityPy.helpers.MeshHelper import MeshHandler

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Reference/SU57/FS41StructureResearch"


def identity():
    return [[float(i == j) for j in range(4)] for i in range(4)]


def multiply(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def inverse(a):
    augmented = [list(row) + identity()[i] for i, row in enumerate(a)]
    for i in range(4):
        pivot = max(range(i, 4), key=lambda r: abs(augmented[r][i]))
        augmented[i], augmented[pivot] = augmented[pivot], augmented[i]
        divisor = augmented[i][i]
        augmented[i] = [x/divisor for x in augmented[i]]
        for j in range(4):
            if j != i:
                factor = augmented[j][i]
                augmented[j] = [x-factor*y for x, y in zip(augmented[j], augmented[i])]
    return [row[4:] for row in augmented]


def transform_point(matrix, point):
    return [sum(matrix[i][j] * (list(point)+[1])[j] for j in range(4)) for i in range(3)]


def bounds(points):
    return [[min(p[i] for p in points), max(p[i] for p in points)] for i in range(3)]


def hull(points):
    points = sorted(set(tuple(p) for p in points))
    if len(points) <= 2:
        return points
    def cross(o, a, b):
        return (a[0]-o[0])*(b[1]-o[1])-(a[1]-o[1])*(b[0]-o[0])
    lo, hi = [], []
    for p in points:
        while len(lo) > 1 and cross(lo[-2], lo[-1], p) <= 0:
            lo.pop()
        lo.append(p)
    for p in reversed(points):
        while len(hi) > 1 and cross(hi[-2], hi[-1], p) <= 0:
            hi.pop()
        hi.append(p)
    return lo[:-1] + hi[:-1]


def write_csv(name, rows):
    with (OUT/name).open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def export(stem):
    data = json.loads((OUT/(stem+"-structure.json")).read_text("utf-8"))
    env = UnityPy.load(data["source"])
    objs = {o.path_id: o for o in env.objects}
    tree = {i: o.read_typetree() for i, o in objs.items() if o.type.name in {"Transform", "RectTransform", "GameObject"}}
    def pid(ref): return ref.get("m_PathID", 0)
    transforms = {pid(d.get("m_GameObject", {})): i for i, d in tree.items() if "m_Father" in d}
    world_cache = {}
    def world(i):
        if not i: return identity()
        if i in world_cache: return world_cache[i]
        d = tree[i]
        x,y,z,w = (d["m_LocalRotation"][k] for k in "xyzw")
        m = identity()
        rot = [[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)], [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)], [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]]
        scale = [d["m_LocalScale"][k] for k in "xyz"]
        for row in range(3):
            for col in range(3): m[row][col] = rot[row][col]*scale[col]
            m[row][3] = d["m_LocalPosition"]["xyz"[row]]
        world_cache[i] = multiply(world(pid(d["m_Father"])), m)
        return world_cache[i]
    root_id = next(t["path_id"] for t in data["transforms"] if t["path"] == data["root"])
    root_inverse = inverse(world(root_id))
    def root_matrix(go): return multiply(root_inverse, world(transforms[go]))
    renderers = {r["path_id"]:r for r in data["renderers"]}
    part_rows = []
    for part in sorted(data["parts"], key=lambda p:p["path"]):
        d = part["data"]
        refs = d["damageMaterial"]["renderers"]
        meshes = [renderers.get(pid(p), {}).get("mesh") for p in refs]
        part_rows.append({"path":part["path"],"mass":d["mass"],"structuralThreshold":d["structuralThreshold"],"integrityThreshold":d["integrityThreshold"],"joints":len(d.get("joints",[])),"damageRendererCount":len(refs),"nonNullDamageMeshes":sum(m is not None for m in meshes),"damageRendererPaths":" | ".join(p["path"] for p in part["damage_renderers"] if p and "path" in p),"disintegrationEffects":len(d["disintegrationEffects"])})
    write_csv(stem+"-parts.csv", part_rows)

    collider_rows, geometry = [], []
    for c in sorted(data["colliders"], key=lambda p:p["path"]):
        d=c["data"];matrix=root_matrix(pid(d["m_GameObject"]))
        mesh=c["mesh"] or {}; points=None
        if d.get("m_Mesh",{}).get("m_FileID",0)==0 and pid(d.get("m_Mesh",{})) in objs:
            handler=MeshHandler(objs[pid(d["m_Mesh"])].read());handler.process()
            points=[transform_point(matrix,p) for p in handler.m_Vertices]
            geometry.append({"path":c["path"],"points":points})
        collider_rows.append({"path":c["path"],"type":c["type"],"convex":d.get("m_Convex",""),"trigger":d.get("m_IsTrigger"),"partOnSameObject":" | ".join(c["same_object_parts"]),"mesh":mesh.get("name",str(mesh)),"vertices":mesh.get("vertices",""),"rootX":round(matrix[0][3],5),"rootY":round(matrix[1][3],5),"rootZ":round(matrix[2][3],5),"rootBounds":json.dumps([[round(v,5) for v in pair] for pair in bounds(points)]) if points is not None else "external mesh; bounds not read"})
    write_csv(stem+"-colliders.csv",collider_rows)

    manifest=json.loads(Path(data["manifests"][0]).read_text("utf-8"))
    patched_options={}
    shader_patches=[]
    for patch in manifest["Patches"]:
        for loc in patch["PatchLocations"]:
            m=re.fullmatch(r"hardpointSets\[(\d+)\]\.weaponOptions\[(\d+)\]",loc.get("memberPath",""))
            if m and loc.get("asset",{}).get("name")==data["root"]:
                patched_options[tuple(map(int,m.groups()))]=patch["GameAsset"]["asset"]["name"]
            if loc.get("memberPath")=="shader":
                shader_patches.append({"material":loc["asset"]["name"],"gameShader":patch["GameAsset"]["asset"]["name"]})
    hardpoint_rows,hardpoint_positions=[],[]
    for wm in data["weapon_managers"]:
        for si,s in enumerate(wm["sets"]):
            options=[]
            for oi,o in enumerate(s["resolved_options"]):
                if o is None: options.append("None")
                elif (si,oi) in patched_options: options.append(patched_options[si,oi])
                else: options.append(o.get("name",str(o)))
            for hi,h in enumerate(s["resolved_hardpoints"]):
                hd=h["resolved"];matrix=multiply(root_inverse,world(pid(h["transform"])));pos=[matrix[i][3] for i in range(3)]
                hardpoint_positions.append({"set":si,"hardpoint":hi,"name":s["name"],"position":pos})
                hardpoint_rows.append({"setIndex":si,"set":s["name"],"hardpointIndexInSet":hi,"transform":hd["transform"],"part":hd["part"]["path"],"rootX":round(pos[0],5),"rootY":round(pos[1],5),"rootZ":round(pos[2],5),"bayDoors":" | ".join(v["path"] for v in hd["bayDoors"]),"doorOpenDuration":h["doorOpenDuration"],"pylon":hd["Pylon"]["path"] if hd["Pylon"] else "","plug":hd["Plug"]["path"] if hd["Plug"] else "","precludingSetIndices":str(s["precludingHardpointSets"]),"optionsIncludingNone":" | ".join(options)})
    write_csv(stem+"-hardpoints.csv",hardpoint_rows)
    summary={"source":data["source"],"sha256":data["sha256"],"modVersion":manifest["modVersion"],"gameVersion":manifest["gameVersion"],"shaderPatches":shader_patches,"partCount":len(part_rows),"colliderCount":len(collider_rows),"hardpointCount":len(hardpoint_rows),"hardpointSetCount":len(wm["sets"]),"damageRenderers":sum(p["damageRendererCount"] for p in part_rows),"nonNullDamageMeshes":sum(p["nonNullDamageMeshes"] for p in part_rows),"hardpointPositions":hardpoint_positions}
    (OUT/(stem+"-summary.json")).write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding="utf-8")
    if stem=="fs41":
        (OUT/"fs41-collider-vertices.json").write_text(json.dumps(geometry,indent=2),encoding="utf-8")
        draw_layout(geometry,hardpoint_positions)
        draw_damage_textures(data,objs)
    print(stem,"parts",len(part_rows),"damage meshes",summary["nonNullDamageMeshes"],"hardpoints",len(hardpoint_rows),"shaders",shader_patches)


def draw_layout(geometry,hardpoints):
    image=Image.new("RGB",(1800,1200),"#0d1726");draw=ImageDraw.Draw(image,"RGBA")
    font_path="C:/Windows/Fonts/segoeui.ttf"
    title=ImageFont.truetype(font_path,30);label=ImageFont.truetype(font_path,20);small=ImageFont.truetype(font_path,16)
    draw.text((40,25),"FS-41 Eclipse 1.1.8 | Authored collision meshes",font=title,fill="white")
    draw.text((40,70),"35 convex MeshColliders / 1,497 source vertices | Yellow dots: 11 hardpoint anchors",font=label,fill="#b7cbe1")
    allpoints=[p for g in geometry for p in g["points"]]
    all_bounds=bounds(allpoints)
    palette=["#55b7ff","#a6da95","#f5a97f","#c6a0f6","#ee99a0","#8bd5ca","#eed49f"]
    panels=[(30,125,870,1110,0,2,"TOP | X / Z"),(910,125,1770,615,2,1,"SIDE | Z / Y"),(910,650,1770,1110,0,1,"FRONT | X / Y")]
    for left,top,right,bottom,ix,iy,name in panels:
        draw.rounded_rectangle((left,top,right,bottom),radius=15,fill="#142238",outline="#28415e",width=2)
        draw.text((left+20,top+15),name,font=label,fill="white")
        extent=[all_bounds[i][1]-all_bounds[i][0] for i in [ix,iy]]
        scale=min((right-left-100)/extent[0],(bottom-top-100)/extent[1])
        center=[sum(all_bounds[i])/2 for i in [ix,iy]]
        def project(p): return ((left+right)/2+(p[ix]-center[0])*scale,(top+bottom)/2+20-(p[iy]-center[1])*scale)
        for n,g in enumerate(geometry):
            polygon=hull([project(p) for p in g["points"]]);color=palette[n%len(palette)]
            if len(polygon)>2:
                draw.polygon(polygon,fill=color+"55",outline=color)
        for hp in hardpoints:
            x,y=project(hp["position"]);draw.ellipse((x-5,y-5,x+5,y+5),fill="#ffdb63",outline="#0d1726",width=1)
            if ix==0 and iy==2: draw.text((x+7,y-13),str(hp["set"]),font=small,fill="#ffebaa")
    draw.text((40,1140),"Offline projection of source vertices; colors distinguish parts. No Unity physics cooking or game test was performed.",font=small,fill="#b7cbe1")
    image.save(OUT/"fs41_collision_layout.png")


def draw_damage_textures(data,objects):
    material=next(m["data"] for m in data["materials"] if m["data"].get("m_Name")=="Aryx_Interceptor1_Skin")
    textures=dict(material["m_SavedProperties"]["m_TexEnvs"])
    image=Image.new("RGB",(1240,1500),"#0d1726");draw=ImageDraw.Draw(image)
    font=ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf",26)
    label=ImageFont.truetype("C:/Windows/Fonts/segoeui.ttf",18)
    draw.text((30,20),"FS-41 | Native AircraftSkin texture inputs",font=font,fill="white")
    metadata=[]
    for i,key in enumerate(["_Basecolor","_BasecolorDmg","_Normal","_NormalDmg"]):
        tex=objects[textures[key]["m_Texture"]["m_PathID"]].read()
        rgba=tex.image.convert("RGBA")
        metadata.append({"property":key,"name":tex.m_Name,"width":tex.m_Width,"height":tex.m_Height,"format":int(tex.m_TextureFormat),"alphaRange":rgba.getchannel("A").getextrema()})
        rgba.thumbnail((570,570),Image.Resampling.LANCZOS)
        x=30+(i%2)*610;y=110+(i//2)*700
        checker=Image.new("RGBA",(570,570),"#7a8491");cd=ImageDraw.Draw(checker)
        for cy in range(0,570,20):
            for cx in range(0,570,20):
                if (cx//20+cy//20)%2:cd.rectangle((cx,cy,cx+19,cy+19),fill="#a4acb5")
        checker.alpha_composite(rgba,((570-rgba.width)//2,(570-rgba.height)//2))
        image.paste(checker.convert("RGB"),(x,y))
        draw.text((x,y-45),key,font=font,fill="white")
        draw.text((x,y+578),tex.m_Name[:55],font=label,fill="#b7cbe1")
    draw.text((30,1450),"Stored texture channels; checker shows alpha. Packed normals are shown without shader decoding.",font=label,fill="#b7cbe1")
    image.save(OUT/"fs41_damage_texture_reference.png")
    (OUT/"fs41-damage-texture-metadata.json").write_text(json.dumps(metadata,indent=2),encoding="utf-8")


if __name__=="__main__":
    export("fs41")
    export("su57-0.2.26")
