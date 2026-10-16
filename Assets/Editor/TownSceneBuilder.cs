using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds Assets/Scenes/City.unity from Assets/Models/Town/Town.fbx.
/// Menu: OverPeak > Build City Scene  (also runnable in batch mode via -executeMethod TownSceneBuilder.Build)
/// </summary>
public static class TownSceneBuilder
{
    const string ModelPath = "Assets/Models/Town/Town.fbx";
    const string ScenePath = "Assets/Scenes/City.unity";

    [MenuItem("OverPeak/Advanced/Reset City Scene (wipes everything)")]
    static void BuildFromMenu()
    {
        if (!EditorUtility.DisplayDialog("Reset City Scene?",
                "This rebuilds City.unity from the bare town and REMOVES Superman, downtown, the sea, people and traffic.\n\n" +
                "To get everything back afterwards use OverPeak > Rebuild Whole World.",
                "Reset", "Cancel")) return;
        Build();
    }

    public static void Build()
    {
        ConfigureImporter();

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) { Debug.LogError("[Town] Town.fbx not found"); return; }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var town = (GameObject)PrefabUtility.InstantiatePrefab(model);
        town.name = "Town";

        // One LOD Group per building / prop group (Unity's automatic one covers the whole town)
        foreach (var g in town.GetComponentsInChildren<LODGroup>()) Object.DestroyImmediate(g);
        int groups = 0;
        foreach (var t in town.GetComponentsInChildren<Transform>())
        {
            var levels = new System.Collections.Generic.List<Renderer>[3];
            for (int i = 0; i < 3; i++)
            {
                var child = t.Cast<Transform>().FirstOrDefault(c => c.name.EndsWith("_LOD" + i));
                levels[i] = child ? child.GetComponents<Renderer>().ToList() : null;
            }
            if (levels[0] == null) continue;
            float[] height = { 0.25f, 0.08f, 0.01f };   // screen height where each level stops
            var lodList = new System.Collections.Generic.List<LOD>();
            for (int i = 0; i < 3; i++)
                if (levels[i] != null) lodList.Add(new LOD(height[i], levels[i].ToArray()));
            var lg = t.gameObject.AddComponent<LODGroup>();
            lg.SetLODs(lodList.ToArray());
            lg.RecalculateBounds();
            groups++;
        }
        Debug.Log($"[Town] created {groups} LOD groups");

        int colliders = 0;
        foreach (var lod in town.GetComponentsInChildren<LODGroup>())
        {
            // Collision uses the light LOD2 mesh: cheap, and the shape is the same
            var lods = lod.GetLODs();
            var lowest = lods[lods.Length - 1].renderers.FirstOrDefault(r => r != null);
            if (lowest == null) continue;
            var mc = lod.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = lowest.GetComponent<MeshFilter>().sharedMesh;
            colliders++;
        }
        foreach (var mf in town.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.name.StartsWith("Ground_"))
            {
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                colliders++;
            }
        }
        foreach (var t in town.GetComponentsInChildren<Transform>())
            GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI);

        var sun = Object.FindAnyObjectByType<Light>();
        sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
        sun.shadows = LightShadows.Soft;

        var cam = Camera.main;
        cam.farClipPlane = 3000f;
        cam.transform.position = new Vector3(-70f, 60f, -90f);
        cam.transform.LookAt(new Vector3(0f, 8f, 0f));
        cam.gameObject.AddComponent<FreeFlyCamera>();

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.72f, 0.80f, 0.90f);
        RenderSettings.fogStartDistance = 250f;
        RenderSettings.fogEndDistance = 1200f;

        EditorSceneManager.SaveScene(scene, ScenePath);
        var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();

        Report(town, colliders);
        Screenshot(cam, "aerial");
        cam.transform.position = new Vector3(-4f, 2f, -40f);
        cam.transform.LookAt(new Vector3(-4f, 8f, 10f));
        Screenshot(cam, "street");
        cam.transform.position = new Vector3(-70f, 60f, -90f);
        cam.transform.LookAt(new Vector3(0f, 8f, 0f));
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("[Town] DONE scene saved: " + ScenePath);
    }

    static void ConfigureImporter()
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        imp.bakeAxisConversion = true;          // Blender is Z-up, Unity is Y-up
        imp.globalScale = 1f;
        imp.useFileScale = true;
        imp.importCameras = false;
        imp.importLights = false;
        imp.importAnimation = false;
        imp.animationType = ModelImporterAnimationType.None;
        imp.isReadable = false;
        imp.meshCompression = ModelImporterMeshCompression.Medium;
        imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
        imp.SaveAndReimport();
    }

    static void Report(GameObject town, int colliders)
    {
        var lods = town.GetComponentsInChildren<LODGroup>();
        var renderers = town.GetComponentsInChildren<MeshRenderer>();
        long lod0 = 0, all = 0;
        foreach (var r in renderers)
        {
            var m = r.GetComponent<MeshFilter>().sharedMesh;
            if (m == null) continue;
            long tris = 0;
            for (int i = 0; i < m.subMeshCount; i++) tris += m.GetIndexCount(i) / 3;
            all += tris;
            if (!r.name.Contains("_LOD") || r.name.EndsWith("_LOD0")) lod0 += tris;
        }
        var b = new Bounds(town.transform.position, Vector3.zero);
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        var mats = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToList();
        Debug.Log($"[Town] LODGroups {lods.Length} | renderers {renderers.Length} | colliders {colliders} | " +
                  $"tris LOD0 {lod0:N0} (all LODs {all:N0}) | size {b.size} center {b.center}");
        foreach (var m in mats)
            Debug.Log($"[Town] material {m.name} shader={m.shader.name} tex={(m.mainTexture ? m.mainTexture.name : "-")} color={m.color}");
    }

    static void Screenshot(Camera cam, string name)
    {
        var rt = new RenderTexture(1600, 900, 24);
        cam.targetTexture = rt;
        cam.Render();   // first frame warms up shaders (renders placeholder colors)
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
        tex.Apply();
        cam.targetTexture = null;
        RenderTexture.active = null;
        Directory.CreateDirectory("Screenshots");
        File.WriteAllBytes($"Screenshots/{name}.png", tex.EncodeToPNG());
        Object.DestroyImmediate(rt);
        Debug.Log($"[Town] screenshot Screenshots/{name}.png");
    }
}
