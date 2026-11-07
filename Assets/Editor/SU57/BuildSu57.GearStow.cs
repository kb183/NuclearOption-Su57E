using System;
using UnityEditor;
using UnityEngine;

public static partial class BuildSu57
{
    const float MainGearFoldDegrees = -90f;
    // 2.8 s lift with 0.15 m inward travel, followed by 1.8 s for the remainder.
    // The native coroutine owns the stroke/doors; the plugin remaps the pose.
    const float MainGearTravelSeconds = 2.8f+1.8f;
    // Start the lift 0.20 m ahead of the inherited hip, keeping the deployed
    // assembly still. At -90 deg this lowers the raised tyre by 0.20 m; moving
    // inward alone would still leave its top above the thin wing at full lift.
    const float MainGearPivotForward = .20f;
    // Shorten only the inherited upper mounting housing above the hinge.
    // Its original 0.322 m top breaks through the thinner Su-57 dorsal skin.
    const float MainGearUpperHousingScale = .20f;
    // The reference shows the main wheel going straight forward/up with the
    // leg. Do not yaw the lower strut or sweep it around an eccentric pivot.
    const float MainGearWheelTurn = 0f;
    // Aircraft-local tyre MESH centre behind the inboard lip of the main bay
    // marked by the user. Positive X is mirrored for the left gear. The native
    // wheel transform is offset from its mesh centre by about 0.33 m when scaled,
    // so a fixed inward travel measured at that transform misses the pocket.
    static readonly Vector3 MainGearStowedWheelCenter = new Vector3(1.40f,-.34f,-1.25f);

    static void ConfigureMainGearStow(Transform root,LandingGear gear)
    {
        var so=new SerializedObject(gear);
        var hinge=so.FindProperty("gearHinge").objectReferenceValue as Transform;
        var unsprung=so.FindProperty("unsprung").objectReferenceValue as GameObject;
        if(hinge==null || hinge.parent==null || hinge.parent.parent==null || unsprung==null)
            throw new Exception("Incomplete main-gear stow references: "+gear.name);

        // Keep the KR-67 mount's cant and right-hand negative-X mirror below
        // a new, unmirrored driving frame. Native LandingGear animates local X;
        // this frame makes that the aircraft's lateral X axis on both sides.
        // Advance its origin from the deployed hinge. Reparent the non-physical
        // mount with worldPositionStays so the deployed gear,
        // bump stop, wheel contacts and brace origin retain their world pose.
        var mount=hinge.parent;
        var bay=mount.parent;
        if(mount.GetComponentsInChildren<AeroPart>(true).Length!=0)
            throw new Exception("Refusing to reparent a physical main-gear part: "+mount.name);
        // Dedicated marker lets the companion apply staged translation only
        // to these two gears, without changing the front gear or old bundles.
        var fold=Child(bay,"SU57_StagedMainFold_"+mount.name);
        fold.position=hinge.position+root.forward*MainGearPivotForward;
        fold.rotation=root.rotation;
        fold.localScale=Vector3.one;
        mount.SetParent(fold,true);

        so.FindProperty("gearHinge").objectReferenceValue=fold;
        so.FindProperty("foldDegrees").floatValue=MainGearFoldDegrees;
        so.FindProperty("foldSpeed").floatValue=Mathf.Abs(MainGearFoldDegrees)/MainGearTravelSeconds;
        // Keep the native unsprung hierarchy and suspension updates. With no
        // lower-strut swivel, the wheel follows the upper folding frame;
        // it no longer turns 180 degrees on its way into/out of the bay.
        so.FindProperty("strutRotationTransform").objectReferenceValue=unsprung.transform;
        so.FindProperty("strutRotation").floatValue=MainGearWheelTurn;
        var target=MainGearStowedWheelCenter;
        target.x*=root.InverseTransformPoint(fold.position).x<0?-1f:1f;
        // PositionLandingGear has already set native zero-compression geometry.
        // Lay out the native folded endpoint, without turning the lower strut,
        // and measure the actual tyre bounds centre. Restore the deployed pose
        // before saving so the ground stance and suspension references stay put.
        var baseFoldRotation=fold.localRotation;
        var baseFoldAngles=fold.localEulerAngles;
        var baseStrutRotation=unsprung.transform.localRotation;
        Vector3 foldedCenter;
        try
        {
            fold.localEulerAngles=baseFoldAngles+new Vector3(MainGearFoldDegrees,0,0);
            unsprung.transform.localEulerAngles=Vector3.zero;
            foldedCenter=root.InverseTransformPoint(MainGearTireMeshCenter(so,unsprung.transform));
        }
        finally
        {
            fold.localRotation=baseFoldRotation;
            unsprung.transform.localRotation=baseStrutRotation;
        }
        var stowMotion=target-foldedCenter;
        // Save the same accepted endpoint. The companion replaces native
        // linear pose before UpdateMovingParts: lift for 2.8 s with exactly
        // 0.15 m inward travel, then apply only the remaining pocket offset.
        // Convert into the hinge PARENT's space above the mirrored right mount.
        so.FindProperty("hingeFoldMotion").vector3Value=bay.InverseTransformVector(root.TransformVector(stowMotion));
        so.ApplyModifiedPropertiesWithoutUndo();
        Notes.Add(gear.name+": main fold origin advanced 0.20 m along aircraft Z with deployed mount pose retained; raised-only wheel is 0.20 m lower before inward travel; endpoint motion recalculated from the same accepted tyre centre");
        Notes.Add(gear.name+": deployed hinge="+root.InverseTransformPoint(fold.position).ToString("F4")+", fold="+MainGearFoldDegrees.ToString("F0",CI)+" deg about aircraft lateral X (forward/up), lower-strut turn="+MainGearWheelTurn.ToString("F0",CI)+" deg; folded tyre mesh centre before motion="+foldedCenter.ToString("F4")+", main-bay target="+target.ToString("F4")+", total aircraft-local motion="+stowMotion.ToString("F4")+" m; companion lifts for 2.8 s while moving 0.15 m inward, then holds the raised angle and applies only the remaining pocket offset over 1.8 s; final tyre target retained; no eccentric carrier or suspension proxy; user acceptance pending");
    }

