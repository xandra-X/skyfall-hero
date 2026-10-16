using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Shared helpers for effects: generated textures, materials, particle systems, debris.</summary>
public static class FxUtil
{
    static Texture2D soft, beam, wallCrack, burn, ring;
    public static Texture2D Soft => soft ? soft : soft = SoftDot(64);
    public static Texture2D Beam => beam ? beam : beam = BeamTexture();
    public static Texture2D WallCrack => wallCrack ? wallCrack : wallCrack = CrackTexture(512, 11, 0.17f);
    public static Texture2D Burn => burn ? burn : burn = BurnTexture(128);
    public static Texture2D Ring => ring ? ring : ring = RingTexture(256);

    public const int HeroLayer = 2;                 // "Ignore Raycast": hero, debris, broken props
    public static readonly int WorldMask = ~(1 << HeroLayer);

    static int liveDebris;
    public const int MaxDebris = 180;

    // ------------------------------------------------------------------ materials
    /// The shader for glows, beams, sparks and dust. Falls back to a simpler one if a build left it out
    /// (better a plain effect than a pink one or none at all).
    public static Shader EffectShader
    {
        get
        {
            if (effectShader) return effectShader;
            foreach (var n in new[] { "Universal Render Pipeline/Particles/Unlit", "Universal Render Pipeline/Unlit", "Sprites/Default" })
                if ((effectShader = Shader.Find(n))) break;
            return effectShader;
        }
    }
    static Shader effectShader;

    /// Effect materials are copied from two ready-made material assets in Resources/Fx. A build always contains
    /// what Resources uses, so the effect shader (and its see-through variants) can never go missing on the phone.
    static Material additiveTemplate, alphaTemplate;
    static Material Template(bool additive)
    {
        if (additive) return additiveTemplate ? additiveTemplate : additiveTemplate = Resources.Load<Material>("Fx/FxAdditive");
        return alphaTemplate ? alphaTemplate : alphaTemplate = Resources.Load<Material>("Fx/FxAlpha");
    }

    public static Material Transparent(Color color, Texture tex, bool additive)
    {
        var t = Template(additive);
        if (t)
        {
            var c = new Material(t);
            c.SetColor("_BaseColor", color);
            if (tex) c.SetTexture("_BaseMap", tex);
            return c;
        }
        var m = new Material(EffectShader);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", additive ? 2f : 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        if (additive) m.EnableKeyword("_BLENDMODE_ADD");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetColor("_BaseColor", color);
        if (tex) m.SetTexture("_BaseMap", tex);
        return m;
    }

