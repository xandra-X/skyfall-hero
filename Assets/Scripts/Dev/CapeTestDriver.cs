using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Automated test: the cape hangs when standing, streams back when flying, never goes through his body.</summary>
public class CapeTestDriver : MonoBehaviour
{
    HeroController hero;
    Camera cam;
    ThirdPersonCamera tpc;
    Cape cape;
    int errors;
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    void Awake()
    {
        Application.logMessageReceived += (msg, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { errors++; Debug.Log($"[CapeTest] ERROR CAUGHT: {msg}\n{stack}"); }
        };
        if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();
        if (Mouse.current == null) InputSystem.AddDevice<Mouse>();
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory("Screenshots/cape");
        hero = FindAnyObjectByType<HeroController>();
        cam = Camera.main;
        tpc = cam.GetComponent<ThirdPersonCamera>();
        cape = hero.GetComponent<Cape>();
        var stateP = typeof(HeroController).GetProperty("Current");
        var hcc = hero.GetComponent<CharacterController>();
        yield return new WaitForSecondsRealtime(2f);
        Log($"cape built: {(cape && cape.Cloth ? $"{cape.Cloth.vertices.Length} cloth points" : "NO")}");
        if (!cape || !cape.Cloth) { Finish(); yield break; }

        // standing: it should hang down behind him
        yield return new WaitForSeconds(2f);
        Report("standing");
        Side("1_standing", 0f, 3.5f);
        Side("1b_standing_three_quarter", 40f, 3.5f);
        Side("1c_standing_front", 180f, 3.5f);

        // walking about with super speed-ish movement
        stateP.SetValue(hero, HeroController.State.Ground);

        // flying at different speeds: it should stream out behind him and flap
        foreach (var (speed, name) in new[] { (20f, "2_fly_slow"), (120f, "3_fly_boost"), (1500f, "4_fly_space_speed") })
        {
            Vector3 start = new Vector3(0f, speed > 500f ? 9000f : 300f, -50f);
            hcc.enabled = false; hero.transform.position = start; hero.transform.rotation = Quaternion.identity; hcc.enabled = true;
            stateP.SetValue(hero, HeroController.State.Fly);
            for (float t = 0; t < 2.5f; t += Time.deltaTime) { hero.Velocity = Vector3.forward * speed; yield return null; }
            hero.Velocity = Vector3.forward * speed;
            Report(name);
            Side(name, 70f, 5.5f);
        }
        // stop and hover: it drops back down
        for (float t = 0; t < 2.5f; t += Time.deltaTime) { hero.Velocity = Vector3.zero; yield return null; }
        Report("5_hover_after_flight");
        Side("5_hover", 150f, 4f);
        Finish();
    }

    /// Where the cloth is: how far behind / below his chest, and how many points are inside his body.
    void Report(string label)
    {
        var smr = cape.Renderer;
        var verts = cape.Cloth.vertices;          // world-ish: in the cape object's space
        var t = smr.transform;
        var anim = hero.GetComponentInChildren<Animator>();
        Vector3 chest = anim.GetBoneTransform(HumanBodyBones.UpperChest).position;
        Vector3 hips = anim.GetBoneTransform(HumanBodyBones.Hips).position;
        Vector3 fwd = (anim.GetBoneTransform(HumanBodyBones.Head).position - hips).normalized;   // along his body (up when standing, forward when flying)
        int inside = 0; float behind = 0f, along = 0f, spread = 0f;
        Vector3 back = -hero.transform.forward;
        var world = verts.Select(v => t.TransformPoint(v)).ToArray();
        foreach (var p in world)
        {
            float d = DistanceToSegment(p, hips, chest);
            if (d < 0.1f) inside++;
        }
        var tip = world.OrderBy(p => Vector3.Dot(p - chest, fwd)).First();         // the point furthest toward his feet
        behind = Vector3.Dot(tip - chest, -hero.Velocity.normalized);
        along = Vector3.Dot(tip - chest, -fwd);
        float minY = world.Min(p => p.y), maxY = world.Max(p => p.y);
        spread = maxY - minY;
        bool finite = world.All(p => !float.IsNaN(p.x) && p.magnitude < 1e6f);
        Log($"{label}: tip {along:0.00} m down his body, {behind:0.00} m behind the motion, height spread {spread:0.00} m, points inside his back {inside}, finite {finite}");
    }

    static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a; float k = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
        return Vector3.Distance(p, a + ab * k);
    }

    void Side(string name, float yawFromBehind, float dist)
    {
        tpc.enabled = false;
        float fov = cam.fieldOfView;
        cam.fieldOfView = 45f;
        Vector3 c = hero.transform.position + Vector3.up * 1f;
        Vector3 dir = Quaternion.Euler(0f, hero.transform.eulerAngles.y + 180f + yawFromBehind, 0f) * Vector3.forward;
        cam.transform.position = c + dir * dist + Vector3.up * 0.8f;
        cam.transform.LookAt(c);
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
        cam.targetTexture = null; RenderTexture.active = null;
        File.WriteAllBytes($"Screenshots/cape/{name}.png", tex.EncodeToPNG());
        Destroy(rt); Destroy(tex);
        cam.fieldOfView = fov;
        tpc.enabled = true;
    }

    void Finish()
    {
        Log($"DONE errors={errors}");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(errors == 0 ? 0 : 2);
#endif
    }

    static void Log(string s) => Debug.Log("[CapeTest] " + s);
}
