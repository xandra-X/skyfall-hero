using System.Collections;
using UnityEngine;

/// <summary>
/// Enemy space fighter. Flies in squadrons (a leader and wingmen in a V) on patrols around space, often
/// sweeping past the hero; when he comes close they break formation, circle him and fire laser bolts
/// (they can't hurt Superman, but they flash and shake). A bright engine flare keeps far ships visible.
/// Heat vision, punches, ramming or being thrown blow it apart into drifting pieces.
/// </summary>
public class SpaceFighter : Grabbable, IHittable
{
    public bool Destroyed { get; private set; }
    public override bool CanGrab => !Destroyed;
    public override Vector3 HoldOffset => Vector3.zero;
    public override float ThrowSpeed => 70f;

    public static readonly System.Collections.Generic.List<SpaceFighter> All = new System.Collections.Generic.List<SpaceFighter>();
    public SpaceFighter leader;          // null for a squadron leader
    public Vector3 slot;                 // place in the formation, relative to the leader

    const float PatrolSpeed = 230f, AttackSpeed = 260f, AttackRange = 3200f;
    Vector3 waypoint, velocity, passOffset;
    bool passing;
    Transform flare;
    static Material flareMat;
    float heat, nextShot, thrownAt = -1f;
    bool thrown;
    Rigidbody body;
    AudioSource engine;
    ParticleSystem trail;
    static Material boltMat, trailMat, boomMat;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Start()
    {
        if (!TryGetComponent(out body)) body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.None;     // flown by moving the transform; smoothing would fight that
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.mass = 8000f;
        if (!GetComponent<Collider>())
        {
            var b = gameObject.AddComponent<BoxCollider>();
            var rs = GetComponentsInChildren<Renderer>();
            var bb = rs[0].bounds; foreach (var r in rs) bb.Encapsulate(r.bounds);
            b.center = transform.InverseTransformPoint(bb.center);
            b.size = new Vector3(bb.size.x, bb.size.y, bb.size.z);
        }
        engine = gameObject.AddComponent<AudioSource>();
        engine.clip = Sfx.Jet; engine.loop = true; engine.spatialBlend = 1f;
        engine.minDistance = 30f; engine.maxDistance = 1500f; engine.volume = 0.6f; engine.pitch = 1.6f;
        engine.dopplerLevel = 0.6f;
        engine.Play();
        BuildTrail();
        flareMat ??= FxUtil.Transparent(new Color(1.6f, 2.4f, 4f, 1f), FxUtil.Soft, true);
        flare = FxUtil.Quad("EngineFlare", trail.transform.position, transform.rotation, 1f, flareMat, transform).transform;
        flare.gameObject.layer = FxUtil.HeroLayer;
        velocity = transform.forward * PatrolSpeed;
        waypoint = SpaceSystem.Instance ? SpaceSystem.Instance.RandomPatrolPoint() : transform.position + transform.forward * 1000f;
        nextShot = Time.time + Random.Range(1f, 3f);
    }

