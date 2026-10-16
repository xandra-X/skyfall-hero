using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A pedestrian built from simple shapes: walks the sidewalks, panics and flees when someone dies nearby,
/// struggles when picked up, goes limp (ragdoll) with blood when killed.
/// </summary>
public class Civilian : Grabbable, IHittable
{
    public static readonly List<Civilian> All = new List<Civilian>();
    public const int RoadArea = 3;
    public const int SidewalkArea = 4;

    public bool Dead { get; private set; }
    public bool Ragdolled { get; private set; }
    public override bool CanGrab => !Dead && !Ragdolled;
    public override Vector3 HoldOffset => new Vector3(0.88f, 0.15f, 0f);           // lying across the hands
    protected override Quaternion HoldRotation => Quaternion.Euler(0f, 0f, 90f);
    public override float ThrowSpeed => 42f;
    public Transform Hips => hips;

    NavMeshAgent agent;
    Rigidbody rootBody;
    CapsuleCollider capsule;
    AudioSource voice;
    Transform hips, spine, head, shoulderL, shoulderR, elbowL, elbowR, hipL, hipR, kneeL, kneeR;
    Transform[] pivots;
    readonly Dictionary<Transform, (Vector3 pos, Quaternion rot)> rest = new Dictionary<Transform, (Vector3, Quaternion)>();
    float walkSpeed, runSpeed, phase, panicUntil, idleUntil, heat, thrownAt = -1f;
    Vector3 danger;
    bool thrownAlive;

    static readonly Color[] Skins = { new Color(0.96f, 0.80f, 0.69f), new Color(0.87f, 0.67f, 0.52f), new Color(0.71f, 0.51f, 0.36f), new Color(0.48f, 0.32f, 0.22f), new Color(0.33f, 0.21f, 0.14f) };
    static readonly Color[] Shirts = { new Color(0.8f, 0.15f, 0.15f), new Color(0.15f, 0.3f, 0.7f), new Color(0.9f, 0.9f, 0.88f), new Color(0.15f, 0.15f, 0.17f), new Color(0.25f, 0.55f, 0.3f), new Color(0.95f, 0.75f, 0.2f), new Color(0.55f, 0.35f, 0.6f), new Color(0.9f, 0.5f, 0.6f), new Color(0.45f, 0.45f, 0.48f) };
    static readonly Color[] Pants = { new Color(0.15f, 0.2f, 0.35f), new Color(0.1f, 0.1f, 0.11f), new Color(0.45f, 0.4f, 0.32f), new Color(0.35f, 0.35f, 0.37f), new Color(0.25f, 0.18f, 0.12f) };
    static readonly Color[] Hair = { new Color(0.08f, 0.06f, 0.05f), new Color(0.3f, 0.18f, 0.08f), new Color(0.75f, 0.6f, 0.3f), new Color(0.5f, 0.5f, 0.5f), new Color(0.45f, 0.15f, 0.05f) };

