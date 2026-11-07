using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class BuildSu57
{
    // The inherited single brace reaches about 1.67 m, past the Su-57 anchor.
    // Two opposed sections overlap as the leg moves instead of overshooting
    // that anchor. Both use native GearPart LookAt; the staged-motion companion
    // shortens their visible reach when the pins approach during the lift.
    const float MainBraceSectionReach = .74f;
    const float MainBracePistonRadius = .027f;

    static Renderer[] ConfigureMainGearBrace(LandingGear gear)
    {
        string side=gear.name=="gear_L_sprung"?"L":gear.name=="gear_R_sprung"?"R":null;
        if(side==null)throw new Exception("Unsupported main-gear brace: "+gear.name);
        var so=new SerializedObject(gear);
        var fold=so.FindProperty("gearHinge").objectReferenceValue as Transform;
        var arm=gear.transform.Find("gear_"+side+"_arm");
        var filter=arm!=null?arm.GetComponent<MeshFilter>():null;
        var armRenderer=arm!=null?arm.GetComponent<MeshRenderer>():null;
        if(fold==null || filter==null || filter.sharedMesh==null || armRenderer==null)
            throw new Exception("Missing native main-gear brace geometry: "+gear.name);
        if(arm.GetComponentsInChildren<AeroPart>(true).Length!=0)
            throw new Exception("Refusing to reparent a physical gear brace: "+arm.name);

        var parts=so.FindProperty("movingParts");
        Transform anchor=null;
        for(int i=0;i<parts.arraySize;i++)
        {
            var part=parts.GetArrayElementAtIndex(i);
            if(part.FindPropertyRelative("transform").objectReferenceValue!=arm)continue;
            anchor=part.FindPropertyRelative("target").objectReferenceValue as Transform;
            // The existing entry continues to point the leg-side barrel at
            // its original body anchor; do not add a second driver for it.
            part.FindPropertyRelative("foldAngles").vector3Value=Vector3.zero;
            break;
        }
        if(anchor==null)throw new Exception("Missing native main-gear brace anchor: "+gear.name);

        var source=filter.sharedMesh;
        if(source.bounds.max.z<=0)throw new Exception("Unexpected main-brace forward axis: "+source.name);
        var barrel=UnityEngine.Object.Instantiate(source);
        barrel.name="SU57_MainBraceBarrel_"+side;
        var vertices=barrel.vertices;
        float scale=MainBraceSectionReach/source.bounds.max.z;
        for(int i=0;i<vertices.Length;i++)vertices[i].z*=scale;
        barrel.vertices=vertices;
        barrel.RecalculateNormals();
        barrel.RecalculateBounds();
        if(barrel.uv.Length==vertices.Length)barrel.RecalculateTangents();
        filter.sharedMesh=SaveMesh(barrel,Root+"/"+barrel.name+".asset");

        // Keep the same moving attachment point, directly below the unmirrored
        // upper drive frame. Remove the right mount's negative-X scale from
        // this LookAt-driven mesh, so its geometric +Z really aims at the anchor.
        // Neither the suspension nor the wheel/axle/contact subtree is moved.
        arm.SetParent(fold,true);
        arm.localScale=Vector3.one;
        arm.LookAt(anchor);

        var piston=Child(anchor,"SU57_MainBracePiston_"+side);
        piston.LookAt(arm);
        var steel=CreateGearMaterial("SU57_GearSteel",new Color(.55f,.58f,.61f),.2f,.25f,true);
        var pistonRenderer=MeshObject(piston,"SU57_MainBracePistonMesh_"+side,
            CreateMainBracePiston(),new[]{steel});

        // The body-side piston points back towards the moving barrel. Fixed
        // sections slide inside one another as the anchor separation changes;
        // the whole old 1.67 m rod no longer extends beyond the body attachment.
        int index=parts.arraySize;
        parts.arraySize=index+1;
        var pistonPart=parts.GetArrayElementAtIndex(index);
        pistonPart.FindPropertyRelative("transform").objectReferenceValue=piston;
        pistonPart.FindPropertyRelative("target").objectReferenceValue=arm;
        pistonPart.FindPropertyRelative("foldAngles").vector3Value=Vector3.zero;
        so.ApplyModifiedPropertiesWithoutUndo();
        Notes.Add(gear.name+": main brace uses two opposed .74 m maximum telescoping sections; staged-motion companion limits each reach to 65% of the current pin span when needed; barrel follows the main fold, piston stays at the original body anchor; both use native GearPart LookAt in unmirrored frames; low-gloss steel retained; wheel target, upper/lower strut, suspension and contact points retained; user acceptance pending");
        return new Renderer[]{armRenderer,pistonRenderer};
    }

    static Mesh CreateMainBracePiston()
    {
        var vertices=new List<Vector3>();
        var triangles=new List<int>();
        AddBraceCylinder(vertices,triangles,MainBracePistonRadius,.035f,MainBraceSectionReach);
        // A short mounting collar joins the piston to the body-side pin.
        AddBraceCylinder(vertices,triangles,.055f,0,.055f);
        var mesh=new Mesh{name="SU57_MainBracePiston"};
        mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);
        mesh.RecalculateNormals();mesh.RecalculateBounds();
        return SaveMesh(mesh,Root+"/"+mesh.name+".asset");
    }

    static void AddBraceCylinder(List<Vector3> vertices,List<int> triangles,float radius,float start,float end)
    {
        const int segments=16;
        int offset=vertices.Count;
        for(int ring=0;ring<2;ring++)
        for(int i=0;i<segments;i++)
        {
            float angle=2*Mathf.PI*i/segments;
            vertices.Add(new Vector3(radius*Mathf.Cos(angle),radius*Mathf.Sin(angle),ring==0?start:end));
        }
        int startCap=vertices.Count;
        vertices.Add(new Vector3(0,0,start));
        int endCap=vertices.Count;
        vertices.Add(new Vector3(0,0,end));
        for(int i=0;i<segments;i++)
        {
            int a=offset+i,b=offset+(i+1)%segments,c=a+segments,d=b+segments;
            triangles.AddRange(new[]{a,b,c,b,d,c,startCap,b,a,endCap,c,d});
        }
    }
}
