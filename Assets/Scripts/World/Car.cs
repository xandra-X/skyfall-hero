using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Traffic: drives along the roads. Punch it and it tumbles; throw it and it smashes into things;
/// burn it, or hit it hard enough, and it explodes into a burning wreck.
/// </summary>
public class Car : Grabbable, IHittable
{
    public static readonly List<Car> All = new List<Car>();

    public bool Wrecked { get; private set; }
    public override bool CanGrab => !Wrecked;
    public override Vector3 HoldOffset => new Vector3(0f, 0.25f, 0f);
    protected override Quaternion HoldRotation => Quaternion.Euler(0f, 90f, 0f);    // held across the shoulders
    public override float ThrowSpeed => 34f;
    public Vector3 Center => transform.TransformPoint(box ? box.center : Vector3.up * 0.75f);

    NavMeshAgent agent;
    Rigidbody body;
    BoxCollider box;
    AudioSource engine;
    readonly List<Transform> wheels = new List<Transform>();
    MeshRenderer[] renderers;
    bool shown = true;
    public const float DrawDistance = 260f;
    bool loose, thrown;
    float looseAt, heat;

    static readonly Color[] Paints = { new Color(0.75f, 0.05f, 0.05f), new Color(0.05f, 0.15f, 0.45f), new Color(0.92f, 0.92f, 0.9f), new Color(0.1f, 0.1f, 0.1f), new Color(0.5f, 0.52f, 0.55f), new Color(0.15f, 0.35f, 0.2f), new Color(0.6f, 0.45f, 0.25f), new Color(0.35f, 0.6f, 0.85f) };

