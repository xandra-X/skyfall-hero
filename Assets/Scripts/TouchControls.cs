using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

/// <summary>
/// On-screen controls for iPhone, OverPeak style (also usable in the editor: menu OverPeak > Touch Controls In Editor).
///   left thumb : joystick (appears where you touch). Push it to the edge = boost; keep it there on the ground = super speed
///   right side : drag anywhere to look around and aim (the laser goes to the crosshair in the middle).
///                Hold LASER and slide that finger to aim while firing. Not dragging: camera swings behind where you go
///   ATTACK     : tap = punch, punch, kick combo   (THROW while carrying something)
///   LASER      : hold = heat vision
///   JUMP       : jump, double-tap = fly; while flying hold = up
///   FLY / LAND : take off / land
///   DOWN       : only while flying
///   GRAB / DROP: only shows when there's something to pick up (or you're carrying it)
/// Runs before everything else each frame so the hero sees the presses in the same frame. Built in code: no prefab needed.
/// </summary>
[DefaultExecutionOrder(-500)]
public class TouchControls : MonoBehaviour
{
    class Btn
    {
        public string id;
        public RectTransform rt;
        public Image img, ring;
        public Text label;
        public Color color;
        public int touch = -1;
        public bool visible = true, pressed;
    }

    const float StickRadius = 130f;
    readonly List<Btn> buttons = new List<Btn>();
    readonly HashSet<int> seen = new HashSet<int>();
    readonly HashSet<int> known = new HashSet<int>();
    readonly Dictionary<GameInput.Btn, int> pulses = new Dictionary<GameInput.Btn, int>();
    RectTransform safe, stickBase, stickKnob;
    Canvas canvas;
    int stickTouch = -1, lookTouch = -1;
    Vector2 stickOrigin, lookLast, stickHome, laserLast;
    HeroController hero;
    HeroGrab grab;
    Btn attack, laser, jump, fly, down, grabBtn;
    int combo;
    float lastAttack = -10f, farSince = -1f, idleSince = -1f, nextGrabCheck;
    bool farPush, somethingToGrab;
    static Sprite circle, ring;

    public const string EditorPrefKey = "OverPeak.TouchControlsInEditor";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Application.isMobilePlatform)
        {
            Application.targetFrameRate = 60;          // iOS defaults to 30 fps
            QualitySettings.vSyncCount = 0;
        }
        bool want = Application.isMobilePlatform;
#if UNITY_EDITOR
        want |= UnityEditor.EditorPrefs.GetBool(EditorPrefKey, false);
