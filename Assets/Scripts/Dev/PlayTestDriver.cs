using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Automated play-mode test (added only by Editor/PlayTest.cs, never saved in the scene).
/// Punches, kicks, lasers, rams and collapses buildings, takes pictures, logs results, then quits.
/// </summary>
public class PlayTestDriver : MonoBehaviour
{
    HeroController hero;
    HeroCombat combat;
    Camera cam;
    int errors;
    string dir = "Screenshots/play";
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    void Awake()
    {
        Application.logMessageReceived += (msg, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                errors++;
                Debug.Log($"[PlayTest] ERROR CAUGHT: {msg}\n{stack}");
            }
        };
        if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();
        if (Mouse.current == null) InputSystem.AddDevice<Mouse>();
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory(dir);
        hero = FindAnyObjectByType<HeroController>();
        combat = hero.GetComponent<HeroCombat>();
        cam = Camera.main;
        var tpc = cam.GetComponent<ThirdPersonCamera>();
        if (tpc) tpc.enabled = false;               // the test places the camera itself
        yield return Wait(1.5f);

        // ---------------------------------------------------------------- pick a town building facing an open street
        var buildings = FindObjectsByType<Destructible>().Where(d => d.name.StartsWith("Bldg_")).ToList();
        Log($"buildings {FindObjectsByType<Destructible>().Length} (town {buildings.Count}), breakables {FindObjectsByType<Breakable>().Length}");
        Destructible target = null; Vector3 wallPoint = default, wallNormal = default; float free = 0;
        foreach (var d in buildings.OrderBy(x => x.WorldBounds.center.magnitude))
            if (OpenWall(d, 22f, out wallPoint, out wallNormal, out free)) { target = d; break; }
        Log($"target {target.name} health {target.Health:0}/{target.maxHealth:0} wall {wallPoint} normal {wallNormal} free {free:0}");
        float groundY = target.WorldBounds.min.y;
        Vector3 Floor(Vector3 v) => new Vector3(v.x, groundY, v.z);

        // ---------------------------------------------------------------- punch (camera behind him, like a player)
        MoveHero(Floor(wallPoint) + wallNormal * 6f, -wallNormal);
        CamBehindHero(-wallNormal);
        yield return Wait(0.5f);
        float hp0 = target.Health;
        var ccd = hero.GetComponent<CharacterController>();
        Log($"pre-punch: state {hero.Current} grounded {ccd.isGrounded} y {hero.transform.position.y:0.00}");
        hero.TriggerPunch();
        for (int k = 0; k < 8; k++)
        {
            yield return Wait(0.2f);
            Log($"  t={0.2f * (k + 1):0.0}s state {hero.Current} grounded {ccd.isGrounded} pos {hero.transform.position} vel {hero.Velocity} timeScale {Time.timeScale}");
        }
        yield return Wait(0.1f);
        PlaceCam(wallPoint + wallNormal * 8f + Vector3.up * 1.5f + Side(wallNormal) * 5f, wallPoint);
        Shot("1_punch_impact");
        yield return Wait(1.0f);
        Shot("2_punch_hole");
        Log($"punch: health {hp0:0} -> {target.Health:0}, hero {Vector3.Distance(Floor(hero.transform.position), Floor(wallPoint)):0.0} m from wall");

