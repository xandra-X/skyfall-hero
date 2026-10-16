using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Menu: OverPeak > Add Downtown District
/// Places the downtown (Assets/Models/Downtown/Downtown.fbx) north of the town, with colliders and static flags.
/// </summary>
public static class DowntownSetup
{
    const string ModelPath = "Assets/Models/Downtown/Downtown.fbx";
    const string ScenePath = "Assets/Scenes/City.unity";
    const string Name = "District_Downtown";
    const float Gap = 240f;     // sea strait between the town and downtown (crossed by the bridge)

    [MenuItem("OverPeak/Add Downtown District")]
    public static void Run()
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        imp.bakeAxisConversion = true;
        imp.importCameras = false; imp.importLights = false;
        imp.importAnimation = false; imp.animationType = ModelImporterAnimationType.None;
        imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        imp.materialLocation = ModelImporterMaterialLocation.InPrefab;
        imp.SaveAndReimport();

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var old = GameObject.Find(Name);
        if (old) Object.DestroyImmediate(old);

        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
        go.name = Name;
        go.transform.position = Vector3.zero;

        // put it north of the town, centered left-right, ground level matched
        var town = GameObject.Find("Town");
        Bounds tb = Encapsulate(town), db = Encapsulate(go);
        float groundOffset = tb.min.y - db.min.y;
        go.transform.position = new Vector3(tb.center.x - db.center.x, groundOffset, tb.max.z + Gap - db.min.z);
        db = Encapsulate(go);

        int colliders = 0;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (!mf.sharedMesh) continue;
            mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            colliders++;
        }
        foreach (var t in go.GetComponentsInChildren<Transform>())
            GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI);

        // fog / view distance for a 700 m city with 200 m towers
        RenderSettings.fogStartDistance = 400f;
        RenderSettings.fogEndDistance = 2200f;
        Camera.main.farClipPlane = 4000f;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        var renderers = go.GetComponentsInChildren<MeshRenderer>();
        var mats = renderers.SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().ToList();
        int noTex = mats.Count(m => !m.mainTexture);
        Debug.Log($"[Downtown] placed at {go.transform.position}  size {db.size}  renderers {renderers.Length}  colliders {colliders}  materials {mats.Count} (without texture: {noTex})");
        Debug.Log($"[Downtown] town bounds {tb.min}..{tb.max}  downtown bounds {db.min}..{db.max}");
    }

    static Bounds Encapsulate(GameObject root)
    {
        var rs = root.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // ---- batch test: aerial + street pictures
    public static void Test()
    {
        Run();
        EditorSceneManager.OpenScene(ScenePath);
        var cam = new GameObject("TestCam").AddComponent<Camera>();
        cam.farClipPlane = 4000f; cam.fieldOfView = 55f;
        var db = Encapsulate(GameObject.Find(Name));
        var tb = Encapsulate(GameObject.Find("Town"));
        Directory.CreateDirectory("Screenshots/downtown");
        Shot(cam, new Vector3(tb.center.x - 260f, 230f, tb.min.z - 220f), new Vector3(db.center.x, 40f, db.center.z - 120f), "Screenshots/downtown/aerial.png");
        Shot(cam, new Vector3(tb.center.x + 2f, 2.5f, tb.max.z - 10f), new Vector3(tb.center.x + 2f, 30f, tb.max.z + 200f), "Screenshots/downtown/from_town.png");
        Shot(cam, new Vector3(db.center.x, 3f, db.center.z - 40f), new Vector3(db.center.x + 40f, 60f, db.center.z + 80f), "Screenshots/downtown/street.png");
        Debug.Log("[Downtown] TEST DONE");
    }

    static void Shot(Camera cam, Vector3 pos, Vector3 target, string file)
    {
        cam.transform.position = pos; cam.transform.LookAt(target);
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt; cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        cam.targetTexture = null; RenderTexture.active = null;
        File.WriteAllBytes(file, tex.EncodeToPNG());
        Object.DestroyImmediate(rt);
    }
}
