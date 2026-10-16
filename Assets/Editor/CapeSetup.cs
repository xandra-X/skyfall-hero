using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Menu: OverPeak > Setup Cape. Gives the hero his cloth cape (built at play time from his pose).</summary>
public static class CapeSetup
{
    [MenuItem("OverPeak/Setup Cape")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/City.unity", OpenSceneMode.Single);
        var hero = Object.FindAnyObjectByType<HeroController>();
        if (!hero) { Debug.LogError("[Cape] no hero"); return; }
        Directory.CreateDirectory("Assets/Generated/Hero");
        const string path = "Assets/Generated/Hero/M_Cape.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(Shader.Find("OverPeak/Cape")); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = Shader.Find("OverPeak/Cape");
        mat.SetColor("_BaseColor", new Color(0.62f, 0.035f, 0.045f));
        mat.SetColor("_InsideColor", new Color(0.5f, 0.03f, 0.04f));
        EditorUtility.SetDirty(mat);
        if (!hero.TryGetComponent(out Cape cape)) cape = hero.gameObject.AddComponent<Cape>();
        cape.material = mat;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Cape] cape added to " + hero.name);
    }
}