    static void ConfigureMainGearUpperHousing(LandingGear gear)
    {
        var filter=gear.GetComponent<MeshFilter>();
        if(filter==null || filter.sharedMesh==null || filter.sharedMesh.bounds.max.y<=0)
            throw new Exception("Missing main-gear upper housing mesh: "+gear.name);
        // Edit a private visual mesh, preserving the hinge, the load-bearing
        // lower section, children, colliders, suspension and contact points.
        // Compressing the closed housing keeps its topology watertight.
        var mesh=UnityEngine.Object.Instantiate(filter.sharedMesh);
        mesh.name="SU57_UpperHousing_"+gear.name;
        var vertices=mesh.vertices;
        for(int i=0;i<vertices.Length;i++)
            if(vertices[i].y>0)vertices[i].y*=MainGearUpperHousingScale;
        mesh.vertices=vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        if(mesh.uv.Length==vertices.Length)mesh.RecalculateTangents();
        filter.sharedMesh=SaveMesh(mesh,Root+"/"+mesh.name+".asset");
        Notes.Add(gear.name+": visual upper housing above local Y=0 compressed to 20% height; private mesh replaces the inherited dorsal protrusion; lower strut geometry, hinge, suspension and contact points retained; user acceptance pending");
    }

    static Vector3 MainGearTireMeshCenter(SerializedObject so,Transform lowerStrut)
    {
        var wheels=so.FindProperty("wheels");
        if(wheels.arraySize==0)throw new Exception("Main gear has no wheels: "+lowerStrut.name);
        var center=Vector3.zero;
        for(int i=0;i<wheels.arraySize;i++)
        {
            var wheel=wheels.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
            var filter=wheel!=null?wheel.GetComponent<MeshFilter>():null;
            if(filter==null || filter.sharedMesh==null || !wheel.IsChildOf(lowerStrut))
                throw new Exception("Missing main-wheel mesh for pocket placement: "+lowerStrut.name);
            center+=wheel.TransformPoint(filter.sharedMesh.bounds.center);
        }
        return center/wheels.arraySize;
    }
}