    void BuildTrail()
    {
        trailMat ??= FxUtil.Transparent(new Color(0.4f, 0.8f, 2.5f, 1f), FxUtil.Soft, true);
        var col = GetComponent<BoxCollider>();
        trail = FxUtil.System("EngineTrail", trailMat, 300, transform);
        trail.transform.localPosition = col ? col.center - Vector3.forward * col.size.z * 0.5f : Vector3.back * 7f;
        var m = trail.main;
        m.startLifetime = 1.4f; m.startSpeed = 0f; m.startSize = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
        var c = trail.colorOverLifetime; c.enabled = true; c.color = FxUtil.Fade(0.02f, 0.2f);
        var sol = trail.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1f, 1, 0.1f));
        var e = trail.emission; e.rateOverTime = 0f; e.rateOverDistance = 0.12f;
    }

    void Update()
    {
        if (Destroyed || Held) return;
        if (thrown)
        {
            if (Time.time - thrownAt > 4f) BlowUp(transform.position, body.linearVelocity);
            return;
        }
        var ss = SpaceSystem.Instance;
        Transform hero = ss ? ss.Hero : null;
        // standing on the Moon he is easy to find: they come hunting from much further away
        float range = ss && ss.OnMoon ? 100000f : AttackRange;
        bool attack = hero && ss.InSpace && Vector3.Distance(hero.position, transform.position) < range;
        if (!attack) passing = false;
        Vector3 target;
        float speed;
        if (attack)
        {
            // strafing runs: dive past him to a point on his far side, swing round, come again
            if (!passing || Vector3.Distance(transform.position, hero.position + passOffset) < 250f)
            {
                Vector3 across = hero.position - transform.position;
                across = across.sqrMagnitude > 1f ? across.normalized : Random.onUnitSphere;
                passOffset = (across + Random.insideUnitSphere * 0.5f).normalized * Random.Range(700f, 1100f);
                if (ss.OnMoon)
                {
                    // on the Moon: low strafing runs skimming over him, never into the ground
                    Vector3 up = (hero.position - ss.MoonCenter).normalized;
                    passOffset = Vector3.ProjectOnPlane(passOffset, up).normalized * Random.Range(800f, 1200f) + up * Random.Range(120f, 320f);
                }
                passing = true;
            }
            target = hero.position + passOffset;
            // far away they race in, slowing to attack speed for the run itself
            speed = Mathf.Lerp(AttackSpeed, 900f, Mathf.InverseLerp(3000f, 15000f, Vector3.Distance(transform.position, hero.position)));
            if (Time.time > nextShot)
            {
                nextShot = Time.time + Random.Range(0.9f, 1.8f);
                StartCoroutine(Burst(hero));
            }
        }
        else if (leader && !leader.Destroyed)
        {
            // keep station in the V behind the leader
            target = leader.transform.position + leader.transform.rotation * slot + leader.velocity * 0.6f;
            speed = Mathf.Min(PatrolSpeed * 1.25f, leader.velocity.magnitude + Vector3.Distance(transform.position, target - leader.velocity * 0.6f) * 0.8f);
        }
        else
        {
            target = waypoint;
            speed = PatrolSpeed;
            if (Vector3.Distance(transform.position, waypoint) < 400f && ss) waypoint = ss.RandomPatrolPoint();
        }
        if (ss && ss.moon) target = AvoidMoon(ss, target);
        if (ss) target = AvoidEarth(ss, target);
        Vector3 want = (target - transform.position).normalized * speed;
        if (ss && ss.moon) want = SteerOffMoon(ss, want);
        if (ss) want = SteerOffEarth(ss, want);
        velocity = Vector3.Lerp(velocity, want, 1f - Mathf.Exp(-(leader ? 2.5f : 1.2f) * Time.deltaTime));
        transform.position += velocity * Time.deltaTime;
        if (velocity.sqrMagnitude > 1f)
        {
            Quaternion look = Quaternion.LookRotation(velocity.normalized);
            float turn = Vector3.SignedAngle(transform.forward, velocity.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look * Quaternion.Euler(0f, 0f, -Mathf.Clamp(turn * 3f, -60f, 60f)), 1f - Mathf.Exp(-3f * Time.deltaTime));
        }
    }

    /// Keep the flight path above the Moon's surface: lift targets that are under it and steer round it.
    Vector3 AvoidMoon(SpaceSystem ss, Vector3 target)
    {
        Vector3 c = ss.MoonCenter;
        float safe = ss.moonRadius + 140f;
        Vector3 t = target - c;
        if (t.magnitude < safe) target = c + t.normalized * safe;
        Vector3 ahead = transform.position + velocity.normalized * Mathf.Min(900f, Vector3.Distance(transform.position, target)) - c;
        if (ahead.magnitude < safe + 150f) target = c + ahead.normalized * (safe + 500f);
        return target;
    }

    /// Never fly through the Earth: keep targets above its atmosphere and steer round the planet.
    Vector3 AvoidEarth(SpaceSystem ss, Vector3 target)
    {
        Vector3 c = ss.EarthCentre;
        float safe = ss.earthRadius + 4000f;
        Vector3 t = target - c;
        if (t.magnitude < safe) target = c + t.normalized * safe;
        Vector3 ahead = transform.position + velocity.normalized * Mathf.Min(3000f, Vector3.Distance(transform.position, target)) - c;
        if (ahead.magnitude < safe + 500f) target = c + ahead.normalized * (safe + 2000f);
        return target;
    }

    Vector3 SteerOffEarth(SpaceSystem ss, Vector3 want)
    {
        Vector3 rel = transform.position - ss.EarthCentre;
        float h = rel.magnitude - ss.earthRadius;
        if (h > 5000f) return want;
        Vector3 n = rel.normalized;
        float inward = Vector3.Dot(want, n);
        if (inward < 0f) want -= n * inward * Mathf.Clamp01(1f - (h - 3500f) / 1500f);
        return want;
    }

    /// Near the surface, don't fly any lower: cancel the downward part of the heading and climb back out.
    Vector3 SteerOffMoon(SpaceSystem ss, Vector3 want)
    {
        Vector3 rel = transform.position - ss.MoonCenter;
        float h = rel.magnitude - ss.moonRadius;
        if (h > 400f) return want;
        Vector3 n = rel.normalized;
        float inward = Vector3.Dot(want, n);
        if (inward < 0f) want -= n * inward * Mathf.Clamp01(1f - (h - 80f) / 320f);
        if (h < 80f) want += n * want.magnitude * 0.4f;
        return want;
    }

    void LateUpdate()
    {
        // the engine glow grows with distance so a ship far away still shows as a moving light
        var cam = Camera.main;
        if (!flare || !cam) return;
        float d = Vector3.Distance(cam.transform.position, flare.position);
        flare.rotation = Quaternion.LookRotation(flare.position - cam.transform.position);
        flare.gameObject.SetActive(d > 150f);                       // close up the ship itself is enough
        flare.localScale = Vector3.one * Mathf.Clamp(d * 0.004f, 1.5f, 40f) / Mathf.Max(transform.lossyScale.x, 0.001f);
    }

    IEnumerator Burst(Transform hero)
    {
        for (int i = 0; i < 2 && hero && !Destroyed; i++)
        {
            Vector3 from = transform.position + transform.forward * 7f + transform.right * (i == 0 ? 1.5f : -1.5f);
            LaserBolt.Fire(from, hero.position + Vector3.up * 1f + Random.insideUnitSphere * 6f, hero);
            Sfx.Play(Sfx.Laser, from, 0.35f, 2.2f, 1f);
            yield return new WaitForSeconds(0.12f);
        }
    }

    // ================================================================== damage
    public void Hit(Vector3 point, Vector3 velocity, float damage, HitKind kind)
    {
        if (Destroyed) return;
        if (kind == HitKind.Laser)
        {
            heat += Time.deltaTime;
            if (heat < 0.45f) return;
        }
        BlowUp(point, velocity);
    }

    void OnCollisionEnter(Collision c)
    {
        if (!thrown || Destroyed || Time.time - thrownAt < 0.2f) return;
        if (c.collider.GetComponentInParent<HeroController>()) return;
        var other = c.collider.GetComponentInParent<IHittable>();
        if (other != null && (Object)other != this) other.Hit(c.GetContact(0).point, body.linearVelocity, 500f, HitKind.Impact);
        BlowUp(c.GetContact(0).point, body.linearVelocity);
    }

    public void BlowUp(Vector3 point, Vector3 push)
    {
        if (Destroyed) return;
        Destroyed = true;
        if (Held) { transform.SetParent(null, true); ForceRelease(); }
        Vector3 baseVel = (thrown ? body.linearVelocity : velocity) + push * 0.3f;
        Boom(transform.position);
        if (flare) Destroy(flare.gameObject);
        // break into its parts, drifting and tumbling in zero gravity
        var model = transform.Find("Model") ?? transform;
        foreach (var mr in model.GetComponentsInChildren<MeshRenderer>())
        {
            var part = mr.transform;
            part.SetParent(null, true);
            part.gameObject.layer = FxUtil.HeroLayer;
            var rb = part.gameObject.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.linearDamping = 0.02f;
            Vector3 out_ = (mr.bounds.center - transform.position);
            out_ = out_.sqrMagnitude > 0.01f ? out_.normalized : Random.onUnitSphere;
            rb.linearVelocity = baseVel * 0.6f + out_ * Random.Range(8f, 30f);
            rb.angularVelocity = Random.insideUnitSphere * 3f;
            mr.sharedMaterials = Darken(mr.sharedMaterials);
            var fx = part.gameObject.AddComponent<TimedFx>();
            fx.life = Random.Range(10f, 16f); fx.fadeTime = 1.5f; fx.mode = TimedFx.Mode.Shrink;
            if (Random.value < 0.3f) Embers(part);
        }
        Destroy(gameObject);
    }

    static Material darkMat;
    static Material[] Darken(Material[] mats)
    {
        darkMat ??= FxUtil.Lit(new Color(0.12f, 0.11f, 0.1f), 0.2f);
        var r = new Material[mats.Length];
        for (int i = 0; i < r.Length; i++) r[i] = Random.value < 0.5f ? darkMat : mats[i];
        return r;
    }

    static void Embers(Transform part)
    {
        var ps = FxUtil.System("Embers", FxUtil.Spark, 80, part);
        var m = ps.main;
        m.startLifetime = 0.6f; m.startSpeed = 2f; m.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        var e = ps.emission; e.rateOverTime = 40f;
        var stop = ps.gameObject.AddComponent<FireLife>(); stop.duration = 4f; stop.others = new ParticleSystem[0];
    }

    /// Explosion in space: fireball, flash, sparks and a ring, no smoke column.
    public static void Boom(Vector3 at)
    {
        boomMat ??= FxUtil.Transparent(new Color(3.5f, 1.6f, 0.5f, 1f), FxUtil.Soft, true);
        FxUtil.Burst("SpaceFireball", boomMat, at, Quaternion.identity, 70, new Vector2(0.4f, 1.1f), new Vector2(8f, 30f),
            new Vector2(4f, 10f), Color.white, 0f, 180f, 1f, false, 0.4f, 2f);
        FxUtil.Burst("SpaceSparks", FxUtil.Spark, at, Quaternion.identity, 120, new Vector2(0.6f, 1.6f), new Vector2(20f, 60f),
            new Vector2(0.3f, 0.6f), Color.white, 0f, 180f, 1f, true);
        Shockwave.Spawn(at, Random.onUnitSphere, 60f, 0.6f);
        var l = new GameObject("BoomFlash").AddComponent<Light>();
        l.transform.position = at; l.type = LightType.Point; l.color = new Color(1f, 0.6f, 0.3f); l.range = 120f; l.intensity = 40f;
        l.gameObject.AddComponent<LightFade>().life = 0.7f;
        Sfx.Play(Sfx.Explosion, at, 1f, 0.9f, 0.6f);
        var cam = Camera.main;
        if (cam) ThirdPersonCamera.Shake(Mathf.Lerp(0.8f, 0.05f, Mathf.InverseLerp(30f, 1500f, Vector3.Distance(cam.transform.position, at))), 0.5f);
    }

    // ================================================================== grab / throw
    protected override void OnGrabbed()
    {
        engine.pitch = 1.1f;
        foreach (var c in GetComponents<Collider>()) c.enabled = false;
    }

    protected override void OnThrown(Vector3 v)
    {
        thrown = true;
        thrownAt = Time.time;
        foreach (var c in GetComponents<Collider>()) c.enabled = true;
        body.isKinematic = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.linearVelocity = v;
        body.angularVelocity = Random.insideUnitSphere * 1.5f;
    }

    protected override void OnDropped(Vector3 groundPoint)
    {
        foreach (var c in GetComponents<Collider>()) c.enabled = true;
        velocity = Vector3.zero;
    }
}

