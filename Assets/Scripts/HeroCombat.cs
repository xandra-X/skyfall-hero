using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Super strength and heat vision.
///   Q punch / E kick: dash to the target, rip props loose, smash holes into walls (hit-stop, shake, debris)
///   Hold left mouse: heat vision from both eyes; burns medium holes clean through buildings,
///                    scorches the ground, boils the sea, flings props
///   Flying (or super-speed running) into a building at speed smashes straight through it
///   A hard landing breaks everything around it
/// </summary>
[RequireComponent(typeof(HeroController))]
public class HeroCombat : MonoBehaviour
{
    [Header("Punch / kick")]
    public float punchDamage = 60f;
    public float kickDamage = 95f;
    public float punchImpactTime = 0.38f;      // when the fist lands in the punch animation
    public float kickImpactTime = 0.30f;
    public float reach = 2.4f;
    public float lungeRange = 9f;
    public float lungeSpeed = 55f;
    public float punchHoleRadius = 1.15f;
    public float kickHoleRadius = 1.45f;
    public float propLaunchSpeed = 30f;

    [Header("Heat vision")]
    public float laserRange = 800f;
    public float laserDps = 45f;
    public float holeRadius = 0.75f;           // medium hole: about 1.5 m across
    public float holeStartRadius = 0.18f;
    public float holeGrowRate = 1.4f;          // m/s: burns through in ~0.4 s

    [Header("Ram")]
    public float ramFlySpeed = 34f;
    public float ramDamage = 220f;
    public float ramHoleRadius = 1.7f;

    /// For tests: aim the heat vision at this point instead of where the camera looks.
    public Vector3? AimOverride { get; set; }
    /// Diagnostics: where the beam ended this frame and what it hit.
    public Vector3 BeamEnd { get; private set; }
    public string BeamHitName { get; private set; } = "";
    public string LastStrike { get; private set; } = "";

    HeroController hero;
    CharacterController cc;
    Animator anim;
    float impactAt = -1f;
    bool impactIsKick;
    float ramCooldown;

    // ---- heat vision visuals
    readonly LineRenderer[] beams = new LineRenderer[4];   // core L, core R, glow L, glow R
    MeshRenderer eyeFlareL, eyeFlareR, hitFlare;
    Light eyeLight, hitLight;
    ParticleSystem sparks, smoke, steam, embers;
    AudioSource laserAudio, sizzleAudio, steamAudio;
    Material scorchMat, scorchGlowMat;
    float beamOn;
    DamageSystem.HoleRef currentHole = new DamageSystem.HoleRef(-1, 0);
    Vector3 lastScorch = Vector3.one * 1e6f;
    readonly Queue<GameObject> scorches = new Queue<GameObject>();

    // ---- ram state
    Collider ramCollider;
    readonly List<Collider> ramLayers = new List<Collider>();     // other wall layers opened by the same smash
    float ramUntil;
    Vector3 ramExit, ramDir;
    bool ramExitDone = true;

    enum BeamHit { None, Building, Prop, Water, Ground }

    void Start()
    {
        hero = GetComponent<HeroController>();
        cc = GetComponent<CharacterController>();
        anim = GetComponentInChildren<Animator>();
        DamageSystem.Ensure();
        hero.PunchStarted += () => OnStrike(false);
        hero.KickStarted += () => OnStrike(true);
        hero.HardLanded += OnHardLanded;
        BuildBeamVisuals();
    }

    void Update()
    {
        if (!anim) anim = GetComponentInChildren<Animator>();
        if (impactAt > 0f && Time.time >= impactAt)
        {
            impactAt = -1f;
            Impact(impactIsKick);
        }
        UpdateHeatVision();
        RamLookAhead();
        UpdateRam();
    }

    // ================================================================== punch / kick
    Vector3 Chest => transform.position + Vector3.up * 1.25f;

    Vector3 AimDir(bool flat)
    {
        var cam = Camera.main;
        Vector3 d = cam ? cam.transform.forward : transform.forward;
        if (flat) { d.y = 0f; if (d.sqrMagnitude < 1e-4f) d = transform.forward; }
        return d.normalized;
    }

    static bool IsTarget(Collider c)
    {
        if (c.GetComponentInParent<IHittable>() != null) return true;
        var d = c.GetComponentInParent<Destructible>();
        if (d) return !d.Collapsing;
        var b = c.GetComponentInParent<Breakable>();
        return b && !b.Broken;
    }