    public static Car Spawn(Vector3 pos, Quaternion rot)
    {
        var go = new GameObject("Car");
        go.transform.SetPositionAndRotation(pos, rot);
        var c = go.AddComponent<Car>();
        c.Build();
        return c;
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Build()
    {
        float r = Random.value;
        var type = r < 0.32f ? CarModel.Type.Sedan : r < 0.48f ? CarModel.Type.Hatchback : r < 0.66f ? CarModel.Type.Suv
                 : r < 0.76f ? CarModel.Type.Van : r < 0.86f ? CarModel.Type.Pickup : CarModel.Type.Taxi;
        Color paintC = type == CarModel.Type.Taxi ? new Color(0.95f, 0.72f, 0.05f) : Paints[Random.Range(0, Paints.Length)];
        var model = Palette.Pivot(transform, Vector3.zero, "Model");
        var built = CarModel.Build(model, type, paintC);
        wheels.AddRange(built.wheels);
        renderers = GetComponentsInChildren<MeshRenderer>();

        body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.mass = built.mass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        box = gameObject.AddComponent<BoxCollider>();
        box.size = built.bounds.size;
        box.center = built.bounds.center;

        agent = gameObject.AddComponent<NavMeshAgent>();
        agent.radius = Mathf.Max(0.9f, box.size.x * 0.55f); agent.height = box.size.y;
        agent.speed = Random.Range(9f, 13f); agent.acceleration = 5f; agent.angularSpeed = 100f;
        agent.stoppingDistance = 2f; agent.autoBraking = true;
        agent.areaMask = 1 << Civilian.RoadArea;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
        agent.avoidancePriority = Random.Range(10, 30);

        engine = gameObject.AddComponent<AudioSource>();
        engine.clip = Sfx.Engine; engine.loop = true; engine.spatialBlend = 1f;
        engine.minDistance = 4f; engine.maxDistance = 70f; engine.volume = 0.35f; engine.dopplerLevel = 0.5f;
        engine.time = Random.value * 0.9f;
        engine.Play();
    }

    void Update()
    {
        // far away: don't draw (keeps hundreds of cars cheap)
        bool near = Civilian.Near(transform.position, DrawDistance);
        if (near != shown) { shown = near; foreach (var mr in renderers) if (mr) mr.enabled = near; }
        if (Wrecked) return;
        float speed = loose ? body.linearVelocity.magnitude : agent.enabled ? agent.velocity.magnitude : 0f;
        engine.pitch = 0.7f + speed * 0.05f;
        if (!loose && agent.enabled && agent.isOnNavMesh)
        {
            if (shown) foreach (var w in wheels) w.Rotate(Vector3.right, speed / 0.36f * Mathf.Rad2Deg * Time.deltaTime, Space.Self);
            if (!agent.pathPending && agent.remainingDistance < agent.stoppingDistance + 1f)
            {
                var pop = Population.Instance;
                if (pop) agent.SetDestination(pop.RandomPoint(true, transform.position, 220f));
            }
        }
    }

    // ================================================================== physics
    void MakeLoose()
    {
        if (loose) return;
        loose = true;
        looseAt = Time.time;
        agent.enabled = false;
        body.isKinematic = false;
        box.enabled = true;
    }

    void OnCollisionEnter(Collision c)
    {
        if (!loose || Wrecked || Time.time - looseAt < 0.05f) return;
        if (c.collider.GetComponentInParent<HeroController>()) return;      // brushing past the hero who threw it
        float impact = c.relativeVelocity.magnitude;
        var contact = c.GetContact(0);
        if (impact > 8f) Sfx.Play(Sfx.Crunch, contact.point, Mathf.Clamp01(impact / 25f), Random.Range(0.7f, 1f));
        var d = c.collider.GetComponentInParent<Destructible>();
        if (d && impact > 10f) HeroCombat.SmashWall(d, contact.point, contact.normal, -contact.normal, Mathf.Lerp(1.3f, 2.2f, impact / 40f), 40f, false);
        var other = c.collider.GetComponentInParent<IHittable>();
        if (other != null && (Object)other != this && impact > 6f) other.Hit(contact.point, c.relativeVelocity * -0.6f, 200f, HitKind.Impact);
        if (impact > 15f || (thrown && impact > 7f)) Explode();      // a thrown car goes up on its first real hit
    }

    // ================================================================== damage
    public void Hit(Vector3 point, Vector3 velocity, float damage, HitKind kind)
    {
        if (Wrecked) { if (loose) body.AddForceAtPosition(velocity * body.mass * 0.3f, point, ForceMode.Impulse); return; }
        switch (kind)
        {
            case HitKind.Laser:
                heat += Time.deltaTime;
                if (heat > 0.9f) Explode();
                break;
            case HitKind.Explosion:
                // set off by another blast: goes up a moment later, so a row of cars pops one after another
                // instead of all in the same frame (that chain used to run the phone out of memory)
                if (damage > 300f)
                {
                    MakeLoose();
                    body.AddForceAtPosition(velocity * body.mass * 0.5f, point, ForceMode.Impulse);
                    if (!explodePending) { explodePending = true; StartCoroutine(ExplodeSoon(Random.Range(0.15f, 0.45f))); }
                }
                else { MakeLoose(); body.AddForceAtPosition(velocity * body.mass * 0.6f, point, ForceMode.Impulse); }
                break;
            default:
                // punched / kicked / rammed: shoved a few metres with a little tumble, no explosion
                MakeLoose();
                Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
                if (flat.sqrMagnitude < 0.01f) flat = transform.forward;
                float shove = kind == HitKind.Punch ? Mathf.Lerp(5f, 9f, Mathf.InverseLerp(50f, 100f, damage)) : 11f;
                body.AddForceAtPosition((flat.normalized * shove + Vector3.up * 2.5f) * body.mass, point, ForceMode.Impulse);
                Sfx.Play(Sfx.Crunch, point, 1f, Random.Range(0.8f, 1f));
                break;
        }
    }

    bool explodePending;
    IEnumerator ExplodeSoon(float delay)
    {
        yield return new WaitForSeconds(delay);
        Explode();
    }

    public void Explode()
    {
        if (Wrecked) return;
        Wrecked = true;
        if (Held) { transform.SetParent(null, true); ForceRelease(); }
        MakeLoose();
        engine.Stop();
        foreach (var r in GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = Palette.Charred;
        body.AddForce(Vector3.up * body.mass * Random.Range(5f, 9f), ForceMode.Impulse);
        body.AddTorque(Random.insideUnitSphere * body.mass * 4f, ForceMode.Impulse);
        Explosion.At(Center, 7f, 18f, this);
        Explosion.Fire(transform, box.center + Vector3.up * 0.3f, 1.4f, 18f);
        if (Population.Instance) Population.Instance.OnCarDestroyed(this);
        StartCoroutine(Decay());
    }

    IEnumerator Decay()
    {
        yield return new WaitForSeconds(30f);
        Vector3 s0 = transform.localScale;
        for (float t = 0f; t < 1.5f; t += Time.deltaTime) { transform.localScale = s0 * (1f - t / 1.5f); yield return null; }
        Destroy(gameObject);
    }

    // ================================================================== grab / throw
    protected override void OnGrabbed()
    {
        agent.enabled = false;
        body.isKinematic = true;
        box.enabled = false;
        loose = false;
    }

    protected override void OnThrown(Vector3 velocity)
    {
        loose = false;
        thrown = true;
        MakeLoose();
        body.linearVelocity = velocity;
        body.angularVelocity = Random.insideUnitSphere * 1.5f;
    }

    protected override void OnDropped(Vector3 groundPoint)
    {
        transform.SetPositionAndRotation(groundPoint, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
        box.enabled = true;
        if (NavMesh.SamplePosition(groundPoint, out var hit, 2.5f, 1 << Civilian.RoadArea))
        {
            loose = false;
            body.isKinematic = true;
            agent.enabled = true;
            agent.Warp(hit.position);
        }
        else { loose = false; MakeLoose(); }
    }
}
