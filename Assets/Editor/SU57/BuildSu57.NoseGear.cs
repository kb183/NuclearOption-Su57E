using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static partial class BuildSu57
{
    const float NoseBraceSectionReach = .95f;

    static void ConfigureNoseGearGeometry(LandingGear gear)
    {
        var so=new SerializedObject(gear);
        var lower=so.FindProperty("unsprung").objectReferenceValue as GameObject;
        var filter=lower!=null?lower.GetComponent<MeshFilter>():null;
        if(filter==null || filter.sharedMesh==null || filter.sharedMesh.subMeshCount!=2)
            throw new Exception("Missing two-section native nose lower-strut mesh");

        // Submesh 0 is the closed chrome piston, not the fork/axle. The bump
        // stop was extended by 15 cm while its visible rod stayed unchanged.
        // Lengthen only that rod upwards from its original lower end, retaining
        // the fork, tyre, contact ray and native suspension transform positions.
        var mesh=UnityEngine.Object.Instantiate(filter.sharedMesh);
        mesh.name="SU57_NoseLowerStrut";
        var vertices=mesh.vertices;
        var pistonVertices=new HashSet<int>(mesh.GetTriangles(0));
        if(pistonVertices.Count==0)throw new Exception("Empty nose piston geometry");
        float bottom=pistonVertices.Min(i=>vertices[i].y);
        float top=pistonVertices.Max(i=>vertices[i].y);
        if(top-bottom<.01f)throw new Exception("Unexpected nose piston axis");
        float stretch=1f+NoseStrutExtension/(top-bottom);
        foreach(int i in pistonVertices)vertices[i].y=bottom+(vertices[i].y-bottom)*stretch;
        mesh.vertices=vertices;
        mesh.RecalculateNormals();mesh.RecalculateBounds();
        if(mesh.uv.Length==vertices.Length)mesh.RecalculateTangents();
        filter.sharedMesh=SaveMesh(mesh,Root+"/"+mesh.name+".asset");
        Notes.Add("Nose visible piston: private closed mesh extended upwards by .15 m to restore overlap after oleo extension; fork/axle, tyre and native suspension transforms retained; user acceptance pending");
    }

    static Renderer[] ConfigureNoseGearBrace(LandingGear gear)
    {
        var so=new SerializedObject(gear);
        var hinge=so.FindProperty("gearHinge").objectReferenceValue as Transform;
        var arm=gear.transform.Find("gear_arm_F");
        var filter=arm!=null?arm.GetComponent<MeshFilter>():null;
        var renderer=arm!=null?arm.GetComponent<MeshRenderer>():null;
        if(hinge==null || filter==null || filter.sharedMesh==null || renderer==null)
            throw new Exception("Missing native nose drag-brace geometry");
        if(arm.GetComponentsInChildren<AeroPart>(true).Length!=0)
            throw new Exception("Refusing to reparent a physical nose brace");

        var parts=so.FindProperty("movingParts");
        Transform anchor=null;
        for(int i=0;i<parts.arraySize;i++)
        {
            var part=parts.GetArrayElementAtIndex(i);
            if(part.FindPropertyRelative("transform").objectReferenceValue!=arm)continue;
            anchor=part.FindPropertyRelative("target").objectReferenceValue as Transform;
            part.FindPropertyRelative("foldAngles").vector3Value=Vector3.zero;
            break;
        }
        if(anchor==null)throw new Exception("Missing nose drag-brace fixed pin");

        var source=filter.sharedMesh;
        if(source.bounds.max.z<=0)throw new Exception("Unexpected nose brace axis");
        var barrel=UnityEngine.Object.Instantiate(source);
        barrel.name="SU57_NoseBraceBarrel";
        var vertices=barrel.vertices;
        float scale=NoseBraceSectionReach/source.bounds.max.z;
        for(int i=0;i<vertices.Length;i++)vertices[i].z*=scale;
        barrel.vertices=vertices;
        barrel.RecalculateNormals();barrel.RecalculateBounds();
        if(barrel.uv.Length==vertices.Length)barrel.RecalculateTangents();
        filter.sharedMesh=SaveMesh(barrel,Root+"/"+barrel.name+".asset");
        arm.SetParent(hinge,true);
        arm.localScale=Vector3.one;
        arm.LookAt(anchor);

        var piston=Child(anchor,"SU57_NoseBracePiston");
        piston.LookAt(arm);
        var points=new List<Vector3>();
        var triangles=new List<int>();
        AddBraceCylinder(points,triangles,.027f,.035f,NoseBraceSectionReach);
        AddBraceCylinder(points,triangles,.055f,0,.055f);
        var pistonMesh=new Mesh{name="SU57_NoseBracePiston"};
        pistonMesh.SetVertices(points);pistonMesh.SetTriangles(triangles,0);
        pistonMesh.RecalculateNormals();pistonMesh.RecalculateBounds();
        pistonMesh=SaveMesh(pistonMesh,Root+"/"+pistonMesh.name+".asset");
        var steel=CreateGearMaterial("SU57_GearSteel",new Color(.55f,.58f,.61f),.2f,.25f,true);
        var pistonRenderer=MeshObject(piston,"SU57_NoseBracePistonMesh",pistonMesh,new[]{steel});

        int index=parts.arraySize;
        parts.arraySize=index+1;
        var added=parts.GetArrayElementAtIndex(index);
        added.FindPropertyRelative("transform").objectReferenceValue=piston;
        added.FindPropertyRelative("target").objectReferenceValue=arm;
        added.FindPropertyRelative("foldAngles").vector3Value=Vector3.zero;
        so.ApplyModifiedPropertiesWithoutUndo();
        // The runtime only limits the two brace lengths, before native LookAt.
        // The nose keeps its native fold, steering, suspension and stroke clock.
        hinge.name="SU57_NoseFold_gearHinge_F";
        Notes.Add("Nose drag brace: replaced fixed 1.67 m arm with opposed .95 m maximum sections; plugin limits each to 65% of current pin span, joining the deployed pins without overshooting the stowed fixed pin; both renderers included in exterior/material lists; user acceptance pending");
        return new Renderer[]{renderer,pistonRenderer};
    }
}
