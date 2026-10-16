using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Orbit camera that follows the hero. Mouse looks around (click the Game view to capture the mouse,
/// Esc to release). Pulls back when flying fast and never goes through buildings.
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    public HeroController hero;
    public float pivotHeight = 1.6f;
    public float distance = 4.5f;
    public float flyDistance = 8f;
    public float shoulderOffset = 0.5f;
    public float sensitivity = 0.12f;
    public float minPitch = -70f, maxPitch = 80f;
    public float collisionRadius = 0.25f;
    public LayerMask collisionMask = ~(1 << 2);   // everything except "Ignore Raycast" (the hero)

    public float baseFov = 60f, speedFov = 82f;

    float yaw, pitch = 12f, currentDistance;
    Camera cam;
    static float shakeAmount, shakeTime, shakeDuration = 1f;

    /// Called by effects (landings, explosions).
    public static void Shake(float amount, float duration)
    {
        shakeAmount = Mathf.Max(shakeAmount * (shakeTime / shakeDuration), amount);
        shakeTime = shakeDuration = duration;
    }
    Vector3 pivotVelocity;
    Vector3 smoothPivot;

    void Start()
    {
        if (hero) { yaw = hero.transform.eulerAngles.y; smoothPivot = hero.transform.position + Vector3.up * pivotHeight; }
        currentDistance = distance;
        cam = GetComponent<Camera>();
        collisionMask &= ~(1 << 3);          // ragdoll bodies don't push the camera around
        LockCursor(true);
    }

    void LateUpdate()
    {
        if (!hero) return;
        var mouse = Mouse.current;
        var kb = Keyboard.current;

        if (kb != null && kb.escapeKey.wasPressedThisFrame) LockCursor(false);
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked && !GameInput.TouchActive) LockCursor(true);

        Vector2 d = GameInput.LookDegrees(sensitivity);      // mouse, touch drag or gamepad
        yaw += d.x;
        pitch = Mathf.Clamp(pitch - d.y, minPitch, maxPitch);
        if (d.sqrMagnitude > 0.0001f) lastLook = Time.time;
        AutoFollow();

        Vector3 pivot = hero.transform.position + Vector3.up * pivotHeight;
        smoothPivot = Vector3.SmoothDamp(smoothPivot, pivot, ref pivotVelocity, hero.IsFlyingFast ? 0.02f : 0.06f);
        // at space speeds (thousands of m/s) smoothing alone would leave him far ahead: never trail by more than a little
        Vector3 lag = smoothPivot - pivot;
        bool phone = GameInput.TouchActive;
        float maxLag = phone ? 0.5f : 1.5f;                  // phone: he stays right in the middle of the small screen
        if (lag.sqrMagnitude > maxLag * maxLag) { smoothPivot = pivot + lag.normalized * maxLag; pivotVelocity = hero.Velocity; }

        float flyDist = phone ? 5.5f : flyDistance;
        float want = hero.Current == HeroController.State.Fly
            ? Mathf.Lerp(distance, flyDist, Mathf.InverseLerp(0f, hero.flySpeed, hero.Velocity.magnitude))
            : distance;

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 shoulder = rot * Vector3.right * shoulderOffset;
        Vector3 back = rot * Vector3.back;

        // keep the camera out of walls: cast from the pivot toward the wanted position
        float allowed = want;
        if (Physics.SphereCast(smoothPivot + shoulder, collisionRadius, back, out RaycastHit hit, want, collisionMask, QueryTriggerInteraction.Ignore))
            allowed = Mathf.Max(0.5f, hit.distance - 0.1f);
        currentDistance = allowed < currentDistance ? allowed : Mathf.Lerp(currentDistance, allowed, 1f - Mathf.Exp(-4f * Time.deltaTime));

        transform.position = smoothPivot + shoulder + back * currentDistance;
        transform.rotation = rot;

        // wider view at high speed
        float fast = Mathf.Max(hero.SuperSpeed01,
            hero.Current == HeroController.State.Fly ? Mathf.InverseLerp(hero.flySpeed, hero.boostSpeed, hero.Velocity.magnitude) : 0f);
        float fovFast = phone ? Mathf.Min(speedFov, 70f) : speedFov;    // a wide lens shrinks him to a dot on a phone
        if (cam) cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, Mathf.Lerp(baseFov, fovFast, fast), 1f - Mathf.Exp(-4f * Time.deltaTime));

        // fast flight: camera leans with his bank and rumbles in the wind
        transform.rotation *= Quaternion.Euler(0f, 0f, hero.Bank * (phone ? 0.12f : 0.3f));
        float rumble = Mathf.Max(hero.Turbulence01, hero.SuperSpeed01 * 0.5f) * (phone ? 0.025f : 0.06f);
        if (rumble > 0.001f)
        {
            float rt = Time.time * 22f;
            transform.position += transform.rotation * new Vector3(Mathf.PerlinNoise(rt, 3f) - 0.5f, Mathf.PerlinNoise(3f, rt) - 0.5f, 0f) * rumble;
        }

        // shake (fades out)
        if (shakeTime > 0f)
        {
            shakeTime -= Time.deltaTime;
            float a = shakeAmount * Mathf.Clamp01(shakeTime / shakeDuration);
            float t = Time.time * 35f;
            transform.position += transform.rotation * new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f, Mathf.PerlinNoise(0f, t) - 0.5f, 0f) * a;
            transform.rotation *= Quaternion.Euler((Mathf.PerlinNoise(t, 7f) - 0.5f) * a * 4f, 0f, (Mathf.PerlinNoise(7f, t) - 0.5f) * a * 3f);
        }
    }


    /// On the phone (OverPeak style): when you aren't dragging the view, the camera swings round behind the
    /// way he is running or flying, so the joystick steers and you always see where you're going.
    float lastLook = -10f;

    /// On the phone: when you aren't dragging the view, the camera swings round behind the way he is running or
    /// flying, so the joystick steers and you always see where you're going.
    void AutoFollow()
    {
        if (!GameInput.TouchActive || hero.Blasting || Time.time - lastLook < 1.2f) return;
        Vector3 v = hero.Velocity;
        Vector3 flat = new Vector3(v.x, 0f, v.z);
        float speed = flat.magnitude;
        if (speed < 1.5f) return;
        float target = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
        float delta = Mathf.DeltaAngle(yaw, target);
        if (Mathf.Abs(delta) > 160f) return;                     // running straight at the camera: don't spin round
        float rate = Mathf.Lerp(1.2f, 3.5f, Mathf.InverseLerp(2f, 40f, speed));
        yaw += delta * (1f - Mathf.Exp(-rate * Time.deltaTime));
    }

    static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    void OnGUI()
    {
        if (!hero || GameInput.TouchActive) return;          // keyboard help isn't for the phone
        GUI.Label(new Rect(10, 10, 900, 22),
            $"{hero.Current}{(hero.SuperMode ? "   SUPER SPEED" : "")}   Speed {hero.Velocity.magnitude:0} m/s   Height {hero.transform.position.y:0} m   FPS {1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f):0}");
        GUI.Label(new Rect(10, 30, 900, 22),
            "R or double Shift = super speed · WASD move · Shift sprint/boost · Alt walk · Space jump (in flight: up) · F or double Space = fly · Ctrl/C down · Hold left mouse = heat vision · Q punch · E kick · Esc = free mouse");
    }
}
