using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Automated play-mode test for people, traffic, planes, grab/throw, gore, mood and sounds.</summary>
public class LifeTestDriver : MonoBehaviour
{
    HeroController hero;
    HeroGrab grab;
    HeroCombat combat;
    Camera cam;
    int errors;
    const string Dir = "Screenshots/life";
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    void Awake()
    {
        Application.logMessageReceived += (msg, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { errors++; Debug.Log($"[LifeTest] ERROR CAUGHT: {msg}\n{stack}"); }
        };
        if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();
        if (Mouse.current == null) InputSystem.AddDevice<Mouse>();
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory(Dir);
        hero = FindAnyObjectByType<HeroController>();
        grab = hero.GetComponent<HeroGrab>();
        combat = hero.GetComponent<HeroCombat>();
        cam = Camera.main;
        var tpc = cam.GetComponent<ThirdPersonCamera>(); if (tpc) tpc.enabled = false;
        yield return Wait(3f);
        int civTarget = Population.Instance.civilians;
        Log($"spawned: civilians {Civilian.All.Count}, cars {Car.All.Count}, planes {Airplane.All.Count}");

        // ---------------------------------------------------------------- do they move?
        var civs = Civilian.All.Take(15).ToArray(); var cars = Car.All.Take(10).ToArray();
        var c0 = civs.Select(c => c.transform.position).ToArray(); var k0 = cars.Select(c => c.transform.position).ToArray();
        yield return Wait(4f);
        float civMove = civs.Select((c, i) => Vector3.Distance(c.transform.position, c0[i])).Average();
        float carMove = cars.Select((c, i) => Vector3.Distance(c.transform.position, k0[i])).Average();
        int carsOnRoad = Car.All.Count(c => UnityEngine.AI.NavMesh.SamplePosition(c.transform.position, out var h, 1.5f, 1 << Civilian.RoadArea));
        Log($"movement in 4s: civilians {civMove:0.0} m avg, cars {carMove:0.0} m avg; cars on road {carsOnRoad}/{Car.All.Count}");

        // cars must stay on the road: watch every car for 6 s
        int samples = 0, offRoad = 0;
        for (float t = 0; t < 6f; t += 0.5f)
        {
            foreach (var c in Car.All)
            {
                if (c.Wrecked || c.Held) continue;
                if (!UnityEngine.AI.NavMesh.SamplePosition(c.transform.position, out var nh, 1.2f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                samples++;
                if ((nh.mask & (1 << Civilian.RoadArea)) == 0) offRoad++;
            }
            yield return Wait(0.5f);
        }
        Log($"cars off the road: {offRoad}/{samples} samples; fps {1f / Time.smoothDeltaTime:0} with {Civilian.All.Count} people, {Car.All.Count} cars");

        // street view near a group of people
        var busy = Civilian.All.OrderBy(c => Civilian.All.Count(o => (o.transform.position - c.transform.position).sqrMagnitude < 400f)).Last();
        Vector3 bp = busy.transform.position;
        var nearCar = Car.All.OrderBy(c => (c.transform.position - bp).sqrMagnitude).First();
        PlaceCam(bp + new Vector3(-9f, 4f, -9f), Vector3.Lerp(bp, nearCar.transform.position, 0.3f) + Vector3.up);
        Shot("1_street_life");

        // ---------------------------------------------------------------- flying low over the ground and the sea
        {
            var state = typeof(HeroController).GetProperty("Current");
            var vel = typeof(HeroController).GetField("velocity", F);
            var road = Population.Instance.RandomPoint(true, Vector3.zero, 500f);
            MoveHero(road + Vector3.up * 3f, Vector3.forward);
            state.SetValue(hero, HeroController.State.Fly);
            float minGap = 99f;
            for (float t = 0; t < 2f; t += Time.unscaledDeltaTime)
            {
                vel.SetValue(hero, new Vector3(0f, -12f, 18f));             // pushing down and forward, low over the street
                if (Physics.Raycast(hero.transform.position + Vector3.up, Vector3.down, out var gh, 20f, ~((1 << 2) | (1 << 3))))
                    minGap = Mathf.Min(minGap, hero.transform.position.y - gh.point.y);
                yield return null;
            }
            Log($"low flight over land: still {hero.Current}, lowest gap {minGap:0.00} m");
            PlaceCam(hero.transform.position + new Vector3(6f, 2f, -6f), hero.transform.position);
            Shot("0a_low_flight_land");
            var seaPt = new Vector3(hero.MapRect.xMin + 40f, 0f, hero.MapRect.center.y);
            MoveHero(new Vector3(seaPt.x, hero.SeaLevel + 3f, seaPt.z), Vector3.forward);
            state.SetValue(hero, HeroController.State.Fly);
            float minY = 99f;
            for (float t = 0; t < 2f; t += Time.unscaledDeltaTime)
            {
                vel.SetValue(hero, new Vector3(0f, -12f, 25f));
                minY = Mathf.Min(minY, hero.transform.position.y - hero.SeaLevel);
                yield return null;
            }
            Log($"low flight over sea: still {hero.Current}, lowest height above water {minY:0.00} m");
            PlaceCam(hero.transform.position + new Vector3(6f, 2f, -6f), hero.transform.position);
            Shot("0b_low_flight_sea");
            var flightSrc = hero.GetComponents<AudioSource>().FirstOrDefault(x => x.clip == Sfx.Flight);
            Log($"flight sound playing {(flightSrc && flightSrc.isPlaying)} volume {(flightSrc ? flightSrc.volume : -1f):0.00}");
            MoveHero(road + Vector3.up * 0.3f, Vector3.forward);          // back on the street, on foot
            state.SetValue(hero, HeroController.State.Air);
            yield return Wait(1.5f);
            Log($"back on foot: {hero.Current}");
        }

        // ---------------------------------------------------------------- grab a person and throw them at a wall
        var victim = Civilian.All.Where(c => !c.Dead && !c.Ragdolled).OrderBy(c => c.transform.position.magnitude).First();
        Vector3 vp = victim.transform.position;
        MoveHero(vp - victim.transform.forward * 1.5f, victim.transform.forward);
        CamBehind();
        yield return Wait(0.3f);
        bool grabbed = grab.TryGrab();
        yield return Wait(0.8f);
        Log($"grab person: {grabbed}, held {(grab.Held ? grab.Held.name : "none")} state {hero.Current}");
        PlaceCam(hero.transform.position + hero.transform.right * 4f + hero.transform.forward * 4f + Vector3.up * 2f, hero.transform.position + Vector3.up * 2f);
        Shot("2_holding_person");
        var thrown = grab.Held as Civilian;
        CamBehind();
        grab.StartThrow();
        yield return Wait(0.25f);
        PlaceCam(hero.transform.position - hero.transform.forward * 4f + Vector3.up * 3f + hero.transform.right * 2f, hero.transform.position + hero.transform.forward * 15f);
        Shot("3_person_thrown");
        yield return Wait(2.5f);
        Log($"thrown person dead={thrown && thrown.Dead} ragdolled={thrown && thrown.Ragdolled} pos {(thrown ? thrown.Hips.position : Vector3.zero)}");
        if (thrown) PlaceCam(thrown.Hips.position + new Vector3(3f, 3f, -3f), thrown.Hips.position);
        Shot("4_thrown_result");

        // ---------------------------------------------------------------- punch a person
        var p2 = Civilian.All.Where(c => !c.Dead && !c.Ragdolled && (c.transform.position - hero.transform.position).magnitude > 5f)
                             .OrderBy(c => (c.transform.position - hero.transform.position).sqrMagnitude).First();
        MoveHero(p2.transform.position - p2.transform.forward * 1.4f, p2.transform.forward);
        var a1 = p2.GetComponent<UnityEngine.AI.NavMeshAgent>(); if (a1.enabled && a1.isOnNavMesh) a1.isStopped = true;
        CamBehind();
        yield return Wait(0.2f);
        hero.TriggerPunch();
        for (int k = 0; k < 6; k++)
        {
            yield return Wait(0.25f);
            var hrb = p2.Hips.GetComponent<Rigidbody>();
            Log($"  ragdoll t={0.25f*(k+1):0.00} dead {p2.Dead} hips {p2.Hips.position} rb {(hrb ? $"kin={hrb.isKinematic} vel={hrb.linearVelocity} sleep={hrb.IsSleeping()}" : "none")} parentRb {p2.GetComponent<Rigidbody>().isKinematic} timeScale {Time.timeScale}");
        }
        PlaceCam(hero.transform.position + hero.transform.right * 5f + Vector3.up * 2.5f, hero.transform.position + hero.transform.forward * 4f);
        Shot("5_punch_person");
        yield return Wait(1.5f);
        Log($"punched person dead={p2.Dead}");
        PlaceCam(p2.Hips.position + new Vector3(2.5f, 2.5f, -2.5f), p2.Hips.position);
        Shot("6_body_blood");

        // ---------------------------------------------------------------- bump into a car: nothing should happen
        var vF = typeof(HeroController).GetField("velocity", F);
        var stF = typeof(HeroController).GetProperty("Current");
        var bumped = Car.All.Where(c => !c.Wrecked).OrderBy(c => (c.transform.position - hero.transform.position).sqrMagnitude).First();
        var bAgent = bumped.GetComponent<UnityEngine.AI.NavMeshAgent>(); if (bAgent.enabled && bAgent.isOnNavMesh) bAgent.isStopped = true;
        Vector3 toCar = bumped.transform.position - hero.transform.position; toCar.y = 0;
        MoveHero(bumped.Center - bumped.transform.right * 4f - Vector3.up * (bumped.Center.y - bumped.transform.position.y), bumped.transform.right);
        Vector3 bumpStart = bumped.transform.position;
        var heroCc = hero.GetComponent<CharacterController>();
        for (float t = 0; t < 1.2f; t += Time.unscaledDeltaTime) { heroCc.Move(bumped.transform.right * 9f * Time.deltaTime + Vector3.down * 0.1f); yield return null; }
        Log($"bump: car moved {Vector3.Distance(bumped.transform.position, bumpStart):0.00} m, still driving (kinematic) {bumped.GetComponent<Rigidbody>().isKinematic}, wrecked {bumped.Wrecked}");

        // ---------------------------------------------------------------- punch a car: a small shove only
        Vector3 pStart = bumped.transform.position;
        MoveHero(bumped.Center - bumped.transform.right * 2.2f - Vector3.up * (bumped.Center.y - bumped.transform.position.y), bumped.transform.right);
        CamBehind();
        yield return Wait(0.2f);
        hero.TriggerPunch();
        yield return Wait(2.5f);
        Log($"punched car: moved {Vector3.Distance(bumped.transform.position, pStart):0.0} m, wrecked {bumped.Wrecked}");
        PlaceCam(bumped.transform.position + new Vector3(6f, 3f, -6f), bumped.transform.position);
        Shot("6c_punched_car");

        // ---------------------------------------------------------------- lift a car and throw it
        var car = Car.All.Where(c => !c.Wrecked && c != bumped).OrderBy(c => (c.transform.position - hero.transform.position).sqrMagnitude).First();
        MoveHero(car.transform.position - car.transform.forward * 3.2f, car.transform.forward);
        CamBehind();
        yield return Wait(0.2f);
        bool gotCar = grab.TryGrab();
        yield return Wait(0.9f);
        PlaceCam(hero.transform.position + hero.transform.right * 6f + hero.transform.forward * 5f + Vector3.up * 3f, hero.transform.position + Vector3.up * 2.5f);
        Shot("7_lifting_car");
        Log($"grab car: {gotCar}, held {(grab.Held ? grab.Held.name : "none")}");
        var thrownCar = grab.Held as Car;

        // carry it while flying: it must be in front of him, not behind his back
        stF.SetValue(hero, HeroController.State.Fly);
        MoveHero(hero.transform.position + Vector3.up * 25f, hero.transform.forward);
        for (float t = 0; t < 1.5f; t += Time.unscaledDeltaTime) { vF.SetValue(hero, hero.transform.forward * 40f); yield return null; }
        Vector3 rel = thrownCar.transform.position - (hero.transform.position + Vector3.up * 0.9f);
        Log($"carrying car in flight: ahead {Vector3.Dot(rel, hero.transform.forward):0.0} m, above {rel.y:0.0} m");
        PlaceCam(hero.transform.position + hero.transform.right * 12f + Vector3.up * 3f, hero.transform.position + hero.transform.forward * 2f);
        Shot("7b_carry_flying");
        stF.SetValue(hero, HeroController.State.Ground);
        MoveHero(car.transform.position - hero.transform.forward * 0.1f + Vector3.down * 25f, hero.transform.forward);
        var road2 = Population.Instance.RandomPoint(true, hero.transform.position, 100f);
        MoveHero(road2 + Vector3.up * 0.3f, hero.transform.forward);
        yield return Wait(0.8f);
        CamBehind();
        grab.StartThrow();
        yield return Wait(1.2f);
        if (thrownCar) PlaceCam(thrownCar.transform.position + new Vector3(10f, 6f, -10f), thrownCar.transform.position);
        Shot("8_car_crash");
        yield return Wait(1.5f);
        Log($"thrown car wrecked={thrownCar && thrownCar.Wrecked}");

        // ---------------------------------------------------------------- heat vision on a car
        var car2 = Car.All.Where(c => !c.Wrecked && !c.Held).OrderBy(c => (c.transform.position - hero.transform.position).sqrMagnitude).First();
        Vector3 cc = car2.Center;
        var a2 = car2.GetComponent<UnityEngine.AI.NavMeshAgent>(); if (a2.enabled && a2.isOnNavMesh) a2.isStopped = true;
        MoveHero(cc - (cc - hero.transform.position).normalized * 12f, (cc - hero.transform.position));
        combat.AimOverride = cc; hero.ForceBlast = true;
        PlaceCam(hero.transform.position - hero.transform.forward * 4f + Vector3.up * 2.5f + hero.transform.right * 2f, cc);
        yield return Wait(1.5f);
        Shot("9_laser_car");
        hero.ForceBlast = false; combat.AimOverride = null;
        yield return Wait(0.5f);
        Log($"lasered car wrecked={car2.Wrecked}");

        // ---------------------------------------------------------------- heat vision on a plane: cut in half
        {
            var victimPlane = Airplane.All.First();
            stF.SetValue(hero, HeroController.State.Fly);
            MoveHero(victimPlane.transform.position - victimPlane.transform.forward * 60f - Vector3.up * 15f, victimPlane.transform.forward);
            hero.ForceBlast = true;
            float tl = 0;
            while (victimPlane && !victimPlane.Destroyed && tl < 4f)
            {
                combat.AimOverride = victimPlane.transform.position;
                PlaceCam(hero.transform.position - hero.transform.forward * 6f + Vector3.up * 3f, victimPlane ? victimPlane.transform.position : hero.transform.position);
                tl += Time.unscaledDeltaTime;
                yield return null;
            }
            hero.ForceBlast = false; combat.AimOverride = null;
            yield return Wait(0.6f);
            var front = GameObject.Find("PlaneFront"); var tail = GameObject.Find("PlaneTail");
            Log($"laser plane: cut in half {(front && tail)}; front {(front ? front.transform.position : Vector3.zero)} tail {(tail ? tail.transform.position : Vector3.zero)}");
            if (front) PlaceCam(front.transform.position + new Vector3(40f, 15f, -40f), front.transform.position);
            Shot("9b_plane_cut");
        }

        // ---------------------------------------------------------------- grab a plane in flight
        var plane = Airplane.All.First(x => !x.Destroyed);
        typeof(HeroController).GetProperty("Current").SetValue(hero, HeroController.State.Fly);
        MoveHero(plane.transform.position + plane.transform.right * 12f - Vector3.up * 4f, -plane.transform.right);
        yield return null;
        bool gotPlane = grab.TryGrab();
        yield return Wait(0.8f);
        PlaceCam(hero.transform.position + new Vector3(30f, 8f, 30f), hero.transform.position + Vector3.up * 4f);
        Shot("10_holding_plane");
        Log($"grab plane: {gotPlane}");
        grab.StartThrow();
        float tThrow = Time.time;
        while (!plane.Destroyed && Time.time - tThrow < 14f) yield return null;
        Log($"plane destroyed={plane.Destroyed} after {Time.time - tThrow:0.0}s at {plane.transform.position}, hero at {hero.transform.position}");
        PlaceCam(plane.transform.position + new Vector3(60f, 40f, -60f), plane.transform.position);
        Shot("11_plane_crash");

        // ---------------------------------------------------------------- respawn, mood, sound
        yield return Wait(3f);
        int alive = Civilian.All.Count(c => !c.Dead);
        Log($"respawn: alive civilians {alive}/{civTarget}, cars intact {Car.All.Count(c => !c.Wrecked)}");
        typeof(HeroController).GetProperty("Current").SetValue(hero, HeroController.State.Fly);
        for (float t = 0; t < 1f; t += Time.unscaledDeltaTime) { typeof(HeroController).GetField("velocity", F).SetValue(hero, Vector3.forward * 90f); yield return null; }
        var fl = hero.GetComponents<AudioSource>().FirstOrDefault(x => x.clip == Sfx.Flight);
        Log($"sound: flight roar volume at 90 m/s {(fl ? fl.volume : -1f):0.00}, pitch {(fl ? fl.pitch : -1f):0.00}");
        var ground = Civilian.All.Where(c => !c.Dead).OrderBy(c => c.transform.position.magnitude).First().transform.position;
        PlaceCam(ground + new Vector3(-15f, 8f, -15f), ground);
        Shot("12_dread_mood");

        Log($"DONE errors={errors}");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(errors == 0 ? 0 : 2);
#endif
    }

    void MoveHero(Vector3 pos, Vector3 facing)
    {
        var c = hero.GetComponent<CharacterController>();
        c.enabled = false;
        hero.transform.position = pos;
        facing.y = 0;
        if (facing.sqrMagnitude > 1e-4f) hero.transform.rotation = Quaternion.LookRotation(facing);
        c.enabled = true;
    }

    void CamBehind() => PlaceCam(hero.transform.position - hero.transform.forward * 4.5f + Vector3.up * 2.4f, hero.transform.position + hero.transform.forward * 10f + Vector3.up * 2f);

    void PlaceCam(Vector3 pos, Vector3 look)
    {
        Vector3 d = pos - look;
        if (Physics.SphereCast(look, 0.3f, d.normalized, out var h, d.magnitude, ~((1 << 2) | (1 << 3)), QueryTriggerInteraction.Ignore))
            pos = look + d.normalized * Mathf.Max(0.8f, h.distance - 0.3f);
        cam.transform.position = pos;
        cam.transform.LookAt(look);
    }

    static IEnumerator Wait(float s) => new WaitForSecondsRealtime(s);

    void Shot(string name)
    {
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        tex.Apply();
        cam.targetTexture = null;
        RenderTexture.active = null;
        File.WriteAllBytes($"{Dir}/{name}.png", tex.EncodeToPNG());
        Destroy(rt); Destroy(tex);
    }

    static void Log(string s) => Debug.Log("[LifeTest] " + s);
}
