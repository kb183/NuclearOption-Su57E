using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class BuildSu57
{
    [Serializable] class DamageSectionDefinition
    {
        public string name, partPath, bone, material;
    }
    [Serializable] class DamageSectionMesh
    {
        public string name;
        public Vector3[] vertices, normals;
        public Vector2[] uv;
        public int[] triangles;
    }
    [Serializable] class DamageSectionLod
    {
        public string meshFile, sourceSha256;
        public int sourceVertexCount, sourceTriangleCount;
        public DamageSectionMesh[] meshes;
    }
    [Serializable] class DamageSectionData
    {
        public int schemaVersion;
        public DamageSectionDefinition[] sections;
        public DamageSectionLod[] lods;
    }
    [Serializable] class CollisionSection
    {
        public string name, partPath;
        public Vector3[] vertices;
        public int[] triangles;
    }
    [Serializable] class CollisionSectionData
    {
        public int schemaVersion;
        public CollisionSection[] shells;
    }

    static Renderer[] CreateDamageGeometry(Transform aircraft, Transform model,
        WingRig wings, Transform nozzleLeft, Transform nozzleRight, Material paint, Material glass)
    {
        var data=JsonUtility.FromJson<DamageSectionData>(File.ReadAllText(Root+"/Source/SU57_DamageSections.json"));
        if(data==null || data.schemaVersion!=1 || data.sections==null || data.sections.Length!=37 ||
            data.lods==null || data.lods.Length!=3)
            throw new Exception("Missing Su-57 damage-section source; run Tools/prepare_su57_damage_sections.py");
        foreach(var lod in data.lods)
        {
            using(var sha=System.Security.Cryptography.SHA256.Create())
            {
                var actual=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Root+"/Source/"+lod.meshFile))).Replace("-","").ToLowerInvariant();
                if(actual!=lod.sourceSha256)throw new Exception("Damage geometry source changed: "+lod.meshFile);
            }
        }
        var all=new List<Renderer>();
        var caps=JsonUtility.FromJson<DamageSectionData>(File.ReadAllText(Root+"/Source/SU57_FractureCaps.json"));
        if(caps==null || caps.schemaVersion!=1 || caps.lods==null || caps.lods.Length!=3)
            throw new Exception("Missing Su-57 fracture interiors");
        var fractureMaterial=CreateFractureMaterial();
        foreach(var section in data.sections)
        {
            var part=DamagePart(aircraft,section.partPath);
            var parent=DamageSectionParent(section,part,wings,nozzleLeft,nozzleRight);
            var holder=Child(parent,"SU57_Section_"+section.name);
            var matrix=holder.worldToLocalMatrix*model.localToWorldMatrix;
            var normalMatrix=matrix.inverse.transpose;
            var levels=new LOD[3];
            for(int lod=0;lod<3;lod++)
            {
                var source=data.lods[lod].meshes.Single(m=>m.name==section.name);
                if(source.vertices==null || source.vertices.Length<3 || source.normals==null ||
                    source.normals.Length!=source.vertices.Length || source.uv==null ||
                    source.uv.Length!=source.vertices.Length || source.triangles==null ||
                    source.triangles.Length==0 || source.triangles.Length%3!=0 ||
                    source.triangles.Any(i=>i<0 || i>=source.vertices.Length))
                    throw new Exception("Invalid damage geometry: "+section.name+" LOD"+lod);
                string name="SU57_"+section.name+"_LOD"+lod;
                var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};
                mesh.vertices=source.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
                mesh.normals=source.normals.Select(n=>normalMatrix.MultiplyVector(n).normalized).ToArray();
                mesh.uv=source.uv;
                mesh.triangles=source.triangles;
                mesh.RecalculateBounds();mesh.RecalculateTangents();
                mesh=SaveMesh(mesh,Root+"/"+name+".asset");
                var cap=caps.lods[lod].meshes.FirstOrDefault(m=>m.name==section.name);
                var renderer=MeshObject(holder,name,mesh,new[]{section.material=="glass"?glass:paint});
                var renderers=new List<Renderer>{renderer};
                all.Add(renderer);
                if(cap!=null)
                {
                    // Fracture faces/edge strips must not appear on intact
                    // interfaces. Keep them separate from the painted shell;
                    // the embedded plugin reveals them after separation.
                    string capName="SU57_Fracture_"+section.name+"_LOD"+lod;
                    var capMesh=new Mesh{name=capName,indexFormat=IndexFormat.UInt32};
                    capMesh.vertices=cap.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
                    capMesh.normals=cap.normals.Select(n=>normalMatrix.MultiplyVector(n).normalized).ToArray();
                    capMesh.uv=cap.uv;capMesh.triangles=cap.triangles;
                    capMesh.RecalculateBounds();capMesh.RecalculateTangents();
                    capMesh=SaveMesh(capMesh,Root+"/"+capName+".asset");
                    var capRenderer=MeshObject(holder,capName,capMesh,new[]{fractureMaterial});
                    capRenderer.gameObject.SetActive(false);
                    renderers.Add(capRenderer);all.Add(capRenderer);
                }
                levels[lod]=new LOD(new[]{.35f,.12f,.015f}[lod],renderers.ToArray());
            }
            // A detached part must keep its own LOD owner. An aircraft-root
            // group would otherwise continue controlling distant wreckage.
            var group=holder.gameObject.AddComponent<LODGroup>();
            group.SetLODs(levels);group.RecalculateBounds();group.size=19.51f;
            Notes.Add("Damage section "+section.name+": native part="+(section.partPath==""?"<root>":section.partPath)+
                ", motion="+(string.IsNullOrEmpty(section.bone)?"rigid":section.bone)+", three local LODs");
        }
        Notes.Add("Damage geometry: 37 sections / 111 exterior LOD renderers plus separate, initially inactive fracture renderers. The single-DLL runtime shows fracture interiors only after a neighbouring structural owner separates. Intact interfaces retain only the original painted surface. Engine outlets/glass stay open.");
        return all.ToArray();
    }

    static AeroPart DamagePart(Transform root,string path)
    {
        var transform=string.IsNullOrEmpty(path)?root:root.Find(path);
        var part=transform!=null?transform.GetComponent<AeroPart>():null;
        if(part==null)throw new Exception("Missing native Su-57 damage part: "+path);
        return part;
    }

    static Transform DamageSectionParent(DamageSectionDefinition section,AeroPart part,
        WingRig wings,Transform nozzleLeft,Transform nozzleRight)
    {
        if(string.IsNullOrEmpty(section.bone))return part.transform;
        if(section.bone=="nozzle_L")return nozzleLeft;
        if(section.bone=="nozzle_R")return nozzleRight;
        int index=Array.IndexOf(wings.names,section.bone);
        if(index<0)throw new Exception("Missing damage motion bone: "+section.bone);
        var bone=wings.bones[index];
        if(bone.GetComponentInParent<AeroPart>()==part)return bone;
        if(!section.name.StartsWith("LeadingOuter_",StringComparison.Ordinal))
            throw new Exception("Damage motion belongs to another part: "+section.name);
        // The original long leading-edge panel is cut into the two native
        // structural slots. Give its outer half the same visual flap motion
        // below its OWN physical part, avoiding cross-part skin stretching.
        var axis=Child(part.transform,"SU57_"+section.name+"_Axis");
        axis.position=bone.parent.position;axis.rotation=bone.parent.rotation;
        return WingHinge(axis,"LandingSlat",part,15,0,0,true,25);
    }

    static void ConfigureDamageColliders(Transform aircraft,Transform model)
    {
        var data=JsonUtility.FromJson<CollisionSectionData>(File.ReadAllText(Root+"/Source/SU57_CollisionSections.json"));
        var parts=aircraft.GetComponentsInChildren<AeroPart>(true);
        if(data==null || data.schemaVersion!=1 || data.shells==null || data.shells.Length!=parts.Length)
            throw new Exception("Expected one authored Su-57 collision shell per native AeroPart");
        var visited=new HashSet<AeroPart>();
        foreach(var shell in data.shells)
        {
            var part=DamagePart(aircraft,shell.partPath);
            var collider=part.GetComponent<MeshCollider>();
            if(!visited.Add(part) || collider==null || shell.vertices==null || shell.vertices.Length<4 ||
                shell.triangles==null || shell.triangles.Length==0 || shell.triangles.Length%3!=0 ||
                shell.triangles.Length/3>255 || shell.triangles.Any(i=>i<0 || i>=shell.vertices.Length))
                throw new Exception("Invalid Su-57 collision shell: "+shell.partPath);
            var matrix=part.transform.worldToLocalMatrix*model.localToWorldMatrix;
            var mesh=new Mesh{name=shell.name};
            mesh.vertices=shell.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
            mesh.triangles=shell.triangles;mesh.RecalculateBounds();mesh.RecalculateNormals();
            mesh=SaveMesh(mesh,Root+"/"+shell.name+".asset");
            // Keep the original component/layer/material and physical datum.
            // 4 mm contact offsets fit within opposing 6 mm face insets.
            collider.sharedMesh=null;collider.convex=true;collider.sharedMesh=mesh;
            collider.contactOffset=.004f;
            collider.isTrigger=false;collider.enabled=true;
            Notes.Add("Su-57 collision "+shell.partPath+": "+mesh.vertexCount+" authored hull vertices, "+shell.triangles.Length/3+" faces");
        }
        Notes.Add("Replaced all "+visited.Count+" structural collision meshes with inset Su-57 rest-pose cells (6 mm original face inset, 24 mm overlap splits, 4 mm contact offset). Visible meshes retain source coordinates. Attached own-aircraft structural pairs are filtered by the embedded plugin; detach restores their prior collision state. Unity cooking and game acceptance remain user-owned.");
    }

    static bool HasVisibleDamageMesh(Renderer renderer)
    {
        if(renderer is SkinnedMeshRenderer skinned)return skinned.sharedMesh!=null;
        var filter=renderer.GetComponent<MeshFilter>();
        return renderer is MeshRenderer && filter!=null && filter.sharedMesh!=null;
    }

    static void ConnectDamageRenderers(Transform aircraft)
    {
        var renderers=aircraft.GetComponentsInChildren<Renderer>(true).Where(HasVisibleDamageMesh)
            .Where(r=>r.sharedMaterial!=null &&
                (r.sharedMaterial.name.StartsWith("SU57_Camouflage",StringComparison.Ordinal) ||
                 r.sharedMaterial.name.StartsWith("SU57_Canopy",StringComparison.Ordinal))).ToArray();
        foreach(var part in aircraft.GetComponentsInChildren<AeroPart>(true))
        {
            var owned=renderers.Where(r=>r.GetComponentInParent<AeroPart>()==part).ToArray();
            var serialized=new SerializedObject(part);
            var damage=serialized.FindProperty("damageMaterial");
            var list=damage.FindPropertyRelative("renderers");
            var indices=damage.FindPropertyRelative("indices");
            list.arraySize=owned.Length;indices.arraySize=owned.Length;
            for(int i=0;i<owned.Length;i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue=owned[i];
                indices.GetArrayElementAtIndex(i).intValue=0;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        Notes.Add("Damage renderer lists rebuilt by nearest AeroPart for Su-57 body/glass/door/visible pylon geometry. Paint is slot zero; fracture interiors, instruments and engine VFX are excluded from native skin damage. Per-part wound visuals use native AircraftSkin.");
    }
}
