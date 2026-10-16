using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Automated test: fly at boost speed into a wall of every tall city mesh and check he comes out the far side.</summary>
public class RamTestDriver : MonoBehaviour
{
    HeroController hero;
    HeroCombat combat;
    Camera cam;
    int errors;
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    void Awake()
    {
        Application.logMessageReceived += (msg, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { errors++; Debug.Log($"[RamTest] ERROR CAUGHT: {msg}\n{stack}"); }
        };
        if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();
        if (Mouse.current == null) InputSystem.AddDevice<Mouse>();
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory("Screenshots/ram");
        hero = FindAnyObjectByType<HeroController>();
        combat = hero.GetComponent<HeroCombat>();
        cam = Camera.main;
        var stateP = typeof(HeroController).GetProperty("Current");
        var hcc = hero.GetComponent<CharacterController>();
        yield return new WaitForSecondsRealtime(1.5f);

        // one wall per tall mesh: look in from the four sides at a few heights, keep a hit that is a wall
        var targets = new List<(Collider col, Vector3 point, Vector3 normal)>();
        foreach (var root in new[] { "Town", "District_Downtown" })
        {
            var go = GameObject.Find(root);
            if (!go) continue;
            foreach (var c in go.GetComponentsInChildren<Collider>())
            {
                var b = c.bounds;
                if (b.size.y < 6f || c.isTrigger) continue;
                bool found = false;
                foreach (var side in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                {
                    foreach (float h in new[] { 6f, 9f, 14f })
                    {
                        if (h > b.max.y - 1f) continue;
                        // aim at many points along that side of the bounds
                        for (float s = 0.1f; s < 1f && !found; s += 0.2f)
                        {
                            Vector3 across = Vector3.Cross(Vector3.up, side);
                            Vector3 target = b.center + across * (s - 0.5f) * Vector3.Scale(b.size, new Vector3(Mathf.Abs(across.x), 0, Mathf.Abs(across.z))).magnitude;
                            target.y = h;
                            Vector3 from = target - side * (Mathf.Max(b.size.x, b.size.z) + 80f);
                            if (Physics.Raycast(from, side, out var hit, Mathf.Max(b.size.x, b.size.z) + 160f, FxUtil.WorldMask, QueryTriggerInteraction.Ignore)
                                && hit.collider == c && Mathf.Abs(hit.normal.y) < 0.3f && hit.distance > 60f)
                            {
                                targets.Add((c, hit.point, hit.normal));
                                found = true;
                            }
                        }
                        if (found) break;
                    }
                    if (found) break;
                }
            }
        }
        Debug.Log($"[RamTest] walls to try: {targets.Count}");

        int through = 0, blocked = 0, shots = 0;
        foreach (var (col, point, normal) in targets)
        {
            Vector3 dir = -normal; dir.y = 0f; dir.Normalize();
            Vector3 start = point - dir * 50f + Vector3.down * 0.95f;
            hcc.enabled = false; hero.transform.position = start; hero.transform.rotation = Quaternion.LookRotation(dir); hcc.enabled = true;
            stateP.SetValue(hero, HeroController.State.Fly);
            string before = combat.LastStrike;
            float t0 = Time.time;
            bool passed = false;
            while (Time.time - t0 < 1.6f)
            {
                hero.Velocity = dir * hero.boostSpeed;
                yield return null;
                if (Vector3.Dot(hero.transform.position + Vector3.up * 0.95f - point, dir) > 2f) { passed = true; break; }
            }
            if (passed) through++;
            else
            {
                blocked++;
                Debug.Log($"[RamTest]   BLOCKED by {Path(col.transform)} at {point} (normal {normal}), stopped {Vector3.Dot(hero.transform.position + Vector3.up * 0.95f - point, dir):0.0} m from the wall, strike '{combat.LastStrike}'");
                if (shots++ < 3)
                {
                    cam.transform.position = point - dir * 25f + Vector3.up * 6f; cam.transform.LookAt(point);
                    Shot($"blocked_{shots}");
                }
            }
            hero.Velocity = Vector3.zero;
            yield return new WaitForSeconds(0.05f);
        }
        Debug.Log($"[RamTest] flew through {through}/{targets.Count} walls, blocked {blocked}");
        Debug.Log($"[RamTest] DONE errors={errors}");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(errors == 0 && blocked == 0 ? 0 : 2);
#endif
    }

    static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;

    void Shot(string name)
    {
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
        cam.targetTexture = null; RenderTexture.active = null;
        File.WriteAllBytes($"Screenshots/ram/{name}.png", tex.EncodeToPNG());
        Destroy(rt); Destroy(tex);
    }
}
