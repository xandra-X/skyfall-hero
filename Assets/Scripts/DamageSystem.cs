using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// World damage manager (one per scene):
///   - holes in buildings (sent to the OverPeak/Damageable shader as capsules; they move with collapsing buildings)
///   - swaps every Destructible's materials to the damageable shader at start
///   - building collapse (sinking, tilting, dust cloud, falling debris, rubble pile)
///   - hit-stop (a tiny freeze on big impacts)
/// </summary>
public class DamageSystem : MonoBehaviour
{
    public static DamageSystem Instance { get; private set; }

    [Tooltip("Assigned by OverPeak > Setup Destruction so the shader is included in builds")]
    public Shader damageShader;

    public const int MaxHoles = 256;

    [Header("Healing")]
    [Tooltip("Seconds a hole stays fully open after it was last hit")]
    public float healDelay = 8f;
    [Tooltip("Seconds it takes to close")]
    public float healDuration = 2.5f;
    public float HealTotal => healDelay + healDuration;

    public readonly struct HoleRef
    {
        public readonly int index, serial;
        public HoleRef(int i, int s) { index = i; serial = s; }
    }

    struct Hole
    {
        public Destructible owner;
        public Vector3 localA, localB;
        public float radius, birth;
        public bool laser;
        public int serial;
        public float touched, fullRadius;     // last time it was hit, size before healing started
    }

    readonly Hole[] holes = new Hole[MaxHoles];
    readonly Vector4[] holeA = new Vector4[MaxHoles];
    readonly Vector4[] holeB = new Vector4[MaxHoles];
    int count, serials;
    bool dirty = true;
    readonly Dictionary<Material, Material> converted = new Dictionary<Material, Material>();
    readonly List<Destructible> moving = new List<Destructible>();

    public Destructible LastDamaged { get; private set; }
    public float LastDamageTime { get; private set; } = -100f;

    float hitStopUntil;
    float hitStopScale = 1f;

    public static DamageSystem Ensure()
    {
        if (!Instance) new GameObject("DamageSystem").AddComponent<DamageSystem>();
        return Instance;
    }