        // ---------------------------------------------------------------- kick (to the side)
        // a spot further along the same wall (checked, so we don't kick past the corner)
        Vector3 kickWall = wallPoint;
        var tcol = target.GetComponentInChildren<Collider>();
        foreach (float sOff in new[] { 3.5f, -3.5f, 2.5f, -2.5f })
            if (tcol.Raycast(new Ray(wallPoint + wallNormal * 5f + Side(wallNormal) * sOff, -wallNormal), out var kh, 10f)) { kickWall = kh.point; break; }
        MoveHero(Floor(kickWall) + wallNormal * 2.5f, -wallNormal);
        CamBehindHero(-wallNormal);
        for (float t = 0; t < 2f && hero.Current != HeroController.State.Ground; t += Time.unscaledDeltaTime) yield return null;
        yield return Wait(0.2f);
        hp0 = target.Health;
        var animr = hero.GetComponentInChildren<Animator>();
        Log($"before kick: state {hero.Current}, has Kick state {animr.HasState(0, Animator.StringToHash("Kick"))}");
        hero.TriggerKick();
        Log($"after kick trigger: kicking {hero.Kicking}");
        yield return Wait(1.3f);
        PlaceCam(wallPoint + wallNormal * 12f + Vector3.up * 2.5f + Side(wallNormal) * 2f, wallPoint + Side(wallNormal) * 2f);
        Shot("3_punch_and_kick_holes");
        Log($"kick: health {hp0:0} -> {target.Health:0}, last strike {combat.LastStrike}");

        // ---------------------------------------------------------------- heat vision: burn through
        float back = Mathf.Min(14f, free - 5f);
        MoveHero(Floor(wallPoint) + wallNormal * back, -wallNormal);
        Vector3 laserSpot = wallPoint + Vector3.up * 3f - Side(wallNormal) * 1.5f;
        combat.AimOverride = laserSpot;
        hero.ForceBlast = true;
        PlaceCam(hero.transform.position + wallNormal * 4f + Vector3.up * 2.2f + Side(wallNormal) * 1.2f, laserSpot);
        yield return Wait(0.35f);
        Log($"laser: hero {hero.transform.position} cam {cam.transform.position} spot {laserSpot} beam end {combat.BeamEnd} hit {combat.BeamHitName}");
        Shot("4_laser_burning");
        yield return Wait(1.6f);
        Shot("5_laser_through");
        Vector3 beamDir = (laserSpot - (hero.transform.position + Vector3.up * 1.75f)).normalized;
        PlaceCam(laserSpot - beamDir * 5f, laserSpot + beamDir * 40f);
        Shot("6_view_through_hole");
        hero.ForceBlast = false;
        yield return Wait(2.5f);
        PlaceCam(laserSpot - beamDir * 5f + Vector3.up * 0.3f, laserSpot + beamDir * 40f);
        Shot("6b_hole_cooling");

        // ---------------------------------------------------------------- heat vision: sweep cut
        hero.ForceBlast = true;
        Vector3 a = wallPoint + Vector3.up * 6f - Side(wallNormal) * 5f, b = wallPoint + Vector3.up * 6f + Side(wallNormal) * 5f;
        for (float t = 0; t < 1.8f; t += Time.unscaledDeltaTime)
        {
            combat.AimOverride = Vector3.Lerp(a, b, t / 1.8f);
            yield return null;
        }
        PlaceCam(hero.transform.position + wallNormal * 4f + Vector3.up * 2.5f + Side(wallNormal) * 1.2f, wallPoint + Vector3.up * 5f);
        Shot("7_laser_sweep");
        // ground scorch
        for (float t = 0; t < 1.2f; t += Time.unscaledDeltaTime)
        {
            combat.AimOverride = Floor(hero.transform.position) + wallNormal * Mathf.Lerp(2f, 6f, t) + Side(wallNormal) * Mathf.Sin(t * 6f) * 2f;
            yield return null;
        }
        PlaceCam(hero.transform.position - wallNormal * 2f + Vector3.up * 6f + Side(wallNormal) * 3f, Floor(hero.transform.position) + wallNormal * 4f);
        Shot("8_ground_scorch");
        hero.ForceBlast = false;
        combat.AimOverride = null;
        Log($"laser: holes {Shader.GetGlobalInt("_HoleCount")}, health now {target.Health:0}, collapsing {target.Collapsing}");

