using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class BuildSu57
{
    [Serializable] class Su57WeaponAnchor
    {
        public int setIndex,index;
        public string name,partPath;
        public Vector3 position,euler;
        public float supportY;
        public string[] doors;
    }
    [Serializable] class Su57WeaponDoor
    {
        public string name,partPath;
        public Vector3 position,euler;
        public float hingeAngle,openSpeed,closeSpeed;
    }
    [Serializable] class Su57WeaponLayout
    {
        public int schemaVersion,reservedDisabledSet;
        public Su57WeaponAnchor[] anchors;
        public Su57WeaponDoor[] doors;
    }

    static Renderer[] ConfigureSu57Weapons(Transform aircraft,Transform model,Material paint,AircraftParameters parameters)
    {
        var layout=JsonUtility.FromJson<Su57WeaponLayout>(File.ReadAllText(Root+"/Source/SU57_Hardpoints.json"));
        var geometry=JsonUtility.FromJson<DamageSectionData>(File.ReadAllText(Root+"/Source/SU57_WeaponDoors.json"));
        var sideGeometry=JsonUtility.FromJson<DamageSectionData>(File.ReadAllText(Root+"/Source/SU57_SideBays.json"));
        if(layout==null || layout.schemaVersion!=1 || layout.anchors==null || layout.anchors.Length!=9 ||
            layout.doors==null || layout.doors.Length!=6 || geometry==null || geometry.lods.Length!=3 ||
            sideGeometry==null || sideGeometry.schemaVersion!=1 || sideGeometry.sections==null || sideGeometry.lods.Length!=3)
            throw new Exception("Missing Su-57 weapon authoring inputs");
        var manager=aircraft.GetComponentInChildren<WeaponManager>(true);
        if(manager.hardpointSets.Length!=7)throw new Exception("Expected original seven hardpoint set indices");
        var all=new List<Renderer>();
        var doors=new Dictionary<string,BayDoor>();
        var hardware=CreateWeaponHardwareMaterial();
        foreach(var rear in new[]{false,true})
        {
            var parent=Child(DamagePart(aircraft,rear?"fuselage_R":"fuselage_F").transform,
                rear?"SU57_RearBayInterior":"SU57_FrontBayInterior");
            parent.position=aircraft.TransformPoint(new Vector3(0,rear?-.33f:-.55f,rear?-5.635f:-.257f));
            parent.rotation=aircraft.rotation;
            float depth=rear?.70f:.65f;float length=rear?4.96f:4.60f;
            var interior=CreateWeaponHardware(parent,parent.name,hardware,
                new[]{new Vector3(0,depth,0),new Vector3(-.51f,depth*.5f,0),new Vector3(.51f,depth*.5f,0),
                    new Vector3(0,depth*.5f,-length*.5f),new Vector3(0,depth*.5f,length*.5f)},
                new[]{new Vector3(1.02f,.018f,length),new Vector3(.018f,depth,length),new Vector3(.018f,depth,length),
                    new Vector3(1.02f,depth,.018f),new Vector3(1.02f,depth,.018f)});
            all.Add(interior);
        }
        foreach(var entry in layout.doors)
        {
            var part=DamagePart(aircraft,entry.partPath);
            var axis=Child(part.transform,"SU57_BayAxis_"+entry.name);
            axis.position=aircraft.TransformPoint(entry.position);
            axis.rotation=aircraft.rotation*Quaternion.Euler(entry.euler);
            // BayDoor overwrites local Euler Z and assumes its zero is zero.
            // Keep the physical parent's orientation in a separate fixed axis.
            var hinge=Child(axis,"SU57_BayDoor_"+entry.name);
            var door=hinge.gameObject.AddComponent<BayDoor>();
            var serialized=new SerializedObject(door);
            serialized.FindProperty("hingeAngle").floatValue=entry.hingeAngle;
            serialized.FindProperty("openSpeed").floatValue=entry.openSpeed;
            serialized.FindProperty("closeSpeed").floatValue=entry.closeSpeed;
            serialized.ApplyModifiedPropertiesWithoutUndo();doors.Add(entry.name,door);
            var levels=new LOD[3];
            for(int lod=0;lod<3;lod++)
            {
                var source=geometry.lods[lod].meshes.Single(m=>m.name==entry.name);
                var matrix=hinge.worldToLocalMatrix*model.localToWorldMatrix;
                var normalMatrix=matrix.inverse.transpose;
                var mesh=new Mesh{name="SU57_WeaponDoor_"+entry.name+"_LOD"+lod};
                mesh.vertices=source.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
                mesh.normals=source.normals.Select(n=>normalMatrix.MultiplyVector(n).normalized).ToArray();
                mesh.uv=source.uv;mesh.triangles=source.triangles;
                bool sideDoor=entry.name.StartsWith("Side_",StringComparison.Ordinal);
                if(sideDoor)
                {
                    var inner=sideGeometry.lods[lod].meshes.Single(m=>m.name=="SideDoorInner_"+entry.name.Substring(5));
                    var outer=mesh.triangles;int offset=mesh.vertexCount;
                    mesh.vertices=mesh.vertices.Concat(inner.vertices.Select(matrix.MultiplyPoint3x4)).ToArray();
                    mesh.normals=mesh.normals.Concat(inner.normals.Select(n=>normalMatrix.MultiplyVector(n).normalized)).ToArray();
                    mesh.uv=mesh.uv.Concat(inner.uv).ToArray();mesh.subMeshCount=2;
                    mesh.SetTriangles(outer,0);mesh.SetTriangles(inner.triangles.Select(i=>i+offset).ToArray(),1);
                }
                mesh.RecalculateBounds();mesh.RecalculateTangents();
                mesh=SaveMesh(mesh,Root+"/"+mesh.name+".asset");
                var renderer=MeshObject(hinge,mesh.name,mesh,sideDoor?new[]{paint,hardware}:new[]{paint});all.Add(renderer);
                levels[lod]=new LOD(new[]{.35f,.12f,.015f}[lod],new Renderer[]{renderer});
            }
            var group=hinge.gameObject.AddComponent<LODGroup>();
            group.SetLODs(levels);group.RecalculateBounds();group.size=19.51f;
        }
        foreach(var section in sideGeometry.sections.Where(s=>s.name.StartsWith("SideFairing_",StringComparison.Ordinal) ||
            s.name.StartsWith("SideInterior_",StringComparison.Ordinal)))
        {
            var holder=Child(DamagePart(aircraft,section.partPath).transform,"SU57_"+section.name);
            var matrix=holder.worldToLocalMatrix*model.localToWorldMatrix;
            var normalMatrix=matrix.inverse.transpose;var levels=new LOD[3];
            for(int lod=0;lod<3;lod++)
            {
                var source=sideGeometry.lods[lod].meshes.Single(m=>m.name==section.name);
                var mesh=new Mesh{name="SU57_"+section.name+"_LOD"+lod};
                mesh.vertices=source.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
                mesh.normals=source.normals.Select(n=>normalMatrix.MultiplyVector(n).normalized).ToArray();
                mesh.uv=source.uv;mesh.triangles=source.triangles;mesh.RecalculateBounds();mesh.RecalculateTangents();
                mesh=SaveMesh(mesh,Root+"/"+mesh.name+".asset");
                var renderer=MeshObject(holder,mesh.name,mesh,new[]{section.material=="skin"?paint:hardware});
                all.Add(renderer);levels[lod]=new LOD(new[]{.35f,.12f,.015f}[lod],new Renderer[]{renderer});
            }
            var group=holder.gameObject.AddComponent<LODGroup>();group.SetLODs(levels);
            group.RecalculateBounds();group.size=19.51f;
        }

        foreach(var entry in layout.anchors)
        {
            var set=manager.hardpointSets[entry.setIndex];
            if(entry.index>=set.hardpoints.Count)throw new Exception("Missing native hardpoint: "+entry.name);
            var part=DamagePart(aircraft,entry.partPath);
            var anchor=Child(part.transform,"SU57_Hardpoint_"+entry.name);
            anchor.position=aircraft.TransformPoint(entry.position);
            anchor.rotation=aircraft.rotation*Quaternion.Euler(entry.euler);
            // Each side station and its original-skin door belong to the same
            // native wing root. Keep old hidden KR-67 doors out of this chain.
            var managerData=new SerializedObject(manager);
            var setsData=managerData.FindProperty("hardpointSets");
            var hardpointData=setsData.GetArrayElementAtIndex(entry.setIndex)
                .FindPropertyRelative("hardpoints").GetArrayElementAtIndex(entry.index);
            hardpointData.FindPropertyRelative("transform").objectReferenceValue=anchor;
            hardpointData.FindPropertyRelative("part").objectReferenceValue=part;
            var doorList=hardpointData.FindPropertyRelative("bayDoors");doorList.arraySize=entry.doors.Length;
            for(int i=0;i<entry.doors.Length;i++)doorList.GetArrayElementAtIndex(i).objectReferenceValue=doors[entry.doors[i]];
            hardpointData.FindPropertyRelative("doorOpenDuration").floatValue=entry.doors.Length>0?.75f:0f;
            var pylonOptions=hardpointData.FindPropertyRelative("pylonOptions");
            pylonOptions.arraySize=0;
            hardpointData.FindPropertyRelative("Pylon").objectReferenceValue=null;
            hardpointData.FindPropertyRelative("Plug").objectReferenceValue=null;
            if(entry.setIndex>=4)
            {
                float height=Mathf.Max(.12f,entry.supportY-entry.position.y);
                var pylon=CreateWeaponHardware(anchor,"SU57_Pylon_"+entry.name,hardware,
                    new[]{new Vector3(0,height*.5f,0),new Vector3(0,.015f,0)},
                    new[]{new Vector3(.13f,height,.94f),new Vector3(.16f,.07f,1.28f)});
                var plug=CreateWeaponHardware(anchor,"SU57_PylonPlug_"+entry.name,hardware,
                    new[]{new Vector3(0,height,0)},new[]{new Vector3(.16f,.022f,.48f)});
                pylon.enabled=false;plug.enabled=true;
                hardpointData.FindPropertyRelative("Pylon").objectReferenceValue=pylon;
                hardpointData.FindPropertyRelative("Plug").objectReferenceValue=plug;
                pylonOptions.arraySize=1;
                var option=pylonOptions.GetArrayElementAtIndex(0);
                option.FindPropertyRelative("cargo").boolValue=false;
                option.FindPropertyRelative("mount").objectReferenceValue=null;
                option.FindPropertyRelative("renderer").objectReferenceValue=pylon;
                all.Add(pylon);all.Add(plug);
                // Do not put these low-poly renderers in a LODGroup: native
                // Hardpoint.ShowPylon owns their enabled state on load/rearm.
            }
            else if(entry.setIndex>=1)
            {
                bool sideRail=entry.setIndex==3;
                var rail=CreateWeaponHardware(anchor,"SU57_LaunchRail_"+entry.name,hardware,
                    new[]{new Vector3(0,sideRail?-.04f:.08f,0)},
                    new[]{sideRail?new Vector3(.065f,.03f,1.2f):new Vector3(.08f,.08f,1.5f)});
                all.Add(rail);
            }
            managerData.ApplyModifiedPropertiesWithoutUndo();
            Notes.Add("Su-57 station "+entry.name+": root position="+entry.position+", owner="+entry.partPath+", doors="+entry.doors.Length);
        }
        EditorUtility.SetDirty(manager);
        // Keep set 6 as a reserved empty entry, preserving saved group indices.
        var reserved=manager.hardpointSets[layout.reservedDisabledSet];
        reserved.name="Reserved (no tail hook)";
        reserved.weaponOptions.Clear();reserved.weaponOptions.Add(null);
        reserved.hardpoints.Clear();
        var parametersData=new SerializedObject(parameters);
        var loadouts=parametersData.FindProperty("loadouts");
        for(int i=0;i<loadouts.arraySize;i++)
        {
            var weapons=loadouts.GetArrayElementAtIndex(i).FindPropertyRelative("weapons");
            if(weapons.arraySize>layout.reservedDisabledSet)
                weapons.GetArrayElementAtIndex(layout.reservedDisabledSet).objectReferenceValue=null;
        }
        parametersData.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(parameters);
        Notes.Add("Weapons: nine authored stations; six visible native bay doors. Slim side doors are extracted from the existing outer intake shoulder skin with matching positions/normals/UVs and 12 mm inner backing; no hanging pod or added hull volume. Side rails/hinges follow the shoulder direction. Existing group indices retained; slot 6 reserved empty. Store fit, motion and multiplayer acceptance remain user-owned.");
        return all.ToArray();
    }

    static Material CreateWeaponHardwareMaterial()
    {
        string path=Root+"/SU57_WeaponHardware.mat";
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader=Shader.Find("Universal Render Pipeline/Lit");
        if(mat==null){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}
        mat.shader=shader;mat.SetColor("_BaseColor",new Color(.25f,.28f,.30f));
        mat.SetFloat("_Metallic",.65f);mat.SetFloat("_Smoothness",.38f);
        EditorUtility.SetDirty(mat);return mat;
    }

    static MeshRenderer CreateWeaponHardware(Transform parent,string name,Material material,Vector3[] centers,Vector3[] sizes)
    {
        var vertices=new List<Vector3>();var triangles=new List<int>();
        for(int i=0;i<centers.Length;i++)
        {
            // Reuse Unity's cube face normals/UV seams, without retaining a
            // primitive collider. Save a mesh asset, never a procedural runtime mesh.
            var primitive=GameObject.CreatePrimitive(PrimitiveType.Cube);
            var cube=primitive.GetComponent<MeshFilter>().sharedMesh;int offset=vertices.Count;
            vertices.AddRange(cube.vertices.Select(p=>centers[i]+Vector3.Scale(p,sizes[i])));
            triangles.AddRange(cube.triangles.Select(t=>offset+t));Object.DestroyImmediate(primitive);
        }
        var mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);
        mesh.RecalculateNormals();mesh.RecalculateBounds();
        mesh=SaveMesh(mesh,Root+"/"+name+".asset");
        return MeshObject(parent,name,mesh,new[]{material});
    }
}
