using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Fills the city with people on the sidewalks, cars on the roads and planes in the sky.
/// Every person or car that dies is replaced by a new one somewhere out of sight.
/// Witnesses panic and run.
/// </summary>
public class Population : MonoBehaviour
{
    public static Population Instance { get; private set; }
    public const int BodyLayer = 3;          // ragdolls: hit by the laser and explosions, don't block the hero or camera

    public int civilians = 320;
    public int cars = 120;
    public int planes = 3;
    public float respawnDelay = 1.5f;
    public float witnessRadius = 45f;

    readonly List<Vector3> walkPoints = new List<Vector3>();
    readonly List<Vector3> roadPoints = new List<Vector3>();
    Transform hero;
    Rect area;
    float seaLevel = float.NegativeInfinity;
    readonly HashSet<Object> sunk = new HashSet<Object>();

    void Awake()
    {
        Instance = this;
        Physics.IgnoreLayerCollision(BodyLayer, FxUtil.HeroLayer, true);
        Physics.IgnoreLayerCollision(BodyLayer, 4, true);      // bodies sink through the sea surface
        Physics.IgnoreLayerCollision(0, 4, true);              // so do cars, planes, props
    }

    IEnumerator Start()
    {
        var hc = FindAnyObjectByType<HeroController>();
        hero = hc ? hc.transform : null;
        area = hc ? hc.MapRect : new Rect(-200, -200, 400, 400);
        var sea = GameObject.Find("SeaSurface");
        if (sea) seaLevel = sea.GetComponent<Collider>().bounds.max.y;
        SamplePoints();
        Debug.Log($"[Population] walk points {walkPoints.Count}, road points {roadPoints.Count}");
        // spread the spawning over a few frames
        for (int i = 0; i < civilians; i++) { if (walkPoints.Count > 0) Civilian.Spawn(Pick(walkPoints)); if (i % 10 == 9) yield return null; }
        for (int i = 0; i < cars; i++) { SpawnCar(false); if (i % 8 == 7) yield return null; }
        for (int i = 0; i < planes; i++) SpawnPlane();
    }

    // ================================================================== points
    void SamplePoints()
    {
        int roadMask = 1 << Civilian.RoadArea;
        for (int i = 0; i < 15000 && (walkPoints.Count < 1500 || roadPoints.Count < 1000); i++)
        {
            Vector3 p = new Vector3(Random.Range(area.xMin, area.xMax), 600f, Random.Range(area.yMin, area.yMax));
            if (!Physics.Raycast(p, Vector3.down, out var h, 1000f, ~((1 << FxUtil.HeroLayer) | (1 << 4)), QueryTriggerInteraction.Ignore)) continue;
            if (h.collider.GetComponentInParent<Destructible>()) continue;     // rooftop
            if (h.collider.name == "Islands") continue;                         // empty beaches
            if (!NavMesh.SamplePosition(h.point, out var nh, 1.5f, NavMesh.AllAreas)) continue;
            if (Mathf.Abs(nh.position.y - h.point.y) > 1.2f) continue;
            if ((nh.mask & roadMask) != 0) roadPoints.Add(nh.position); else walkPoints.Add(nh.position);
        }
    }

    static Vector3 Pick(List<Vector3> list) => list[Random.Range(0, list.Count)];

    /// A random walk / road point, preferably within radius of near.
    public Vector3 RandomPoint(bool road, Vector3 near, float radius)
    {
        var list = road ? roadPoints : walkPoints;
        if (list.Count == 0) return near;
        float r2 = radius * radius;
        for (int i = 0; i < 20; i++)
        {
            var p = Pick(list);
            if ((p - near).sqrMagnitude < r2) return p;
        }
        return Pick(list);
    }

    /// Somewhere the player is not looking, not too close.
    Vector3 HiddenPoint(List<Vector3> list)
    {
        var cam = Camera.main;
        Vector3 best = Pick(list);
        for (int i = 0; i < 40; i++)
        {
            var p = Pick(list);
            if (hero && (p - hero.position).sqrMagnitude < 60f * 60f) continue;
            if (cam)
            {
                var vp = cam.WorldToViewportPoint(p);
                bool visible = vp.z > 0f && vp.x > -0.1f && vp.x < 1.1f && vp.y > -0.1f && vp.y < 1.1f && vp.z < 180f;
                if (visible) continue;
            }
            return p;
        }
        return best;
    }