        // ---------------------------------------------------------------- props: a lamp with open space around it
        Breakable lamp = null; Vector3 lampDir = Vector3.forward;
        foreach (var x in FindObjectsByType<Breakable>().Where(x => x.isPole && !x.Broken))
        {
            foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                if (!Physics.Raycast(x.transform.position + Vector3.up * 1.5f + d * 0.6f, d, 9f, FxUtil.WorldMask)) { lamp = x; lampDir = d; break; }
            if (lamp) break;
        }
        MoveHero(lamp.transform.position + lampDir * 1.7f, -lampDir);
        CamBehindHero(-lampDir);
        yield return Wait(0.3f);
        hero.TriggerPunch();
        yield return Wait(0.75f);
        PlaceCam(lamp.transform.position + lampDir * 8f + Vector3.up * 3f, lamp.transform.position - lampDir * 3f + Vector3.up * 2f);
        Shot("9_prop_flying");
        Log($"prop {lamp.name} broken={lamp.Broken} pole={lamp.isPole} light={lamp.hasLight}");
        yield return Wait(1.5f);

        // ---------------------------------------------------------------- ram through a downtown building
        var down = FindObjectsByType<Destructible>().Where(d => !d.name.StartsWith("Bldg_") && d.WorldBounds.size.y > 40f).ToList();
        Destructible rammed = null; Vector3 start = default, rdir = Vector3.forward;
        foreach (var d in down)
        {
            var c = d.WorldBounds.center; c.y = d.WorldBounds.min.y + 20f;
            Vector3 s0 = c - Vector3.forward * (d.WorldBounds.extents.z + 35f);
            if (Physics.Raycast(s0, Vector3.forward, out var rh, 80f, FxUtil.WorldMask) && rh.collider.GetComponentInParent<Destructible>() == d)
            { rammed = d; start = s0; break; }
        }
        typeof(HeroController).GetProperty("Current").SetValue(hero, HeroController.State.Fly);
        MoveHero(start, rdir);
        float hpT = rammed.Health;
        Vector3 ramCam = start + new Vector3(30f, 10f, 15f);
        for (float t = 0; t < 0.9f; t += Time.unscaledDeltaTime)
        {
            typeof(HeroController).GetField("velocity", F).SetValue(hero, rdir * 90f);
            PlaceCam(ramCam, hero.transform.position);
            if (t > 0.45f && t < 0.45f + Time.unscaledDeltaTime * 1.5f) Shot("10a_ram_entry");
            yield return null;
        }
        PlaceCam(ramCam + new Vector3(10f, 5f, 20f), new Vector3(rammed.WorldBounds.center.x, start.y, rammed.WorldBounds.center.z));
        Shot("10b_ram_through");
        Log($"ram: {rammed.name} health {hpT:0} -> {rammed.Health:0}; last strike '{combat.LastStrike}'; hero z {hero.transform.position.z:0} (building z {rammed.WorldBounds.min.z:0}..{rammed.WorldBounds.max.z:0})");

        // ---------------------------------------------------------------- no collapse, healing
        var victim = buildings.First(x => x != target);
        victim.Damage(99999f, victim.WorldBounds.center);
        Log($"huge damage: {victim.name} collapsing={victim.Collapsing} active={victim.gameObject.activeSelf}; target active={target.gameObject.activeSelf}");
        int holesBefore = Shader.GetGlobalInt("_HoleCount");
        Vector3 lampHome = lamp.transform.position;
        var ds = DamageSystem.Instance;
        PlaceCam(wallPoint + wallNormal * 12f + Vector3.up * 2.5f + Side(wallNormal) * 2f, wallPoint + Side(wallNormal) * 2f);
        Shot("11_before_heal");
        yield return Wait(ds.HealTotal + 1.5f);
        PlaceCam(wallPoint + wallNormal * 12f + Vector3.up * 2.5f + Side(wallNormal) * 2f, wallPoint + Side(wallNormal) * 2f);
        Shot("12_healed");
        Log($"heal: holes {holesBefore} -> {Shader.GetGlobalInt("_HoleCount")}");
        yield return Wait(3f);
        Log($"respawn: {lamp.name} broken={lamp.Broken} scale {lamp.transform.localScale.x:0.00} pos {lamp.transform.position}");