    public static Material Lit(Color c, float smooth)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        return m;
    }

    static Material concrete, spark, smoke, dust, steam;
    public static Material Concrete => concrete ? concrete : concrete = Lit(new Color(0.42f, 0.41f, 0.39f), 0.08f);
    public static Material Spark => spark ? spark : spark = Transparent(new Color(4f, 1.6f, 0.4f, 1f), Soft, true);
    public static Material Smoke => smoke ? smoke : smoke = Transparent(new Color(0.16f, 0.15f, 0.14f, 1f), Soft, false);
    public static Material Dust => dust ? dust : dust = Transparent(new Color(0.66f, 0.63f, 0.58f, 1f), Soft, false);
    public static Material Steam => steam ? steam : steam = Transparent(new Color(0.95f, 0.97f, 1f, 1f), Soft, false);

    public static Gradient Fade(float fadeIn, float holdUntil)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(1f, holdUntil), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    // ------------------------------------------------------------------ particles
    /// A looping, world-space system that emits nothing until you set its rate or call Emit.
    public static ParticleSystem System(string name, Material mat, int max, Transform parent = null)
    {
        var go = new GameObject(name);
        if (parent) go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.duration = 1f; m.loop = true; m.playOnAwake = false;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.maxParticles = max;
        var em = ps.emission; em.rateOverTime = 0f;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        ps.Play();
        return ps;
    }

    /// One-shot burst that destroys itself.
    public static ParticleSystem Burst(string name, Material mat, Vector3 pos, Quaternion rot, int count,
        Vector2 life, Vector2 speed, Vector2 size, Color color, float gravity, float coneAngle, float radius,
        bool stretch = false, float drag = 0f, float sizeGrow = 1f)
    {
        var go = new GameObject(name);
        go.transform.SetPositionAndRotation(pos, rot);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.duration = 0.1f; m.loop = false; m.playOnAwake = false;
        m.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        m.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        m.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m.startColor = color;
        m.gravityModifier = gravity;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.maxParticles = count + 10;
        m.stopAction = ParticleSystemStopAction.Destroy;
        var sh = ps.shape;
        sh.shapeType = coneAngle >= 180f ? ParticleSystemShapeType.Sphere : ParticleSystemShapeType.Cone;
        sh.angle = Mathf.Min(coneAngle, 89f); sh.radius = radius;
        var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(0.04f, 0.45f);
        if (sizeGrow != 1f)
        {
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1f, 1, sizeGrow));
        }
        if (drag > 0f) { var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 0.5f; lim.dampen = drag; }
        var em = ps.emission; em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        if (stretch) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.04f; r.lengthScale = 1.5f; }
        ps.Play();
        return ps;
    }

    // ------------------------------------------------------------------ debris
    public static Rigidbody Chunk(Vector3 pos, float size, Material mat, Vector3 velocity, float life)
    {
        if (liveDebris >= MaxDebris) return null;
        var d = GameObject.CreatePrimitive(PrimitiveType.Cube);
        d.name = "Debris";
        d.layer = HeroLayer;
        d.transform.position = pos;
        d.transform.rotation = Random.rotation;
        d.transform.localScale = new Vector3(size, size * Random.Range(0.35f, 0.9f), size * Random.Range(0.5f, 1.2f));
        d.GetComponent<MeshRenderer>().sharedMaterial = mat ? mat : Concrete;
        var rb = d.AddComponent<Rigidbody>();
        rb.mass = Mathf.Max(0.5f, size * size * size * 2000f);
        rb.linearVelocity = velocity;
        rb.angularVelocity = Random.insideUnitSphere * 14f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        var t = d.AddComponent<TimedFx>();
        t.life = life; t.mode = TimedFx.Mode.Shrink;
        t.onDone = () => liveDebris--;
        liveDebris++;
        return rb;
    }

    public static MeshRenderer Quad(string name, Vector3 pos, Quaternion rot, float size, Material mat, Transform parent = null)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = name;
        Object.Destroy(q.GetComponent<Collider>());
        q.layer = HeroLayer;
        q.transform.SetPositionAndRotation(pos, rot);
        q.transform.localScale = Vector3.one * size;
        if (parent) q.transform.SetParent(parent, true);
        var r = q.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return r;
    }

    // ------------------------------------------------------------------ textures
    static Texture2D SoftDot(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude / (n / 2f);
                float a = Mathf.Clamp01(1f - d);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * a * 255));
            }
        t.SetPixels32(px); t.Apply();
        return t;
    }

    /// Bright in the middle of the beam's width, soft at the edges (v = across the beam).
    static Texture2D BeamTexture()
    {
        const int w = 8, h = 64;
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h * 2f - 1f;
            float a = Mathf.Exp(-v * v * 6f);
            for (int x = 0; x < w; x++) px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255));
        }
        t.SetPixels32(px); t.Apply();
        return t;
    }

    static Texture2D RingTexture(int n)
    {
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = new Vector2(x - n / 2f, y - n / 2f).magnitude / (n / 2f);
                float a = Mathf.Exp(-Mathf.Pow((d - 0.82f) / 0.07f, 2f)) + 0.3f * Mathf.Exp(-Mathf.Pow((d - 0.68f) / 0.12f, 2f));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }
        var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        t.SetPixels32(px); t.Apply();
        return t;
    }

    /// Black burn mark with a ragged edge.
    static Texture2D BurnTexture(int n)
    {
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x - n / 2f) / (n / 2f), dy = (y - n / 2f) / (n / 2f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float edge = 0.7f + 0.25f * Mathf.PerlinNoise(Mathf.Atan2(dy, dx) * 1.7f + 3f, 1.1f);
                float a = Mathf.Clamp01((edge - d) * 3f) * (0.7f + 0.3f * Mathf.PerlinNoise(x * 0.15f, y * 0.15f));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 235));
            }
        var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        t.SetPixels32(px); t.Apply();
        return t;
    }

    /// Radial cracks around an empty middle (for walls: the hole itself is cut by the shader).
    static Texture2D CrackTexture(int n, int seed, float inner)
    {
        var rng = new System.Random(seed);
        float R() => (float)rng.NextDouble();
        var alpha = new float[n * n];
        void Stamp(float cx, float cy, float rad)
        {
            int r = Mathf.CeilToInt(rad) + 1;
            for (int y = (int)cy - r; y <= (int)cy + r; y++)
                for (int x = (int)cx - r; x <= (int)cx + r; x++)
                {
                    if (x < 0 || y < 0 || x >= n || y >= n) continue;
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float v = Mathf.Clamp01(rad + 0.5f - d);
                    int i = y * n + x;
                    if (v > alpha[i]) alpha[i] = v;
                }
        }
        void Crack(float x, float y, float angle, float length, float width, int depth)
        {
            const float step = 3f;
            for (float l = 0; l < length; l += step)
            {
                angle += R() < 0.18f ? (R() - 0.5f) * 1.1f : (R() - 0.5f) * 0.06f;
                x += Mathf.Cos(angle) * step; y += Mathf.Sin(angle) * step;
                float w = width * (1f - l / length) * 0.75f + 0.5f;
                Stamp(x, y, w);
                if (depth < 2 && R() < 0.025f)
                    Crack(x, y, angle + (R() < 0.5f ? -1 : 1) * (0.5f + R() * 0.6f), (length - l) * (0.4f + R() * 0.3f), w * 0.7f, depth + 1);
            }
        }
        float c = n / 2f;
        int count = 10 + rng.Next(5);
        for (int i = 0; i < count; i++)
        {
            float a = i * Mathf.PI * 2f / count + (R() - 0.5f) * 0.5f;
            float r0 = n * inner * (0.9f + R() * 0.2f);
            Crack(c + Mathf.Cos(a) * r0, c + Mathf.Sin(a) * r0, a, n * (0.22f + R() * 0.2f), 3f + R() * 2f, 0);
        }
        var px = new Color32[n * n];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, (byte)(alpha[i] * 255));
        var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        t.SetPixels32(px); t.Apply();
        return t;
    }
}