    // ================================================================== spawning
    public static Civilian Spawn(Vector3 pos)
    {
        var go = new GameObject("Civilian");
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        var c = go.AddComponent<Civilian>();
        c.Build();
        return c;
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Build()
    {
        rootBody = gameObject.AddComponent<Rigidbody>();
        rootBody.isKinematic = true;
        capsule = gameObject.AddComponent<CapsuleCollider>();
        capsule.height = 1.75f; capsule.radius = 0.28f; capsule.center = new Vector3(0f, 0.875f, 0f);

        agent = gameObject.AddComponent<NavMeshAgent>();
        agent.radius = 0.3f; agent.height = 1.8f;
        walkSpeed = Random.Range(1.1f, 1.6f); runSpeed = Random.Range(4f, 5.5f);
        agent.speed = walkSpeed; agent.acceleration = 8f; agent.angularSpeed = 420f;
        agent.stoppingDistance = 0.4f;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
        agent.avoidancePriority = Random.Range(40, 80);
        agent.SetAreaCost(RoadArea, 6f);                     // prefer sidewalks, cross roads when needed

        voice = gameObject.AddComponent<AudioSource>();
        voice.spatialBlend = 1f; voice.minDistance = 4f; voice.maxDistance = 90f; voice.dopplerLevel = 0f;
        voice.clip = Sfx.Scream; voice.playOnAwake = false;

        Material skin = Palette.Lit(Skins[Random.Range(0, Skins.Length)], 0.3f);
        Material shirt = Palette.Lit(Shirts[Random.Range(0, Shirts.Length)]);
        Material pants = Palette.Lit(Pants[Random.Range(0, Pants.Length)]);
        Material hair = Palette.Lit(Hair[Random.Range(0, Hair.Length)], 0.15f);
        Material shoes = Palette.Lit(Random.value < 0.6f ? new Color(0.07f, 0.06f, 0.06f) : new Color(0.85f, 0.85f, 0.85f), 0.4f);
        Material dark = Palette.Lit(new Color(0.03f, 0.03f, 0.03f), 0.6f);
        bool female = Random.value < 0.5f;
        bool skirt = female && Random.value < 0.4f;
        bool longSleeves = Random.value < 0.5f;
        float h = Random.Range(0.92f, 1.06f) * (female ? 0.95f : 1f);    // height
        float bulk = Random.Range(0.9f, 1.15f);                            // build

        var rig = Palette.Pivot(transform, Vector3.zero, "Rig");
        rig.localScale = Vector3.one * h;
        hips = Palette.Pivot(rig, new Vector3(0f, 0.95f, 0f), "Hips");
        Palette.Part(PrimitiveType.Sphere, hips, new Vector3(0f, 0.02f, 0f), new Vector3((female ? 0.36f : 0.33f) * bulk, 0.24f, 0.23f * bulk), skirt ? Palette.Lit(Shirts[Random.Range(0, Shirts.Length)]) : pants, "PelvisMesh");
        spine = Palette.Pivot(hips, new Vector3(0f, 0.08f, 0f), "Spine");
        // torso: rounded, wider at the shoulders (men) or narrower waist (women)
        Palette.Part(PrimitiveType.Capsule, spine, new Vector3(0f, 0.24f, 0f), new Vector3((female ? 0.32f : 0.38f) * bulk, 0.27f, 0.22f * bulk), shirt, "TorsoMesh");
        Palette.Part(PrimitiveType.Sphere, spine, new Vector3(0f, 0.42f, 0f), new Vector3((female ? 0.36f : 0.44f) * bulk, 0.2f, 0.24f * bulk), shirt, "ChestMesh");
        Palette.Part(PrimitiveType.Cylinder, spine, new Vector3(0f, 0.53f, 0f), new Vector3(0.1f, 0.05f, 0.1f), skin, "NeckMesh");
        head = Palette.Pivot(spine, new Vector3(0f, 0.56f, 0f), "Head");
        Palette.Part(PrimitiveType.Sphere, head, new Vector3(0f, 0.12f, 0f), new Vector3(0.19f, 0.235f, 0.215f), skin, "HeadMesh");
        Palette.Part(PrimitiveType.Sphere, head, new Vector3(0f, 0.04f, 0.035f), new Vector3(0.14f, 0.1f, 0.15f), skin, "JawMesh");
        foreach (float e in new[] { -1f, 1f })
        {
            Palette.Part(PrimitiveType.Sphere, head, new Vector3(e * 0.04f, 0.14f, 0.098f), new Vector3(0.025f, 0.018f, 0.012f), dark, "EyeMesh");
            Palette.Part(PrimitiveType.Sphere, head, new Vector3(e * 0.097f, 0.115f, 0f), new Vector3(0.03f, 0.05f, 0.025f), skin, "EarMesh");
        }
        Palette.Part(PrimitiveType.Cube, head, new Vector3(0f, 0.105f, 0.11f), new Vector3(0.025f, 0.05f, 0.03f), skin, "NoseMesh", Quaternion.Euler(-15f, 0f, 0f));
        int hairStyle = female ? Random.Range(1, 4) : Random.Range(0, 3) == 0 ? 0 : Random.value < 0.15f ? 4 : 1;
        if (hairStyle != 0)
            Palette.Part(PrimitiveType.Sphere, head, new Vector3(0f, 0.17f, -0.012f), new Vector3(0.205f, 0.17f, 0.23f), hair, "HairMesh");
        if (hairStyle == 2)       // long hair down the back
            Palette.Part(PrimitiveType.Capsule, head, new Vector3(0f, 0.0f, -0.07f), new Vector3(0.19f, 0.16f, 0.09f), hair, "HairMesh");
        if (hairStyle == 3)       // bun
            Palette.Part(PrimitiveType.Sphere, head, new Vector3(0f, 0.21f, -0.1f), new Vector3(0.09f, 0.09f, 0.09f), hair, "HairMesh");
        if (hairStyle == 4 || (!female && Random.value < 0.12f))   // cap
        {
            var capM = Palette.Lit(Shirts[Random.Range(0, Shirts.Length)]);
            Palette.Part(PrimitiveType.Sphere, head, new Vector3(0f, 0.19f, 0f), new Vector3(0.215f, 0.14f, 0.235f), capM, "CapMesh");
            Palette.Part(PrimitiveType.Cube, head, new Vector3(0f, 0.18f, 0.13f), new Vector3(0.17f, 0.015f, 0.1f), capM, "BrimMesh");
        }
        if (Random.value < 0.25f)                                 // backpack
            Palette.Part(PrimitiveType.Cube, spine, new Vector3(0f, 0.3f, -0.17f), new Vector3(0.28f, 0.34f, 0.13f), Palette.Lit(Pants[Random.Range(0, Pants.Length)]), "BackpackMesh");

        foreach (int sd in new[] { -1, 1 })
        {
            var sh = Palette.Pivot(spine, new Vector3((female ? 0.2f : 0.235f) * bulk * sd, 0.46f, 0f), sd < 0 ? "ShoulderR" : "ShoulderL");
            Palette.Part(PrimitiveType.Sphere, sh, new Vector3(0f, -0.02f, 0f), new Vector3(0.13f, 0.13f, 0.13f) * bulk, shirt, "DeltoidMesh");
            Palette.Part(PrimitiveType.Capsule, sh, new Vector3(0f, -0.15f, 0f), new Vector3(0.1f, 0.16f, 0.1f) * bulk, shirt, "UpperArmMesh");
            var el = Palette.Pivot(sh, new Vector3(0f, -0.3f, 0f), sd < 0 ? "ElbowR" : "ElbowL");
            Palette.Part(PrimitiveType.Capsule, el, new Vector3(0f, -0.13f, 0f), new Vector3(0.085f, 0.14f, 0.085f), longSleeves ? shirt : skin, "ForearmMesh");
            Palette.Part(PrimitiveType.Sphere, el, new Vector3(0f, -0.3f, 0.01f), new Vector3(0.07f, 0.1f, 0.05f), skin, "HandMesh");
            if (sd > 0 && female && Random.value < 0.35f)            // handbag
                Palette.Part(PrimitiveType.Cube, el, new Vector3(0.06f, -0.32f, 0f), new Vector3(0.08f, 0.2f, 0.26f), Palette.Lit(Pants[Random.Range(0, Pants.Length)], 0.5f), "BagMesh");
            var hp = Palette.Pivot(hips, new Vector3(0.1f * sd, -0.07f, 0f), sd < 0 ? "HipR" : "HipL");
            Palette.Part(PrimitiveType.Capsule, hp, new Vector3(0f, -0.22f, 0f), new Vector3(0.15f, 0.23f, 0.15f) * bulk, skirt ? skin : pants, "ThighMesh");
            var kn = Palette.Pivot(hp, new Vector3(0f, -0.45f, 0f), sd < 0 ? "KneeR" : "KneeL");
            Palette.Part(PrimitiveType.Capsule, kn, new Vector3(0f, -0.2f, 0f), new Vector3(0.115f, 0.21f, 0.115f), skirt ? skin : pants, "ShinMesh");
            Palette.Part(PrimitiveType.Sphere, kn, new Vector3(0f, -0.42f, 0.05f), new Vector3(0.11f, 0.08f, 0.27f), shoes, "ShoeMesh");
            if (sd < 0) { shoulderR = sh; elbowR = el; hipR = hp; kneeR = kn; } else { shoulderL = sh; elbowL = el; hipL = hp; kneeL = kn; }
        }
        if (skirt)
            Palette.Part(PrimitiveType.Cylinder, hips, new Vector3(0f, -0.14f, 0f), new Vector3(0.4f * bulk, 0.15f, 0.3f * bulk), Palette.Lit(Shirts[Random.Range(0, Shirts.Length)]), "SkirtMesh");
        pivots = new[] { hips, spine, head, shoulderL, elbowL, shoulderR, elbowR, hipL, kneeL, hipR, kneeR };
        foreach (var p in pivots) rest[p] = (p.localPosition, p.localRotation);
        foreach (var r in GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
    }

    // ================================================================== life
    // ---- distance culling: far-away people are not drawn or animated
    public const float DrawDistance = 170f;
    static Vector3 camPos;
    static int camFrame = -1;
    MeshRenderer[] renderers;
    bool shown = true;

    /// Is this point within range of the camera? (shared by cars)
    public static bool Near(Vector3 p, float range)
    {
        if (camFrame != Time.frameCount)
        {
            camFrame = Time.frameCount;
            var c = Camera.main;
            camPos = c ? c.transform.position : Vector3.zero;
        }
        return (p - camPos).sqrMagnitude < range * range;
    }

    void Update()
    {
        bool near = Ragdolled || Held || Near(transform.position, DrawDistance);
        if (near != shown)
        {
            shown = near;
            renderers ??= GetComponentsInChildren<MeshRenderer>();
            foreach (var mr in renderers) if (mr) mr.enabled = near;
        }
        if (Dead) return;
        if (Held) { Flail(1f); return; }
        if (Ragdolled)
        {
            // thrown but alive: get back up if it landed softly
            if (thrownAlive && Time.time - thrownAt > 2.5f && hips.GetComponent<Rigidbody>().linearVelocity.magnitude < 0.8f) StartCoroutine(GetUp());
            return;
        }
        Think();
        if (shown) Animate();
    }

    void Think()
    {
        if (!agent.enabled || !agent.isOnNavMesh) return;
        bool panic = Time.time < panicUntil;
        agent.speed = panic ? runSpeed : walkSpeed;
        if (agent.pathPending) return;
        if (agent.remainingDistance > agent.stoppingDistance + 0.3f) return;
        if (panic) { Flee(); return; }
        if (idleUntil < 0f) { idleUntil = Time.time + Random.Range(0f, 3f); return; }
        if (Time.time < idleUntil) return;
        idleUntil = -1f;
        var pop = Population.Instance;
        if (pop) agent.SetDestination(pop.RandomPoint(false, transform.position, 70f));
    }

    void Flee()
    {
        Vector3 away = transform.position - danger; away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
        away.y = 0f;
        Vector3 target = transform.position + away.normalized * Random.Range(25f, 40f) + Random.insideUnitSphere * 6f;
        if (NavMesh.SamplePosition(target, out var hit, 10f, NavMesh.AllAreas)) agent.SetDestination(hit.position);
    }

    public void Panic(Vector3 from)
    {
        if (Dead) return;
        bool wasCalm = Time.time >= panicUntil;
        panicUntil = Time.time + Random.Range(8f, 13f);
        danger = from;
        if (!agent.enabled || !agent.isOnNavMesh) return;
        Flee();
        if (wasCalm && Random.value < 0.55f && !voice.isPlaying) { voice.pitch = Random.Range(0.85f, 1.35f); voice.PlayDelayed(Random.Range(0f, 0.4f)); }
    }

    void Animate()
    {
        float v = agent.enabled ? agent.velocity.magnitude : 0f;
        bool panic = Time.time < panicUntil;
        phase += Time.deltaTime * v * (panic ? 3.0f : 4.2f);
        float k = Mathf.Clamp01(v / 1.3f);
        float swing = Mathf.Lerp(0f, panic ? 50f : 30f, k);
        float s = Mathf.Sin(phase), c = Mathf.Cos(phase);
        hipL.localRotation = Quaternion.Euler(-s * swing, 0f, 0f);
        hipR.localRotation = Quaternion.Euler(s * swing, 0f, 0f);
        kneeL.localRotation = Quaternion.Euler(Mathf.Max(0f, c) * swing * 1.3f, 0f, 0f);
        kneeR.localRotation = Quaternion.Euler(Mathf.Max(0f, -c) * swing * 1.3f, 0f, 0f);
        if (panic && v > 0.5f)
        {
            // arms up, waving, running for their life
            float w = Mathf.Sin(Time.time * 13f) * 25f;
            shoulderL.localRotation = Quaternion.Euler(-150f + w, 0f, -15f);
            shoulderR.localRotation = Quaternion.Euler(-150f - w, 0f, 15f);
            elbowL.localRotation = elbowR.localRotation = Quaternion.Euler(-35f, 0f, 0f);
            spine.localRotation = Quaternion.Euler(12f, 0f, 0f);
        }
        else
        {
            shoulderL.localRotation = Quaternion.Euler(s * swing * 0.9f, 0f, -4f);
            shoulderR.localRotation = Quaternion.Euler(-s * swing * 0.9f, 0f, 4f);
            elbowL.localRotation = elbowR.localRotation = Quaternion.Euler(-12f - swing * 0.4f, 0f, 0f);
            spine.localRotation = Quaternion.Euler(3f * k, 0f, 0f);
        }
        hips.localPosition = rest[hips].pos + Vector3.up * Mathf.Abs(s) * 0.035f * k;
    }

    void Flail(float strength)
    {
        float t = Time.time * 14f;
        hipL.localRotation = Quaternion.Euler(Mathf.Sin(t) * 45f * strength, 0f, 0f);
        hipR.localRotation = Quaternion.Euler(Mathf.Sin(t + 2f) * 45f * strength, 0f, 0f);
        kneeL.localRotation = Quaternion.Euler(30f + Mathf.Sin(t * 1.3f) * 30f, 0f, 0f);
        kneeR.localRotation = Quaternion.Euler(30f + Mathf.Sin(t * 1.1f + 1f) * 30f, 0f, 0f);
        shoulderL.localRotation = Quaternion.Euler(-90f + Mathf.Sin(t * 0.9f) * 70f, 0f, -30f);
        shoulderR.localRotation = Quaternion.Euler(-90f + Mathf.Sin(t * 0.8f + 1.5f) * 70f, 0f, 30f);
        elbowL.localRotation = elbowR.localRotation = Quaternion.Euler(-40f + Mathf.Sin(t) * 30f, 0f, 0f);
        head.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.7f) * 25f, Mathf.Sin(t * 0.5f) * 40f, 0f);
    }

