using System.Collections.Generic;
using UnityEngine;

/// <summary>Fireball, smoke, shockwave, flash, debris and damage to everything around.</summary>
public static class Explosion
{
    static Material fireMat, flameMat;
    static Material FireMat => fireMat ? fireMat : fireMat = FxUtil.Transparent(new Color(3.5f, 1.4f, 0.35f, 1f), FxUtil.Soft, true);
    static Material FlameMat => flameMat ? flameMat : flameMat = FxUtil.Transparent(new Color(2.8f, 0.9f, 0.2f, 1f), FxUtil.Soft, true);

    /// radius: damage range. power: how hard things are thrown (m/s at the centre).
    static readonly Queue<float> recent = new Queue<float>();

    public static void At(Vector3 pos, float radius, float power, Object source = null)
    {
        float k = Mathf.InverseLerp(4f, 20f, radius);
        // many blasts at once (a street of cars going up): keep the damage, cut the effects so memory stays low
        while (recent.Count > 0 && Time.time - recent.Peek() > 1.5f) recent.Dequeue();
        recent.Enqueue(Time.time);
        int busy = recent.Count;
        if (busy > 12) { Damage(pos, radius, power, k, source); return; }
        float fx = busy > 4 ? 0.35f : 1f;
        if (Application.isMobilePlatform) fx *= 0.6f;
        // fireball + smoke column + sparks
        FxUtil.Burst("Fireball", FireMat, pos, Quaternion.identity, Mathf.RoundToInt(Mathf.Lerp(40, 120, k) * fx),
            new Vector2(0.35f, 0.9f), new Vector2(radius * 0.4f, radius * 1.1f), new Vector2(radius * 0.25f, radius * 0.55f),
            Color.white, -0.2f, 180f, radius * 0.15f, false, 0.25f, 1.8f);
        FxUtil.Burst("ExplosionSmoke", FxUtil.Smoke, pos + Vector3.up * radius * 0.2f, Quaternion.LookRotation(Vector3.up), Mathf.RoundToInt(Mathf.Lerp(30, 90, k) * fx),
            new Vector2(3f, 6f), new Vector2(1.5f, radius * 0.6f), new Vector2(radius * 0.3f, radius * 0.7f),
            new Color(0.12f, 0.11f, 0.1f, 0.75f), -0.08f, 40f, radius * 0.25f, false, 0.08f, 2.8f);
        FxUtil.Burst("ExplosionSparks", FxUtil.Spark, pos, Quaternion.identity, Mathf.RoundToInt(Mathf.Lerp(60, 160, k) * fx),
            new Vector2(0.4f, 1.4f), new Vector2(8f, 25f), new Vector2(0.05f, 0.12f), Color.white, 1.2f, 180f, 0.3f, true);
        Shockwave.Spawn(pos + Vector3.up * 0.3f, Vector3.up, radius * 3f, 0.5f);
        if (busy <= 4) Flash(pos, radius);
        for (int i = 0; i < Mathf.RoundToInt(Mathf.Lerp(6, 16, k) * fx * fx); i++)
            FxUtil.Chunk(pos + Random.insideUnitSphere * 0.6f, Random.Range(0.15f, 0.5f), Palette.Charred,
                Random.insideUnitSphere * power * 0.6f + Vector3.up * Random.Range(4f, 12f), Random.Range(5f, 8f));

        Sfx.Play(Sfx.Explosion, pos, 1f, Random.Range(0.85f, 1.05f) * Mathf.Lerp(1.1f, 0.7f, k), 0.7f);
        var cam = Camera.main;
        float dist = cam ? Vector3.Distance(cam.transform.position, pos) : 50f;
        ThirdPersonCamera.Shake(Mathf.Lerp(1.2f, 0.05f, Mathf.InverseLerp(5f, 250f, dist)) * Mathf.Lerp(0.7f, 1.3f, k), 0.6f);

        Damage(pos, radius, power, k, source);
    }

