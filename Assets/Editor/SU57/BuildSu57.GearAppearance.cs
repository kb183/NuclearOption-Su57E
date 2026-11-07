using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class BuildSu57
{
    static void ConfigureLandingGearLighting(Transform root,LandingGear nose)
    {
        var fixture=root.Find("fuselage_F/cockpit/gearDoor_FF/gearLight_F");
        if(fixture==null)throw new Exception("Missing native nose landing light");
        fixture.SetParent(nose.transform,true);
        // The fixture used to turn -86.57 degrees with the KR-67 door. Without
        // that door it left its child spotlight pointing down into the runway.
        fixture.rotation=root.rotation*Quaternion.Euler(-86.57f,0,0);
        var emitter=fixture.Find("gearLight_F_effects");
        if(emitter==null)throw new Exception("Missing native landing light emitter");
        // Attach to the actual relocated upper leg, rather than the old fixed
        // aircraft Z=3.60 position which left this mesh floating ahead of it.
        fixture.position+=nose.transform.TransformPoint(new Vector3(0,.05f,.12f))-emitter.position;
        emitter.rotation=root.rotation*Quaternion.Euler(5f,0,0);
        foreach(var lamp in emitter.GetComponentsInChildren<Light>(true))
        {
            lamp.intensity=12f;
            lamp.range=120f;
            lamp.spotAngle=40f;
            lamp.innerSpotAngle=22f;
        }
        // Keep the actual spotlight. Remove the additive billboard flare that
        // made the whole strut appear emissive in the user's ground screenshot.
        foreach(var particles in emitter.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main=particles.main;main.playOnAwake=false;
            var emission=particles.emission;emission.enabled=false;
            var renderer=particles.GetComponent<ParticleSystemRenderer>();
            if(renderer!=null)renderer.enabled=false;
        }
        Notes.Add("Landing light: emitter anchored to actual nose strut local (0,.05,.12), following nose placement instead of old aircraft Z=3.60; forward/down 5 deg, intensity=12, range=120; additive flare removed");
    }

    static void ConfigureLandingGearMaterials(LandingGear[] gears,IEnumerable<Renderer> braceRenderers)
    {
        var paint=CreateGearMaterial("SU57_GearPaint",new Color(.64f,.66f,.65f),0f,.18f,false);
        var steel=CreateGearMaterial("SU57_GearSteel",new Color(.55f,.58f,.61f),.2f,.25f,true);
        var black=CreateGearMaterial("SU57_GearBlack",new Color(.035f,.04f,.045f),0f,.12f,false);
        var wheelSource=AssetDatabase.LoadAssetAtPath<Material>(Game+"/Material/Aircraft_wheels_PLACEHOLDER.mat");
        if(wheelSource==null)throw new Exception("Missing native wheel texture material");
        var wheels=CreateGearMaterial("SU57_GearWheels",Color.white,0f,.12f,false,wheelSource);
        foreach(var renderer in gears.SelectMany(gear=>gear.GetComponentsInChildren<Renderer>(true)).Concat(braceRenderers).Distinct())
        {
            if(!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))continue;
            renderer.sharedMaterials=renderer.sharedMaterials.Select(material=>
            {
                if(material==null)return null;
                // Replace only gear surfaces. Ground equipment and unrelated
                // aircraft materials continue to use their own original assets.
                switch(material.name)
                {
                    case "Mirror":return steel;
                    case "Multirole1_gear":return paint;
                    case "Matte_black":return black;
                    case "Aircraft_wheels":return wheels;
                    case "Multirole1_skin":return renderer.name=="gearLight_F"?paint:material;
                    default:return material;
                }
            }).ToArray();
        }
        Notes.Add("Gear materials: local non-emissive paint/steel/rubber; smoothness=.18/.25/.12; mirror chrome removed, wheel albedo/normal maps preserved without glossy mask");
    }

    static Material CreateGearMaterial(string name,Color color,float metallic,float smoothness,bool highlights,Material textureSource=null)
    {
        var shader=Shader.Find("Universal Render Pipeline/Lit");
        if(shader==null)throw new Exception("URP Lit shader is unavailable for landing gear");
        var path=Root+"/"+name+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null)
        {
            material=new Material(shader);
            AssetDatabase.CreateAsset(material,path);
        }
        material.shader=shader;
        material.shaderKeywords=Array.Empty<string>();
        material.name=name;
        material.SetColor("_BaseColor",color);
        material.SetColor("_Color",color);
        material.SetFloat("_Metallic",metallic);
        material.SetFloat("_Smoothness",smoothness);
        material.SetFloat("_SmoothnessTextureChannel",0f);
        material.SetFloat("_SpecularHighlights",highlights?1f:0f);
        material.SetFloat("_EnvironmentReflections",highlights?1f:0f);
        if(!highlights)
        {
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        }
        material.SetTexture("_MetallicGlossMap",null);
        material.SetTexture("_SpecGlossMap",null);
        material.SetTexture("_EmissionMap",null);
        material.SetColor("_EmissionColor",Color.black);
        material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        material.SetFloat("_Surface",0f);
        material.SetFloat("_AlphaClip",0f);
        material.SetFloat("_SrcBlend",(float)BlendMode.One);
        material.SetFloat("_DstBlend",(float)BlendMode.Zero);
        material.SetFloat("_ZWrite",1f);
        material.SetFloat("_Cull",(float)CullMode.Back);
        material.SetOverrideTag("RenderType","Opaque");
        material.renderQueue=(int)RenderQueue.Geometry;
        Texture albedo=null,normal=null;
        if(textureSource!=null)
        {
            if(textureSource.HasProperty("_BaseMap"))albedo=textureSource.GetTexture("_BaseMap");
            if(albedo==null && textureSource.HasProperty("_MainTex"))albedo=textureSource.GetTexture("_MainTex");
            if(textureSource.HasProperty("_BumpMap"))normal=textureSource.GetTexture("_BumpMap");
        }
        material.SetTexture("_BaseMap",albedo);
        material.SetTexture("_BumpMap",normal);
        material.SetFloat("_BumpScale",.5f);
        if(normal!=null)material.EnableKeyword("_NORMALMAP");
        EditorUtility.SetDirty(material);
        return material;
    }
}