/// <summary>Removes an effect object after a while: shrinks it, or fades its material colour.</summary>
public class TimedFx : MonoBehaviour
{
    public enum Mode { Shrink, Fade }
    public float life = 5f, fadeTime = 0.6f;
    public Mode mode = Mode.Shrink;
    public System.Action onDone;
    float age;
    Vector3 scale0;
    Renderer r;
    Color c0;
    MaterialPropertyBlock block;

    void Start()
    {
        scale0 = transform.localScale;
        r = GetComponent<Renderer>();
        if (r && r.sharedMaterial && r.sharedMaterial.HasProperty("_BaseColor")) c0 = r.sharedMaterial.GetColor("_BaseColor");
        block = new MaterialPropertyBlock();
    }

    void Update()
    {
        age += Time.deltaTime;
        float k = Mathf.Clamp01((age - (life - fadeTime)) / fadeTime);
        if (k > 0f)
        {
            if (mode == Mode.Shrink) transform.localScale = scale0 * (1f - k);
            else if (r)
            {
                r.GetPropertyBlock(block);
                var c = c0; c.a *= 1f - k;
                block.SetColor("_BaseColor", c);
                r.SetPropertyBlock(block);
            }
        }
        if (age >= life) Destroy(gameObject);
    }

    void OnDestroy() => onDone?.Invoke();
}