    static void Damage(Vector3 pos, float radius, float power, float k, Object source)
    {
        var done = new HashSet<Object>();
        if (source) done.Add(source);
        foreach (var c in Physics.OverlapSphere(pos, radius, ~0, QueryTriggerInteraction.Ignore))
        {
            Vector3 cp = c.bounds.center;
            Vector3 away = cp - pos;
            float falloff = 1f - Mathf.Clamp01(away.magnitude / radius);
            Vector3 push = (away.sqrMagnitude > 0.01f ? away.normalized : Random.onUnitSphere) * power * (0.4f + falloff) + Vector3.up * power * 0.35f;

            var hit = c.GetComponentInParent<IHittable>();
            if (hit != null && done.Add((Object)hit)) { hit.Hit(cp, push, 1000f * falloff + 50f, HitKind.Explosion); continue; }
            var b = c.GetComponentInParent<Breakable>();
            if (b && done.Add(b)) { b.Break(cp, push); continue; }
            var d = c.GetComponentInParent<Destructible>();
            if (d && done.Add(d))
            {
                // blow a hole in the nearest wall facing the blast
                Vector3 toward = d.WorldBounds.center - pos; toward.y = 0f;
                if (toward.sqrMagnitude < 0.01f) continue;
                if (c.Raycast(new Ray(pos, toward.normalized), out var wh, radius * 1.2f))
                    HeroCombat.SmashWall(d, wh.point, wh.normal, toward.normalized, Mathf.Lerp(1.4f, 3.5f, k), 60f, false);
                continue;
            }
            var rb = c.attachedRigidbody;
            if (rb && !rb.isKinematic && done.Add(rb)) rb.AddExplosionForce(power * rb.mass, pos, radius, 1f, ForceMode.Impulse);
        }
    }

    static void Flash(Vector3 pos, float radius)
    {
        var l = new GameObject("ExplosionFlash").AddComponent<Light>();
        l.transform.position = pos + Vector3.up;
        l.type = LightType.Point;
        l.color = new Color(1f, 0.6f, 0.25f);
        l.range = radius * 5f;
        l.intensity = 25f;
        l.gameObject.AddComponent<LightFade>().life = 0.6f;
    }

    /// Lingering fire on a wreck.
    public static ParticleSystem Fire(Transform parent, Vector3 localPos, float size, float duration)
    {
        var ps = FxUtil.System("WreckFire", FlameMat, 200, parent);
        ps.transform.localPosition = localPos;
        var m = ps.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
        m.startSize = new ParticleSystem.MinMaxCurve(size * 0.4f, size);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m.gravityModifier = -0.3f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = size * 0.5f;
        var col = ps.colorOverLifetime; col.enabled = true; col.color = FxUtil.Fade(0.1f, 0.4f);
        var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1f, 1, 0.3f));
        var em = ps.emission; em.rateOverTime = 60f * size;

        var smoke = FxUtil.System("WreckSmoke", FxUtil.Smoke, 200, parent);
        smoke.transform.localPosition = localPos + Vector3.up * size * 0.5f;
        var sm = smoke.main;
        sm.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
        sm.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
        sm.startSize = new ParticleSystem.MinMaxCurve(size, size * 2.5f);
        sm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        sm.startColor = new Color(0.1f, 0.095f, 0.09f, 0.6f);
        sm.gravityModifier = -0.15f;
        var ssh = smoke.shape; ssh.shapeType = ParticleSystemShapeType.Sphere; ssh.radius = size * 0.3f;
        var scol = smoke.colorOverLifetime; scol.enabled = true; scol.color = FxUtil.Fade(0.1f, 0.5f);
        var ssol = smoke.sizeOverLifetime; ssol.enabled = true; ssol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 2.5f));
        var sem = smoke.emission; sem.rateOverTime = 12f * size;

        var stop = ps.gameObject.AddComponent<FireLife>();
        stop.duration = duration; stop.others = new[] { smoke };
        return ps;
    }
}

public class LightFade : MonoBehaviour
{
    public float life = 0.5f;
    Light l; float i0, age;
    void Start() { l = GetComponent<Light>(); i0 = l.intensity; }
    void Update()
    {
        age += Time.deltaTime;
        l.intensity = i0 * Mathf.Clamp01(1f - age / life);
        if (age >= life) Destroy(gameObject);
    }
}

/// <summary>Stops a fire (and its smoke) after a while.</summary>
public class FireLife : MonoBehaviour
{
    public float duration = 15f;
    public ParticleSystem[] others;
    float age;
    void Update()
    {
        age += Time.deltaTime;
        if (age < duration) return;
        foreach (var ps in new[] { GetComponent<ParticleSystem>() }) { var e = ps.emission; e.rateOverTime = 0f; }
        foreach (var o in others) if (o) { var e = o.emission; e.rateOverTime = 0f; }
        enabled = false;
    }
}
