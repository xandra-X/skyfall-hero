using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Menu: OverPeak > Build Islands, Sea and Bridge
///   - an island under every city part (follows the city's hills, beaches slope down into the sea)
///   - an ocean all around (OverPeak/Ocean shader) + an invisible surface he can run on at super speed
///   - a bridge road across the water from the town to downtown
/// Re-run any time after adding a district: everything under "World" is rebuilt.
/// </summary>
public static class WorldBuilder
{
    const string ScenePath = "Assets/Scenes/City.unity";
    const string AssetDir = "Assets/Generated/World/";
    public const float SeaLevel = -2.0f;
    const float Cell = 4f;              // island height-map resolution (m)
    const float Margin = 140f;          // sea around the cities inside the map
    const float CoastWidth = 40f;       // beach + underwater slope
    const float SeaBed = -12f;
    const float LandBelowCity = 0.35f;  // island surface sits just under the city's own ground

    [MenuItem("OverPeak/Build Islands, Sea and Bridge")]
    public static void Run()
    {
        Directory.CreateDirectory(AssetDir);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var old = GameObject.Find("World");
        if (old) Object.DestroyImmediate(old);
        var world = new GameObject("World");

        var districts = scene.GetRootGameObjects().Where(g => g.name == "Town" || g.name.StartsWith("District_")).ToList();
        Bounds all = Encapsulate(districts);

        // ---------------------------------------------------------------- island height map
        float minX = all.min.x - Margin, minZ = all.min.z - Margin;
        int nx = Mathf.CeilToInt((all.size.x + Margin * 2) / Cell) + 1;
        int nz = Mathf.CeilToInt((all.size.z + Margin * 2) / Cell) + 1;
        var city = new float[nx * nz];
        for (int i = 0; i < city.Length; i++) city[i] = float.PositiveInfinity;
        var owner = new int[nx * nz];                       // which district a land cell belongs to (for the bridge)
        for (int i = 0; i < owner.Length; i++) owner[i] = -1;

        for (int d = 0; d < districts.Count; d++)
            foreach (var mf in districts[d].GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh || !mf.GetComponent<MeshRenderer>() || !mf.GetComponent<MeshRenderer>().enabled) continue;
                if (mf.name.Contains("_LOD1") || mf.name.Contains("_LOD2")) continue;
                Rasterize(mf, city, owner, d, nx, nz, minX, minZ);
            }

        // close small gaps (courtyards, between pieces), then 1 cell of shoulder around the edge
        for (int pass = 0; pass < 3; pass++) Close(city, owner, nx, nz, pass < 2 ? 5 : 3);

        var land = new float[nx * nz];
        var nearestH = new float[nx * nz];
        var dist = new float[nx * nz];
        DistanceFromLand(city, nx, nz, dist, nearestH);
        for (int i = 0; i < land.Length; i++)
        {
            float top = (float.IsInfinity(city[i]) ? nearestH[i] : city[i]) - LandBelowCity;
            float k = Mathf.SmoothStep(0f, 1f, dist[i] / CoastWidth);
            land[i] = Mathf.Lerp(top, SeaBed, k);
        }

        var islandMat = Mat("M_Island", new Material(Shader.Find("OverPeak/VertexColorLit")));
        var islands = BuildHeightMesh(land, nx, nz, minX, minZ, "Islands");
        var go = new GameObject("Islands", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
        go.transform.SetParent(world.transform);
        go.GetComponent<MeshFilter>().sharedMesh = islands;
        go.GetComponent<MeshRenderer>().sharedMaterial = islandMat;
        go.GetComponent<MeshCollider>().sharedMesh = islands;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);

        // ---------------------------------------------------------------- ocean
        var oceanMat = Mat("M_Ocean", new Material(Shader.Find("OverPeak/Ocean")));
        var center = new Vector3(all.center.x, SeaLevel, all.center.z);
        var ocean = new GameObject("Ocean", typeof(MeshFilter), typeof(MeshRenderer));
        ocean.transform.SetParent(world.transform);
        ocean.transform.position = center;
        ocean.GetComponent<MeshFilter>().sharedMesh = SaveMesh(GridMesh(3200f, 300), "OceanGrid");
        var orr = ocean.GetComponent<MeshRenderer>();
        orr.sharedMaterial = oceanMat;
        orr.shadowCastingMode = ShadowCastingMode.Off;