    // ================================================================== spawning
    void SpawnCivilian(bool hidden)
    {
        if (walkPoints.Count == 0) return;
        Civilian.Spawn(hidden ? HiddenPoint(walkPoints) : Pick(walkPoints));
    }

    void SpawnCar(bool hidden)
    {
        if (roadPoints.Count == 0) return;
        // a free spot: no other car within 9 m
        Vector3 p = hidden ? HiddenPoint(roadPoints) : Pick(roadPoints);
        for (int tries = 0; tries < 15; tries++)
        {
            bool free = true;
            foreach (var other in Car.All) if ((other.transform.position - p).sqrMagnitude < 81f) { free = false; break; }
            if (free) break;
            p = hidden ? HiddenPoint(roadPoints) : Pick(roadPoints);
        }
        Vector3 next = RandomPoint(true, p, 60f) - p; next.y = 0f;
        Car.Spawn(p, next.sqrMagnitude > 0.1f ? Quaternion.LookRotation(next) : Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
    }

    void SpawnPlane()
    {
        // circle inside the map edge so the hero can fly up and catch them
        Vector3 c = new Vector3(area.center.x, 0f, area.center.y);
        float maxR = Mathf.Min(area.width, area.height) * 0.5f - 30f;
        Airplane.Spawn(c, Random.Range(maxR * 0.55f, maxR), Random.Range(180f, 320f));
    }

    IEnumerator Later(float delay, System.Action a) { yield return new WaitForSeconds(delay); a(); }

    // ================================================================== deaths
    public void OnCivilianKilled(Civilian c, Vector3 where)
    {
        Witness(where, witnessRadius);
        StartCoroutine(Later(respawnDelay, () => SpawnCivilian(true)));
    }

    public void OnCarDestroyed(Car car)
    {
        Witness(car.transform.position, witnessRadius * 0.8f);
        StartCoroutine(Later(respawnDelay, () => SpawnCar(true)));
    }

    public void OnPlaneDestroyed(Airplane p)
    {
        Witness(p.transform.position, witnessRadius * 2f);
        StartCoroutine(Later(15f, SpawnPlane));
    }

    void Witness(Vector3 where, float radius)
    {
        float r2 = radius * radius;
        foreach (var c in Civilian.All.ToArray())
            if (!c.Dead && (c.transform.position - where).sqrMagnitude < r2) c.Panic(where);
    }

    // ================================================================== the sea
    void Update()
    {
        if (float.IsNegativeInfinity(seaLevel)) return;
        foreach (var c in Civilian.All.ToArray())
        {
            if (!c.Ragdolled || sunk.Contains(c)) continue;
            if (c.Hips.position.y < seaLevel - 0.3f) Sink(c.Hips.position, c, () => { if (!c.Dead) c.Kill(c.Hips.position, Vector3.down, HitKind.Impact); });
        }
        foreach (var car in Car.All.ToArray())
            if (!sunk.Contains(car) && car.transform.position.y < seaLevel - 0.5f && !car.Held)
                Sink(car.transform.position, car, () => { if (!car.Wrecked) OnCarDestroyed(car); });
    }

    void Sink(Vector3 at, Component who, System.Action onSink)
    {
        sunk.Add(who);
        Vector3 surface = new Vector3(at.x, seaLevel, at.z);
        Sfx.Play(Sfx.Splash, surface, 1f, Random.Range(0.8f, 1.1f));
        FxUtil.Burst("Splash", FxUtil.Steam, surface, Quaternion.LookRotation(Vector3.up), 50,
            new Vector2(0.8f, 1.6f), new Vector2(4f, 10f), new Vector2(0.5f, 1.4f), new Color(0.92f, 0.96f, 1f, 0.8f), 1.5f, 30f, 0.6f, false, 0f, 2f);
        onSink();
        StartCoroutine(Later(4f, () => { if (who) Destroy(who.gameObject); sunk.Remove(who); }));
    }
}