    void OnStrike(bool kick)
    {
        impactIsKick = kick;
        impactAt = Time.time + (kick ? kickImpactTime : punchImpactTime);
        Sfx.Play(Sfx.Whoosh, Chest, 0.5f, kick ? 0.8f : 1.1f, 0.2f);

        bool flat = hero.Current != HeroController.State.Fly;
        Vector3 dir = AimDir(flat);
        // dash to whatever is in front of the camera, stopping just in reach
        if (Physics.SphereCast(Chest, 0.45f, dir, out var hit, lungeRange, FxUtil.WorldMask, QueryTriggerInteraction.Ignore)
            && IsTarget(hit.collider) && hit.distance > 1.5f)
        {
            float travel = hit.distance - 1.1f;
            hero.Lunge(dir * lungeSpeed, travel / lungeSpeed);
        }
        else
        {
            Vector3 flatDir = new Vector3(dir.x, 0f, dir.z);
            if (flatDir.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(flatDir);
        }
    }

    void Impact(bool kick)
    {
        LastStrike = kick ? "kick" : "punch";
        bool flat = hero.Current != HeroController.State.Fly;
        Vector3 dir = flat ? transform.forward : AimDir(false);
        Vector3 origin = kick ? transform.position + Vector3.up * 1.0f : Chest;
        bool hitSomething = false;

        // people, cars and props right in front of him
        var done = new HashSet<Object>();
        foreach (var c in Physics.OverlapSphere(origin + dir * 1.2f, 1.3f, FxUtil.WorldMask, QueryTriggerInteraction.Ignore))
        {
            var target = c.GetComponentInParent<IHittable>();
            if (target != null)
            {
                if (!done.Add((Object)target)) continue;
                float sp = propLaunchSpeed * (kick ? 1.6f : 1.3f);
                target.Hit(c.ClosestPoint(origin), dir * sp + Vector3.up * (kick ? 8f : 5f), kick ? kickDamage : punchDamage, HitKind.Punch);
                hitSomething = true;
                continue;
            }
            var b = c.GetComponentInParent<Breakable>();
            if (!b || b.Broken || !done.Add(b)) continue;
            Vector3 p = c.ClosestPoint(origin);
            float speed = propLaunchSpeed * (kick ? 1.3f : 1f);
            b.Break(p, dir * speed + Vector3.up * (kick ? 9f : 6f));
            hitSomething = true;
        }

        // the wall in front of him
        if (Physics.SphereCast(origin - dir * 0.2f, 0.35f, dir, out var hit, reach + 0.4f, FxUtil.WorldMask, QueryTriggerInteraction.Ignore))
        {
            var d = hit.collider.GetComponentInParent<Destructible>();
            if (d && !d.Collapsing) { SmashWall(d, hit.point, hit.normal, dir, kick ? kickHoleRadius : punchHoleRadius, kick ? kickDamage : punchDamage, false); hitSomething = true; }
            else if (!hit.collider.GetComponentInParent<Breakable>())
            {
                // road, bridge, island: a puff of dust and a crack
                FxUtil.Burst("PunchDust", FxUtil.Dust, hit.point, Quaternion.LookRotation(hit.normal), 20,
                    new Vector2(0.5f, 1.2f), new Vector2(2f, 6f), new Vector2(0.5f, 1.5f), new Color(0.65f, 0.62f, 0.57f, 0.6f), -0.05f, 60f, 0.4f, false, 0.2f, 2f);
                Sfx.Play(Sfx.Impact, hit.point, 0.6f, 1.3f);
                hitSomething = true;
            }
        }

        if (hitSomething)
        {
            DamageSystem.Instance.HitStop(kick ? 0.09f : 0.075f);
            ThirdPersonCamera.Shake(kick ? 0.6f : 0.45f, 0.3f);
        }
    }

    /// Punch, kick or ram: a jagged hole, cracks, flying concrete, dust and a shockwave.
    public static void SmashWall(Destructible d, Vector3 p, Vector3 n, Vector3 dir, float radius, float damage, bool throughAndThrough, Vector3? exit = null)
    {
        var ds = DamageSystem.Instance;
        Vector3 a = p + n * 0.9f;
        Vector3 b = exit.HasValue ? exit.Value + dir * 1.5f : p - n * 1.2f;
        ds.AddHole(d, a, b, radius, false);

        var crack = FxUtil.Quad("WallCracks", p + n * 0.04f, Quaternion.LookRotation(-n), radius * 6f,
            FxUtil.Transparent(new Color(0.06f, 0.055f, 0.05f, 0.95f), FxUtil.WallCrack, false), d.transform);
        crack.transform.Rotate(0f, 0f, Random.Range(0f, 360f), Space.Self);
        var cf = crack.gameObject.AddComponent<TimedFx>();          // cracks fade as the hole heals
        cf.mode = TimedFx.Mode.Fade; cf.life = ds.HealTotal; cf.fadeTime = ds.healDuration;

        int chunks = Mathf.RoundToInt(Mathf.Lerp(10f, 22f, Mathf.InverseLerp(1f, 1.8f, radius)));
        for (int i = 0; i < chunks; i++)
        {
            Vector2 rc = Random.insideUnitCircle * radius;
            Vector3 side = Vector3.Cross(n, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.Cross(n, Vector3.up).normalized : Vector3.right;
            Vector3 up = Vector3.Cross(side, n);
            Vector3 pos = p + side * rc.x + up * rc.y;
            bool inward = Random.value < 0.6f;
            Vector3 v = (inward ? dir * Random.Range(8f, 20f) : n * Random.Range(4f, 12f)) + Random.insideUnitSphere * 4f + Vector3.up * Random.Range(1f, 5f);
            FxUtil.Chunk(pos + (inward ? -n : n) * 0.3f, Random.Range(0.15f, 0.55f), null, v, Random.Range(4f, 7f));
        }
        FxUtil.Burst("WallDust", FxUtil.Dust, p + n * 0.3f, Quaternion.LookRotation(n), 45,
            new Vector2(0.8f, 1.9f), new Vector2(3f, 11f), new Vector2(0.8f, 2.4f), new Color(0.66f, 0.63f, 0.58f, 0.7f), -0.05f, 55f, radius * 0.6f, false, 0.15f, 2.6f);
        FxUtil.Burst("WallDustIn", FxUtil.Dust, p - n * 0.5f, Quaternion.LookRotation(-n), 25,
            new Vector2(0.8f, 1.6f), new Vector2(3f, 9f), new Vector2(0.8f, 2f), new Color(0.6f, 0.57f, 0.53f, 0.65f), -0.05f, 45f, radius * 0.5f, false, 0.15f, 2.4f);
        Shockwave.Spawn(p + n * 0.15f, n, radius * 5f, 0.35f);
        Sfx.Play(Sfx.Impact, p, 1f, Random.Range(0.85f, 1.05f));
        d.Damage(damage, p);
    }

    // ================================================================== ram through buildings
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (Time.time < ramCooldown || hit.collider == ramCollider) return;
        Vector3 v = hero.Velocity;
        float speed = v.magnitude;

        // walking or running into a car or a person does nothing (it just blocks him);
        // only slamming into them at super speed / fast flight knocks them
        var victim = hit.collider.GetComponentInParent<IHittable>();
        if (victim != null)
        {
            if (RamSpeed(out _))
            {
                victim.Hit(hit.point, v * 0.9f + Vector3.up * 4f, 500f, HitKind.Impact);
                ThirdPersonCamera.Shake(0.2f, 0.2f);
            }
            return;
        }
        var b = hit.collider.GetComponentInParent<Breakable>();
        if (b && !b.Broken && speed > 10f)
        {
            b.Break(hit.point, v * 0.8f + Vector3.up * 6f);
            ThirdPersonCamera.Shake(0.15f, 0.2f);
            return;
        }

        if (!RamSpeed(out Vector3 dir)) return;
        var d = hit.collider.GetComponentInParent<Destructible>();
        if (!d || d.Collapsing || !CanRamSurface(hit.point, hit.normal)) return;
        RamThrough(d, hit.collider, hit.point, dir);
    }

    /// Fast enough to smash through? (flying fast, or running at super speed)
    bool RamSpeed(out Vector3 dir)
    {
        Vector3 v = hero.Velocity;
        bool flyingFast = hero.Current == HeroController.State.Fly && v.magnitude > ramFlySpeed;
        bool superRun = hero.Current == HeroController.State.Ground && hero.SuperSpeed01 > 0.75f;
        dir = v.sqrMagnitude > 0.01f ? v.normalized : transform.forward;
        if (superRun) { dir.y = 0f; dir.Normalize(); }
        return flyingFast || superRun;
    }

    /// Look ahead every frame so he smashes in right before contact (instead of sliding along the wall).
    void RamLookAhead()
    {
        if (Time.time < ramCooldown || !RamSpeed(out Vector3 dir)) return;
        Vector3 body = transform.position + Vector3.up * 0.95f;
        float dist = hero.Velocity.magnitude * Time.deltaTime * 2f + 1.2f;
        // nearest wall that isn't one he is already passing through (downtown walls come in several layers)
        RaycastHit hit = default; bool any = false;
        foreach (var h in Physics.SphereCastAll(body, 0.5f, dir, dist, FxUtil.WorldMask, QueryTriggerInteraction.Ignore))
        {
            if (!ramExitDone && (h.collider == ramCollider || ramLayers.Contains(h.collider))) continue;
            if (!any || h.distance < hit.distance) { hit = h; any = true; }
        }
        if (!any) return;
        var d = hit.collider.GetComponentInParent<Destructible>();
        if (d && !d.Collapsing && CanRamSurface(hit.point, hit.normal)) RamThrough(d, hit.collider, hit.point, dir);
    }

    /// Walls always; roofs only up high (never the streets and pavements, or he'd punch through the ground).
    static bool CanRamSurface(Vector3 point, Vector3 normal) => normal.y < 0.6f || point.y > 8f;

    void RamThrough(Destructible d, Collider col, Vector3 point, Vector3 dir)
    {
        float speed = hero.Velocity.magnitude;
        Vector3 body = transform.position + Vector3.up * 0.95f;
        Vector3 exit = FarSide(col, d, body, dir, out _);
        if (ramCollider && ramCollider != col && cc) Physics.IgnoreCollision(cc, ramCollider, false);   // chaining through buildings
        foreach (var l in ramLayers) if (l && cc) Physics.IgnoreCollision(cc, l, false);
        ramLayers.Clear();
        ramCollider = col;
        Physics.IgnoreCollision(cc, ramCollider, true);
        // walls built from several overlapping meshes (facade, windows, trim): open them all here
        foreach (var other in Physics.OverlapCapsule(point - dir * 0.5f, point + dir * 2.5f, 1.6f, FxUtil.WorldMask, QueryTriggerInteraction.Ignore))
        {
            if (other == col || other.isTrigger || !other.GetComponentInParent<Destructible>()) continue;
            ramLayers.Add(other);
            Physics.IgnoreCollision(cc, other, true);
        }
        ramUntil = Time.time + (Vector3.Distance(body, exit) + 5f) / Mathf.Max(speed, 10f) + 0.4f;
        ramExit = exit; ramDir = dir; ramExitDone = false;
        ramCooldown = Time.time + 0.1f;

        SmashWall(d, point, -dir, dir, ramHoleRadius, ramDamage, true, exit);
        LastStrike = "ram " + d.name;
        DamageSystem.Instance.HitStop(0.06f);
        ThirdPersonCamera.Shake(0.9f, 0.45f);
    }

    void UpdateRam()
    {
        if (!ramCollider) return;
        Vector3 body = transform.position + Vector3.up * 0.95f;
        if (!ramExitDone && Vector3.Dot(body - ramExit, ramDir) > -0.5f)
        {
            ramExitDone = true;
            for (int i = 0; i < 14; i++)
                FxUtil.Chunk(ramExit + Random.insideUnitSphere * 1.2f, Random.Range(0.2f, 0.7f), null,
                    ramDir * Random.Range(10f, 22f) + Random.insideUnitSphere * 5f, Random.Range(4f, 7f));
            FxUtil.Burst("ExitDust", FxUtil.Dust, ramExit, Quaternion.LookRotation(ramDir), 40,
                new Vector2(0.8f, 1.8f), new Vector2(4f, 12f), new Vector2(1f, 2.6f), new Color(0.66f, 0.63f, 0.58f, 0.7f), -0.05f, 40f, 1f, false, 0.15f, 2.4f);
            Shockwave.Spawn(ramExit, ramDir, 9f, 0.35f);
            Sfx.Play(Sfx.Impact, ramExit, 0.9f, 0.9f);
            ThirdPersonCamera.Shake(0.5f, 0.3f);
        }
        if (Time.time > ramUntil)
        {
            if (cc && ramCollider) Physics.IgnoreCollision(cc, ramCollider, false);
            foreach (var l in ramLayers) if (l && cc) Physics.IgnoreCollision(cc, l, false);
            ramLayers.Clear();
            ramCollider = null;
        }
    }

    /// Where a ray that enters this building comes out the other side.
    static Vector3 FarSide(Collider col, Destructible d, Vector3 origin, Vector3 dir, out Vector3 normal)
    {
        // step forward until we're out the other side of *this* building: a ray looking back from there hits
        // its far wall. (Downtown meshes hold many buildings, so looking back from far away would find another one.)
        for (float L = 3f; L <= 400f; L *= 1.45f)
        {
            var back = new Ray(origin + dir * L, -dir);
            if (col.Raycast(back, out var hb, L - 0.3f))
            {
                normal = hb.normal;
                return hb.point;
            }
        }
        normal = dir;
        Bounds b = d.WorldBounds;
        if (b.size.magnitude < 400f && b.IntersectRay(new Ray(origin + dir * 400f, -dir), out float dist)) return origin + dir * (400f - dist);
        return origin + dir * 20f;
    }

    // ================================================================== hard landing shockwave
    void OnHardLanded(Vector3 p, Vector3 n, float impact)
    {
        float k = Mathf.InverseLerp(hero.hardLandingSpeed, 60f, impact);
        float radius = Mathf.Lerp(3f, 9f, k);
        var seen = new HashSet<Object>();
        foreach (var c in Physics.OverlapSphere(p, radius, FxUtil.WorldMask, QueryTriggerInteraction.Ignore))
        {
            var victim = c.GetComponentInParent<IHittable>();
            if (victim != null && seen.Add((Object)victim))
            {
                Vector3 off = c.bounds.center - p; off.y = 0f;
                victim.Hit(c.bounds.center, off.normalized * Mathf.Lerp(8f, 22f, k) + Vector3.up * Mathf.Lerp(6f, 14f, k), 500f, HitKind.Explosion);
                continue;
            }
            var b = c.GetComponentInParent<Breakable>();
            if (b && !b.Broken && seen.Add(b))
            {
                Vector3 away = c.ClosestPoint(p) - p; away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
                away.y = 0f;
                b.Break(c.ClosestPoint(p), away.normalized * Mathf.Lerp(8f, 22f, k) + Vector3.up * Mathf.Lerp(6f, 14f, k));
                continue;
            }
            var d = c.GetComponentInParent<Destructible>();
            if (d && seen.Add(d)) d.Damage(Mathf.Lerp(15f, 80f, k), p);
        }
        Shockwave.Spawn(p + n * 0.1f, n, radius * 2.2f, 0.45f);
    }

    // ================================================================== heat vision
    void BuildBeamVisuals()
    {
        var core = FxUtil.Transparent(new Color(4f, 1.3f, 1.1f, 1f), FxUtil.Beam, true);
        var glow = FxUtil.Transparent(new Color(2.6f, 0.12f, 0.04f, 1f), FxUtil.Beam, true);
        for (int i = 0; i < 4; i++)
        {
            var go = new GameObject(i < 2 ? "HeatBeamCore" : "HeatBeamGlow");
            go.layer = FxUtil.HeroLayer;
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.textureMode = LineTextureMode.Stretch;
            lr.alignment = LineAlignment.View;
            lr.numCapVertices = 4;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = i < 2 ? core : glow;
            lr.enabled = false;
            beams[i] = lr;
        }
        var flareMat = FxUtil.Transparent(new Color(3f, 0.25f, 0.1f, 1f), FxUtil.Soft, true);
        var hitMat = FxUtil.Transparent(new Color(4f, 1.6f, 0.6f, 1f), FxUtil.Soft, true);
        eyeFlareL = FxUtil.Quad("EyeFlareL", transform.position, Quaternion.identity, 0.2f, flareMat);
        eyeFlareR = FxUtil.Quad("EyeFlareR", transform.position, Quaternion.identity, 0.2f, flareMat);
        hitFlare = FxUtil.Quad("HitFlare", transform.position, Quaternion.identity, 1.5f, hitMat);
        foreach (var r in new[] { eyeFlareL, eyeFlareR, hitFlare }) r.enabled = false;

        eyeLight = new GameObject("EyeLight").AddComponent<Light>();
        eyeLight.type = LightType.Point; eyeLight.color = new Color(1f, 0.15f, 0.08f); eyeLight.range = 3f; eyeLight.intensity = 0f;
        hitLight = new GameObject("HitLight").AddComponent<Light>();
        hitLight.type = LightType.Point; hitLight.color = new Color(1f, 0.42f, 0.15f); hitLight.range = 12f; hitLight.intensity = 0f;

        sparks = FxUtil.System("HeatSparks", FxUtil.Spark, 600);
        var m = sparks.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.8f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(4f, 14f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        m.gravityModifier = 1.6f;
        var sh = sparks.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 60f; sh.radius = 0.1f;
        var sr = sparks.GetComponent<ParticleSystemRenderer>();
        sr.renderMode = ParticleSystemRenderMode.Stretch; sr.velocityScale = 0.05f; sr.lengthScale = 1.5f;

        embers = FxUtil.System("HeatEmbers", FxUtil.Spark, 200);
        m = embers.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.5f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.5f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
        m.gravityModifier = -0.15f;
        sh = embers.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.4f;
        var ec = embers.colorOverLifetime; ec.enabled = true; ec.color = FxUtil.Fade(0.1f, 0.4f);

        smoke = FxUtil.System("HeatSmoke", FxUtil.Smoke, 300);
        m = smoke.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.5f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m.startColor = new Color(0.2f, 0.19f, 0.18f, 0.55f);
        m.gravityModifier = -0.12f;
        sh = smoke.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.3f;
        var sol = smoke.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 3f));
        var scol = smoke.colorOverLifetime; scol.enabled = true; scol.color = FxUtil.Fade(0.1f, 0.4f);

        steam = FxUtil.System("HeatSteam", FxUtil.Steam, 400);
        m = steam.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 5f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.8f, 2.4f);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m.startColor = new Color(0.95f, 0.97f, 1f, 0.6f);
        m.gravityModifier = -0.25f;
        sh = steam.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 25f; sh.radius = 0.4f;
        sol = steam.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.5f, 1, 2.5f));
        var stc = steam.colorOverLifetime; stc.enabled = true; stc.color = FxUtil.Fade(0.08f, 0.35f);

        laserAudio = Sfx.Loop(gameObject, Sfx.Laser, 0.2f);
        sizzleAudio = Sfx.Loop(hitLight.gameObject, Sfx.Sizzle, 0.8f);
        steamAudio = Sfx.Loop(steam.gameObject, Sfx.Steam, 0.8f);

        scorchMat = FxUtil.Transparent(new Color(0.03f, 0.025f, 0.02f, 0.9f), FxUtil.Burn, false);
        scorchGlowMat = FxUtil.Transparent(new Color(3f, 0.8f, 0.15f, 1f), FxUtil.Soft, true);
    }

    void UpdateHeatVision()
    {
        bool on = hero.Blasting && anim;
        beamOn = Mathf.MoveTowards(beamOn, on ? 1f : 0f, Time.deltaTime * (on ? 10f : 7f));
        bool visible = beamOn > 0.001f;
        foreach (var lr in beams) lr.enabled = visible;
        eyeFlareL.enabled = eyeFlareR.enabled = visible;
        eyeLight.intensity = visible ? 3f * beamOn : 0f;
        laserAudio.volume = 0.4f * beamOn;
        laserAudio.pitch = 0.95f + 0.1f * Mathf.PerlinNoise(Time.time * 3f, 0f);
        if (visible && !laserAudio.isPlaying) laserAudio.Play();
        if (!visible)
        {
            if (laserAudio.isPlaying) laserAudio.Stop();
            StopHitFx();
            return;
        }

        // eyes: in front of the head, spread across the face
        var head = anim.GetBoneTransform(HumanBodyBones.Head);
        Vector3 headPos = head ? head.position : transform.position + Vector3.up * 1.7f;
        Vector3 aim = AimPoint(headPos);
        Vector3 toAim = (aim - headPos).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, toAim);
        right = right.sqrMagnitude > 1e-4f ? right.normalized : transform.right;
        Vector3 eyes = headPos + Vector3.up * 0.035f + toAim * 0.1f;
        Vector3 eyeL = eyes - right * 0.032f, eyeR = eyes + right * 0.032f;
        Vector3 dir = (aim - eyes).normalized;

        Vector3 end; BeamHit kind = BeamHit.None; Vector3 normal = -dir;
        if (beamOn > 0.6f) Trace(eyes, dir, out end, out kind, out normal);
        else end = eyes + dir * Mathf.Lerp(0.5f, 30f, beamOn);
        BeamEnd = end; BeamHitName = kind.ToString();

        // beams
        float flicker = 0.85f + 0.3f * Mathf.PerlinNoise(Time.time * 40f, 0.3f);
        Vector3[] starts = { eyeL, eyeR, eyeL, eyeR };
        for (int i = 0; i < 4; i++)
        {
            beams[i].SetPosition(0, starts[i]);
            beams[i].SetPosition(1, end);
            float w = (i < 2 ? 0.035f : 0.2f) * beamOn * flicker;
            beams[i].startWidth = w * 0.7f;
            beams[i].endWidth = w * (i < 2 ? 1.6f : 2.2f);
        }
        var cam = Camera.main;
        Quaternion face = cam ? Quaternion.LookRotation(cam.transform.forward) : Quaternion.identity;
        eyeFlareL.transform.SetPositionAndRotation(eyeL, face);
        eyeFlareR.transform.SetPositionAndRotation(eyeR, face);
        eyeFlareL.transform.localScale = eyeFlareR.transform.localScale = Vector3.one * 0.22f * flicker * beamOn;
        eyeLight.transform.position = eyes + dir * 0.3f;

        if (kind == BeamHit.None) { StopHitFx(); return; }
        hitFlare.enabled = true;
        hitFlare.transform.SetPositionAndRotation(end - dir * 0.2f, face);
        hitFlare.transform.localScale = Vector3.one * (kind == BeamHit.Water ? 1.2f : 1.8f) * flicker;
        hitLight.transform.position = end + normal * 0.6f;
        hitLight.intensity = 8f * flicker;
        Vector3 bounce = Vector3.Reflect(dir, normal);
        bool hot = kind != BeamHit.Water;
        sparks.transform.SetPositionAndRotation(end, Quaternion.LookRotation(bounce));
        SetRate(sparks, hot ? 260f : 0f);
        embers.transform.position = end;
        SetRate(embers, hot ? 25f : 0f);
        smoke.transform.position = end + normal * 0.3f;
        SetRate(smoke, hot ? 14f : 0f);
        steam.transform.SetPositionAndRotation(end, Quaternion.LookRotation(Vector3.up));
        SetRate(steam, kind == BeamHit.Water ? 90f : 0f);
        sizzleAudio.volume = hot ? 0.5f : 0f;
        steamAudio.volume = kind == BeamHit.Water ? 0.6f : 0f;
        if (!sizzleAudio.isPlaying) sizzleAudio.Play();
        if (!steamAudio.isPlaying) steamAudio.Play();

        if (kind == BeamHit.Ground) Scorch(end, normal);
    }

    const float SpaceLaserRange = 3000f;
    /// The beam reaches much further in space, where the targets are ships kilometres away.
    float Range => SpaceSystem.Instance && SpaceSystem.Instance.InSpace ? SpaceLaserRange : laserRange;

    Vector3 AimPoint(Vector3 headPos)
    {
        if (AimOverride.HasValue) return AimOverride.Value;
        var cam = Camera.main;
        if (!cam) return headPos + transform.forward * 50f;
        Vector3 o = cam.transform.position, f = cam.transform.forward;
        // in space, heat vision locks onto a fighter close to the crosshair (they are small and fast)
        var ss = SpaceSystem.Instance;
        if (ss && ss.InSpace)
        {
            SpaceFighter best = null; float bestAngle = 8f;
            foreach (var sf in SpaceFighter.All)
            {
                if (!sf || sf.Destroyed || sf.Held) continue;
                Vector3 to = sf.transform.position - o;
                if (to.magnitude > SpaceLaserRange) continue;
                float ang = Vector3.Angle(f, to);
                if (ang < bestAngle) { bestAngle = ang; best = sf; }
            }
            if (best) return best.transform.position;
        }
        // start the aim ray level with the hero so walls behind him don't count
        float skip = Mathf.Max(0f, Vector3.Dot(headPos - o, f));
        return Physics.Raycast(o + f * skip, f, out var h, Range, FxUtil.WorldMask, QueryTriggerInteraction.Ignore)
            ? h.point : o + f * (skip + Range);
    }

    /// Follow the beam: burn through buildings (passing through finished holes), heat props, scorch ground.
    void Trace(Vector3 origin, Vector3 dir, out Vector3 end, out BeamHit kind, out Vector3 normal)
    {
        var ds = DamageSystem.Instance;
        var hits = Physics.RaycastAll(origin, dir, Range, FxUtil.WorldMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        float dt = Time.deltaTime;

        foreach (var h in hits)
        {
            var d = h.collider.GetComponentInParent<Destructible>();
            if (d)
            {
                if (d.Collapsing) { end = h.point; normal = h.normal; kind = BeamHit.Building; return; }
                Vector3 exit = FarSide(h.collider, d, h.point - dir * 0.05f, dir, out Vector3 exitNormal);
                if (ds.FindHole(d, h.point, out var hole))
                {
                    if (ds.Radius(hole) < holeRadius - 0.01f) { BurnHole(d, hole, h.point, dt); end = h.point; normal = h.normal; kind = BeamHit.Building; return; }
                    // entry is open: is the far wall open too?
                    if (ds.FindHole(d, exit, out var hole2))
                    {
                        if (ds.Radius(hole2) < holeRadius - 0.01f) { BurnHole(d, hole2, exit, dt); end = exit; normal = -dir; kind = BeamHit.Building; return; }
                        continue;   // clean through: keep going to whatever is behind
                    }
                    // the beam moved inside the building: burn the far wall from the inside
                    currentHole = ds.AddHole(d, exit - dir * 0.6f, exit + dir * 0.6f, holeStartRadius, true);
                    BurnHole(d, currentHole, exit, dt);
                    end = exit; normal = -dir; kind = BeamHit.Building; return;
                }
                // fresh wall: start a tunnel through to the far side
                currentHole = ds.AddHole(d, h.point - dir * 0.4f, exit + dir * 0.4f, holeStartRadius, true);
                BurnHole(d, currentHole, h.point, dt);
                end = h.point; normal = h.normal; kind = BeamHit.Building; return;
            }
            var target = h.collider.GetComponentInParent<IHittable>();
            if (target != null)
            {
                target.Hit(h.point, dir * 6f, laserDps * dt, HitKind.Laser);
                end = h.point; normal = h.normal; kind = BeamHit.Prop; return;
            }
            var b = h.collider.GetComponentInParent<Breakable>();
            if (b)
            {
                b.Heat(dt, h.point, dir);
                end = h.point; normal = h.normal; kind = BeamHit.Prop; return;
            }
            end = h.point; normal = h.normal;
            kind = h.collider.name == "SeaSurface" ? BeamHit.Water : BeamHit.Ground;
            return;
        }
        end = origin + dir * Range; normal = -dir; kind = BeamHit.None;
    }

    void BurnHole(Destructible d, DamageSystem.HoleRef hole, Vector3 p, float dt)
    {
        DamageSystem.Instance.Grow(hole, holeRadius, holeGrowRate);
        d.Damage(laserDps * dt, p);
    }

    void Scorch(Vector3 p, Vector3 n)
    {
        if ((p - lastScorch).sqrMagnitude < 0.35f * 0.35f) return;
        lastScorch = p;
        var root = new GameObject("Scorch");
        root.layer = FxUtil.HeroLayer;
        root.transform.position = p;
        var burn = FxUtil.Quad("Burn", p + n * 0.03f, Quaternion.LookRotation(-n) * Quaternion.Euler(0, 0, Random.Range(0f, 360f)), Random.Range(1.0f, 1.5f), scorchMat, root.transform);
        var glow = FxUtil.Quad("Glow", p + n * 0.05f, Quaternion.LookRotation(-n), 0.9f, scorchGlowMat, root.transform);
        var bf = burn.gameObject.AddComponent<TimedFx>(); bf.mode = TimedFx.Mode.Fade; bf.life = 12f; bf.fadeTime = 3f;
        var gf = glow.gameObject.AddComponent<TimedFx>(); gf.mode = TimedFx.Mode.Fade; gf.life = 3f; gf.fadeTime = 2.5f;
        Destroy(root, 12.5f);
        scorches.Enqueue(root);
        while (scorches.Count > 200) { var old = scorches.Dequeue(); if (old) Destroy(old); }
    }

    void StopHitFx()
    {
        if (hitFlare) hitFlare.enabled = false;
        if (hitLight) hitLight.intensity = 0f;
        SetRate(sparks, 0f); SetRate(embers, 0f); SetRate(smoke, 0f); SetRate(steam, 0f);
        if (sizzleAudio) sizzleAudio.volume = 0f;
        if (steamAudio) steamAudio.volume = 0f;
    }

    static void SetRate(ParticleSystem ps, float rate)
    {
        if (!ps) return;
        var e = ps.emission; e.rateOverTime = rate;
    }
}

/// <summary>An expanding, fading ring (impact shockwave).</summary>
public class Shockwave : MonoBehaviour
{
    static Material mat;
    float size, life, age;
    MeshRenderer r;
    MaterialPropertyBlock block;

    public static void Spawn(Vector3 pos, Vector3 normal, float size, float life)
    {
        if (!mat) mat = FxUtil.Transparent(new Color(1f, 0.97f, 0.92f, 1f), FxUtil.Ring, true);
        var q = FxUtil.Quad("Shockwave", pos, Quaternion.LookRotation(-normal), 0.1f, mat);
        var s = q.gameObject.AddComponent<Shockwave>();
        s.size = size; s.life = life; s.r = q; s.block = new MaterialPropertyBlock();
    }

    void Update()
    {
        age += Time.deltaTime;
        float k = age / life;
        if (k >= 1f) { Destroy(gameObject); return; }
        transform.localScale = Vector3.one * Mathf.Lerp(0.5f, size, 1f - (1f - k) * (1f - k));
        float a = (1f - k) * 0.9f;
        block.SetColor("_BaseColor", new Color(a, a * 0.97f, a * 0.92f, a));
        r.SetPropertyBlock(block);
    }
}
