using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Menu: OverPeak > Setup Space
/// Imports the Earth, Moon, crater, Sun and space-fighter models, builds their materials and places them
/// (with the cloud layer, the space sky and SpaceSystem) in City.unity. Safe to run again.
/// </summary>
public static class SpaceSetup
{
    const string Src = "Assets/Models/Space/";
    const string Gen = "Assets/Generated/Space/";
    const string ScenePath = "Assets/Scenes/City.unity";

    // out to the side of the Earth (about level with its middle), so from the Moon the real Earth stands above the horizon
    public static Vector3 MoonOffset = new Vector3(0f, -30000f, 66000f);
    public static Vector3 EarthEuler = new Vector3(-50f, 0f, 0f);

    [MenuItem("OverPeak/Setup Space")]
    public static void Run()
    {
        Directory.CreateDirectory(Gen);
        ImportSettings();
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var old = GameObject.Find("Space");
        if (old) Object.DestroyImmediate(old);
        var root = new GameObject("Space");
        var ss = root.AddComponent<SpaceSystem>();

        var hero = Object.FindAnyObjectByType<HeroController>();
        var rect = MapRect();
        Vector3 city = new Vector3(rect.center.x, 0f, rect.center.y);

        ss.skyMaterial = Mat("M_SpaceSky", "OverPeak/SpaceSky");
        float lift = ss.earthRadius - Mathf.Sqrt(ss.earthRadius * ss.earthRadius - ss.flatRadius * ss.flatRadius);
        ss.earth = BuildEarth(root.transform, city, ss.earthRadius, lift);
        // what the globe's oceans look like on screen (texture + atmosphere tint): the city's sea fades to this
        ss.cityCutTint = BlueOcean(AverageOcean());
        ss.moon = BuildMoon(root.transform, city + MoonOffset, ss.moonRadius, out ss.crater);
        ss.moonCenterLocal = ss.moon.InverseTransformPoint(city + MoonOffset);
        ss.plasmaMaterial = Mat("M_Plasma", "OverPeak/Plasma");
        string sea = CurveSea(scene, city, ss.earthRadius, ss.flatRadius, lift, ss.cityHoleRadius);
        ss.sunBody = BuildSun(root.transform);
        ss.earthCenterLocal = ss.earth.InverseTransformPoint(new Vector3(city.x, -ss.earthRadius + lift, city.z));
        ss.cloudDeck = BuildCloudDeck(root.transform, ss.cloudHeight);
        ss.cloudSea = BuildCloudSea(root.transform, city, ss.cloudHeight, ss.earthRadius, ss.flatRadius, lift);
        ss.fighterTemplate = BuildFighter(root.transform);
        ss.cityRoots = scene.GetRootGameObjects()
            .Where(g => g.name == "Town" || g.name.StartsWith("District_") || g.name == "World" || g.name == "Breakables").ToArray();
        RenderSettings.skybox = ss.skyMaterial;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Space] city map {rect.size}, {sea}");
        Debug.Log($"[Space] earth r={ss.earthRadius} at {ss.earth.position} | moon r={ss.moonRadius} at {ss.moon.position} | crater {ss.crater.position} | " +
                  $"city roots {string.Join(",", ss.cityRoots.Select(g => g.name))} | fighter {(ss.fighterTemplate ? "ok" : "MISSING")}");
    }

    static Rect MapRect()
    {
        var b = new Bounds(); bool any = false;
        foreach (var g in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (g.name == "Town" || g.name.StartsWith("District_"))
                foreach (var r in g.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
        return Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
    }

    // ================================================================== imports
    static void ImportSettings()
    {
        Tex(Src + "Earth/Albedo.jpeg", 4096, true);
        Tex(Src + "Earth/Clouds.png", 4096, false);
        Tex(Src + "Earth/Ocean_Mask.png", 2048, false);
        Tex(Src + "Moon/Moon_Diffuse.jpeg", 2048, true);
        Tex(Src + "Crater/1_Base_Color.jpg", 2048, true);
        Tex(Src + "Crater/1_Mixed_AO.jpg", 1024, false);
        Normal(Src + "Crater/1_Normal_DirectX.jpg");
        Tex(Src + "Sun/suncyl1.jpg", 2048, true);
        Tex(Src + "Fighter/15_-_Default_Base_Color.png", 2048, true);
        Tex(Src + "Fighter/15_-_Default_Emissive.png", 2048, true);
        Tex(Src + "Fighter/gun_Base_Color.png", 1024, true);
        Normal(Src + "Fighter/15_-_Default_Normal_DirectX.png");
        Normal(Src + "Fighter/gun_Normal_DirectX.png");
        Tex(Src + "Cloud/Cloud_Alpha.png", 1024, true);
        Normal(Src + "Cloud/Cloud_Nor2.png");
        foreach (var m in new[] { "Earth/Earth.obj", "Moon/Moon.fbx", "Crater/Crater.fbx", "Sun/Sun.fbx", "Fighter/Fighter.fbx", "Cloud/Cloud.fbx" })
        {
            var imp = AssetImporter.GetAtPath(Src + m) as ModelImporter;
            if (imp == null) { Debug.LogError("[Space] missing " + m); continue; }
            imp.importAnimation = false; imp.animationType = ModelImporterAnimationType.None;
            imp.importCameras = false; imp.importLights = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            imp.isReadable = m.StartsWith("Crater") || m.StartsWith("Earth") || m.StartsWith("Moon") || m.StartsWith("Cloud");
            imp.SaveAndReimport();
        }
    }

    static void Tex(string path, int max, bool srgb)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogError("[Space] missing " + path); return; }
        ti.maxTextureSize = max; ti.sRGBTexture = srgb; ti.textureType = TextureImporterType.Default;
        ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = path.Contains("Cloud_Alpha");
        ti.SaveAndReimport();
    }

    static void Normal(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) { Debug.LogError("[Space] missing " + path); return; }
        ti.textureType = TextureImporterType.NormalMap;
        ti.flipGreenChannel = true;                          // DirectX-style normal map
        ti.maxTextureSize = 2048;
        ti.SaveAndReimport();
    }

    static Texture2D T(string p) => AssetDatabase.LoadAssetAtPath<Texture2D>(Src + p);

    static Material Mat(string name, string shader)
    {
        string path = Gen + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
        else m.shader = Shader.Find(shader);
        EditorUtility.SetDirty(m);
        return m;
    }

    static GameObject Place(string model, Transform parent, string name)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Src + model);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = name;
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        foreach (var a in go.GetComponentsInChildren<Animator>()) Object.DestroyImmediate(a);
        foreach (var r in go.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
        foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = false;
        return go;
    }

    static Bounds LocalBounds(GameObject go, Transform space)
    {
        var b = new Bounds(); bool any = false;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            var mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z);
                var p = space.InverseTransformPoint(mf.transform.TransformPoint(c));
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
            }
        }
        return b;
    }

    // ================================================================== Earth
    static Transform BuildEarth(Transform parent, Vector3 city, float radius, float lift)
    {
        // The Earth model's surface is lumpy (its radius varies by kilometres), so near the city it could never
        // meet the curved sea. The globe is a perfect sphere instead, wearing the model's Earth textures.
        var go = new GameObject("Earth");
        go.transform.SetParent(parent, false);
        var globe = new GameObject("Globe", typeof(MeshFilter), typeof(MeshRenderer));
        globe.transform.SetParent(go.transform, false);
        globe.GetComponent<MeshFilter>().sharedMesh = Save(Sphere(256, 128), "EarthSphere");
        var surface = Mat("M_EarthSurface", "OverPeak/Planet");
        surface.SetTexture("_BaseMap", T("Earth/Albedo.jpeg"));
        surface.SetTexture("_OceanMask", T("Earth/Ocean_Mask.png"));
        surface.SetColor("_RimColor", new Color(0.35f, 0.6f, 1f));
        surface.SetFloat("_CapCos", 2f);
        surface.SetFloat("_UseCityCut", 1f);
        var rend = globe.GetComponent<MeshRenderer>();
        rend.sharedMaterial = surface;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        var clouds = Mat("M_EarthClouds", "OverPeak/CloudShell");
        clouds.SetTexture("_BaseMap", T("Earth/Clouds.png"));
        var atmo = Mat("M_EarthAtmosphere", "OverPeak/Atmosphere");
        Shell(globe.transform, "EarthClouds", clouds, 1.012f);
        Shell(globe.transform, "EarthAtmosphere", atmo, 1.04f);
        go.transform.localScale = Vector3.one * radius;
        go.transform.rotation = Quaternion.Euler(EarthEuler);
        // centre straight under the city; raised a little so that, outside the hole the city sits in,
        // its surface meets the curved sea exactly (the sea is flat near the city, then follows the globe)
        go.transform.position = new Vector3(city.x, -radius + lift, city.z);
        return go.transform;
    }

    /// Average colour of the oceans in the Earth texture (linear), so the city's sea can blend into the globe.
    static Color AverageOcean()
    {
        var alb = new Texture2D(2, 2); alb.LoadImage(File.ReadAllBytes(Src + "Earth/Albedo.jpeg"));
        var mask = new Texture2D(2, 2); mask.LoadImage(File.ReadAllBytes(Src + "Earth/Ocean_Mask.png"));
        Color sum = Color.clear; int n = 0;
        for (int y = 8; y < 120; y++)                       // skip the polar ice
            for (int x = 0; x < 256; x++)
            {
                float u = (x + 0.5f) / 256f, v = (y + 0.5f) / 128f;
                if (mask.GetPixelBilinear(u, v).r < 0.5f) continue;
                sum += alb.GetPixelBilinear(u, v).linear; n++;
            }
        Object.DestroyImmediate(alb); Object.DestroyImmediate(mask);
        var c = n > 0 ? sum / n : new Color(0.02f, 0.045f, 0.085f);
        c.a = 1f;
        Debug.Log($"[Space] average ocean colour {c} from {n} samples");
        return c;
    }

    static Color BlueOcean(Color avg)
    {
        // keep the texture's brightness, give it the blue the atmosphere adds when seen from space
        float lum = avg.r * 0.3f + avg.g * 0.59f + avg.b * 0.11f;
        var c = new Color(0.05f, 0.16f, 0.45f) * Mathf.Clamp(lum / 0.115f, 0.6f, 1.4f);
        c.a = 1f;
        return c;
    }

    /// Unit sphere with equirectangular UVs (u = longitude, v = latitude), faces pointing out.
    static Mesh Sphere(int lon, int lat)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        for (int a = 0; a <= lat; a++)
        {
            float phi = Mathf.PI * (a / (float)lat - 0.5f);                 // -90..90 degrees
            for (int b = 0; b <= lon; b++)
            {
                float th = 2f * Mathf.PI * b / lon;
                v.Add(new Vector3(Mathf.Cos(phi) * Mathf.Cos(th), Mathf.Sin(phi), Mathf.Cos(phi) * Mathf.Sin(th)));
                uv.Add(new Vector2(b / (float)lon, a / (float)lat));
            }
        }
        for (int a = 0; a < lat; a++)
            for (int b = 0; b < lon; b++)
            {
                int i0 = a * (lon + 1) + b, i1 = i0 + 1, i2 = i0 + lon + 1, i3 = i2 + 1;
                t.Add(i0); t.Add(i1); t.Add(i2);
                t.Add(i1); t.Add(i3); t.Add(i2);
            }
        var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateNormals();
        // make sure the faces point outward
        if (Vector3.Dot(m.normals[(lat / 2) * (lon + 1) + 3], v[(lat / 2) * (lon + 1) + 3]) < 0f)
        {
            t.Reverse(); m.SetTriangles(t, 0); m.RecalculateNormals();
        }
        m.SetNormals(v);
        m.RecalculateTangents();
        m.RecalculateBounds();
        return m;
    }

    // ================================================================== Moon + crater landing site
    static void Shell(Transform globe, string name, Material mat, float scale)
    {
        var mf = globe.GetComponent<MeshFilter>();
        var g = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        g.transform.SetParent(globe, false);
        g.transform.localScale = Vector3.one * scale;
        g.GetComponent<MeshFilter>().sharedMesh = mf.sharedMesh;      // same sphere, so the cloud map lines up too
        var r = g.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    static Transform BuildMoon(Transform parent, Vector3 center, float radius, out Transform crater)
    {
        var go = Place("Moon/Moon.fbx", parent, "Moon");
        var mat = Mat("M_Moon", "OverPeak/Planet");
        mat.SetTexture("_BaseMap", T("Moon/Moon_Diffuse.jpeg"));
        mat.SetColor("_RimColor", new Color(0f, 0f, 0f));
        mat.SetFloat("_Specular", 0f);
        mat.SetColor("_NightColor", new Color(0.004f, 0.004f, 0.006f));
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = mat;
        var lb = LocalBounds(go, go.transform);
        float s = radius / Mathf.Max(lb.extents.x, Mathf.Max(lb.extents.y, lb.extents.z));
        go.transform.localScale = Vector3.one * s;
        go.transform.position = center - go.transform.TransformVector(lb.center);

        // the landing site cuts a hole in the top of the sphere: hide that cap in the shader
        const float siteRadius = 1300f;
        var mr = go.GetComponentInChildren<MeshRenderer>();
        Vector3 capObj = mr.transform.InverseTransformDirection(Vector3.up).normalized;
        mat.SetVector("_CapDir", capObj);
        mat.SetFloat("_CapCos", Mathf.Cos(Mathf.Asin(siteRadius / radius)));
        // close up the blurry global map gets fine detail from the crater scan's textures
        mat.SetTexture("_DetailMap", T("Crater/1_Base_Color.jpg"));
        mat.SetTexture("_DetailBump", T("Crater/1_Normal_DirectX.jpg"));
        mat.SetVector("_DetailTiling", new Vector4(90f, 45f, 0f, 0f));
        mat.SetFloat("_DetailDistance", 2500f);
        // physics: the Moon's own surface (so he walks on what he sees), minus the hole the crater fills
        var mf = mr.GetComponent<MeshFilter>();
        var src = mf.sharedMesh;
        var mv = src.vertices;
        float capCos = Mathf.Cos(Mathf.Asin(siteRadius / radius));
        var keep = new List<int>();
        var all = src.triangles;
        for (int i = 0; i < all.Length; i += 3)
        {
            Vector3 cc = (mv[all[i]] + mv[all[i + 1]] + mv[all[i + 2]]) / 3f;
            if (Vector3.Dot(cc.normalized, capObj) < capCos) { keep.Add(all[i]); keep.Add(all[i + 1]); keep.Add(all[i + 2]); }
        }
        var col = new Mesh { name = "MoonCollider", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        col.vertices = mv; col.SetTriangles(keep, 0); col.RecalculateBounds();
        AssetDatabase.DeleteAsset(Gen + "MoonCollider.asset");
        AssetDatabase.CreateAsset(col, Gen + "MoonCollider.asset");
        var mc = mr.gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = col;
        // it turns under his feet while he walks: a kinematic body moves cleanly
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true; rb.useGravity = false;
        Debug.Log($"[Space] moon mesh {mv.Length} verts, {all.Length / 3} tris, collider {keep.Count / 3} tris");

        crater = BuildCrater(parent, center + Vector3.up * radius, radius, out _);
        crater.SetParent(go.transform, true);
        ScatterRocks(go.transform, mc, center, radius, siteRadius);
        return go.transform;
    }

    static Transform BuildCrater(Transform parent, Vector3 top, float moonRadius, out float depth)
    {
        var src = Place("Crater/Crater.fbx", parent, "CraterSource");
        var mf = src.GetComponentInChildren<MeshFilter>();
        var mesh = mf.sharedMesh;
        var verts = mesh.vertices.Select(v => mf.transform.TransformPoint(v) - src.transform.position).ToArray();
        // centre it, find the rim height, then bend it onto the sphere so its edge meets the Moon's surface
        var b = new Bounds(verts[0], Vector3.zero); foreach (var v in verts) b.Encapsulate(v);
        float maxR = b.extents.x;
        var rim = verts.Where(v => new Vector2(v.x - b.center.x, v.z - b.center.z).magnitude > maxR * 0.85f).Select(v => v.y).OrderBy(y => y).ToList();
        float rimH = rim.Count > 0 ? rim[rim.Count / 2] : b.center.y;
        // the scan is ~840 m deep: squash it to a walkable bowl
        float rawDepth = rimH - b.min.y;
        float k = Mathf.Min(1f, 160f / Mathf.Max(rawDepth, 1f));
        depth = rawDepth * k;
        var bent = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            float x = verts[i].x - b.center.x, z = verts[i].z - b.center.z;
            float r = Mathf.Sqrt(x * x + z * z);
            float drop = moonRadius - Mathf.Sqrt(Mathf.Max(0f, moonRadius * moonRadius - r * r));
            bent[i] = new Vector3(x, (verts[i].y - rimH) * k - drop + 4f, z);
        }
        var m = Object.Instantiate(mesh);
        m.name = "CraterOnMoon";
        m.vertices = bent;
        // round: drop the square corners (they would stick out past the Moon's curve)
        var tris = new List<int>();
        var all = mesh.triangles;
        for (int i = 0; i < all.Length; i += 3)
        {
            Vector3 c = (bent[all[i]] + bent[all[i + 1]] + bent[all[i + 2]]) / 3f;
            if (new Vector2(c.x, c.z).magnitude < maxR * 0.97f) { tris.Add(all[i]); tris.Add(all[i + 1]); tris.Add(all[i + 2]); }
        }
        m.subMeshCount = 1;
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        // the import can leave it facing down: make it face up so it is visible and solid from above
        float up = 0f; foreach (var n in m.normals) up += n.y;
        if (up < 0f) { tris.Reverse(); m.SetTriangles(tris, 0); m.RecalculateNormals(); }
        m.RecalculateBounds(); m.RecalculateTangents();
        AssetDatabase.DeleteAsset(Gen + "CraterOnMoon.asset");
        AssetDatabase.CreateAsset(m, Gen + "CraterOnMoon.asset");
        // collision from both sides, so nothing can slip through it
        var col = new Mesh { name = "CraterCollider" };
        col.vertices = bent;
        var both = new List<int>(tris);
        for (int i = 0; i < tris.Count; i += 3) { both.Add(tris[i]); both.Add(tris[i + 2]); both.Add(tris[i + 1]); }
        col.SetTriangles(both, 0);
        col.RecalculateBounds();
        AssetDatabase.AddObjectToAsset(col, Gen + "CraterOnMoon.asset");
        Debug.Log($"[Space] crater faced {(up < 0f ? "down -> flipped up" : "up")}, {tris.Count / 3} triangles, depth {rawDepth:0} m -> {depth:0} m");

        var go = new GameObject("MoonCrater", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
        go.transform.SetParent(parent);
        go.transform.position = top;
        go.GetComponent<MeshFilter>().sharedMesh = m;
        go.GetComponent<MeshCollider>().sharedMesh = col;
        var mat = Mat("M_Crater", "Universal Render Pipeline/Lit");
        mat.SetTexture("_BaseMap", T("Crater/1_Base_Color.jpg"));
        mat.SetTexture("_BumpMap", T("Crater/1_Normal_DirectX.jpg")); mat.EnableKeyword("_NORMALMAP");
        mat.SetTexture("_OcclusionMap", T("Crater/1_Mixed_AO.jpg")); mat.EnableKeyword("_OCCLUSIONMAP");
        mat.SetFloat("_Smoothness", 0.08f);
        mat.SetColor("_BaseColor", new Color(0.82f, 0.8f, 0.78f));
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        // its UVs were made for the original 3 km patch: keep them; the texture covers it once
        Object.DestroyImmediate(src);
        return go.transform;
    }

    // ================================================================== Sun
    static Transform BuildSun(Transform parent)
    {
        var go = Place("Sun/Sun.fbx", parent, "Sun");
        var mat = Mat("M_Sun", "Universal Render Pipeline/Unlit");
        mat.SetTexture("_BaseMap", T("Sun/suncyl1.jpg"));
        mat.SetColor("_BaseColor", new Color(4f, 2.6f, 1.3f));
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
        {
            bool core = r.name.ToLowerInvariant().Contains("core");
            r.gameObject.SetActive(core || go.GetComponentsInChildren<MeshRenderer>(true).Length == 1);
            r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
        }
        var lb = LocalBounds(go, go.transform);
        go.transform.localScale = Vector3.one * (3500f / Mathf.Max(lb.extents.x, 0.001f));
        return go.transform;
    }

    // ================================================================== cloud layer
    static Transform BuildCloudDeck(Transform parent, float height)
    {
        var g = new GameObject("CloudDeck", typeof(MeshFilter), typeof(MeshRenderer));
        g.GetComponent<MeshFilter>().sharedMesh = Save(Grid(10f, 96), "CloudDeckGrid");
        g.transform.SetParent(parent);
        g.transform.position = new Vector3(0f, height, 0f);
        g.transform.localScale = new Vector3(3200f, 1f, 3200f);          // 32 km, follows the camera
        var r = g.GetComponent<MeshRenderer>();
        r.sharedMaterial = Mat("M_CloudDeck", "OverPeak/CloudDeck");
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g.transform;
    }

    // ================================================================== the city's sea, curved with the Earth
    /// The far sea becomes a round disc: flat around the city, then curving down exactly like the globe, so the
    /// city fits into the hole cut in the top of the Earth and looks right from high up.
    static string CurveSea(UnityEngine.SceneManagement.Scene scene, Vector3 city, float R, float flat, float lift, float hole)
    {
        var far = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshFilter>(true)).FirstOrDefault(f => f.name == "OceanFar");
        if (!far) return "no OceanFar found";
        float rMax = hole + 15f;
        const int rings = 90, segs = 192;
        var verts = new List<Vector3> { Vector3.zero };
        var tris = new List<int>();
        Vector3 c = far.transform.InverseTransformPoint(new Vector3(city.x, far.transform.position.y, city.z));
        for (int r = 1; r <= rings; r++)
        {
            float t = r / (float)rings;
            float rad = rMax * t * t * 0.4f + rMax * t * 0.6f;               // denser near the city
            float drop = rad > flat ? (R - Mathf.Sqrt(R * R - rad * rad)) - lift : 0f;
            for (int k = 0; k < segs; k++)
            {
                float a = k * Mathf.PI * 2f / segs;
                verts.Add(c + new Vector3(Mathf.Cos(a) * rad, -drop, Mathf.Sin(a) * rad));
            }
        }
        for (int k = 0; k < segs; k++) { tris.Add(0); tris.Add(1 + (k + 1) % segs); tris.Add(1 + k); }
        for (int r = 1; r < rings; r++)
            for (int k = 0; k < segs; k++)
            {
                int a = 1 + (r - 1) * segs + k, b = 1 + (r - 1) * segs + (k + 1) % segs, a2 = a + segs, b2 = b + segs;
                tris.Add(a); tris.Add(b); tris.Add(a2);
                tris.Add(b); tris.Add(b2); tris.Add(a2);
            }
        var m = new Mesh { name = "OceanCurved", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        m.SetVertices(verts); m.SetTriangles(tris, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        far.sharedMesh = Save(m, "OceanCurved");
        return $"sea curved: {verts.Count} verts, edge {rMax:0} m drops {(R - Mathf.Sqrt(R * R - rMax * rMax)) - lift:0} m";
    }

    static Mesh Grid(float size, int n)
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
        var m = new Mesh();
        m.vertices = verts; m.triangles = tris;
        m.RecalculateNormals();
        m.bounds = new Bounds(Vector3.zero, new Vector3(size, 14000f, size));  // room for the curve
        return m;
    }

    static Mesh Save(Mesh m, string name)
    {
        m.name = name;
        AssetDatabase.DeleteAsset(Gen + name + ".asset");
        AssetDatabase.CreateAsset(m, Gen + name + ".asset");
        return m;
    }

    // ================================================================== Moon rocks
    static void ScatterRocks(Transform moon, MeshCollider surface, Vector3 center, float radius, float siteRadius)
    {
        var meshes = new Mesh[5];
        for (int i = 0; i < meshes.Length; i++) meshes[i] = Save(RockMesh(i), "MoonRock" + i);
        var mat = Mat("M_MoonRock", "Universal Render Pipeline/Lit");
        mat.SetTexture("_BaseMap", T("Crater/1_Base_Color.jpg"));
        mat.SetTextureScale("_BaseMap", new Vector2(3f, 3f));
        mat.SetTexture("_BumpMap", T("Crater/1_Normal_DirectX.jpg")); mat.EnableKeyword("_NORMALMAP");
        mat.SetColor("_BaseColor", new Color(0.78f, 0.76f, 0.74f));
        mat.SetFloat("_Smoothness", 0.05f);
        mat.enableInstancing = true;
        var root = new GameObject("Rocks").transform;
        root.SetParent(moon, false);
        Physics.SyncTransforms();
        var rng = new System.Random(7);
        float R() => (float)rng.NextDouble();
        int placed = 0;
        for (int i = 0; i < 2600; i++)
        {
            Vector3 dir = new Vector3(R() * 2 - 1, R() * 2 - 1, R() * 2 - 1);
            if (dir.sqrMagnitude > 1f || dir.sqrMagnitude < 0.01f) { i--; continue; }
            dir.Normalize();
            if (Vector3.Dot(dir, Vector3.up) > Mathf.Cos(Mathf.Asin((siteRadius + 60f) / radius))) continue;   // not in the crater site
            if (!surface.Raycast(new Ray(center + dir * (radius * 1.2f), -dir), out var hit, radius)) continue;
            // mostly small stones, some boulders, a few huge ones
            float u = R();
            float size = u < 0.75f ? Mathf.Lerp(0.6f, 2.5f, R()) : u < 0.97f ? Mathf.Lerp(2.5f, 9f, R()) : Mathf.Lerp(12f, 30f, R());
            var go = new GameObject("Rock", typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = meshes[rng.Next(meshes.Length)];
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            go.transform.position = hit.point - dir * size * 0.3f;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir) * Quaternion.Euler(0f, R() * 360f, 0f);
            go.transform.localScale = new Vector3(size * Mathf.Lerp(0.8f, 1.3f, R()), size * Mathf.Lerp(0.45f, 0.8f, R()), size * Mathf.Lerp(0.8f, 1.3f, R()));
            if (size > 1.5f) go.AddComponent<SphereCollider>().radius = 0.75f;
            go.transform.SetParent(root, true);
            placed++;
        }
        Debug.Log($"[Space] moon rocks: {placed}");
    }

    /// A lumpy stone: a sphere pushed in and out by noise, flattened underneath.
    static Mesh RockMesh(int seed)
    {
        const int lat = 10, lon = 14;
        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
        float ox = seed * 13.7f, oz = seed * 7.1f;
        for (int a = 0; a <= lat; a++)
            for (int b = 0; b <= lon; b++)
            {
                float th = Mathf.PI * a / lat, ph = Mathf.PI * 2f * b / lon;
                Vector3 d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                Vector3 q = d * 1.7f;
                float n = Mathf.PerlinNoise(q.x + ox + 50f, q.z + oz + 50f) * 0.6f + Mathf.PerlinNoise(q.y * 2f + ox, q.x * 2f + oz) * 0.4f;
                float r = 0.5f * (0.75f + n * 0.5f);
                Vector3 p = d * r;
                if (p.y < -0.15f) p.y = -0.15f + (p.y + 0.15f) * 0.3f;
                verts.Add(p);
                uvs.Add(new Vector2(b / (float)lon, a / (float)lat));
            }
        for (int a = 0; a < lat; a++)
            for (int b = 0; b < lon; b++)
            {
                int i = a * (lon + 1) + b;
                tris.Add(i); tris.Add(i + 1); tris.Add(i + lon + 1);
                tris.Add(i + 1); tris.Add(i + lon + 2); tris.Add(i + lon + 1);
            }
        var m = new Mesh();
        m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
        m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
        return m;
    }

    // ================================================================== 3D clouds (the cloud model)
    /// The cloud model is ~11,500 soft cards. Two lighter versions are made from it (fewer, bigger cards)
    /// with normals pointing out of the cloud's middle so it lights like one puffy volume, then copies of it
    /// are scattered round the city: a clearing over the city, a thick sea of clouds further out.
    static Transform BuildCloudSea(Transform parent, Vector3 city, float top, float R, float flat, float lift)
    {
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Src + "Cloud/Cloud.fbx");
        var srcMf = fbx.GetComponentInChildren<MeshFilter>();
        var src = srcMf.sharedMesh;
        var xf = srcMf.transform.localToWorldMatrix;
        var v = src.vertices.Select(p => xf.MultiplyPoint3x4(p)).ToArray();
        var uv = src.uv;
        var tri = src.triangles;
        // cards = groups of triangles joined by shared corners (matched by position)
        var parentOf = Enumerable.Range(0, v.Length).ToArray();
        int Find(int a) { while (parentOf[a] != a) { parentOf[a] = parentOf[parentOf[a]]; a = parentOf[a]; } return a; }
        var byPos = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < v.Length; i++)
        {
            var key = Vector3Int.RoundToInt(v[i] * 10000f);
            if (byPos.TryGetValue(key, out int j)) parentOf[Find(i)] = Find(j); else byPos[key] = i;
        }
        for (int t = 0; t < tri.Length; t += 3) { parentOf[Find(tri[t + 1])] = Find(tri[t]); parentOf[Find(tri[t + 2])] = Find(tri[t]); }
        var cards = new Dictionary<int, List<int>>();
        for (int t = 0; t < tri.Length; t += 3)
        {
            int r = Find(tri[t]);
            if (!cards.TryGetValue(r, out var l)) cards[r] = l = new List<int>();
            l.Add(t);
        }
        var b = new Bounds(v[0], Vector3.zero); foreach (var p in v) b.Encapsulate(p);
        Vector3 puffCentre = new Vector3(b.center.x, b.min.y + b.size.y * 0.35f, b.center.z);

        Mesh MakeLod(float keep, float grow, string name)
        {
            var rng = new System.Random(11);
            var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nu = new List<Vector2>(); var nt = new List<int>();
            foreach (var card in cards.Values)
            {
                if (rng.NextDouble() > keep) continue;
                var map = new Dictionary<int, int>();
                Vector3 c = Vector3.zero; int n = 0;
                foreach (int t in card) for (int k = 0; k < 3; k++) { c += v[tri[t + k]]; n++; }
                c /= n;
                foreach (int t in card)
                    for (int k = 0; k < 3; k++)
                    {
                        int vi = tri[t + k];
                        if (!map.TryGetValue(vi, out int ni))
                        {
                            ni = nv.Count; map[vi] = ni;
                            Vector3 p = c + (v[vi] - c) * grow;
                            nv.Add(p - new Vector3(b.center.x, b.min.y, b.center.z));          // pivot at the cloud's base centre
                            Vector3 o = p - puffCentre;
                            nn.Add(new Vector3(o.x / b.extents.x, o.y / b.extents.y, o.z / b.extents.z).normalized);
                            nu.Add(uv[vi]);
                        }
                        nt.Add(ni);
                    }
            }
            var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(nv); m.SetNormals(nn); m.SetUVs(0, nu); m.SetTriangles(nt, 0);
            m.RecalculateBounds();
            return Save(m, name);
        }
        var lod0 = MakeLod(0.25f, 1.6f, "CloudLOD0");
        var lod1 = MakeLod(0.06f, 2.6f, "CloudLOD1");

        var mat = Mat("M_CloudCard", "OverPeak/CloudCard");
        mat.SetTexture("_BaseMap", T("Cloud/Cloud_Alpha.png"));
        mat.SetTexture("_BumpMap", T("Cloud/Cloud_Nor2.png"));
        mat.enableInstancing = true;

        var root = new GameObject("CloudSea").transform;
        root.SetParent(parent, false);
        var rng2 = new System.Random(5);
        float Rn() => (float)rng2.NextDouble();
        int placed = 0;
        for (int i = 0; i < 600 && placed < 120; i++)
        {
            float a = Rn() * Mathf.PI * 2f;
            float r = Mathf.Lerp(1600f, 12000f, Mathf.Pow(Rn(), 0.8f));
            if (Rn() > Mathf.InverseLerp(1000f, 6000f, r) + 0.15f) continue;      // fewer near the city
            float size = Mathf.Lerp(22f, 60f, Rn()) * Mathf.Lerp(0.8f, 2.2f, Mathf.InverseLerp(1600f, 12000f, r));   // bigger far out
            float drop = r > flat ? (R - Mathf.Sqrt(R * R - r * r)) - lift : 0f;
            float h = b.size.y * size;
            var pos = new Vector3(city.x + Mathf.Cos(a) * r, top - h * 0.55f - drop + Mathf.Lerp(-60f, 60f, Rn()), city.z + Mathf.Sin(a) * r);
            var go = new GameObject("Cloud");
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, Rn() * 360f, 0f);
            go.transform.localScale = new Vector3(size, size * Mathf.Lerp(0.7f, 1.1f, Rn()), size);
            var lods = new LOD[2];
            for (int l = 0; l < 2; l++)
            {
                var c = new GameObject("LOD" + l, typeof(MeshFilter), typeof(MeshRenderer));
                c.transform.SetParent(go.transform, false);
                c.GetComponent<MeshFilter>().sharedMesh = l == 0 ? lod0 : lod1;
                var mr = c.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                lods[l] = new LOD(l == 0 ? 0.1f : 0.006f, new Renderer[] { mr });
            }
            var lg = go.AddComponent<LODGroup>();
            lg.SetLODs(lods);
            lg.RecalculateBounds();
            placed++;
        }
        Debug.Log($"[Space] cloud model: {cards.Count} cards -> LOD0 {lod0.triangles.Length / 3} tris, LOD1 {lod1.triangles.Length / 3} tris; {placed} clouds placed, size {b.size}");
        return root;
    }

    // ================================================================== fighter template
    static GameObject BuildFighter(Transform parent)
    {
        var root = new GameObject("FighterTemplate");
        root.transform.SetParent(parent);
        var model = Place("Fighter/Fighter.fbx", root.transform, "Model");
        var body = Mat("M_Fighter", "Universal Render Pipeline/Lit");
        body.SetTexture("_BaseMap", T("Fighter/15_-_Default_Base_Color.png"));
        body.SetTexture("_BumpMap", T("Fighter/15_-_Default_Normal_DirectX.png")); body.EnableKeyword("_NORMALMAP");
        body.SetTexture("_EmissionMap", T("Fighter/15_-_Default_Emissive.png"));
        body.SetColor("_EmissionColor", new Color(2.5f, 2.5f, 2.5f)); body.EnableKeyword("_EMISSION");
        body.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        body.SetFloat("_Metallic", 0.5f); body.SetFloat("_Smoothness", 0.55f);
        var gun = Mat("M_FighterGun", "Universal Render Pipeline/Lit");
        gun.SetTexture("_BaseMap", T("Fighter/gun_Base_Color.png"));
        gun.SetTexture("_BumpMap", T("Fighter/gun_Normal_DirectX.png")); gun.EnableKeyword("_NORMALMAP");
        gun.SetFloat("_Metallic", 0.8f); gun.SetFloat("_Smoothness", 0.5f);
        foreach (var r in model.GetComponentsInChildren<MeshRenderer>())
            r.sharedMaterials = r.sharedMaterials.Select(m => m && m.name.ToLowerInvariant().Contains("gun") ? gun : body).ToArray();

        // nose = the end of the longest axis nearest the gun barrel; turn it to +Z and size it to ~14 m
        var lb = LocalBounds(model, root.transform);
        int axis = lb.size.x >= lb.size.y && lb.size.x >= lb.size.z ? 0 : lb.size.y >= lb.size.z ? 1 : 2;
        var barrel = model.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r => r.name.ToLowerInvariant().Contains("barrel"));
        float sign = 1f;
        if (barrel) sign = Mathf.Sign(root.transform.InverseTransformPoint(barrel.bounds.center)[axis] - lb.center[axis]);
        Vector3 nose = Vector3.zero; nose[axis] = sign;
        model.transform.rotation = Quaternion.FromToRotation(nose, Vector3.forward) * model.transform.rotation;
        lb = LocalBounds(model, root.transform);
        float s = 14f / lb.size.z;
        model.transform.localScale *= s;
        lb = LocalBounds(model, root.transform);
        // the hull should face up: if it is upside down (more height below centre), the gun hangs under it — that's fine
        model.transform.localPosition -= lb.center;
        lb = LocalBounds(model, root.transform);
        var box = root.AddComponent<BoxCollider>();
        box.center = lb.center; box.size = lb.size;
        root.SetActive(false);
        Debug.Log($"[Space] fighter size {lb.size} nose axis {axis} sign {sign}");
        return root;
    }
}