        Log($"DONE errors={errors} timeScale={Time.timeScale}");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(errors == 0 ? 0 : 2);
#endif
    }

    // ------------------------------------------------------------------ helpers
    static Vector3 Side(Vector3 n) => Vector3.Cross(Vector3.up, n).normalized;

    /// A wall of this building with at least `need` metres of open space in front of it.
    static bool OpenWall(Destructible d, float need, out Vector3 point, out Vector3 normal, out float free)
    {
        Bounds b = d.WorldBounds;
        var col = d.GetComponentInChildren<Collider>();
        point = normal = default; free = 0;
        foreach (var dir in new[] { Vector3.back, Vector3.forward, Vector3.left, Vector3.right })
        {
            Vector3 from = new Vector3(b.center.x, b.min.y + 1.25f, b.center.z) + dir * (b.extents.magnitude + 30f);
            if (!col.Raycast(new Ray(from, -dir), out var hit, 300f)) continue;
            Vector3 n = hit.normal; n.y = 0;
            if (n.sqrMagnitude < 0.5f) continue;
            n.Normalize();
            free = 60f;
            foreach (float up in new[] { 0f, 2f, 4f, 6f })
                if (Physics.Raycast(hit.point + n * 0.2f + Vector3.up * up, n, out var h2, 60f, FxUtil.WorldMask)) free = Mathf.Min(free, h2.distance);
            if (free >= need) { point = hit.point; normal = n; return true; }
        }
        return false;
    }

    void CamBehindHero(Vector3 fwd) => PlaceCam(hero.transform.position - fwd * 4.5f + Vector3.up * 2.2f + Side(fwd) * 0.6f, hero.transform.position + fwd * 10f + Vector3.up * 1.4f);

    void StandFacingWall(Destructible d, float dist, out Vector3 point, out Vector3 normal)
    {
        Bounds b = d.WorldBounds;
        // approach from the side facing the town centre, at chest height
        Vector3 from = new Vector3(b.center.x, b.min.y + 1.25f, b.min.z - 40f);
        var col = d.GetComponentInChildren<Collider>();
        if (col.Raycast(new Ray(from, Vector3.forward), out var hit, 200f)) { point = hit.point; normal = hit.normal; }
        else { point = new Vector3(b.center.x, b.min.y + 1.25f, b.min.z); normal = Vector3.back; }
        normal.y = 0; normal.Normalize();
        MoveHero(new Vector3(point.x, b.min.y, point.z) + normal * dist, -normal);
    }

    void MoveHero(Vector3 pos, Vector3 facing)
    {
        var cc = hero.GetComponent<CharacterController>();
        cc.enabled = false;
        hero.transform.position = pos;
        facing.y = 0;
        if (facing.sqrMagnitude > 1e-4f) hero.transform.rotation = Quaternion.LookRotation(facing);
        cc.enabled = true;
    }

    /// Put the camera at pos looking at look, pulled in so no wall is between them (like a game camera).
    void PlaceCam(Vector3 pos, Vector3 look)
    {
        Vector3 d = pos - look;
        if (Physics.SphereCast(look, 0.3f, d.normalized, out var h, d.magnitude, FxUtil.WorldMask, QueryTriggerInteraction.Ignore))
            pos = look + d.normalized * Mathf.Max(0.5f, h.distance - 0.3f);
        cam.transform.position = pos;
        cam.transform.LookAt(look);
    }

    void PlaceCamBehind(Vector3 fwd, Vector3 look) => PlaceCam(hero.transform.position - fwd * 5f + Vector3.up * 2.2f + Side(fwd) * 2f, look);

    static IEnumerator Wait(float s) => new WaitForSecondsRealtime(s);

    void Shot(string name)
    {
        var rt = new RenderTexture(1280, 720, 24);
        var old = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        tex.Apply();
        cam.targetTexture = old;
        RenderTexture.active = null;
        File.WriteAllBytes($"{dir}/{name}.png", tex.EncodeToPNG());
        Destroy(rt); Destroy(tex);
    }

    static void Log(string s) => Debug.Log("[PlayTest] " + s);
}
