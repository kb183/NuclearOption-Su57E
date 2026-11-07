using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static partial class BuildSu57
{
    [Serializable] class Su57LiveryManifest { public Su57LiverySpec[] liveries; }
    [Serializable] class Su57LiverySpec
    {
        public string key, name, texture, asset;
        public Su57LiveryColor[] colors;
    }
    [Serializable] class Su57LiveryColor { public int r, g, b, count; }

    static void ConfigureLivery(AircraftParameters parameters)
    {
        var manifestPath=Root+"/Source/SU57_Liveries.json";
        if(!File.Exists(manifestPath))throw new Exception("Run Tools/prepare_su57_liveries.py to author the five Su-57 liveries");
        var manifest=JsonUtility.FromJson<Su57LiveryManifest>(File.ReadAllText(manifestPath));
        if(manifest==null || manifest.liveries==null || manifest.liveries.Length!=5)
            throw new Exception("Expected all five authored Su-57 liveries");
        var liveries=new List<AircraftParameters.Livery>();
        foreach(var entry in manifest.liveries)
        {
            string texturePath=Root+"/Source/"+entry.texture;
            ConfigureSkinTextureImporter(texturePath,4096,false);
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if(texture==null || entry.colors==null || entry.colors.Length==0)
                throw new Exception("Missing Su-57 livery texture/palette: "+entry.name);
            string path=Root+"/"+entry.asset;
            var data=AssetDatabase.LoadAssetAtPath<LiveryData>(path);
            if(data==null){data=ScriptableObject.CreateInstance<LiveryData>();AssetDatabase.CreateAsset(data,path);}
            data.name=Path.GetFileNameWithoutExtension(path);
            data.Texture=texture;
            data.Glossiness=.5f;
            data.Colors=entry.colors.Select(c=>new LiveryData.TextureColor
            {
                Color=new Color32((byte)c.r,(byte)c.g,(byte)c.b,255),Count=c.count
            }).ToArray();
            EditorUtility.SetDirty(data);
            liveries.Add(new AircraftParameters.Livery
            {
                name=entry.name,faction=null,
                assetReference=new AssetReferenceLiveryData(AssetDatabase.AssetPathToGUID(path))
            });
        }
        // Keep the original digital livery first and retain its existing GUID.
        parameters.liveries=liveries;
        Notes.Add("Five selectable native Su-57 liveries: digital, blue-grey, light-grey, grey splinter and blue-grey splinter; 4K edge-filtered atlases, trilinear mipmaps, 16x anisotropy, BC7 high quality; shared native AircraftSkin damage/normal/metallic/AO inputs. Native _Livery selection and independent damage state apply to body/wing/doors.");
    }

    static void ConfigureSkinTextureImporter(string path,int size,bool linear)
    {
        var importer=AssetImporter.GetAtPath(path) as TextureImporter;
        if(importer==null)throw new Exception("Missing Su-57 surface texture importer: "+path);
        var platform=importer.GetPlatformTextureSettings("Standalone");
        bool changed=importer.textureType!=TextureImporterType.Default || importer.sRGBTexture==linear ||
            importer.alphaSource!=TextureImporterAlphaSource.FromInput || importer.alphaIsTransparency ||
            !importer.mipmapEnabled || importer.mipmapFilter!=TextureImporterMipFilter.BoxFilter ||
            importer.filterMode!=FilterMode.Trilinear || importer.anisoLevel!=16 ||
            Mathf.Abs(importer.mipMapBias-.15f)>.001f || importer.maxTextureSize!=size ||
            importer.textureCompression!=TextureImporterCompression.CompressedHQ ||
            importer.compressionQuality!=100 || importer.crunchedCompression ||
            !platform.overridden || platform.maxTextureSize!=size || platform.format!=TextureImporterFormat.BC7 ||
            platform.compressionQuality!=100 || platform.crunchedCompression;
        if(!changed)return;
        importer.textureType=TextureImporterType.Default;
        importer.sRGBTexture=!linear;
        importer.alphaSource=TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency=false;
        importer.mipmapEnabled=true;
        importer.mipmapFilter=TextureImporterMipFilter.BoxFilter;
        importer.filterMode=FilterMode.Trilinear;
        importer.anisoLevel=16;
        importer.mipMapBias=.15f;
        importer.maxTextureSize=size;
        importer.textureCompression=TextureImporterCompression.CompressedHQ;
        importer.compressionQuality=100;
        importer.crunchedCompression=false;
        platform.name="Standalone";
        platform.overridden=true;
        platform.maxTextureSize=size;
        platform.format=TextureImporterFormat.BC7;
        platform.textureCompression=TextureImporterCompression.CompressedHQ;
        platform.compressionQuality=100;
        platform.crunchedCompression=false;
        importer.SetPlatformTextureSettings(platform);
        importer.SaveAndReimport();
    }
}