        // far ocean ring to the horizon (flat, cheap)
        var far = new GameObject("OceanFar", typeof(MeshFilter), typeof(MeshRenderer));
        far.transform.SetParent(world.transform);
        far.transform.position = center + Vector3.down * 0.05f;
        far.GetComponent<MeshFilter>().sharedMesh = SaveMesh(GridMesh(20000f, 40), "OceanFarGrid");
        far.GetComponent<MeshRenderer>().sharedMaterial = oceanMat;
        far.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        // surface he can run on at super speed (collision switched on/off by HeroController)
        var surface = new GameObject("SeaSurface", typeof(BoxCollider));
        surface.layer = 4;     // Water
        surface.transform.SetParent(world.transform);
        surface.transform.position = center + Vector3.down * 0.5f;
        var box = surface.GetComponent<BoxCollider>();
        box.size = new Vector3(20000f, 1f, 20000f);

        // ---------------------------------------------------------------- bridge
        string bridgeInfo = BuildBridge(world.transform, districts);

        // sun reflection + fog colour that suits the sea
        RenderSettings.fogColor = new Color(0.70f, 0.79f, 0.88f);
        RenderSettings.fogStartDistance = 400f;
        RenderSettings.fogEndDistance = 2600f;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[World] islands {nx}x{nz} cells ({islands.vertexCount} verts), ocean ready, {bridgeInfo}");
    }

    // ==================================================================== height map helpers
    static void Rasterize(MeshFilter mf, float[] city, int[] owner, int d, int nx, int nz, float minX, float minZ)
    {
        var mesh = mf.sharedMesh;
        var m = mf.transform.localToWorldMatrix;
        var v = mesh.vertices.Select(p => m.MultiplyPoint3x4(p)).ToArray();
        void Put(int cx, int cz, float h)
        {
            if (cx < 0 || cz < 0 || cx >= nx || cz >= nz) return;
            int i = cz * nx + cx;
            if (h < city[i]) { city[i] = h; owner[i] = d; }
        }
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var t = mesh.GetTriangles(s);
            for (int k = 0; k < t.Length; k += 3)
            {
                Vector3 a = v[t[k]], b = v[t[k + 1]], c = v[t[k + 2]];
                // vertical walls don't cover ground; skip almost-vertical triangles except for their bottom edge
                float area = (b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z);
                foreach (var p in new[] { a, b, c })
                    Put(Mathf.RoundToInt((p.x - minX) / Cell), Mathf.RoundToInt((p.z - minZ) / Cell), p.y);
                if (Mathf.Abs(area) < 1e-4f) continue;
                int x0 = Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - minX) / Cell), x1 = Mathf.CeilToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - minX) / Cell);
                int z0 = Mathf.FloorToInt((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - minZ) / Cell), z1 = Mathf.CeilToInt((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - minZ) / Cell);
                for (int cz = z0; cz <= z1; cz++)
                    for (int cx = x0; cx <= x1; cx++)
                    {
                        float px = minX + cx * Cell, pz = minZ + cz * Cell;
                        float w0 = ((b.x - px) * (c.z - pz) - (c.x - px) * (b.z - pz)) / area;
                        float w1 = ((c.x - px) * (a.z - pz) - (a.x - px) * (c.z - pz)) / area;
                        float w2 = 1f - w0 - w1;
                        if (w0 < -0.01f || w1 < -0.01f || w2 < -0.01f) continue;
                        Put(cx, cz, w0 * a.y + w1 * b.y + w2 * c.y);
                    }
            }
        }
    }

    static void Close(float[] city, int[] owner, int nx, int nz, int need)
    {
        var add = new List<(int, float, int)>();
        for (int z = 1; z < nz - 1; z++)
            for (int x = 1; x < nx - 1; x++)
            {
                int i = z * nx + x;
                if (!float.IsInfinity(city[i])) continue;
                int n = 0; float sum = 0; int o = -1;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int j = (z + dz) * nx + x + dx;
                        if (j == i || float.IsInfinity(city[j])) continue;
                        n++; sum += city[j]; o = owner[j];
                    }
                if (n >= need) add.Add((i, sum / n, o));
            }
        foreach (var (i, h, o) in add) { city[i] = h; owner[i] = o; }
    }

    static void DistanceFromLand(float[] city, int nx, int nz, float[] dist, float[] nearestH)
    {
        // two-pass chamfer distance transform that also carries the nearest land height
        for (int i = 0; i < dist.Length; i++)
        {
            bool l = !float.IsInfinity(city[i]);
            dist[i] = l ? 0f : 1e9f; nearestH[i] = l ? city[i] : 0f;
        }
        float a = Cell, b = Cell * 1.4142f;
        void Relax(int i, int j, float w) { if (dist[j] + w < dist[i]) { dist[i] = dist[j] + w; nearestH[i] = nearestH[j]; } }
        for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                int i = z * nx + x;
                if (x > 0) Relax(i, i - 1, a);
                if (z > 0) { Relax(i, i - nx, a); if (x > 0) Relax(i, i - nx - 1, b); if (x < nx - 1) Relax(i, i - nx + 1, b); }
            }
        for (int z = nz - 1; z >= 0; z--)
            for (int x = nx - 1; x >= 0; x--)
            {
                int i = z * nx + x;
                if (x < nx - 1) Relax(i, i + 1, a);
                if (z < nz - 1) { Relax(i, i + nx, a); if (x < nx - 1) Relax(i, i + nx + 1, b); if (x > 0) Relax(i, i + nx - 1, b); }
            }
    }

    static Mesh BuildHeightMesh(float[] h, int nx, int nz, float minX, float minZ, string name)
    {
        var verts = new Vector3[nx * nz];
        var cols = new Color[nx * nz];
        var concrete = new Color(0.47f, 0.46f, 0.44f);
        var sand = new Color(0.78f, 0.71f, 0.55f);
        var wetSand = new Color(0.52f, 0.47f, 0.37f);
        var rock = new Color(0.20f, 0.23f, 0.22f);
        for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                int i = z * nx + x;
                float y = h[i];
                verts[i] = new Vector3(minX + x * Cell, y, minZ + z * Cell);
                float above = y - SeaLevel;
                cols[i] = above > 1.0f ? concrete
                    : above > 0.25f ? Color.Lerp(sand, concrete, (above - 0.25f) / 0.75f)
                    : above > -1.2f ? Color.Lerp(wetSand, sand, (above + 1.2f) / 1.45f)
                    : Color.Lerp(rock, wetSand, Mathf.Clamp01((above + 6f) / 4.8f));
            }
        var tris = new int[(nx - 1) * (nz - 1) * 6];
        int t = 0;
        for (int z = 0; z < nz - 1; z++)
            for (int x = 0; x < nx - 1; x++)
            {
                int i = z * nx + x;
                tris[t++] = i; tris[t++] = i + nx; tris[t++] = i + 1;
                tris[t++] = i + 1; tris[t++] = i + nx; tris[t++] = i + nx + 1;
            }
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.vertices = verts; mesh.colors = cols; mesh.triangles = tris;
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return SaveMesh(mesh, name);
    }

    static Mesh GridMesh(float size, int n)
    {
        var verts = new Vector3[(n + 1) * (n + 1)];
        for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
                verts[z * (n + 1) + x] = new Vector3((x / (float)n - 0.5f) * size, 0f, (z / (float)n - 0.5f) * size);
        var tris = new int[n * n * 6];
        int t = 0;
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * (n + 1) + x;
                tris[t++] = i; tris[t++] = i + n + 1; tris[t++] = i + 1;
                tris[t++] = i + 1; tris[t++] = i + n + 1; tris[t++] = i + n + 2;
            }
        var m = new Mesh { indexFormat = IndexFormat.UInt32 };
        m.vertices = verts; m.triangles = tris;
        m.RecalculateNormals();
        m.bounds = new Bounds(Vector3.zero, new Vector3(size, 20f, size));   // room for the waves
        return m;
    }

    // ==================================================================== bridge
    static string BuildBridge(Transform parent, List<GameObject> districts)
    {
        var town = districts.FirstOrDefault(d => d.name == "Town");
        var down = districts.FirstOrDefault(d => d.name == "District_Downtown");
        if (!town || !down) return "no bridge (need Town + District_Downtown)";
        Bounds tb = Encapsulate(new List<GameObject> { town });

        // start: the end of the town's north-south main street, at the north edge
        Vector3 start = new Vector3(tb.center.x, 0f, tb.max.z);
        // end: the nearest point on downtown's road network (its big flat ground meshes)
        Vector3 end = Vector3.zero; float best = float.MaxValue;
        foreach (var mf in down.GetComponentsInChildren<MeshFilter>())
        {
            var b = mf.GetComponent<Renderer>().bounds;
            if (b.size.x < 250f || b.size.y > 20f) continue;               // only the street / sidewalk networks
            var m = mf.transform.localToWorldMatrix;
            foreach (var p in mf.sharedMesh.vertices)
            {
                var w = m.MultiplyPoint3x4(p);
                float d = (new Vector2(w.x, w.z) - new Vector2(start.x, start.z)).sqrMagnitude;
                if (d < best) { best = d; end = w; }
            }
        }
        start.y = GroundY(start + Vector3.back * 2f, 0.15f);
        end.y = GroundY(end, end.y);
        // land a little way into downtown's street, and start a little inside the town
        Vector3 dir = new Vector3(end.x - start.x, 0, end.z - start.z).normalized;
        start -= dir * 3f;
        end += dir * 6f; end.y = GroundY(end, end.y);
        float length = Vector3.Distance(new Vector3(start.x, 0, start.z), new Vector3(end.x, 0, end.z));
        if (length < 12f) return "no bridge needed (already touching)";

        var root = new GameObject("Bridge");
        root.transform.SetParent(parent);

        float width = 14f, arch = Mathf.Min(9f, length * 0.04f);
        int segs = Mathf.CeilToInt(length / 4f);
        Vector3 Path(float t) => Vector3.Lerp(start, end, t) + Vector3.up * (arch * Mathf.Sin(Mathf.PI * t));
        Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;

        // deck: road surface with UVs in meters (asphalt repeats every 14 m)
        var deck = Ribbon(segs, t => Path(t), side, width, 0.9f, length, "BridgeDeck");
        var asphalt = AssetDatabase.FindAssets("asphalt_14x14m t:Texture2D").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
        var deckMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (asphalt != null) deckMat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(asphalt));
        deckMat.SetColor("_BaseColor", new Color(0.85f, 0.85f, 0.85f));
        deckMat.SetFloat("_Smoothness", 0.15f);
        deckMat = Mat("M_BridgeDeck", deckMat);
        var deckGo = new GameObject("Deck", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
        deckGo.transform.SetParent(root.transform);
        deckGo.GetComponent<MeshFilter>().sharedMesh = deck;
        deckGo.GetComponent<MeshRenderer>().sharedMaterial = deckMat;
        deckGo.GetComponent<MeshCollider>().sharedMesh = deck;

        // yellow centre line, white edge lines
        var lineMat = Mat("M_RoadLineYellow", Lit(new Color(0.85f, 0.65f, 0.1f), 0.3f));
        var whiteMat = Mat("M_RoadLineWhite", Lit(new Color(0.9f, 0.9f, 0.88f), 0.3f));
        AddStrip(root.transform, "CenterLine", segs, t => Path(t) + Vector3.up * 0.02f, side, 0.3f, lineMat);
        AddStrip(root.transform, "EdgeLineL", segs, t => Path(t) + side * (width / 2 - 1.2f) + Vector3.up * 0.02f, side, 0.2f, whiteMat);
        AddStrip(root.transform, "EdgeLineR", segs, t => Path(t) - side * (width / 2 - 1.2f) + Vector3.up * 0.02f, side, 0.2f, whiteMat);

        // railings (also stop him running off the side by accident)
        var railMat = Mat("M_BridgeRail", Lit(new Color(0.72f, 0.73f, 0.74f), 0.4f));
        foreach (float s in new[] { 1f, -1f })
        {
            var rail = Ribbon(segs, t => Path(t) + side * s * (width / 2 - 0.25f) + Vector3.up * 1.1f, side, 0.5f, 1.1f, length, "Rail");
            var rg = new GameObject("Railing", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            rg.transform.SetParent(root.transform);
            rg.GetComponent<MeshFilter>().sharedMesh = rail;
            rg.GetComponent<MeshRenderer>().sharedMaterial = railMat;
            rg.GetComponent<MeshCollider>().sharedMesh = rail;
        }

        // pillars down to the sea bed
        var pillarMat = Mat("M_BridgePillar", Lit(new Color(0.55f, 0.55f, 0.53f), 0.1f));
        int pillars = 0;
        for (float d = 20f; d < length - 15f; d += 32f)
        {
            Vector3 top = Path(d / length);
            foreach (float s in new[] { 1f, -1f })
            {
                var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                p.name = "Pillar";
                p.transform.SetParent(root.transform);
                float h = top.y - 0.9f - SeaBed;
                p.transform.position = top + side * s * (width / 2 - 2f) + Vector3.down * (0.9f + h / 2f);
                p.transform.localScale = new Vector3(1.6f, h / 2f, 1.6f);
                p.GetComponent<MeshRenderer>().sharedMaterial = pillarMat;
                pillars++;
            }
        }
        foreach (var t in root.GetComponentsInChildren<Transform>())
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        return $"bridge {length:0} m from {start} to {end}, arch {arch:0.0} m, {pillars} pillars";
    }

    static float GroundY(Vector3 p, float fallback)
    {
        return Physics.Raycast(p + Vector3.up * 50f, Vector3.down, out var hit, 100f) ? hit.point.y : fallback;
    }

    /// A box-shaped ribbon following a path: top, sides and bottom. UV v = meters along the path / 14.
    static Mesh Ribbon(int segs, System.Func<float, Vector3> path, Vector3 side, float width, float thickness, float length, string name)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        Vector3 hw = side * width / 2, down = Vector3.down * thickness;
        // 4 faces: top (L->R), right side, bottom, left side
        var faces = new (System.Func<Vector3, Vector3> a, System.Func<Vector3, Vector3> b)[]
        {
            (c => c - hw, c => c + hw),
            (c => c + hw, c => c + hw + down),
            (c => c + hw + down, c => c - hw + down),
            (c => c - hw + down, c => c - hw),
        };
        foreach (var f in faces)
        {
            int baseIndex = v.Count;
            for (int i = 0; i <= segs; i++)
            {
                float t = i / (float)segs;
                var c = path(t);
                v.Add(f.a(c)); v.Add(f.b(c));
                float vv = t * length / 14f;
                uv.Add(new Vector2(0f, vv)); uv.Add(new Vector2(1f, vv));
            }
            for (int i = 0; i < segs; i++)
            {
                int k = baseIndex + i * 2;
                tri.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
            }
        }
        var m = new Mesh { name = name };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        return SaveMesh(m, name + "_" + Mathf.Abs(path(0f).GetHashCode()));
    }

    static void AddStrip(Transform parent, string name, int segs, System.Func<float, Vector3> path, Vector3 side, float width, Material mat)
    {
        var mesh = Ribbon(segs, path, side, width, 0.01f, 1f, name);
        var g = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        g.transform.SetParent(parent);
        g.GetComponent<MeshFilter>().sharedMesh = mesh;
        g.GetComponent<MeshRenderer>().sharedMaterial = mat;
        g.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    // ==================================================================== misc
    static Material Lit(Color c, float smooth)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth);
        return m;
    }

    static Material Mat(string name, Material m)
    {
        string path = AssetDir + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing) { existing.shader = m.shader; existing.CopyPropertiesFromMaterial(m); EditorUtility.SetDirty(existing); return existing; }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Mesh SaveMesh(Mesh m, string name)
    {
        string path = AssetDir + name + ".asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Bounds Encapsulate(List<GameObject> roots)
    {
        bool any = false; var b = new Bounds();
        foreach (var r in roots.SelectMany(g => g.GetComponentsInChildren<Renderer>()))
        {
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    // ==================================================================== batch test
    public static void TestAll()
    {
        DowntownSetup.Run();
        Test();
    }

    public static void Test()
    {
        Run();
        EditorSceneManager.OpenScene(ScenePath);
        var cam = new GameObject("TestCam").AddComponent<Camera>();
        cam.farClipPlane = 25000f; cam.fieldOfView = 55f;
        var bridge = GameObject.Find("World/Bridge");
        var bb = bridge ? bridge.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; }) : new Bounds();
        Directory.CreateDirectory("Screenshots/world");
        Shot(cam, new Vector3(-700f, 420f, -420f), new Vector3(0f, 0f, 420f), "Screenshots/world/aerial.png");
        if (bridge)
        {
            Shot(cam, bb.center + new Vector3(-120f, 35f, -40f), bb.center, "Screenshots/world/bridge.png");
            Shot(cam, bb.min + new Vector3(bb.size.x * 0.1f, 4f, 0f), bb.center + Vector3.up * 3f, "Screenshots/world/on_bridge.png");
        }
        Shot(cam, new Vector3(-40f, 4f, -45f), new Vector3(-160f, -1f, -200f), "Screenshots/world/shore.png");
        Shot(cam, new Vector3(-60f, 25f, 40f), new Vector3(200f, 5f, 260f), "Screenshots/world/strait.png");
        Debug.Log("[World] TEST DONE");
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
