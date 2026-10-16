using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Menu: OverPeak > Fix Hero Material
/// Builds a URP Lit material from the hero textures and makes Hero.fbx use it (scene copies update too).
/// </summary>
public static class HeroMaterialFix
{
    const string Dir = "Assets/Characters/Hero/";
    const string HeroModel = Dir + "Hero.fbx";
    const string MatPath = Dir + "M_Hero.mat";

    [MenuItem("OverPeak/Fix Hero Material")]
    public static void Run()
    {
        // texture import settings
        SetTexture(Dir + "Textures/Hero_BaseColor.png", TextureImporterType.Default, true);
        SetTexture(Dir + "Textures/Hero_Normal.png", TextureImporterType.NormalMap, false);
        SetTexture(Dir + "Textures/Hero_MetallicSmoothness.png", TextureImporterType.Default, false);

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (!mat)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, MatPath);
        }
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "Textures/Hero_BaseColor.png"));
        mat.SetColor("_BaseColor", Color.white);
        mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "Textures/Hero_Normal.png"));
        mat.SetFloat("_BumpScale", 1f);
        mat.EnableKeyword("_NORMALMAP");
        mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "Textures/Hero_MetallicSmoothness.png"));
        mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        mat.SetFloat("_Smoothness", 1f);            // scales the smoothness stored in the texture's alpha
        EditorUtility.SetDirty(mat);

        // every material slot in the hero model -> M_Hero
        var imp = (ModelImporter)AssetImporter.GetAtPath(HeroModel);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(HeroModel);
        foreach (var name in model.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials)
                     .Where(m => m).Select(m => m.name).Distinct())
            imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), mat);
        imp.SaveAndReimport();

        // and the copy already placed in the open scene
        foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>())
            if (smr.transform.root.name == "Hero")
                smr.sharedMaterials = Enumerable.Repeat(mat, smr.sharedMaterials.Length).ToArray();

        AssetDatabase.SaveAssets();
        Debug.Log("[HeroMaterial] DONE - hero now uses M_Hero");
    }

    static void SetTexture(string path, TextureImporterType type, bool srgb)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (ti == null) { Debug.LogError("[HeroMaterial] missing " + path); return; }
        ti.textureType = type;
        ti.sRGBTexture = srgb;
        ti.maxTextureSize = 2048;
        ti.SaveAndReimport();
    }
}
