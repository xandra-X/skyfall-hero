using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>Automated test of the iPhone controls with simulated finger touches on a virtual touchscreen.</summary>
public class MobileTestDriver : MonoBehaviour
{
    HeroController hero;
    Camera cam;
    Touchscreen ts;
    TouchControls tc;
    int errors, fails;
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    void Awake()
    {
        Application.logMessageReceived += (msg, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { errors++; Debug.Log($"[MobileTest] ERROR CAUGHT: {msg}\n{stack}"); }
        };
        var st = ScriptableObject.CreateInstance<InputSettings>();
        st.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        st.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = st;
        ts = InputSystem.AddDevice<Touchscreen>();
    }

    IEnumerator Start()
    {
        Directory.CreateDirectory("Screenshots/mobile");
        hero = FindAnyObjectByType<HeroController>();
        cam = Camera.main;
        var stateP = typeof(HeroController).GetProperty("Current");
        tc = new GameObject("TouchControls").AddComponent<TouchControls>();
        yield return new WaitForSecondsRealtime(1.5f);
        Log($"screen {Screen.width}x{Screen.height}");

        // ---- diagnostics: does a queued touch arrive?
        Touch(9, UnityEngine.InputSystem.TouchPhase.Began, new Vector2(100, 100));
        yield return null;
        Log($"  diag: touchscreen current {Touchscreen.current == ts}, primary pressed {ts.primaryTouch.press.isPressed}, pos {ts.primaryTouch.position.ReadValue()}, enhanced enabled {UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.enabled}, active touches {UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count}, fingers {UnityEngine.InputSystem.EnhancedTouch.Touch.activeFingers.Count}, update mode {InputSystem.settings.updateMode}");
        Touch(9, UnityEngine.InputSystem.TouchPhase.Ended, new Vector2(100, 100));
        yield return null;

        // ---- does a touch reach the controls at all? drag the joystick forward
        Vector3 p0 = hero.transform.position;
        yield return Drag(2, new Vector2(Screen.width * 0.15f, Screen.height * 0.3f), new Vector2(0f, Screen.height * 0.12f), 1.2f);
        float moved = Vector3.Distance(p0, hero.transform.position);
        Check($"joystick moves him ({moved:0.0} m)", moved > 2f);

        // ---- ATTACK: punch
        yield return Tap("ATTACK");
        float punch = (float)typeof(HeroController).GetField("punchTimer", F).GetValue(hero);
        Check($"ATTACK punches (punch timer {punch:0.00})", punch > 0f);
        yield return new WaitForSeconds(0.3f);
        yield return Tap("ATTACK");
        yield return new WaitForSeconds(0.3f);
        yield return Tap("ATTACK");
        Check($"third ATTACK kicks (kicking {hero.Kicking})", hero.Kicking);
        yield return new WaitForSeconds(1.2f);

        // ---- JUMP on the ground
        stateP.SetValue(hero, HeroController.State.Ground);
        yield return new WaitForSeconds(0.3f);
        yield return Tap("JUMP");
        yield return new WaitForSeconds(0.15f);
        Check($"JUMP jumps (state {hero.Current}, vy {hero.Velocity.y:0.0})", hero.Current == HeroController.State.Air || hero.Velocity.y > 1f);
        yield return new WaitForSeconds(1.5f);

        // ---- FLY / UP / DOWN / LAND
        yield return Tap("FLY");
        yield return new WaitForSeconds(0.8f);
        Check($"FLY takes off (state {hero.Current})", hero.Current == HeroController.State.Fly);
        float y0 = hero.transform.position.y;
        yield return Hold("UP", 1.5f);
        Check($"UP climbs ({hero.transform.position.y - y0:0.0} m)", hero.transform.position.y - y0 > 3f);
        y0 = hero.transform.position.y;
        yield return Hold("DOWN", 1.0f);
        Check($"DOWN descends ({hero.transform.position.y - y0:0.0} m)", hero.transform.position.y - y0 < -2f);

        // ---- the camera swings round behind where he flies (joystick to the right, no dragging)
        var yawF = typeof(ThirdPersonCamera).GetField("yaw", F);
        var tpc = cam.GetComponent<ThirdPersonCamera>();
        float yaw0 = (float)yawF.GetValue(tpc);
        yield return Drag(3, new Vector2(Screen.width * 0.15f, Screen.height * 0.3f), new Vector2(Screen.height * 0.12f, 0f), 2.5f);
        float yaw1 = (float)yawF.GetValue(tpc);
        Vector3 v = hero.Velocity; float heading = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
        Check($"camera follows the way he goes (yaw {yaw0:0} -> {yaw1:0})", Mathf.Abs(Mathf.DeltaAngle(yaw0, yaw1)) > 30f);

        // ---- LASER hold
        yield return Hold("LASER", 0.6f, () => Check($"LASER fires (blasting {hero.Blasting})", hero.Blasting));

        // ---- LAND
        yield return Tap("LAND");
        yield return new WaitForSeconds(0.3f);
        Check($"LAND stops flying (state {hero.Current})", hero.Current != HeroController.State.Fly);

        // ---- buttons on screen: how many, picture
        yield return new WaitForSeconds(2.5f);
        var visible = tc.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t => t.text).ToArray();
        Log($"buttons showing on the ground: {string.Join(", ", visible)}");
        var cv = tc.GetComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceCamera; cv.worldCamera = cam; cv.planeDistance = 1.5f;
        yield return new WaitForSeconds(0.3f);
        Shot("controls_ground");

