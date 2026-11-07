using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class BuildSu57
{
    static void ConfigureCircularFlame(SerializedProperty burner,Transform thrust,string side,int slot)
    {
        var renderer=burner.FindPropertyRelative("flameRenderer").objectReferenceValue as Renderer;
        var filter=renderer!=null?renderer.GetComponent<MeshFilter>():null;
        if(filter==null)throw new Exception("Missing Su-57 afterburner mesh filter: "+side);
        var source=AssetDatabase.LoadAssetAtPath<Mesh>(Game+"/Mesh/fighter1_afterburner_flame_PLACEHOLDER.asset");
        if(source==null)throw new Exception("Missing native FS-12 circular afterburner mesh");

        // FS-12 and KR-67 already use the same native Afterburner material.
        // Clone the round FS-12 geometry with all UV/color channels intact.
        // Bake size/offset into the mesh: Afterburner.Run resets transform scale
        // to (1,1,1..1.05) every frame, so transform-only fitting will not hold.
        var mesh=Object.Instantiate(source);
        string suffix=side+(slot==0?"":"_"+slot);
        mesh.name="SU57_Flame_"+suffix;
        var vertices=mesh.vertices;
        var bounds=source.bounds;
        var centre=bounds.center;
        float inlet=bounds.max.z;
        float inletRadius=0;
        foreach(var vertex in vertices)
        {
            if(vertex.z<inlet-.025f)continue;
            inletRadius=Mathf.Max(inletRadius,new Vector2(vertex.x-centre.x,vertex.y-centre.y).magnitude);
        }
        if(inletRadius<.01f || bounds.size.z<.01f)
        {
            Object.DestroyImmediate(mesh);
            throw new Exception("Invalid native FS-12 afterburner geometry");
        }
        const float radius=.455f;
        const float length=4.75f; // Previous KR-67 mesh length after its .7 bake.
        const float lipInset=.025f;
        float radialScale=radius/inletRadius;
        float axialScale=length/bounds.size.z;
        for(int i=0;i<vertices.Length;i++)
        {
            var v=vertices[i];
            vertices[i]=new Vector3((v.x-centre.x)*radialScale,(v.y-centre.y)*radialScale,(v.z-inlet)*axialScale+lipInset);
        }
        mesh.vertices=vertices;
        // Preserve the source's smoothed normals under the baked affine fit.
        var normals=mesh.normals;
        for(int i=0;i<normals.Length;i++)
            normals[i]=new Vector3(normals[i].x/radialScale,normals[i].y/radialScale,normals[i].z/axialScale).normalized;
        mesh.normals=normals;
        mesh.RecalculateBounds();mesh.RecalculateTangents();
        filter.sharedMesh=SaveMesh(mesh,Root+"/"+mesh.name+".asset");
        if(renderer.transform!=thrust)
        {
            renderer.transform.SetParent(thrust,false);
            renderer.transform.localPosition=Vector3.zero;
            renderer.transform.localRotation=Quaternion.identity;
        }
        renderer.transform.localScale=Vector3.one;
        Notes.Add("Afterburner "+suffix+": native FS-12 round flame mesh, 32-segment inlet; radius=.455 m, length=4.75 m, lip inset=.025 m; original UV/color channels and native material retained; follows the common vectoring pivot; native thrust/fuel/IR unchanged.");
    }
}