    // ================================================================== grab / throw
    protected override void OnGrabbed()
    {
        agent.enabled = false;
        capsule.enabled = false;
        voice.pitch = Random.Range(1f, 1.4f);
        voice.Play();
    }

    protected override void OnThrown(Vector3 velocity)
    {
        thrownAlive = true;
        thrownAt = Time.time;
        BecomeRagdoll(velocity, transform.position + Vector3.up);
        voice.pitch = Random.Range(1.1f, 1.5f);
        voice.Play();
    }

    protected override void OnDropped(Vector3 groundPoint)
    {
        transform.SetPositionAndRotation(groundPoint, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
        ResetPose();
        capsule.enabled = true;
        agent.enabled = true;
        if (agent.isOnNavMesh) agent.Warp(groundPoint);
        Panic(groundPoint + transform.forward);
    }

    /// Called by the ragdoll parts when they hit something.
    public void OnPartImpact(Collision c)
    {
        if (Dead || !thrownAlive) return;
        if (Time.time - thrownAt < 0.2f) return;                         // still leaving the hero's hands
        if (c.collider.GetComponentInParent<Civilian>() == this) return;  // own limbs touching
        if (c.collider.GetComponentInParent<HeroController>()) return;
        if (c.relativeVelocity.magnitude < 7f) return;
        var contact = c.GetContact(0);
        Kill(contact.point, -c.relativeVelocity * 0.3f, HitKind.Impact);
        var d = c.collider.GetComponentInParent<Destructible>();
        if (d && c.relativeVelocity.magnitude > 20f) HeroCombat.SmashWall(d, contact.point, contact.normal, -contact.normal, 0.9f, 25f, false);
    }

    void OnCollisionEnter(Collision c)
    {
        // walking person hit by a thrown car, a thrown body, a flying lamp...
        if (Dead || Ragdolled || c.relativeVelocity.magnitude < 6f) return;
        var other = c.collider;
        bool heavy = other.GetComponentInParent<Car>() || other.GetComponentInParent<Civilian>() || other.GetComponentInParent<Airplane>() || other.GetComponentInParent<Breakable>();
        if (!heavy) return;
        Kill(c.GetContact(0).point, c.relativeVelocity, HitKind.Impact);
    }

    // ================================================================== damage
    public void Hit(Vector3 point, Vector3 velocity, float damage, HitKind kind)
    {
        if (Dead)
        {
            if (!Ragdolled) return;
            foreach (var rb in GetComponentsInChildren<Rigidbody>()) rb.AddForce(velocity * 0.5f, ForceMode.VelocityChange);
            return;
        }
        if (kind == HitKind.Laser)
        {
            heat += Time.deltaTime;
            if (heat < 0.12f) return;
        }
        Kill(point, velocity, kind);
    }

    public void Kill(Vector3 point, Vector3 velocity, HitKind kind)
    {
        if (Dead) return;
        Dead = true;
        if (Held) { transform.SetParent(null, true); ForceRelease(); }
        voice.Stop();
        bool laser = kind == HitKind.Laser;
        bool torn = !laser && (velocity.magnitude > 38f || kind == HitKind.Explosion);
        if (!Ragdolled) BecomeRagdoll(velocity, point);
        else foreach (var rb in GetComponentsInChildren<Rigidbody>()) rb.AddForce(velocity * 0.4f, ForceMode.VelocityChange);

        Gore.Burst(point, velocity.sqrMagnitude > 0.01f ? velocity : Vector3.up, torn ? 2f : laser ? 0.8f : 1f);
        Gore.Pool(hips, torn ? 2.3f : 1.7f);
        if (!laser) Sfx.Play(Sfx.Crunch, point, 0.8f, Random.Range(0.8f, 1.1f));
        if (torn) TearApart(point, velocity);
        if (Population.Instance) Population.Instance.OnCivilianKilled(this, point);
        StartCoroutine(Decay());
    }


    void TearApart(Vector3 point, Vector3 velocity)
    {
        // limbs break off at the joints nearest the blow
        var joints = new List<CharacterJoint>(GetComponentsInChildren<CharacterJoint>());
        joints.Sort((a, b) => Vector3.Distance(a.transform.position, point).CompareTo(Vector3.Distance(b.transform.position, point)));
        int n = Mathf.Min(joints.Count, Random.Range(1, 4));
        for (int i = 0; i < n; i++)
        {
            Gore.Burst(joints[i].transform.position, velocity.normalized + Random.insideUnitSphere * 0.5f, 1.2f);
            var rb = joints[i].GetComponent<Rigidbody>();
            Destroy(joints[i]);
            if (rb) rb.AddForce(velocity * 0.3f + Random.insideUnitSphere * 6f, ForceMode.VelocityChange);
        }
    }

    // ================================================================== ragdoll
    void BecomeRagdoll(Vector3 velocity, Vector3 point)
    {
        Ragdolled = true;
        agent.enabled = false;
        capsule.enabled = false;
        rootBody.isKinematic = true;
        foreach (var t in GetComponentsInChildren<Transform>()) t.gameObject.layer = Population.BodyLayer;
        var bodies = new Dictionary<Transform, Rigidbody>();
        foreach (var p in pivots)
        {
            var rb = p.gameObject.AddComponent<Rigidbody>();
            rb.mass = p == spine ? 22f : p == hips ? 14f : p == head ? 5f : p.name.StartsWith("Hip") || p.name.StartsWith("Knee") ? 7f : 3f;
            rb.linearDamping = 0.05f; rb.angularDamping = 0.6f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            bodies[p] = rb;
            foreach (Transform m in p)
            {
                if (!m.GetComponent<MeshRenderer>()) continue;
                // one collider per body part (the small face / hair / accessory pieces don't need one)
                if (m.name.StartsWith("Torso") || m.name.StartsWith("Pelvis")) m.gameObject.AddComponent<BoxCollider>();
                else if (m.name.StartsWith("HeadMesh")) m.gameObject.AddComponent<SphereCollider>();
                else if (m.name.StartsWith("UpperArm") || m.name.StartsWith("Forearm") || m.name.StartsWith("Thigh") || m.name.StartsWith("Shin"))
                    m.gameObject.AddComponent<CapsuleCollider>();
            }
            p.gameObject.AddComponent<RagdollPart>().owner = this;
        }
        foreach (var p in pivots)
        {
            if (p == hips) continue;
            var j = p.gameObject.AddComponent<CharacterJoint>();
            j.connectedBody = bodies[p.parent];
            bool hinge = p.name.StartsWith("Knee") || p.name.StartsWith("Elbow");
            j.lowTwistLimit = new SoftJointLimit { limit = hinge ? -5f : -30f };
            j.highTwistLimit = new SoftJointLimit { limit = hinge ? 110f : 30f };
            j.swing1Limit = new SoftJointLimit { limit = hinge ? 5f : 50f };
            j.swing2Limit = new SoftJointLimit { limit = hinge ? 5f : 40f };
            j.enableProjection = true;
        }
        Transform nearest = hips; float best = float.MaxValue;
        foreach (var p in pivots) { float d = (p.position - point).sqrMagnitude; if (d < best) { best = d; nearest = p; } }
        foreach (var kv in bodies)
            kv.Value.linearVelocity = velocity * (kv.Key == nearest ? 1.25f : Random.Range(0.75f, 1.0f)) + Random.insideUnitSphere * 1.5f;
    }

    IEnumerator GetUp()
    {
        thrownAlive = false;
        Vector3 at = hips.position;
        foreach (var j in GetComponentsInChildren<CharacterJoint>()) Destroy(j);
        foreach (var p in pivots)
        {
            var rp = p.GetComponent<RagdollPart>(); if (rp) Destroy(rp);
            foreach (Transform m in p) foreach (var col in m.GetComponents<Collider>()) Destroy(col);
        }
        yield return null;
        foreach (var p in pivots) { var rb = p.GetComponent<Rigidbody>(); if (rb) Destroy(rb); }
        yield return null;
        if (!NavMesh.SamplePosition(at, out var hit, 4f, NavMesh.AllAreas)) { Kill(at, Vector3.zero, HitKind.Impact); yield break; }
        foreach (var t in GetComponentsInChildren<Transform>()) t.gameObject.layer = 0;
        transform.SetPositionAndRotation(hit.position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        ResetPose();
        Ragdolled = false;
        capsule.enabled = true;
        agent.enabled = true;
        agent.Warp(hit.position);
        var hero = FindAnyObjectByType<HeroController>();
        Panic(hero ? hero.transform.position : hit.position);
    }

    void ResetPose()
    {
        foreach (var p in pivots) { p.localPosition = rest[p].pos; p.localRotation = rest[p].rot; }
    }

    IEnumerator Decay()
    {
        yield return new WaitForSeconds(45f);
        // sink into the ground and disappear
        foreach (var rb in GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
        foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false;
        for (float t = 0f; t < 3f; t += Time.deltaTime)
        {
            foreach (var p in pivots) if (p) p.position += Vector3.down * Time.deltaTime * 0.4f;
            yield return null;
        }
        Destroy(gameObject);
    }
}

/// <summary>Forwards ragdoll collisions to the person they belong to.</summary>
public class RagdollPart : MonoBehaviour
{
    public Civilian owner;
    void OnCollisionEnter(Collision c) { if (owner) owner.OnPartImpact(c); }
}