        Log($"DONE fails={fails} errors={errors}");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(errors == 0 && fails == 0 ? 0 : 2);
#endif
    }

    Vector2 ButtonPos(string label)
    {
        var t = tc.GetComponentsInChildren<UnityEngine.UI.Text>().FirstOrDefault(x => x.text == label);
        return t ? RectTransformUtility.WorldToScreenPoint(null, t.transform.parent.position) : new Vector2(-999, -999);
    }

    void Touch(int id, UnityEngine.InputSystem.TouchPhase phase, Vector2 pos)
    {
        InputSystem.QueueStateEvent(ts, new TouchState { touchId = id, phase = phase, position = pos });
        InputSystem.Update();
    }

    IEnumerator Tap(string label)
    {
        Vector2 p = ButtonPos(label);
        if (p.x < -900) { Check($"button {label} on screen", false); yield break; }
        Touch(1, UnityEngine.InputSystem.TouchPhase.Began, p);
        yield return null;
        yield return null;
        Touch(1, UnityEngine.InputSystem.TouchPhase.Ended, p);
        yield return null;
    }

    IEnumerator Hold(string label, float seconds, System.Action during = null)
    {
        Vector2 p = ButtonPos(label);
        if (p.x < -900) { Check($"button {label} on screen", false); yield break; }
        Touch(1, UnityEngine.InputSystem.TouchPhase.Began, p);
        for (float t = 0; t < seconds; t += Time.deltaTime) { Touch(1, UnityEngine.InputSystem.TouchPhase.Stationary, p); yield return null; }
        during?.Invoke();
        Touch(1, UnityEngine.InputSystem.TouchPhase.Ended, p);
        yield return null;
    }

    IEnumerator Drag(int id, Vector2 from, Vector2 offset, float seconds)
    {
        Touch(id, UnityEngine.InputSystem.TouchPhase.Began, from);
        for (float t = 0; t < seconds; t += Time.deltaTime)
        {
            Touch(id, UnityEngine.InputSystem.TouchPhase.Moved, from + offset * Mathf.Clamp01(t / 0.15f));
            yield return null;
        }
        Touch(id, UnityEngine.InputSystem.TouchPhase.Ended, from + offset);
        yield return null;
    }

    void Check(string what, bool ok) { if (!ok) fails++; Log($"{(ok ? "OK  " : "FAIL")} {what}"); }

    void Shot(string name)
    {
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
        cam.targetTexture = null; RenderTexture.active = null;
        File.WriteAllBytes($"Screenshots/mobile/{name}.png", tex.EncodeToPNG());
        Destroy(rt); Destroy(tex);
    }

    static void Log(string s) => Debug.Log("[MobileTest] " + s);
}
