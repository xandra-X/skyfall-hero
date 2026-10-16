using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Menu: OverPeak > Setup City Life
///   - marks road surfaces (NavMesh area 3) so cars stay on the road and people prefer the sidewalk
///   - bakes the NavMesh (walkable ground, buildings and props as obstacles, the sea excluded)
///   - adds Population (people, traffic, planes) and the hero's HeroAudio / HeroGrab
/// </summary>
public static class LifeSetup
{
    const string ScenePath = "Assets/Scenes/City.unity";
    const string NavAsset = "Assets/Generated/NavMesh/CityNavMesh.asset";
    static readonly string[] RoadWords = { "road", "highway", "asphalt", "lane", "crosswalk", "centerline", "stop", "bridgedeck" };

    [MenuItem("OverPeak/Setup City Life")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // ---- road marking
        foreach (var m in Object.FindObjectsByType<NavMeshModifier>()) Object.DestroyImmediate(m);
        int roads = 0;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>())
        {
            if (r.GetComponentInParent<Destructible>() || r.GetComponentInParent<Breakable>() || r.GetComponentInParent<HeroController>()) continue;
            if (!r.GetComponent<Collider>()) continue;
            // every surface is labelled: road (3) or sidewalk (4). Where a sidewalk sits on top of the
            // road sheet the higher area number wins, so the sidewalk number must be the bigger one.
            bool road = IsRoad(r);
            var mod = r.gameObject.AddComponent<NavMeshModifier>();
            mod.overrideArea = true;
            mod.area = road ? Civilian.RoadArea : Civilian.SidewalkArea;
            mod.applyToChildren = false;
            if (road) roads++;
        }

        // ---- the island terrain around the city is not for walking (people would wander the beaches)
        var islands = GameObject.Find("World/Islands");
        if (islands)
        {
            var im = islands.AddComponent<NavMeshModifier>();
            im.ignoreFromBuild = true;
        }

        // ---- bake
        var nav = GameObject.Find("Navigation") ?? new GameObject("Navigation");
        var surf = (nav.TryGetComponent<NavMeshSurface>(out var existing4) ? existing4 : nav.AddComponent<NavMeshSurface>());
        surf.agentTypeID = 0;
        surf.collectObjects = CollectObjects.All;
        surf.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surf.layerMask = ~((1 << 2) | (1 << 4) | (1 << 3));      // not the hero / debris, the sea, or bodies
        surf.overrideVoxelSize = true; surf.voxelSize = 0.25f;
        surf.overrideTileSize = true; surf.tileSize = 256;
        surf.minRegionArea = 8f;
        var t0 = System.DateTime.Now;
        surf.BuildNavMesh();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(NavAsset));
        AssetDatabase.DeleteAsset(NavAsset);
        AssetDatabase.CreateAsset(surf.navMeshData, NavAsset);
        var tri = NavMesh.CalculateTriangulation();
        int roadTris = 0;
        for (int i = 0; i < tri.areas.Length; i++) if (tri.areas[i] == Civilian.RoadArea) roadTris++;

        // ---- systems
        var pop = Object.FindAnyObjectByType<Population>();
        if (!pop) pop = new GameObject("Population").AddComponent<Population>();
        pop.civilians = 320;
        pop.cars = 120;
        var oldDread = GameObject.Find("Dread");                     // the removed mood effect
        if (oldDread) Object.DestroyImmediate(oldDread);
        var hero = GameObject.Find("Hero");
        if (!hero.GetComponent<HeroAudio>()) hero.AddComponent<HeroAudio>();
        if (!hero.GetComponent<HeroGrab>()) hero.AddComponent<HeroGrab>();
        var obstacle = (hero.TryGetComponent<NavMeshObstacle>(out var existing5) ? existing5 : hero.AddComponent<NavMeshObstacle>());
        obstacle.shape = NavMeshObstacleShape.Capsule;
        obstacle.center = new Vector3(0f, 0.95f, 0f); obstacle.radius = 0.45f; obstacle.height = 1.85f;
        obstacle.carving = false;                                   // people and cars steer around him
        Camera.main.GetUniversalAdditionalCameraData().renderPostProcessing = true;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        // check: no sidewalk should be road
        int sideOnRoad = 0, sideTotal = 0;
        foreach (var m in Object.FindObjectsByType<NavMeshModifier>())
        {
            if (m.area != Civilian.SidewalkArea || !m.TryGetComponent<MeshFilter>(out var mf) || !mf.sharedMesh) continue;
            var verts = mf.sharedMesh.vertices;
            for (int i = 0; i < verts.Length; i += Mathf.Max(1, verts.Length / 60))
            {
                if (!NavMesh.SamplePosition(m.transform.TransformPoint(verts[i]), out var h, 0.4f, NavMesh.AllAreas)) continue;
                sideTotal++;
                if ((h.mask & (1 << Civilian.RoadArea)) != 0) sideOnRoad++;
            }
        }
        Debug.Log($"[Life] sidewalk samples marked road: {sideOnRoad}/{sideTotal}");
        Debug.Log($"[Life] road surfaces {roads} | navmesh {tri.indices.Length / 3} triangles ({roadTris} road) in {(System.DateTime.Now - t0).TotalSeconds:0.0}s | population, hero audio/grab added");
    }

    static bool IsRoad(MeshRenderer r)
    {
        string n = r.name.ToLowerInvariant();
        if (RoadWords.Any(w => n.Contains(w))) return true;
        foreach (var m in r.sharedMaterials)
        {
            if (!m) continue;
            string mn = m.name.ToLowerInvariant();
            string tn = m.mainTexture ? m.mainTexture.name.ToLowerInvariant() : "";
            if (RoadWords.Any(w => tn.Contains(w) || mn.Contains(w))) return true;
        }
        return false;
    }
}