#endif
        if (!want || FindAnyObjectByType<HeroController>() == null || FindAnyObjectByType<TouchControls>()) return;
        new GameObject("TouchControls").AddComponent<TouchControls>();
    }

    void Awake()
    {
        EnhancedTouchSupport.Enable();
#if UNITY_EDITOR
        TouchSimulation.Enable();                       // click the buttons with the mouse in the Game view
#endif
        hero = FindAnyObjectByType<HeroController>();
        grab = hero ? hero.GetComponent<HeroGrab>() : null;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = !Application.isMobilePlatform;
        GameInput.TouchActive = true;
        Build();
    }

    void OnDestroy()
    {
        GameInput.TouchActive = false;
        GameInput.TouchMove = Vector2.zero;
        GameInput.TouchLookDegrees = Vector2.zero;
        foreach (GameInput.Btn b in System.Enum.GetValues(typeof(GameInput.Btn))) GameInput.SetTouch(b, false);
    }

    // ------------------------------------------------------------------ layout
    void Build()
    {
        circle ??= MakeCircle(false);
        ring ??= MakeCircle(true);
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(2340f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        safe = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
        safe.SetParent(transform, false);
        ApplySafeArea();

        stickHome = new Vector2(330f, 300f);
        stickBase = MakeImage("StickBase", ring, new Color(1f, 1f, 1f, 0.3f), StickRadius * 2f, Vector2.zero, stickHome);
        stickKnob = MakeImage("StickKnob", circle, new Color(1f, 1f, 1f, 0.45f), 120f, Vector2.zero, stickHome);

        Color dark = new Color(0.05f, 0.07f, 0.12f, 0.45f);
        attack = Add("ATTACK", new Vector2(-250, 250), 120, new Color(0.95f, 0.75f, 0.2f, 0.5f));
        laser = Add("LASER", new Vector2(-530, 170), 85, new Color(1f, 0.25f, 0.2f, 0.5f));
        jump = Add("JUMP", new Vector2(-200, 520), 85, new Color(0.3f, 0.6f, 1f, 0.5f));
        down = Add("DOWN", new Vector2(-420, 560), 65, dark);
        grabBtn = Add("GRAB", new Vector2(-500, 390), 68, new Color(0.3f, 0.9f, 0.45f, 0.5f));
        fly = Add("FLY", new Vector2(-150, 720), 58, dark);
    }

    void ApplySafeArea()
    {
        Rect sa = Screen.safeArea;
        safe.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
        safe.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
        safe.offsetMin = safe.offsetMax = Vector2.zero;
    }

    Btn Add(string text, Vector2 fromBottomRight, float radius, Color color)
    {
        var rt = MakeImage(text, circle, color, radius * 2f, new Vector2(1f, 0f), fromBottomRight);
        var edge = MakeImage("Ring", ring, new Color(1f, 1f, 1f, 0.7f), radius * 2f, new Vector2(0.5f, 0.5f), Vector2.zero, rt);
        var label = new GameObject("Label", typeof(RectTransform)).AddComponent<Text>();
        label.rectTransform.SetParent(rt, false);
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = Mathf.RoundToInt(Mathf.Clamp(radius * 0.33f, 20f, 38f));
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(1f, 1f, 1f, 0.95f);
        label.text = text;
        label.raycastTarget = false;
        var outline = label.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.6f);
        var b = new Btn { id = text, rt = rt, img = rt.GetComponent<Image>(), ring = edge.GetComponent<Image>(), label = label, color = color };
        buttons.Add(b);
        return b;
    }

    RectTransform MakeImage(string name, Sprite sprite, Color color, float size, Vector2 anchor, Vector2 pos, Transform parent = null)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent ? parent : safe, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = pos;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return rt;
    }

    static Sprite MakeCircle(bool ringOnly)
    {
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                float a = Mathf.Clamp01((1f - d) * n / 2f);
                if (ringOnly) a *= Mathf.Clamp01((d - 0.9f) * n / 2f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
    }

    // ------------------------------------------------------------------ input
    void Pulse(GameInput.Btn b) => pulses[b] = 2;           // pressed this frame, released next

    void Update()
    {
        GameInput.TouchActive = true;
        if (Time.frameCount % 30 == 0) ApplySafeArea();
        float scale = canvas.scaleFactor;
        Vector2 look = Vector2.zero;
        seen.Clear();
        foreach (var b in buttons) b.pressed = false;

        foreach (var t in ETouch.activeTouches)
        {
            int id = t.touchId;
            Vector2 pos = t.screenPosition;
            bool ended = t.phase == UnityEngine.InputSystem.TouchPhase.Ended || t.phase == UnityEngine.InputSystem.TouchPhase.Canceled;
            if (!ended) seen.Add(id);

            // a finger we haven't met yet is a new touch, whatever its phase says (a quick tap can begin and end
            // between two frames, so "Began" is easily missed): decide what it is from where it first touched
            if (known.Add(id))
            {
                Vector2 start = t.startScreenPosition;
                var hit = HitButton(start, scale);
                if (hit != null) { hit.touch = ended ? -1 : id; hit.pressed = true; }
                else if (ended) { }
                else if (start.x < Screen.width * 0.45f && stickTouch < 0) { stickTouch = id; stickOrigin = start; }
                else if (lookTouch < 0) { lookTouch = id; lookLast = pos; }
                if (hit == laser && !ended) laserLast = pos;
            }
            if (id == lookTouch && !ended)
            {
                look += (pos - lookLast) / Screen.height * 200f;      // a full-height swipe turns ~200 degrees
                lookLast = pos;
            }
            if (id == laser.touch && !ended)
            {
                look += (pos - laserLast) / Screen.height * 160f;     // slide the finger on LASER to aim the beam
                laserLast = pos;
            }
        }
        known.IntersectWith(seen);                                     // forget fingers that lifted
        foreach (var b in buttons) if (b.touch >= 0 && !seen.Contains(b.touch)) b.touch = -1;
        if (stickTouch >= 0 && !seen.Contains(stickTouch)) stickTouch = -1;
        if (lookTouch >= 0 && !seen.Contains(lookTouch)) lookTouch = -1;

        // ---- joystick
        Vector2 move = Vector2.zero;
        if (stickTouch >= 0)
        {
            Vector2 cur = stickOrigin;
            foreach (var t in ETouch.activeTouches) if (t.touchId == stickTouch) cur = t.screenPosition;
            Vector2 d = (cur - stickOrigin) / scale;
            move = Vector2.ClampMagnitude(d / StickRadius, 1f);
            float far = d.magnitude / StickRadius;
            farPush = farPush ? far > 1.05f : far > 1.3f;          // a little hysteresis so it doesn't flicker
            stickBase.position = stickOrigin;
            stickKnob.position = stickOrigin + Vector2.ClampMagnitude(d, StickRadius * 1.3f) * scale;
        }
        else
        {
            farPush = false;
            stickBase.anchoredPosition = stickKnob.anchoredPosition = stickHome;
        }
        GameInput.TouchMove = move;
        GameInput.TouchLookDegrees = look;

        bool flying = hero && hero.Current == HeroController.State.Fly;
        bool onGround = hero && (hero.Current == HeroController.State.Ground);
        bool carrying = grab && grab.Held;

        // push to the edge: boost (flying) / sprint, and kept there on the ground switches on super speed
        if (farPush) { if (farSince < 0f) farSince = Time.time; } else farSince = -1f;
        if (move.sqrMagnitude < 0.02f) { if (idleSince < 0f) idleSince = Time.time; } else idleSince = -1f;
        if (hero && onGround && farSince > 0f && Time.time - farSince > 0.5f && !hero.SuperMode) Pulse(GameInput.Btn.Super);
        if (hero && hero.SuperMode && idleSince > 0f && Time.time - idleSince > 0.6f) Pulse(GameInput.Btn.Super);

        // ---- buttons
        if (attack.pressed)
        {
            if (carrying) Pulse(GameInput.Btn.Fire);                 // throw
            else
            {
                combo = Time.time - lastAttack < 0.9f ? combo + 1 : 0;
                lastAttack = Time.time;
                Pulse(combo % 3 == 2 ? GameInput.Btn.Kick : GameInput.Btn.Punch);
            }
        }
        if (fly.pressed) Pulse(GameInput.Btn.Fly);
        if (grabBtn.pressed) Pulse(GameInput.Btn.Grab);

        Set(GameInput.Btn.Jump, jump.touch >= 0 || jump.pressed, true);
        Set(GameInput.Btn.Descend, (down.touch >= 0 || down.pressed) && down.visible, true);
        Set(GameInput.Btn.Fire, (laser.touch >= 0 || laser.pressed) && laser.visible, true);
        Set(GameInput.Btn.Boost, farPush, false);                     // held only: never counts as a double-tap
        Set(GameInput.Btn.Punch, false, true);
        Set(GameInput.Btn.Kick, false, true);
        Set(GameInput.Btn.Fly, false, true);
        Set(GameInput.Btn.Grab, false, true);
        Set(GameInput.Btn.Super, false, true);
        var keys = new List<GameInput.Btn>(pulses.Keys);
        foreach (var k in keys) if (--pulses[k] <= 0) pulses.Remove(k);

        // ---- what shows right now
        if (grab && Time.time > nextGrabCheck) { nextGrabCheck = Time.time + 0.2f; somethingToGrab = grab.HasTargetNearby(); }
        down.visible = flying;
        laser.visible = !carrying;
        grabBtn.visible = carrying || somethingToGrab;
        attack.label.text = carrying ? "THROW" : "ATTACK";
        jump.label.text = flying ? "UP" : "JUMP";
        fly.label.text = flying ? "LAND" : "FLY";
        grabBtn.label.text = carrying ? "DROP" : "GRAB";

        foreach (var b in buttons)
        {
            b.rt.gameObject.SetActive(b.visible);
            if (!b.visible) { b.touch = -1; continue; }
            bool held = b.touch >= 0;
            b.img.color = held ? new Color(b.color.r, b.color.g, b.color.b, Mathf.Min(1f, b.color.a + 0.3f)) : b.color;
            b.rt.localScale = Vector3.one * (held ? 0.9f : 1f);
        }
        stickBase.GetComponent<Image>().color = new Color(1f, farPush ? 0.85f : 1f, farPush ? 0.35f : 1f, stickTouch >= 0 ? 0.55f : 0.3f);
    }

    /// Held by a finger, or pulsed this frame. Pulses (taps) also count as a fresh press.
    void Set(GameInput.Btn b, bool held, bool countsAsPress)
    {
        bool pulsed = pulses.ContainsKey(b) && pulses[b] == 2;
        GameInput.SetTouch(b, held || pulsed, countsAsPress);
    }

    Btn HitButton(Vector2 screenPos, float scale)
    {
        Btn best = null; float bestD = float.MaxValue;
        foreach (var b in buttons)
        {
            if (!b.visible) continue;
            Vector2 c = RectTransformUtility.WorldToScreenPoint(null, b.rt.position);
            float r = b.rt.rect.width * 0.5f * scale * 1.2f;          // a little extra so it's easy to hit
            float d = Vector2.Distance(screenPos, c);
            if (d < r && d < bestD) { best = b; bestD = d; }
        }
        return best;
    }
}
