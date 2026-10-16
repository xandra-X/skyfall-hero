using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Automated play-mode test: climb into space, check the sky / Earth / Sun / Moon, land on the Moon, fight a ship.</summary>
public class SpaceTestDriver : MonoBehaviour
{
    HeroController hero;
    HeroCombat combat;
    Camera cam;
    ThirdPersonCamera tpc;
    int errors;
    const string Dir = "Screenshots/space";
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    PropertyInfo stateP;
    FieldInfo velF, yawF, pitchF;

    void Awake()
    {
        Application.logMessageReceived += (msg, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { errors++; Debug.Log($"[SpaceTest] ERROR CAUGHT: {msg}\n{stack}"); }
        };
        if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();
        if (Mouse.current == null) InputSystem.AddDevice<Mouse>();
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory(Dir);
        hero = FindAnyObjectByType<HeroController>();
        combat = hero.GetComponent<HeroCombat>();
        cam = Camera.main;
        tpc = cam.GetComponent<ThirdPersonCamera>();
        stateP = typeof(HeroController).GetProperty("Current");
        velF = typeof(HeroController).GetField("velocity", F);
        yawF = typeof(ThirdPersonCamera).GetField("yaw", F);
        pitchF = typeof(ThirdPersonCamera).GetField("pitch", F);
        var ss = SpaceSystem.Instance;
        yield return Wait(2f);
        Log($"start: fighters {FindObjectsByType<SpaceFighter>().Length}");

        // ---------------------------------------------------------------- how long to space boosting straight up
        {
            Fly(ss.CityCenter + new Vector3(0f, 80f, -20f), Vector3.forward);
            float t0c = Time.time, at1200 = -1f;
            while (Time.time - t0c < 90f && hero.transform.position.y < ss.spaceFull)
            {
                velF.SetValue(hero, Vector3.up * hero.boostSpeed * hero.SpeedMultiplier);   // what Shift+Space asks for
                if (at1200 < 0f && hero.transform.position.y > 1200f) at1200 = Time.time - t0c;
                yield return null;
            }
            Log($"climb with Shift+Space: above clouds after {at1200:0.0}s, space ({ss.spaceFull:0} m, sky {ss.Space01:0.00}) after {Time.time - t0c:0.0}s, boost drain x{hero.BoostDrainScale:0.0}");
            yield return null;
        }

        // ---------------------------------------------------------------- the climb
        Vector3 basePos = ss.CityCenter + new Vector3(0f, 0f, -20f);
        Vector3 sunFlat = -RenderSettings.sun.transform.forward; sunFlat.y = 0f;
        float sunYaw = Mathf.Atan2(sunFlat.x, sunFlat.z) * Mathf.Rad2Deg;
        foreach (var (h, name, pitch) in new[] { (1450f, "0a_golden_cloud_sea", 4f), (2200f, "0b_golden_high", 12f) })
        {
            Fly(basePos + new Vector3(-2500f, h, -2500f), Vector3.forward);
            Aim(sunYaw, pitch);
            yield return Wait(1.2f);
            Shot(name);
            Log($"{name}: height {h}, golden sun elevation {90f - Vector3.Angle(Vector3.up, -RenderSettings.sun.transform.forward):0.0} deg, sky {ss.Space01:0.00}");
        }
        Aim(sunYaw + 150f, 6f);
        yield return Wait(0.3f);
        Shot("0c_cloud_sea_away_from_sun");
        foreach (var (h, name, pitch) in new[] { (60f, "1_city", 5f), (900f, "2_under_clouds", 40f), (2600f, "3_darkening", 72f), (5000f, "4_city_from_high", 78f),
                                                   (10000f, "5_city_and_earth", 62f), (20000f, "6_space", 35f) })
        {
            Fly(basePos + Vector3.up * h, Vector3.forward);
            Aim(0f, pitch);
            yield return Wait(0.8f);
            Shot(name);
            Log($"{name}: height {hero.transform.position.y:0}, space {ss.Space01:0.00}, city shown {GameObject.Find("Town") != null}, earth shown {ss.earth.gameObject.activeSelf}, speed x{hero.SpeedMultiplier:0.0}, fog {RenderSettings.fog}");
        }
        // looking toward the Sun from space
        Vector3 sunDir = (ss.sunBody.position - cam.transform.position).normalized;
        Aim(Mathf.Atan2(sunDir.x, sunDir.z) * Mathf.Rad2Deg, -Mathf.Asin(sunDir.y) * Mathf.Rad2Deg);
        yield return Wait(0.5f);
        Shot("7_sun");
        float sunDist0 = Vector3.Distance(cam.transform.position, ss.sunBody.position);

        // ---------------------------------------------------------------- try flying at the Sun
        Fly(hero.transform.position, sunDir);
        for (float t = 0; t < 3f; t += Time.unscaledDeltaTime) { velF.SetValue(hero, sunDir * 1500f); yield return null; }
        Log($"chasing the sun: distance {sunDist0:0} -> {Vector3.Distance(cam.transform.position, ss.sunBody.position):0} (must stay the same)");
        Fly(ss.CityCenter + new Vector3(0f, 40000f, 0f), Vector3.forward);
        yield return Wait(0.3f);
        Log($"space speed multiplier: x{hero.SpeedMultiplier:0.0} (boost {hero.boostSpeed * hero.SpeedMultiplier:0} m/s)");

        // ---------------------------------------------------------------- straight from the city to the Moon
        {
            Fly(ss.CityCenter + new Vector3(0f, 9000f, 0f), Vector3.forward);
            yield return null;
            float tm = Time.time; float minAlt = 1e9f;
            while (Time.time - tm < 40f && Vector3.Distance(hero.transform.position, ss.MoonCenter) > ss.moonRadius + 2500f)
            {
                Vector3 to = (ss.MoonCenter + Vector3.up * (ss.moonRadius + 500f) - hero.transform.position).normalized;
                velF.SetValue(hero, to * hero.boostSpeed * hero.SpeedMultiplier);
                minAlt = Mathf.Min(minAlt, ss.Altitude(hero.transform.position));
                Vector3 before = hero.transform.position;
                yield return null;
                if (Vector3.Distance(before, hero.transform.position) > 400f || Time.frameCount % 200 == 0)
                    Log($"  trip: {before} -> {hero.transform.position}, alt {ss.Altitude(hero.transform.position):0}, state {hero.Current}, sea {hero.SeaNearby}, mult {hero.SpeedMultiplier:0}");
            }
            Log($"city -> moon: {Time.time - tm:0.0}s, {Vector3.Distance(hero.transform.position, ss.MoonCenter) - ss.moonRadius:0} m from the surface, lowest height over Earth on the way {minAlt:0} m");
        }

        // ---------------------------------------------------------------- diving back in: re-entry fire
        var fire = hero.GetComponent<ReentryFx>();
        Fly(ss.CityCenter + new Vector3(0f, 12500f, -3000f), Vector3.forward);
        Vector3 dive = new Vector3(0f, -1f, 0.25f).normalized * 1400f;
        float peakFire = 0f;
        for (float t = 0; t < 1.3f; t += Time.unscaledDeltaTime) { velF.SetValue(hero, dive); peakFire = Mathf.Max(peakFire, fire ? fire.Intensity : 0f); yield return null; }
        Aim(60f, 15f);
        for (int i = 0; i < 3; i++) { velF.SetValue(hero, dive); yield return null; }
        Shot("6b_reentry_fire");
        Log($"re-entry: fire component {(fire ? "yes" : "MISSING")}, peak intensity {peakFire:0.00} at height {hero.transform.position.y:0}");
        Fly(hero.transform.position, Vector3.forward);
        yield return Wait(1.5f);

        // ---------------------------------------------------------------- to the Moon
        Vector3 moonTop = ss.crater.position;
        Fly(moonTop + new Vector3(0f, 3000f, -9000f), Vector3.forward);
        Aim(0f, 15f);
        yield return Wait(0.6f);
        Shot("8_moon_approach");
        Physics.Raycast(moonTop + new Vector3(30f, 900f, -200f), Vector3.down, out var landHit, 3000f, 1);
        Fly(landHit.point + Vector3.up * 30f, Vector3.forward);
        yield return Wait(0.3f);
        foreach (var h in Physics.RaycastAll(hero.transform.position + Vector3.up * 5f, Vector3.down, 2000f, ~0, QueryTriggerInteraction.Collide))
            Log($"  below landing spot: {h.collider.name} ({h.collider.GetType().Name}) layer {h.collider.gameObject.layer} enabled {h.collider.enabled} at {h.point.y - moonTop.y:0.0}");
        var mc = ss.crater.GetComponent<MeshCollider>();
        int gh = 0, gn = 0;
        for (float x = -1000; x <= 1000; x += 250) for (float z = -1000; z <= 1000; z += 250)
        { gn++; if (Physics.Raycast(moonTop + new Vector3(x, 600f, z), Vector3.down, out var rh, 2000f) && rh.collider == mc && rh.point.y > moonTop.y - 400f) gh++; }
        Log($"  play-mode grid: top hits {gh}/{gn}; hero offset from crater {hero.transform.position - moonTop}; crater at {ss.crater.position} scale {ss.crater.lossyScale} rot {ss.crater.eulerAngles}");
        Log($"  crater collider: {(mc ? $"{mc.sharedMesh?.name} tris {mc.sharedMesh?.triangles.Length / 3} bounds {mc.bounds}" : "none")} active {ss.crater.gameObject.activeInHierarchy}");
        {
            var hcc = hero.GetComponent<CharacterController>();
            Log($"  ignore-collision hero/crater: {Physics.GetIgnoreCollision(hcc, mc)}, layer-pair ignored {Physics.GetIgnoreLayerCollision(hero.gameObject.layer, mc.gameObject.layer)}, hero layer {hero.gameObject.layer}");
            hcc.enabled = false; hero.transform.position = landHit.point + Vector3.up * 30f; hcc.enabled = true;
            yield return null;
            var before = hero.transform.position;
            var flags = hcc.Move(Vector3.down * 30f);
            Log($"  direct Move down 30 m: flags {flags}, moved {before.y - hero.transform.position.y:0.0} m, now {hero.transform.position.y - moonTop.y:0.0} over crater centre");
        }
        stateP.SetValue(hero, HeroController.State.Air);                // stop flying above the crater
        float t0 = Time.time;
        while (hero.Current != HeroController.State.Ground && Time.time - t0 < 15f) yield return null;
        Log($"moon landing: state {hero.Current} after {Time.time - t0:0.0}s, gravity x{hero.GravityScale:0.00}, height over crater surface {hero.transform.position.y - landHit.point.y:0.0}");
        Aim(20f, 12f);
        yield return Wait(0.8f);
        Shot("9_on_the_moon");
        // a moon jump
        float y0 = hero.transform.position.y, peak = y0;
        typeof(HeroController).GetMethod("UpdateGround", F);
        velF.SetValue(hero, new Vector3(0f, hero.jumpSpeed, 0f));
        stateP.SetValue(hero, HeroController.State.Air);
        t0 = Time.time;
        while (Time.time - t0 < 6f && (hero.Current != HeroController.State.Ground || Time.time - t0 < 0.3f)) { peak = Mathf.Max(peak, hero.transform.position.y); yield return null; }
        Log($"moon jump: peak {peak - y0:0.0} m (earth jump ~1.2 m)");

        // walk 2 km across the Moon: the Moon turns under him, he stays on top and on the ground
        {
            var hcc = hero.GetComponent<CharacterController>();
            Quaternion moonRot0 = ss.moon.rotation;
            float topY = hero.transform.position.y;
            int grounded = 0, frames = 0;
            for (int i = 0; i < 160; i++)
            {
                hcc.Move(hero.transform.forward * 14f + Vector3.down * 2f);
                yield return null;
                frames++; if (hero.Current == HeroController.State.Ground) grounded++;
            }
            float off = Vector3.Angle(hero.transform.position - ss.MoonCenter, Vector3.up);
            Log($"moon walk: moon turned {Quaternion.Angle(moonRot0, ss.moon.rotation):0.0} deg, hero off top {off:0.00} deg, height change {hero.transform.position.y - topY:0.0} m, grounded {grounded}/{frames}");
            Aim(hero.transform.eulerAngles.y + 30f, 8f);
            yield return Wait(0.6f);
            Shot("9b_walking_on_the_moon");
        }
        // the Earth up in the lunar sky
        {
            Vector3 toE = ss.EarthCentre - cam.transform.position;
            float elev = Mathf.Asin(toE.normalized.y) * Mathf.Rad2Deg;
            float angR = Mathf.Asin(ss.earthRadius / toE.magnitude) * Mathf.Rad2Deg;
            Aim(Mathf.Atan2(toE.x, toE.z) * Mathf.Rad2Deg, -Mathf.Max(elev, 5f) * 0.6f);
            yield return Wait(1.5f);
            Shot("9c_earth_from_the_moon");
            Log($"real earth from the moon: centre {elev:0.0} deg up, spans {elev - angR:0.0}..{elev + angR:0.0} deg, earth object active {ss.earth.gameObject.activeInHierarchy}");
            // fighters come hunting while he stands on the Moon
            float waitStart = Time.time; int closest = 999999;
            while (Time.time - waitStart < 25f)
            {
                foreach (var f in SpaceFighter.All) if (f) closest = Mathf.Min(closest, (int)Vector3.Distance(f.transform.position, hero.transform.position));
                if (closest < 1200 && FindObjectsByType<LaserBolt>().Length > 0) break;
                yield return null;
            }
            Log($"fighters on the moon: closest {closest} m after {Time.time - waitStart:0.0}s, bolts in flight {FindObjectsByType<LaserBolt>().Length}");
            // heat vision with aim lock: point roughly at the nearest one (a few degrees off) and fire
            var near = SpaceFighter.All.Where(f => f && !f.Destroyed).OrderBy(f => Vector3.Distance(f.transform.position, hero.transform.position)).FirstOrDefault();
            if (near)
            {
                t0 = Time.time;
                hero.ForceBlast = true;
                while (near && !near.Destroyed && Time.time - t0 < 5f)
                {
                    Vector3 to = Quaternion.Euler(0f, 3f, 0f) * (near.transform.position - cam.transform.position);
                    Aim(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -Mathf.Asin(to.normalized.y) * Mathf.Rad2Deg);
                    yield return null;
                }
                hero.ForceBlast = false;
                Log($"aim-lock laser from the moon: fighter destroyed {(!near || near.Destroyed)} after {Time.time - t0:0.0}s");
            }
        }
        // land on the side of the Moon: it swings him round to the top
        Fly(ss.MoonCenter + Vector3.right * (ss.moonRadius + 25f), Vector3.forward);
        yield return null;
        stateP.SetValue(hero, HeroController.State.Air);
        t0 = Time.time;
        while (Time.time - t0 < 15f && !(hero.Current == HeroController.State.Ground && Vector3.Angle(hero.transform.position - ss.MoonCenter, Vector3.up) < 1f)) yield return null;
        Log($"side landing: state {hero.Current} after {Time.time - t0:0.0}s, {Vector3.Distance(hero.transform.position, ss.MoonCenter) - ss.moonRadius:0} m above radius, off top {Vector3.Angle(hero.transform.position - ss.MoonCenter, Vector3.up):0.00} deg");

        // ---------------------------------------------------------------- fight a space fighter
        var fighter = FindObjectsByType<SpaceFighter>().OrderBy(f => (f.transform.position - hero.transform.position).sqrMagnitude).First();
        Vector3 fp = fighter.transform.position;
        {
            var vf = typeof(SpaceFighter).GetField("velocity", F);
            var all = FindObjectsByType<SpaceFighter>();
            var start = all.Select(f => f.transform.position).ToArray();
            float g0 = Time.time;
            yield return Wait(3f);
            for (int i = 0; i < all.Length; i++) if (all[i])
                Log($"  fighter {i} {(all[i].leader ? "wing" : "lead")}: avg {Vector3.Distance(start[i], all[i].transform.position) / (Time.time - g0):0} m/s, now {((Vector3)vf.GetValue(all[i])).magnitude:0} m/s, {Vector3.Distance(all[i].transform.position, hero.transform.position):0} m from hero");
        }
        Vector3 p0 = fighter.transform.position;
        float gt0 = Time.time;
        yield return Wait(1f);
        Log($"fighter speed {Vector3.Distance(p0, fighter.transform.position) / Mathf.Max(Time.time - gt0, 0.01f):0} m/s (fps {Time.frameCount / Time.realtimeSinceStartup:0}), squadron mates within 150 m: {FindObjectsByType<SpaceFighter>().Count(f => f != fighter && Vector3.Distance(f.transform.position, fighter.transform.position) < 150f)}");
        // watch the squadron fly past from the side
        tpc.enabled = false;
        cam.transform.position = fighter.transform.position + fighter.transform.right * 45f + fighter.transform.forward * 35f + Vector3.up * 10f;
        cam.transform.LookAt(fighter.transform.position - fighter.transform.forward * 20f);
        Shot("10a_squadron");
        tpc.enabled = true;
        fp = fighter.transform.position;
        Fly(fp + new Vector3(0f, -60f, -220f), Vector3.forward);
        yield return Wait(1.5f);
        fp = fighter.transform.position;
        Aim(Mathf.Atan2(fp.x - hero.transform.position.x, fp.z - hero.transform.position.z) * Mathf.Rad2Deg, 0f);
        PlaceCamNear(fighter.transform);
        Shot("10_fighter");
        int bolts = FindObjectsByType<LaserBolt>().Length;
        combat.AimOverride = fighter.transform.position;
        hero.ForceBlast = true;
        t0 = Time.time;
        while (fighter && !fighter.Destroyed && Time.time - t0 < 4f) { combat.AimOverride = fighter.transform.position; yield return null; }
        hero.ForceBlast = false; combat.AimOverride = null;
        Log($"laser fighter: destroyed {(!fighter || fighter.Destroyed)} after {Time.time - t0:0.0}s; bolts in flight before {bolts}");
        yield return Wait(0.4f);
        Shot("11_fighter_destroyed");
        yield return Wait(4f);
        Log($"fighters after respawn wait: {FindObjectsByType<SpaceFighter>().Length}");

        // ---------------------------------------------------------------- dive home from far away (the old glitch)
        {
            Vector3 earthC = ss.CityCenter + Vector3.down * ss.earthRadius;
            Fly(ss.CityCenter + new Vector3(15000f, 22000f, 38000f), Vector3.back);
            yield return null;
            yield return null;
            float t0d = Time.time, worstDt = 0f, worstJump = 0f, minSkyWhileHigh = 1f, worstAt = 0f;
            Vector3 last = hero.transform.position;
            int shot = 0;
            while (Time.time - t0d < 60f && ss.Altitude(hero.transform.position) > 1700f)
            {
                Vector3 toEarth = (earthC - hero.transform.position).normalized;
                Vector3 v = toEarth * Mathf.Lerp(250f, 1600f, Mathf.InverseLerp(3000f, 15000f, ss.Altitude(hero.transform.position)));
                velF.SetValue(hero, v);
                Aim(Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg, 35f);
                yield return null;
                if (Time.unscaledDeltaTime > worstDt) { worstDt = Time.unscaledDeltaTime; worstAt = ss.Altitude(hero.transform.position); }
                float step = Vector3.Distance(last, hero.transform.position);
                float extra = step - v.magnitude * Time.deltaTime;
                if (extra > 500f) Log($"  JUMP {extra:0} m: {last} -> {hero.transform.position}, state {hero.Current}, alt {ss.Altitude(hero.transform.position):0}, dt {Time.deltaTime:0.000}");
                worstJump = Mathf.Max(worstJump, extra);       // anything beyond normal motion
                last = hero.transform.position;
                float alt = ss.Altitude(hero.transform.position);
                if (alt > 6000f) minSkyWhileHigh = Mathf.Min(minSkyWhileHigh, ss.Space01);
                if (shot == 0 && alt < 14000f) { Shot("13a_dive_home_high"); shot++; }
                if (shot == 1 && alt < 4500f) { Shot("13b_dive_home_low"); shot++; }
            }
            Vector3 hp = hero.transform.position;
            Log($"dive home: {Time.time - t0d:0.0}s, ends {new Vector2(hp.x - ss.CityCenter.x, hp.z - ss.CityCenter.z).magnitude:0} m from city at {ss.Altitude(hp):0} m, " +
                $"worst frame {worstDt * 1000f:0} ms at {worstAt:0} m, worst extra jump {worstJump:0.0} m, darkest-sky check above 6 km {minSkyWhileHigh:0.00} (must stay ~1)");
            Fly(hero.transform.position, Vector3.forward);
            yield return Wait(0.5f);
        }

        // ---------------------------------------------------------------- touch controls
        {
            var tc = new GameObject("TouchControls").AddComponent<TouchControls>();
            yield return null;
            tc.enabled = false;                      // no real fingers in a batch test: press the buttons directly
            GameInput.TouchActive = true;
            Fly(ss.CityCenter + new Vector3(0f, 300f, 0f), Vector3.forward);
            float ty0 = hero.transform.position.y;
            for (float tt = 0f; tt < 1.5f; tt += Time.deltaTime) { GameInput.SetTouch(GameInput.Btn.Jump, true); yield return null; }
            Log($"  touch debug: held {GameInput.Held(GameInput.Btn.Jump)}, state {hero.Current}, vel {hero.Velocity}, pos {hero.transform.position}");
            GameInput.SetTouch(GameInput.Btn.Jump, false);
            float climbed = hero.transform.position.y - ty0;
            Vector3 tp0 = hero.transform.position;
            for (float tt = 0f; tt < 1.5f; tt += Time.deltaTime) { GameInput.TouchMove = new Vector2(0f, 1f); yield return null; }
            float moved = Vector3.Distance(new Vector3(tp0.x, 0f, tp0.z), new Vector3(hero.transform.position.x, 0f, hero.transform.position.z));
            GameInput.TouchMove = Vector2.zero;
            // picture of the buttons: draw the overlay through the camera for the screenshot
            var cv = tc.GetComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceCamera; cv.worldCamera = cam; cv.planeDistance = 1.5f;
            Aim(0f, 10f);
            yield return Wait(0.4f);
            Shot("14_touch_controls");
            Log($"touch: UP button climbed {climbed:0.0} m, stick moved {moved:0.0} m, buttons {tc.GetComponentsInChildren<UnityEngine.UI.Text>().Length}");
            tc.enabled = true;
            Destroy(tc.gameObject);
            yield return null;
        }

        // ---------------------------------------------------------------- back down to the city
        Fly(ss.CityCenter + new Vector3(0f, 6000f, 0f), Vector3.forward);
        yield return null;
        Fly(ss.CityCenter + new Vector3(0f, 1000f, 0f), Vector3.forward);
        Aim(0f, 30f);
        yield return Wait(0.8f);
        Shot("12_back_home");
        Log($"back home: city shown {GameObject.Find("Town") != null}, earth shown {ss.earth.gameObject.activeSelf}, space {ss.Space01:0.00}");

        Log($"DONE errors={errors}");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(errors == 0 ? 0 : 2);
#endif
    }

    void Fly(Vector3 pos, Vector3 facing)
    {
        var cc = hero.GetComponent<CharacterController>();
        cc.enabled = false;
        hero.transform.position = pos;
        facing.y = 0;
        if (facing.sqrMagnitude > 1e-4f) hero.transform.rotation = Quaternion.LookRotation(facing);
        cc.enabled = true;
        stateP.SetValue(hero, HeroController.State.Fly);
        velF.SetValue(hero, Vector3.zero);
    }

    void Aim(float yaw, float pitch)
    {
        yawF.SetValue(tpc, yaw);
        pitchF.SetValue(tpc, pitch);
    }

    void PlaceCamNear(Transform t)
    {
        tpc.enabled = false;
        cam.transform.position = t.position + t.right * 25f + Vector3.up * 8f - t.forward * 10f;
        cam.transform.LookAt(t.position);
        tpc.enabled = true;
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

    static void Log(string s) => Debug.Log("[SpaceTest] " + s);
}
