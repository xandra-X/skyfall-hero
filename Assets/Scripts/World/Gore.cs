using UnityEngine;

/// <summary>Blood: spray, splatter stuck to walls and ground, and pools that spread under bodies.</summary>
public static class Gore
{
    static Texture2D splatTex;
    static Material dropMat, mistMat, splatMat;
    static readonly Color Blood = new Color(0.42f, 0.01f, 0.01f);
    static int liveDecals;
    const int MaxDecals = 220;

    static Texture2D SplatTex => splatTex ? splatTex : splatTex = MakeSplat(128);
    static Material DropMat => dropMat ? dropMat : dropMat = FxUtil.Transparent(new Color(0.5f, 0.02f, 0.02f, 1f), FxUtil.Soft, false);
    static Material MistMat => mistMat ? mistMat : mistMat = FxUtil.Transparent(new Color(0.45f, 0.03f, 0.03f, 1f), FxUtil.Soft, false);
    static Material SplatMat => splatMat ? splatMat : splatMat = FxUtil.Transparent(new Color(Blood.r, Blood.g, Blood.b, 0.92f), SplatTex, false);

    /// amount 0..1 (light hit) up to ~2 (torn apart)
    public static void Burst(Vector3 pos, Vector3 dir, float amount)
    {
        if (dir.sqrMagnitude < 1e-4f) dir = Vector3.up;
        dir.Normalize();
        int drops = Mathf.RoundToInt(Mathf.Lerp(30f, 140f, amount * 0.5f));
        FxUtil.Burst("BloodDrops", DropMat, pos, Quaternion.LookRotation(dir), drops,
            new Vector2(0.4f, 1.1f), new Vector2(3f, 9f + amount * 4f), new Vector2(0.05f, 0.14f),
            new Color(0.55f, 0.02f, 0.02f, 1f), 1.6f, 55f, 0.15f, true);
        FxUtil.Burst("BloodMist", MistMat, pos, Quaternion.LookRotation(dir), Mathf.RoundToInt(10 + amount * 12),
            new Vector2(0.4f, 0.9f), new Vector2(0.5f, 2.5f), new Vector2(0.3f, 0.8f),
            new Color(0.5f, 0.03f, 0.03f, 0.55f), 0.1f, 70f, 0.1f, false, 0.3f, 2.2f);
        // splatter on whatever is around: mostly along the hit direction, some on the ground
        int rays = Mathf.RoundToInt(5 + amount * 6);
        for (int i = 0; i < rays; i++)
        {
            Vector3 d = i < rays / 3 ? (Vector3.down + Random.insideUnitSphere * 0.6f).normalized
                                     : (dir + Random.insideUnitSphere * 0.7f).normalized;
            if (Physics.Raycast(pos, d, out var h, 6f, FxUtil.WorldMask, QueryTriggerInteraction.Ignore))
                Splat(h.point, h.normal, Random.Range(0.35f, 1.1f) * (0.7f + amount * 0.3f), h.collider.transform);
        }
        Sfx.Play(Sfx.Splat, pos, 0.9f, Random.Range(0.85f, 1.1f));
    }

    public static void Splat(Vector3 p, Vector3 n, float size, Transform parent = null)
    {
        if (liveDecals >= MaxDecals) return;
        var q = FxUtil.Quad("BloodSplat", p + n * (0.02f + Random.value * 0.01f),
            Quaternion.LookRotation(-n) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)), size, SplatMat);
        // keep it with moving things (cars, collapsing parts) but not under the scaled hero etc.
        if (parent && parent.lossyScale == Vector3.one) q.transform.SetParent(parent, true);
        var t = q.gameObject.AddComponent<TimedFx>();
        t.mode = TimedFx.Mode.Fade; t.life = 60f; t.fadeTime = 5f;
        t.onDone = () => liveDecals--;
        liveDecals++;
    }

    /// A pool that spreads under a body.
    public static void Pool(Transform body, float maxSize = 1.8f)
    {
        var go = new GameObject("BloodPool");
        go.layer = FxUtil.HeroLayer;
        var p = go.AddComponent<BloodPool>();
        p.body = body; p.maxSize = maxSize;
    }

    static Texture2D MakeSplat(int n)
    {
        var px = new Color32[n * n];
        var rng = new System.Random(7);
        float[] a = new float[n * n];
        void Blob(float cx, float cy, float r)
        {
            for (int y = Mathf.Max(0, (int)(cy - r - 2)); y < Mathf.Min(n, (int)(cy + r + 2)); y++)
                for (int x = Mathf.Max(0, (int)(cx - r - 2)); x < Mathf.Min(n, (int)(cx + r + 2)); x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float v = Mathf.Clamp01(r - d + 0.5f);
                    if (v > a[y * n + x]) a[y * n + x] = v;
                }
        }
        float c = n / 2f;
        for (int i = 0; i < 9; i++)                                  // irregular main blob
            Blob(c + (float)(rng.NextDouble() - 0.5) * n * 0.2f, c + (float)(rng.NextDouble() - 0.5) * n * 0.2f, n * (0.1f + (float)rng.NextDouble() * 0.1f));
        for (int i = 0; i < 26; i++)                                 // droplets thrown outward
        {
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f, dist = n * (0.2f + (float)rng.NextDouble() * 0.27f);
            Blob(c + Mathf.Cos(ang) * dist, c + Mathf.Sin(ang) * dist, n * (0.01f + (float)rng.NextDouble() * 0.03f));
        }
        for (int i = 0; i < px.Length; i++)
        {
            float shade = 0.75f + 0.25f * Mathf.PerlinNoise((i % n) * 0.1f, (i / n) * 0.1f);
            px[i] = new Color32((byte)(255 * shade), (byte)(255 * shade), (byte)(255 * shade), (byte)(a[i] * 255));
        }
        var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        t.SetPixels32(px); t.Apply();
        return t;
    }

    internal static Material PoolMat => SplatMat;
}

/// <summary>Spreads slowly under a body once it has come to rest.</summary>
public class BloodPool : MonoBehaviour
{
    public Transform body;
    public float maxSize = 1.8f;
    float wait = 1.2f, size;
    MeshRenderer quad;

    void Update()
    {
        if (!quad)
        {
            wait -= Time.deltaTime;
            if (wait > 0f) return;
            if (!body) { Destroy(gameObject); return; }
            if (!Physics.Raycast(body.position + Vector3.up * 0.5f, Vector3.down, out var h, 4f, FxUtil.WorldMask, QueryTriggerInteraction.Ignore))
            { Destroy(gameObject); return; }
            quad = FxUtil.Quad("Pool", h.point + h.normal * 0.015f, Quaternion.LookRotation(-h.normal) * Quaternion.Euler(0, 0, Random.Range(0f, 360f)), 0.1f, Gore.PoolMat, transform);
            var t = gameObject.AddComponent<TimedFx>();
            t.mode = TimedFx.Mode.Shrink; t.life = 60f; t.fadeTime = 4f;
            return;
        }
        size = Mathf.MoveTowards(size, maxSize, Time.deltaTime * 0.35f);
        quad.transform.localScale = Vector3.one * Mathf.Max(0.1f, size);
    }
}