/// <summary>A red laser bolt from a fighter. Harmless to Superman: it splashes off him.</summary>
public class LaserBolt : MonoBehaviour
{
    static Material mat;
    Vector3 dir;
    Transform target;
    float life = 2.5f;
    const float Speed = 700f;

    public static void Fire(Vector3 from, Vector3 at, Transform target)
    {
        mat ??= FxUtil.Transparent(new Color(4f, 0.3f, 0.2f, 1f), FxUtil.Beam, true);
        var go = new GameObject("LaserBolt");
        go.layer = FxUtil.HeroLayer;
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = mat;
        lr.positionCount = 2;
        lr.startWidth = lr.endWidth = 0.6f;
        lr.textureMode = LineTextureMode.Stretch;
        lr.alignment = LineAlignment.View;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var b = go.AddComponent<LaserBolt>();
        b.transform.position = from;
        b.dir = (at - from).normalized;
        b.target = target;
    }

    void Update()
    {
        Vector3 p = transform.position + dir * Speed * Time.deltaTime;
        var lr = GetComponent<LineRenderer>();
        lr.SetPosition(0, p - dir * 18f);
        lr.SetPosition(1, p);
        if (target && DamageSystem.DistanceToSegment(target.position + Vector3.up, transform.position, p) < 2.5f)
        {
            // splashes harmlessly off his chest
            FxUtil.Burst("BoltSplash", FxUtil.Spark, target.position + Vector3.up, Quaternion.LookRotation(-dir), 25,
                new Vector2(0.2f, 0.5f), new Vector2(5f, 15f), new Vector2(0.1f, 0.25f), Color.white, 0f, 70f, 0.2f, true);
            Sfx.Play(Sfx.Clank, target.position, 0.4f, 1.8f, 0.3f);
            ThirdPersonCamera.Shake(0.12f, 0.15f);
            Destroy(gameObject);
            return;
        }
        transform.position = p;
        life -= Time.deltaTime;
        if (life <= 0f) Destroy(gameObject);
    }
}
