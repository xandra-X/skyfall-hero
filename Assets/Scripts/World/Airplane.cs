using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>An airliner circling the city. Burn it, punch it or throw it and it comes down in flames.</summary>
public class Airplane : Grabbable, IHittable
{
    public static readonly List<Airplane> All = new List<Airplane>();

    public bool Destroyed { get; private set; }
    public override bool CanGrab => !Destroyed && !falling;
    public override Vector3 HoldOffset => new Vector3(0f, 3.5f, 0f);
    protected override Quaternion HoldRotation => Quaternion.Euler(0f, 90f, 0f);
    public override float ThrowSpeed => 24f;

    Vector3 center;
    float radius, altitude, speed, angle, dir;
    bool falling;
    float heat, fallAt;
    Rigidbody body;
    BoxCollider box;
    AudioSource jet;
    MeshRenderer strobe;
    bool exploded;

    public static Airplane Spawn(Vector3 center, float radius, float altitude)
    {
        var go = new GameObject("Airplane");
        var a = go.AddComponent<Airplane>();
        a.center = center; a.radius = radius; a.altitude = altitude;
        a.speed = Random.Range(40f, 50f);          // the hero's boost (120 m/s) is well over twice as fast
        a.dir = Random.value < 0.5f ? 1f : -1f;
        a.angle = Random.Range(0f, Mathf.PI * 2f);
        a.Build();
        a.Fly(0f);
        return a;
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Build()
    {
        var white = Palette.Lit(new Color(0.92f, 0.93f, 0.95f), 0.6f);
        Color liveryC = Random.value < 0.5f ? new Color(0.1f, 0.25f, 0.6f) : new Color(0.7f, 0.1f, 0.12f);
        var livery = Palette.Lit(liveryC, 0.6f);
        var grey = Palette.Lit(new Color(0.55f, 0.57f, 0.6f), 0.5f);
        var glass = Palette.Lit(new Color(0.05f, 0.07f, 0.1f), 0.9f);
        var m = Palette.Pivot(transform, Vector3.zero, "Model");
        // fuselage in two halves that meet in the middle (heat vision can cut the plane apart there)
        Palette.Part(PrimitiveType.Capsule, m, new Vector3(0f, 0f, 8.6f), new Vector3(3.8f, 10.4f, 3.8f), white, "FuselageFront", Quaternion.Euler(90f, 0f, 0f));
        Palette.Part(PrimitiveType.Capsule, m, new Vector3(0f, 0f, -8.6f), new Vector3(3.8f, 10.4f, 3.8f), white, "FuselageRear", Quaternion.Euler(90f, 0f, 0f));
        Palette.Part(PrimitiveType.Capsule, m, new Vector3(0f, -0.6f, 8.2f), new Vector3(3.5f, 10f, 2.9f), livery, "BellyFront", Quaternion.Euler(90f, 0f, 0f));
        Palette.Part(PrimitiveType.Capsule, m, new Vector3(0f, -0.6f, -8.2f), new Vector3(3.5f, 10f, 2.9f), livery, "BellyRear", Quaternion.Euler(90f, 0f, 0f));
        for (int i = 0; i < 14; i++)               // window rows
            foreach (float sd in new[] { -1f, 1f })
                Palette.Part(PrimitiveType.Cube, m, new Vector3(sd * 1.88f, 0.6f, -12f + i * 1.9f), new Vector3(0.05f, 0.35f, 0.9f), glass, "Windows");
        Palette.Part(PrimitiveType.Cube, m, new Vector3(0f, 0.9f, 16.2f), new Vector3(2.2f, 0.7f, 1.4f), glass, "Cockpit");
        Palette.Part(PrimitiveType.Cube, m, new Vector3(0f, -0.4f, 1f), new Vector3(34f, 0.5f, 5.5f), white, "Wings");
        Palette.Part(PrimitiveType.Cube, m, new Vector3(0f, 4f, -16.5f), new Vector3(0.5f, 6.5f, 4.5f), livery, "Fin");
        Palette.Part(PrimitiveType.Cube, m, new Vector3(0f, 1f, -17f), new Vector3(12f, 0.4f, 3f), white, "Stabilizer");
        foreach (float s in new[] { -1f, 1f })
        {
            Palette.Part(PrimitiveType.Cylinder, m, new Vector3(s * 6.5f, -1.8f, 3f), new Vector3(2f, 2.2f, 2f), grey, "Engine", Quaternion.Euler(90f, 0f, 0f));
            Palette.Part(PrimitiveType.Sphere, m, new Vector3(s * 17f, -0.4f, 1f), Vector3.one * 0.6f, Palette.Glow(s < 0 ? new Color(1f, 0.1f, 0.1f) : new Color(0.1f, 1f, 0.2f)), "NavLight");
        }
        strobe = Palette.Part(PrimitiveType.Sphere, m, new Vector3(0f, 7.4f, -17f), Vector3.one * 0.7f, Palette.Glow(Color.white), "Strobe").GetComponent<MeshRenderer>();

        body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.mass = 40000f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        box = gameObject.AddComponent<BoxCollider>();
        box.size = new Vector3(34f, 5f, 38f);
        box.center = new Vector3(0f, 0.5f, 0f);

        jet = gameObject.AddComponent<AudioSource>();
        jet.clip = Sfx.Jet; jet.loop = true; jet.spatialBlend = 1f;
        jet.minDistance = 40f; jet.maxDistance = 1500f; jet.volume = 0.8f; jet.dopplerLevel = 0.8f;
        jet.rolloffMode = AudioRolloffMode.Logarithmic;
        jet.Play();
    }

    void Update()
    {
        if (strobe) strobe.enabled = Mathf.Repeat(Time.time, 1.2f) < 0.08f;
        if (Destroyed || Held || falling) return;
        Fly(Time.deltaTime);
    }

    void Fly(float dt)
    {
        angle += dir * speed / radius * dt;
        Vector3 pos = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius + Vector3.up * altitude;
        Vector3 tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * dir;
        transform.SetPositionAndRotation(pos, Quaternion.LookRotation(tangent) * Quaternion.Euler(0f, 0f, dir * 14f));
    }

    void StartFalling(Vector3 push)
    {
        if (falling) return;
        falling = true;
        fallAt = Time.time;
        Vector3 v = transform.forward * speed * 0.7f + push;
        transform.SetParent(null, true);
        box.enabled = true;
        body.isKinematic = false;
        body.useGravity = true;
        body.linearVelocity = v;
        body.angularVelocity = new Vector3(Random.Range(-0.2f, 0.2f), 0f, Random.Range(-0.6f, 0.6f));
        Explosion.Fire(transform, new Vector3(6.5f, -1.8f, 3f), 2.5f, 60f);
    }

    void OnCollisionEnter(Collision c)
    {
        if (!falling || exploded || Time.time - fallAt < 0.1f) return;
        if (c.collider.GetComponentInParent<HeroController>()) return;
        var contact = c.GetContact(0);
        var d = c.collider.GetComponentInParent<Destructible>();
        if (d) HeroCombat.SmashWall(d, contact.point, contact.normal, -contact.normal, 4f, 200f, false);
        Crash(contact.point);
    }

    void Crash(Vector3 at)
    {
        exploded = true;
        Explosion.At(at, 20f, 30f, this);
        Explosion.Fire(transform, Vector3.zero, 5f, 40f);
        Finish();
    }

    void Finish()
    {
        if (Destroyed) return;
        Destroyed = true;
        jet.Stop();
        foreach (var r in GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = Palette.Charred;
        if (Population.Instance) Population.Instance.OnPlaneDestroyed(this);
        StartCoroutine(Decay());
    }

    public void Hit(Vector3 point, Vector3 velocity, float damage, HitKind kind)
    {
        if (Destroyed) return;
        if (kind == HitKind.Laser)
        {
            heat += Time.deltaTime;
            if (heat < 1.0f || falling) return;
            BreakInHalf(point);
            return;
        }
        if (!falling)
        {
            if (Held) { transform.SetParent(null, true); ForceRelease(); }
            Explosion.At(point, 10f, 15f, this);
            StartFalling(velocity * 0.5f);
        }
    }

    /// Heat vision cuts it apart: front and tail fall separately, burning, and explode where they hit.
    void BreakInHalf(Vector3 cut)
    {
        if (Held) { transform.SetParent(null, true); ForceRelease(); }
        Vector3 v = transform.forward * speed * 0.75f;
        var model = transform.Find("Model");
        var front = new GameObject("PlaneFront"); var tail = new GameObject("PlaneTail");
        foreach (var half in new[] { front, tail }) half.transform.SetPositionAndRotation(transform.position, transform.rotation);
        foreach (Transform part in model.Cast<Transform>().ToArray())
            part.SetParent(part.localPosition.z >= 0f ? front.transform : tail.transform, true);
        Explosion.At(cut, 12f, 15f, this);
        MakeHalf(front, v + transform.up * 2f, new Vector3(0.5f, 0f, Random.Range(-0.4f, 0.4f)));
        MakeHalf(tail, v * 0.85f - transform.forward * 4f, new Vector3(-0.7f, Random.Range(-0.3f, 0.3f), Random.Range(-0.8f, 0.8f)));
        Destroyed = true;
        jet.Stop();
        if (Population.Instance) Population.Instance.OnPlaneDestroyed(this);
        Destroy(gameObject);
    }

    static void MakeHalf(GameObject half, Vector3 velocity, Vector3 spin)
    {
        var rs = half.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        var col = half.AddComponent<BoxCollider>();
        col.center = half.transform.InverseTransformPoint(b.center);
        col.size = half.transform.InverseTransformVector(b.size);
        col.size = new Vector3(Mathf.Abs(col.size.x), Mathf.Abs(col.size.y), Mathf.Abs(col.size.z));
        var rb = half.AddComponent<Rigidbody>();
        rb.mass = 20000f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.linearVelocity = velocity;
        rb.angularVelocity = spin;
        // fire and smoke trailing from the torn end
        Vector3 cutLocal = new Vector3(0f, 0f, half.name == "PlaneFront" ? 0.5f : -0.5f);
        Explosion.Fire(half.transform, cutLocal, 3f, 60f);
        half.AddComponent<PlaneHalf>();
    }

    protected override void OnGrabbed() { box.enabled = false; }

    protected override void OnThrown(Vector3 velocity)
    {
        speed = 0f;
        StartFalling(velocity);
        body.linearVelocity = velocity;
    }

    protected override void OnDropped(Vector3 groundPoint)
    {
        // too big to set down gently: it falls
        speed = 0f;
        StartFalling(Vector3.down * 2f);
    }

    IEnumerator Decay()
    {
        yield return new WaitForSeconds(45f);
        Vector3 s0 = transform.localScale;
        for (float t = 0f; t < 2f; t += Time.deltaTime) { transform.localScale = s0 * (1f - t / 2f); yield return null; }
        Destroy(gameObject);
    }
}

/// <summary>A falling half of a plane: explodes once where it hits, then burns out and disappears.</summary>
public class PlaneHalf : MonoBehaviour
{
    bool done;
    float born;

    void Start() => born = Time.time;

    void OnCollisionEnter(Collision c)
    {
        if (done || Time.time - born < 0.3f) return;
        if (c.collider.GetComponentInParent<HeroController>() || c.collider.GetComponentInParent<PlaneHalf>()) return;
        done = true;
        var contact = c.GetContact(0);
        var d = c.collider.GetComponentInParent<Destructible>();
        if (d) HeroCombat.SmashWall(d, contact.point, contact.normal, -contact.normal, 3.5f, 150f, false);
        Explosion.At(contact.point, 16f, 25f, this);
        foreach (var r in GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = Palette.Charred;
        StartCoroutine(Decay());
    }

    static float seaLevel = float.NaN;

    void Update()
    {
        if (float.IsNaN(seaLevel))
        {
            var sea = GameObject.Find("SeaSurface");
            seaLevel = sea ? sea.GetComponent<Collider>().bounds.max.y : float.NegativeInfinity;
        }
        if (!done && transform.position.y < seaLevel)
        {
            // crashed into the sea: huge splash, then it sinks
            done = true;
            Vector3 at = new Vector3(transform.position.x, seaLevel, transform.position.z);
            Sfx.Play(Sfx.Splash, at, 1f, 0.6f);
            Sfx.Play(Sfx.Explosion, at, 0.7f, 0.7f);
            FxUtil.Burst("PlaneSplash", FxUtil.Steam, at, Quaternion.LookRotation(Vector3.up), 120,
                new Vector2(1.5f, 3f), new Vector2(10f, 25f), new Vector2(1.5f, 4f), new Color(0.92f, 0.96f, 1f, 0.85f), 1.2f, 35f, 3f, false, 0f, 2.5f);
            foreach (var ps in GetComponentsInChildren<ParticleSystem>()) { var e = ps.emission; e.rateOverTime = 0f; }
        }
        if (transform.position.y < seaLevel - 30f) Destroy(gameObject);
    }

    System.Collections.IEnumerator Decay()
    {
        yield return new WaitForSeconds(40f);
        Vector3 s0 = transform.localScale;
        for (float t = 0f; t < 2f; t += Time.deltaTime) { transform.localScale = s0 * (1f - t / 2f); yield return null; }
        Destroy(gameObject);
    }
}
