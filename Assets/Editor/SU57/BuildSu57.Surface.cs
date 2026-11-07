using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class BuildSu57
{
    static Material CreateSkin()
    {
        // Blueprinter resolves this project placeholder to the game's native
        // AircraftSkin. Its editor preview is unlit and does not show damage.
        var shader=Shader.Find("Shader Graphs/AircraftSkin");
        if(shader==null)throw new Exception("Native AircraftSkin placeholder is unavailable");
        var albedo=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Source/SU-57_SU57_1_BaseMap_4131054348559121509.png");
        if(albedo==null)throw new Exception("Missing Su-57 base color");

        var path=Root+"/SU57_Camouflage.mat";
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(mat==null){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}
        mat.name="SU57_Camouflage";
        mat.shader=shader;
        mat.shaderKeywords=new[]{"_ALPHATEST_ON","_NORMALMAP"};
        mat.SetTexture("_Basecolor",albedo);
        mat.SetTexture("_Livery",albedo);
        mat.SetTexture("_BasecolorDmg",DamageTexture("SU57_DamageBasecolor",false));
        mat.SetTexture("_Normal",DamageTexture("SU57_SkinNormal",true,true));
        mat.SetTexture("_NormalDmg",DamageTexture("SU57_DamageNormal",true,true));
        mat.SetTexture("_Metallic",DamageTexture("SU57_SkinMetallic",true));
        mat.SetTexture("_AO",DamageTexture("SU57_SkinAO",true));
        mat.SetFloat("_HitPoints",100f);
        mat.SetFloat("_Glossiness",.5f);
        mat.globalIlluminationFlags=MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        mat.SetOverrideTag("RenderType","TransparentCutout");
        mat.renderQueue=-1;
        EditorUtility.SetDirty(mat);
        Notes.Add("Su-57 native AircraftSkin damage material: own UV color/damage/normal/metallic/AO inputs, per-renderer _HitPoints and native _Livery/_Glossiness; five liveries retained. Editor placeholder is unlit; native blend, alpha and metallic response await user acceptance.");
        return mat;
    }

    static Texture2D DamageTexture(string name,bool linear,bool normal=false)
    {
        string path=Root+"/Source/"+name+".png";
        ConfigureSkinTextureImporter(path,2048,linear);
        if(normal)
        {
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType=TextureImporterType.NormalMap;
            importer.convertToNormalmap=false;importer.flipGreenChannel=false;
            var platform=importer.GetPlatformTextureSettings("Standalone");
            platform.format=TextureImporterFormat.BC5;importer.SetPlatformTextureSettings(platform);
            importer.SaveAndReimport();
        }
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if(texture==null)throw new Exception("Run Tools/prepare_su57_damage_details.py: missing "+name);
        return texture;
    }

    static Material CreateFractureMaterial()
    {
        string path=Root+"/SU57_FractureInterior.mat";
        var shader=Shader.Find("Universal Render Pipeline/Lit");
        var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(mat==null){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}
        mat.shader=shader;mat.shaderKeywords=Array.Empty<string>();
        mat.SetTexture("_BaseMap",DamageTexture("SU57_FractureInterior",false));
        mat.SetColor("_BaseColor",Color.white);mat.SetFloat("_Metallic",.55f);mat.SetFloat("_Smoothness",.22f);
        mat.SetFloat("_Cull",(float)CullMode.Off);
        mat.renderQueue=(int)RenderQueue.Geometry;EditorUtility.SetDirty(mat);
        return mat;
    }

}
