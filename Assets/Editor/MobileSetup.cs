using UnityEditor;
using UnityEngine;

/// <summary>As soon as the scripts load in the editor: create the effect materials if they aren't there yet.</summary>
[InitializeOnLoad]
static class EffectMaterialsOnLoad
{
    static EffectMaterialsOnLoad()
    {
        EditorApplication.delayCall += () =>
        {
            if (!AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Fx/FxAdditive.mat")) MobileSetup.EnsureEffectMaterials();
        };
    }
}

/// <summary>Before every build: make sure the shaders the game looks up while running are in it.</summary>
public class IncludeShadersOnBuild : UnityEditor.Build.IPreprocessBuildWithReport
{
    public int callbackOrder => 0;
    public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report) => MobileSetup.IncludeRuntimeShaders();
}

/// <summary>
/// iPhone helpers:
///   OverPeak > Touch Controls In Editor  - show the on-screen buttons when you press Play on the Mac (click them with the mouse)
///   OverPeak > Setup iPhone Build        - landscape only, 60 fps friendly settings
/// </summary>
public static class MobileSetup
{
    const string ToggleMenu = "OverPeak/Touch Controls In Editor";

    [MenuItem(ToggleMenu)]
    static void ToggleTouch() => EditorPrefs.SetBool(TouchControls.EditorPrefKey, !EditorPrefs.GetBool(TouchControls.EditorPrefKey, false));

    [MenuItem(ToggleMenu, true)]
    static bool ToggleTouchCheck()
    {
        Menu.SetChecked(ToggleMenu, EditorPrefs.GetBool(TouchControls.EditorPrefKey, false));
        return true;
    }

    /// Shaders the game asks for by name while it runs (effects, afterimages, sparks, glows). A build only contains
    /// shaders that some material in a scene uses, so these must be listed or they show up pink on the phone.
    static readonly string[] RuntimeShaders =
    {
        "Universal Render Pipeline/Particles/Unlit",
        "OverPeak/Damageable", "OverPeak/Plasma", "OverPeak/Cape", "OverPeak/CloudCard", "OverPeak/CloudDeck",
        "OverPeak/Planet", "OverPeak/SpaceSky", "OverPeak/Atmosphere", "OverPeak/CloudShell",
    };

    /// The two effect materials every glow, spark, beam and dust puff is copied from (see FxUtil).
    public static void EnsureEffectMaterials()
    {
        System.IO.Directory.CreateDirectory("Assets/Resources/Fx");
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (!shader) { Debug.LogError("[Mobile] particle shader missing"); return; }
        foreach (bool additive in new[] { true, false })
        {
            string path = additive ? "Assets/Resources/Fx/FxAdditive.mat" : "Assets/Resources/Fx/FxAlpha.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (additive) m.EnableKeyword("_BLENDMODE_ADD"); else m.DisableKeyword("_BLENDMODE_ADD");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(m);
        }
        AssetDatabase.SaveAssets();
    }

    public static void IncludeRuntimeShaders()
    {
        EnsureEffectMaterials();
        var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0];
        var so = new SerializedObject(gs);
        var list = so.FindProperty("m_AlwaysIncludedShaders");
        int added = 0;
        foreach (var name in RuntimeShaders)
        {
            var sh = Shader.Find(name);
            if (!sh) { Debug.LogWarning("[Mobile] shader not found: " + name); continue; }
            bool has = false;
            for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == sh) has = true;
            if (has) continue;
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = sh;
            added++;
        }
        so.ApplyModifiedProperties();
        Debug.Log($"[Mobile] always-included shaders: +{added} (total {list.arraySize})");
    }

    [MenuItem("OverPeak/Setup iPhone Build")]
    public static void Run()
    {
        // the game is played sideways
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.productName = "OverPeak";
        PlayerSettings.iOS.requiresFullScreen = true;
        PlayerSettings.iOS.hideHomeButton = true;          // the home bar fades while playing
        PlayerSettings.iOS.deferSystemGesturesMode = UnityEngine.iOS.SystemGestureDeferMode.All;
        IncludeRuntimeShaders();
        AssetDatabase.SaveAssets();
        Debug.Log("[Mobile] iPhone settings: landscape only, full screen, edge swipes deferred");
    }
}