    void Awake()
    {
        Instance = this;
        if (!damageShader) damageShader = Shader.Find("OverPeak/Damageable");
        Push();
        foreach (var d in FindObjectsByType<Destructible>())
        {
            d.Init();
            ConvertMaterials(d);
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Time.timeScale = 1f;
        Shader.SetGlobalInt("_HoleCount", 0);
    }

    // ================================================================== holes
    public HoleRef AddHole(Destructible owner, Vector3 worldA, Vector3 worldB, float radius, bool laser)
    {
        int i;
        if (count < MaxHoles) i = count++;
        else i = OldestReplaceable();
        holes[i] = new Hole
        {
            owner = owner,
            localA = owner.transform.InverseTransformPoint(worldA),
            localB = owner.transform.InverseTransformPoint(worldB),
            radius = radius,
            birth = laser ? Time.timeSinceLevelLoad : -1f,
            laser = laser,
            serial = ++serials,
            touched = Time.time,
            fullRadius = radius,
        };
        Write(i);
        return new HoleRef(i, holes[i].serial);
    }

    int OldestReplaceable()
    {
        // prefer the oldest small hole (pieces of a laser cut) so big holes stay open as long as possible
        int oldestSmall = -1, oldest = 0;
        for (int i = 0; i < count; i++)
        {
            if (holes[i].serial < holes[oldest].serial) oldest = i;
            if (holes[i].radius < 0.45f && (oldestSmall < 0 || holes[i].serial < holes[oldestSmall].serial)) oldestSmall = i;
        }
        return oldestSmall >= 0 ? oldestSmall : oldest;
    }

    public bool Valid(HoleRef h) => h.index >= 0 && h.index < count && holes[h.index].serial == h.serial;
    public float Radius(HoleRef h) => Valid(h) ? holes[h.index].radius : 0f;

    /// Grow a hole toward targetRadius. Returns the new radius.
    public float Grow(HoleRef h, float targetRadius, float rate)
    {
        if (!Valid(h)) return 0f;
        holes[h.index].radius = Mathf.MoveTowards(holes[h.index].radius, targetRadius, rate * Time.deltaTime);
        holes[h.index].fullRadius = holes[h.index].radius;
        holes[h.index].touched = Time.time;
        Write(h.index);
        return holes[h.index].radius;
    }

    /// Is this point inside a hole of this building? Returns the hole it is most inside.
    public bool FindHole(Destructible owner, Vector3 p, out HoleRef hole, float shrink = 0.85f)
    {
        hole = new HoleRef(-1, 0);
        float best = 0f;
        for (int i = 0; i < count; i++)
        {
            if (holes[i].owner != owner) continue;
            Vector3 a = holeA[i], b = holeB[i];
            float depth = holes[i].radius * shrink - DistanceToSegment(p, a, b);
            if (depth > best) { best = depth; hole = new HoleRef(i, holes[i].serial); }
        }
        return hole.index >= 0;
    }

    public static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-5f));
        return Vector3.Distance(p, a + ab * t);
    }

    void Write(int i)
    {
        var h = holes[i];
        if (!h.owner) { holeA[i] = new Vector4(0, -9999, 0, 0); holeB[i] = new Vector4(0, -9999, 0, -1); dirty = true; return; }
        var t = h.owner.transform;
        Vector3 a = t.TransformPoint(h.localA), b = t.TransformPoint(h.localB);
        holeA[i] = new Vector4(a.x, a.y, a.z, h.radius);
        holeB[i] = new Vector4(b.x, b.y, b.z, h.birth);
        dirty = true;
    }

    void RemoveAt(int i)
    {
        count--;
        holes[i] = holes[count];
        holeA[i] = holeA[count];
        holeB[i] = holeB[count];
        dirty = true;
    }

    void RemoveHolesOf(Destructible owner)
    {
        for (int i = count - 1; i >= 0; i--)
        {
            if (holes[i].owner != owner) continue;
            count--;
            holes[i] = holes[count];
            holeA[i] = holeA[count];
            holeB[i] = holeB[count];
        }
        dirty = true;
    }

    void Push()
    {
        Shader.SetGlobalVectorArray("_HoleA", holeA);
        Shader.SetGlobalVectorArray("_HoleB", holeB);
        Shader.SetGlobalInt("_HoleCount", count);
        dirty = false;
    }

    void LateUpdate()
    {
        // heal: after healDelay a hole shrinks closed, then it is removed
        for (int i = count - 1; i >= 0; i--)
        {
            float age = Time.time - holes[i].touched;
            if (age < healDelay) continue;
            float k = (age - healDelay) / healDuration;
            if (k >= 1f || !holes[i].owner) { RemoveAt(i); continue; }
            holes[i].radius = holes[i].fullRadius * (1f - Mathf.SmoothStep(0f, 1f, k));
            Write(i);
        }
        foreach (var d in moving)
            for (int i = 0; i < count; i++)
                if (holes[i].owner == d) Write(i);
        if (dirty) Push();
    }

    void Update()
    {
        // hit-stop: freeze time briefly on big hits
        Time.timeScale = Time.unscaledTime < hitStopUntil ? hitStopScale : 1f;
    }

    public void HitStop(float seconds, float scale = 0.04f)
    {
        hitStopUntil = Mathf.Max(hitStopUntil, Time.unscaledTime + seconds);
        hitStopScale = scale;
    }

    public void NoteDamage(Destructible d)
    {
        LastDamaged = d;
        LastDamageTime = Time.time;
    }

    // ================================================================== materials
    void ConvertMaterials(Destructible d)
    {
        if (!damageShader) return;
        foreach (var r in d.Renderers)
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i]) mats[i] = Convert(mats[i]);
            r.sharedMaterials = mats;
        }
    }

    Material Convert(Material src)
    {
        if (src.shader == damageShader) return src;
        if (converted.TryGetValue(src, out var m)) return m;
        m = new Material(damageShader) { name = src.name + " (Damageable)" };
        string baseTex = src.HasProperty("_BaseMap") ? "_BaseMap" : src.HasProperty("_MainTex") ? "_MainTex" : null;
        if (baseTex != null)
        {
            m.SetTexture("_BaseMap", src.GetTexture(baseTex));
            m.SetTextureScale("_BaseMap", src.GetTextureScale(baseTex));
            m.SetTextureOffset("_BaseMap", src.GetTextureOffset(baseTex));
        }
        Color c = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
        c.a = 1f;
        m.SetColor("_BaseColor", c);
        if (src.HasProperty("_BumpMap") && src.GetTexture("_BumpMap"))
        {
            m.SetTexture("_BumpMap", src.GetTexture("_BumpMap"));
            m.SetFloat("_BumpScale", src.HasProperty("_BumpScale") ? src.GetFloat("_BumpScale") : 1f);
            m.EnableKeyword("_NORMALMAP");
        }
        m.SetFloat("_Smoothness", src.HasProperty("_Smoothness") ? src.GetFloat("_Smoothness") : src.HasProperty("_Glossiness") ? src.GetFloat("_Glossiness") : 0.3f);
        m.SetFloat("_Metallic", src.HasProperty("_Metallic") ? src.GetFloat("_Metallic") : 0f);
        if (src.IsKeywordEnabled("_EMISSION") && src.HasProperty("_EmissionColor"))
        {
            m.SetColor("_EmissionColor", src.GetColor("_EmissionColor"));
            if (src.HasProperty("_EmissionMap") && src.GetTexture("_EmissionMap")) m.SetTexture("_EmissionMap", src.GetTexture("_EmissionMap"));
            m.EnableKeyword("_EMISSION");
        }
        converted[src] = m;
        return m;
    }

    // ================================================================== collapse
    public void Collapse(Destructible d, Vector3 from) => StartCoroutine(CollapseRoutine(d, from));

    IEnumerator CollapseRoutine(Destructible d, Vector3 from)
    {
        d.RecalculateBounds();
        Bounds b = d.WorldBounds;
        float h = b.size.y;
        Vector3 pivot = new Vector3(b.center.x, b.min.y, b.center.z);
        Vector3 away = b.center - from; away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
        away.y = 0f; away.Normalize();
        Vector3 axis = Vector3.Cross(Vector3.up, away);
        float duration = Mathf.Lerp(2.6f, 6.5f, Mathf.InverseLerp(10f, 200f, h));
        float maxTilt = Random.Range(5f, 11f);
        var t = d.transform;
        Vector3 pos0 = t.position;
        Quaternion rot0 = t.rotation;
        float footprint = Mathf.Max(b.size.x, b.size.z);
        moving.Add(d);

        var dust = CollapseDust(pivot, footprint, h);
        Sfx.Play(Sfx.Crumble, pivot, 1f, Mathf.Lerp(1.1f, 0.6f, Mathf.InverseLerp(10f, 200f, h)), 0.6f);
        Sfx.Play(Sfx.Impact, pivot, 0.8f, 0.5f, 0.6f);
        float nextShake = 0f, nextDebris = 0f;

        for (float e = 0f; e < duration; e += Time.deltaTime)
        {
            float k = e / duration;
            float sink = (h + 3f) * k * k;
            Quaternion q = Quaternion.AngleAxis(maxTilt * Mathf.SmoothStep(0f, 1f, k), axis);
            float j = 0.35f * (1f - k * 0.5f);
            Vector3 jitter = new Vector3(Mathf.PerlinNoise(e * 9f, 1.7f) - 0.5f, 0f, Mathf.PerlinNoise(3.1f, e * 9f) - 0.5f) * j;
            t.SetPositionAndRotation(pivot + q * (pos0 - pivot) + Vector3.down * sink + jitter, q * rot0);

            var em = dust.emission;
            em.rateOverTime = k < 0.92f ? Mathf.Lerp(90f, 220f, Mathf.InverseLerp(10f, 120f, footprint)) : 0f;

            if (e >= nextDebris)
            {
                nextDebris = e + Mathf.Lerp(0.18f, 0.06f, Mathf.InverseLerp(10f, 150f, h));
                float topY = b.max.y - sink;
                if (topY > b.min.y + 1f)
                {
                    Vector3 edge = new Vector3(
                        Random.value < 0.5f ? (Random.value < 0.5f ? b.min.x : b.max.x) : Random.Range(b.min.x, b.max.x),
                        Random.Range(Mathf.Max(b.min.y, topY - 8f), topY),
                        Random.value < 0.5f ? Random.Range(b.min.z, b.max.z) : (Random.value < 0.5f ? b.min.z : b.max.z));
                    Vector3 outward = (edge - new Vector3(b.center.x, edge.y, b.center.z)).normalized;
                    FxUtil.Chunk(edge + outward * 0.5f, Random.Range(0.4f, 1.6f), null, outward * Random.Range(2f, 7f) + Vector3.up * Random.Range(0f, 3f), Random.Range(4f, 7f));
                }
            }

            if (e >= nextShake)
            {
                nextShake = e + 0.35f;
                var cam = Camera.main;
                float dist = cam ? Vector3.Distance(cam.transform.position, pivot) : 100f;
                ThirdPersonCamera.Shake(Mathf.Lerp(1.1f, 0.08f, Mathf.InverseLerp(20f, 600f, dist)) * Mathf.Lerp(0.6f, 1.3f, Mathf.InverseLerp(10f, 150f, h)), 0.5f);
            }
            yield return null;
        }

        // final puff and rubble
        dust.Emit(Mathf.RoundToInt(Mathf.Clamp(footprint * 3f, 40f, 250f)));
        var dm = dust.main; dm.loop = false;
        dust.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        moving.Remove(d);
        RemoveHolesOf(d);
        d.gameObject.SetActive(false);
        d.MarkCollapsed();
        Rubble(b, h);
    }

    ParticleSystem CollapseDust(Vector3 pivot, float footprint, float height)
    {
        var ps = FxUtil.System("CollapseDust", FxUtil.Dust, 900);
        ps.transform.SetPositionAndRotation(pivot + Vector3.up * 0.5f, Quaternion.Euler(-90f, 0f, 0f));   // circle lies flat
        var m = ps.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(3f, 9f);
        float s = Mathf.Lerp(5f, 14f, Mathf.InverseLerp(10f, 120f, footprint));
        m.startSize = new ParticleSystem.MinMaxCurve(s, s * 2.2f);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m.startColor = new Color(0.58f, 0.55f, 0.51f, 0.8f);
        m.stopAction = ParticleSystemStopAction.Destroy;
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radius = footprint * 0.55f; sh.radiusThickness = 1f;
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        // all three axes must use the same curve mode (two constants)
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.5f, Mathf.Lerp(2f, 5f, Mathf.InverseLerp(10f, 200f, height)));
        var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 1.5f; lim.dampen = 0.05f;
        var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 2.6f));
        var col = ps.colorOverLifetime; col.enabled = true; col.color = FxUtil.Fade(0.08f, 0.55f);
        return ps;
    }

    void Rubble(Bounds b, float h)
    {
        var root = new GameObject("Rubble");
        Vector3 c = new Vector3(b.center.x, b.min.y, b.center.z);
        var mound = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        mound.name = "Mound";
        mound.transform.SetParent(root.transform);
        float mh = Mathf.Clamp(h * 0.06f, 1.5f, 6f);
        mound.transform.position = c;
        mound.transform.localScale = new Vector3(b.size.x * 0.95f, mh * 2f, b.size.z * 0.95f);
        mound.GetComponent<MeshRenderer>().sharedMaterial = FxUtil.Concrete;
        int n = Mathf.RoundToInt(Mathf.Clamp(b.size.x * b.size.z / 14f, 10f, 45f));
        var dark = FxUtil.Lit(new Color(0.3f, 0.29f, 0.28f), 0.05f);
        for (int i = 0; i < n; i++)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "Slab";
            g.transform.SetParent(root.transform);
            float sz = Random.Range(1.2f, 4.5f);
            g.transform.localScale = new Vector3(sz, sz * Random.Range(0.2f, 0.5f), sz * Random.Range(0.5f, 1.3f));
            Vector2 p = new Vector2(Random.Range(-0.45f, 0.45f) * b.size.x, Random.Range(-0.45f, 0.45f) * b.size.z);
            float rise = mh * (1f - Mathf.Clamp01(p.magnitude / (Mathf.Max(b.size.x, b.size.z) * 0.5f)));
            g.transform.position = c + new Vector3(p.x, rise * Random.Range(0.4f, 1f), p.y);
            g.transform.rotation = Random.rotation;
            g.GetComponent<MeshRenderer>().sharedMaterial = Random.value < 0.5f ? dark : FxUtil.Concrete;
        }
        foreach (var t in root.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = false;
    }
}
