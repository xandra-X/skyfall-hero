using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Menu: OverPeak > Setup Destruction
///   - every building (town Bldg_*, downtown towers) gets a Destructible and loses its static-batching flag (so it can collapse)
///   - the town's merged street props are split into separate Breakable objects (each lamp, pole, tree, sign...)
///   - adds the DamageSystem and HeroCombat
/// Safe to run again: the props are rebuilt from the original merged meshes.
/// </summary>
public static class DestructionSetup
{
    const string ScenePath = "Assets/Scenes/City.unity";
    const string MeshAsset = "Assets/Generated/Props/PropMeshes.asset";

    [MenuItem("OverPeak/Setup Destruction")]
    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // ---- damage system + hero combat
        var ds = Object.FindAnyObjectByType<DamageSystem>();
        if (!ds) ds = new GameObject("DamageSystem").AddComponent<DamageSystem>();
        ds.damageShader = Shader.Find("OverPeak/Damageable");
        var hero = GameObject.Find("Hero");
        if (hero && !hero.GetComponent<HeroCombat>()) hero.AddComponent<HeroCombat>();

        // ---- buildings
        int buildings = MarkAllBuildings(scene);
        var town = GameObject.Find("Town");

        // ---- props
        string propReport = SplitProps(town);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Destruction] {buildings} destructible buildings | {propReport} | hero combat {(hero && hero.GetComponent<HeroCombat>() ? "ok" : "MISSING")} | shader {(ds.damageShader ? "ok" : "MISSING")}");
    }

    /// Every building can be smashed through: the town's Bldg_* and every wall-height mesh downtown. Downtown is
    /// one big export grouped by material, so one mesh can hold the walls of many buildings across the district.
    static int MarkAllBuildings(UnityEngine.SceneManagement.Scene scene)
    {
        int buildings = 0;
        var town = GameObject.Find("Town");
        if (town)
            foreach (Transform t in town.transform)
                if (t.name.StartsWith("Bldg_")) { MakeDestructible(t.gameObject); buildings++; }
        foreach (var root in scene.GetRootGameObjects().Where(g => g.name.StartsWith("District_")))
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                if (r.bounds.size.y > 5f) { MakeDestructible(r.gameObject); buildings++; }
        return buildings;
    }

    /// Only the building part (keeps the street props and everything else as they are).
    [MenuItem("OverPeak/Advanced/Make All Buildings Smashable")]
    public static void MarkBuildingsOnly()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int n = MarkAllBuildings(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Destruction] {n} smashable building meshes");
    }

    static void MakeDestructible(GameObject g)
    {
        if (!g.GetComponent<Destructible>()) g.AddComponent<Destructible>();
        foreach (var t in g.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
    }

    // ================================================================== prop splitting
    class Src { public Transform t; public Mesh mesh; public Material[] mats; public Vector3[] wv; public Vector3[] wn; public Vector2[] uv; }
    class Comp { public int src; public List<(int sub, int a, int b, int c)> tris = new List<(int, int, int, int)>(); public Bounds b; }

    static string SplitProps(GameObject town)
    {
        // undo a previous run
        var oldRoot = GameObject.Find("Breakables");
        if (oldRoot) Object.DestroyImmediate(oldRoot);
        var groups = new List<GameObject>();
        foreach (Transform t in town.transform)
            if (t.name.StartsWith("Props_")) { t.gameObject.SetActive(true); groups.Add(t.gameObject); }

        // read the full-detail meshes
        var srcs = new List<Src>();
        foreach (var g in groups)
        {
            var lod0 = g.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(m => m.name.EndsWith("_LOD0"));
            if (!lod0 || !lod0.sharedMesh) continue;
            var m = lod0.sharedMesh;
            var tr = lod0.transform;
            srcs.Add(new Src
            {
                t = tr, mesh = m, mats = lod0.GetComponent<MeshRenderer>().sharedMaterials,
                wv = m.vertices.Select(v => tr.TransformPoint(v)).ToArray(),
                wn = m.normals.Select(v => tr.TransformDirection(v).normalized).ToArray(),
                uv = m.uv,
            });
        }

        // connected pieces in each mesh (vertices welded by position)
        var comps = new List<Comp>();
        for (int s = 0; s < srcs.Count; s++)
        {
            var src = srcs[s];
            int n = src.wv.Length;
            var parent = Enumerable.Range(0, n).ToArray();
            int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
            var weld = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < n; i++)
            {
                var k = Vector3Int.RoundToInt(src.wv[i] * 1000f);
                if (weld.TryGetValue(k, out int j)) Union(i, j); else weld[k] = i;
            }
            for (int sub = 0; sub < src.mesh.subMeshCount; sub++)
            {
                var t = src.mesh.GetTriangles(sub);
                for (int i = 0; i < t.Length; i += 3) { Union(t[i], t[i + 1]); Union(t[i], t[i + 2]); }
            }
            var byRoot = new Dictionary<int, Comp>();
            for (int sub = 0; sub < src.mesh.subMeshCount; sub++)
            {
                var t = src.mesh.GetTriangles(sub);
                for (int i = 0; i < t.Length; i += 3)
                {
                    int r = Find(t[i]);
                    if (!byRoot.TryGetValue(r, out var c))
                    {
                        c = new Comp { src = s, b = new Bounds(src.wv[t[i]], Vector3.zero) };
                        byRoot[r] = c; comps.Add(c);
                    }
                    c.tris.Add((sub, t[i], t[i + 1], t[i + 2]));
                    c.b.Encapsulate(src.wv[t[i]]); c.b.Encapsulate(src.wv[t[i + 1]]); c.b.Encapsulate(src.wv[t[i + 2]]);
                }
            }
        }

        // pieces that touch belong to the same prop (spatial grid keeps this fast)
        int nc = comps.Count;
        var cp = Enumerable.Range(0, nc).ToArray();
        int CF(int a) { while (cp[a] != a) { cp[a] = cp[cp[a]]; a = cp[a]; } return a; }
        const float cell = 2f, margin = 0.03f;
        var grid = new Dictionary<Vector3Int, List<int>>();
        for (int i = 0; i < nc; i++)
        {
            var b = comps[i].b;
            Vector3Int lo = Vector3Int.FloorToInt((b.min - Vector3.one * margin) / cell), hi = Vector3Int.FloorToInt((b.max + Vector3.one * margin) / cell);
            for (int x = lo.x; x <= hi.x; x++)
                for (int y = lo.y; y <= hi.y; y++)
                    for (int z = lo.z; z <= hi.z; z++)
                    {
                        var key = new Vector3Int(x, y, z);
                        if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                        foreach (int j in list)
                        {
                            var bj = comps[j].b; bj.Expand(margin * 2f);
                            if (bj.Intersects(b)) { int ra = CF(i), rb = CF(j); if (ra != rb) cp[ra] = rb; }
                        }
                        list.Add(i);
                    }
        }
        var clusters = new Dictionary<int, List<int>>();
        for (int i = 0; i < nc; i++)
        {
            int r = CF(i);
            if (!clusters.TryGetValue(r, out var l)) clusters[r] = l = new List<int>();
            l.Add(i);
        }

        // split groups that grew together (e.g. touching tree crowns): one prop per ground base
        var props = new List<List<int>>();
        foreach (var cl in clusters.Values)
        {
            Bounds cb = comps[cl[0]].b;
            foreach (int i in cl) cb.Encapsulate(comps[i].b);
            float baseY = cb.min.y + 0.5f;
            var bases = cl.Where(i => comps[i].b.min.y < baseY).ToList();
            var baseGroups = new List<List<int>>();
            foreach (int i in bases)
            {
                var near = baseGroups.FirstOrDefault(g => g.Any(j => Vector2.Distance(XZ(comps[j].b.center), XZ(comps[i].b.center)) < 1.2f));
                if (near != null) near.Add(i); else baseGroups.Add(new List<int> { i });
            }
            float extent = Mathf.Max(cb.size.x, cb.size.z);
            if (baseGroups.Count < 2 || extent < 5f) { props.Add(cl); continue; }
            var parts = baseGroups.Select(g => new List<int>()).ToList();
            var centers = baseGroups.Select(g => g.Select(j => XZ(comps[j].b.center)).Aggregate((a, b) => a + b) / g.Count).ToList();
            foreach (int i in cl)
            {
                Vector2 c = XZ(comps[i].b.center);
                int best = 0; float bd = float.MaxValue;
                for (int g = 0; g < centers.Count; g++) { float d = (centers[g] - c).sqrMagnitude; if (d < bd) { bd = d; best = g; } }
                parts[best].Add(i);
            }
            props.AddRange(parts.Where(p => p.Count > 0));
        }

        // build the objects
        var root = new GameObject("Breakables");
        var meshes = new List<Mesh>();
        int breakables = 0, scenery = 0;
        var propBounds = props.Select(p => { var b = comps[p[0]].b; foreach (int i in p) b.Encapsulate(comps[i].b); return b; }).ToList();
        // tiny bits join the nearest real prop
        for (int i = 0; i < props.Count; i++)
        {
            if (propBounds[i].size.magnitude >= 0.25f) continue;
            int best = -1; float bd = 4f;
            for (int j = 0; j < props.Count; j++)
            {
                if (j == i || propBounds[j].size.magnitude < 0.25f || props[j].Count == 0) continue;
                float d = (propBounds[j].center - propBounds[i].center).sqrMagnitude;
                if (d < bd) { bd = d; best = j; }
            }
            if (best >= 0) { props[best].AddRange(props[i]); propBounds[best].Encapsulate(propBounds[i]); props[i].Clear(); }
        }

        int idx = 0;
        for (int i = 0; i < props.Count; i++)
        {
            var p = props[i];
            if (p.Count == 0) continue;
            Bounds b = propBounds[i];
            Vector3 pivot = new Vector3(b.center.x, b.min.y, b.center.z);
            var mesh = BuildMesh(p, comps, srcs, pivot, out var mats);
            mesh.name = $"Prop_{idx:0000}";
            meshes.Add(mesh);
            var go = new GameObject($"Prop_{idx:0000}");
            idx++;
            go.transform.SetParent(root.transform);
            go.transform.position = pivot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;

            bool huge = b.size.magnitude > 22f, tiny = b.size.magnitude < 0.25f;
            if (huge || tiny)
            {
                if (huge) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                scenery++;
                continue;
            }
            // collider: just the base / trunk, full height (crowns and lamp heads don't block him)
            var baseComps = p.Where(c => comps[c].b.min.y < b.min.y + 0.5f).ToList();
            Bounds bb = baseComps.Count > 0 ? comps[baseComps[0]].b : b;
            foreach (int c in baseComps) bb.Encapsulate(comps[c].b);
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(bb.center.x - pivot.x, b.size.y / 2f, bb.center.z - pivot.z);
            box.size = new Vector3(Mathf.Max(bb.size.x, 0.15f), b.size.y, Mathf.Max(bb.size.z, 0.15f));
            var br = go.AddComponent<Breakable>();
            br.fullCenter = b.center - pivot;
            br.fullSize = b.size;
            br.isPole = b.size.y > 3f && Mathf.Max(bb.size.x, bb.size.z) < 0.8f;
            br.hasLight = mats.Any(m => m && m.name.Contains("Light"));
            breakables++;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(MeshAsset));
        AssetDatabase.DeleteAsset(MeshAsset);
        if (meshes.Count > 0)
        {
            AssetDatabase.CreateAsset(meshes[0], MeshAsset);
            for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], MeshAsset);
            AssetDatabase.SaveAssets();
        }
        foreach (var g in groups) g.SetActive(false);    // the merged originals are replaced
        return $"props: {comps.Count} pieces -> {breakables} breakable props + {scenery} fixed scenery";
    }

    static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);

    static Mesh BuildMesh(List<int> compIds, List<Comp> comps, List<Src> srcs, Vector3 pivot, out Material[] mats)
    {
        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>();
        var subs = new Dictionary<Material, List<int>>();
        var remap = new Dictionary<(int, int), int>();
        foreach (int ci in compIds)
        {
            var c = comps[ci];
            var s = srcs[c.src];
            foreach (var (sub, a, b, cc) in c.tris)
            {
                var mat = s.mats[Mathf.Min(sub, s.mats.Length - 1)];
                if (!subs.TryGetValue(mat, out var list)) subs[mat] = list = new List<int>();
                foreach (int v in new[] { a, b, cc })
                {
                    if (!remap.TryGetValue((c.src, v), out int ni))
                    {
                        ni = verts.Count;
                        remap[(c.src, v)] = ni;
                        verts.Add(s.wv[v] - pivot);
                        norms.Add(s.wn.Length > v ? s.wn[v] : Vector3.up);
                        uvs.Add(s.uv.Length > v ? s.uv[v] : Vector2.zero);
                    }
                    list.Add(ni);
                }
            }
        }
        var mesh = new Mesh { indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs);
        mats = subs.Keys.ToArray();
        mesh.subMeshCount = mats.Length;
        for (int i = 0; i < mats.Length; i++) mesh.SetTriangles(subs[mats[i]], i);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }
}
