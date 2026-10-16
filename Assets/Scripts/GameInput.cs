using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// All hero controls in one place: keyboard + mouse on Mac, on-screen buttons on iPhone (TouchControls).
/// Both work at the same time, so the touch buttons can also be tried in the editor.
/// </summary>
public static class GameInput
{
    public enum Btn { Jump, Descend, Fly, Boost, Super, Fire, Punch, Kick, Grab, Walk }
    const int Count = 10;

    // written by TouchControls every frame
    public static bool TouchActive;
    public static Vector2 TouchMove;            // virtual stick, -1..1
    public static Vector2 TouchLookDegrees;     // camera turn from dragging this frame (yaw, pitch)
    static readonly bool[] touchHeld = new bool[Count];
    static readonly int[] touchDownFrame = new int[Count];

    /// countsAsPress = false: holding it never counts as a fresh press (so it can't trigger double-taps).
    public static void SetTouch(Btn b, bool held, bool countsAsPress = true)
    {
        if (held && !touchHeld[(int)b] && countsAsPress) touchDownFrame[(int)b] = Time.frameCount;
        touchHeld[(int)b] = held;
    }

    static bool MouseLooking => Cursor.lockState == CursorLockMode.Locked;

    public static Vector2 Move
    {
        get
        {
            var kb = Keyboard.current;
            Vector2 m = TouchMove;
            if (kb != null)
                m += new Vector2((kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0), (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0));
            var pad = Gamepad.current;
            if (pad != null) m += pad.leftStick.ReadValue();
            return m.sqrMagnitude > 1f ? m.normalized : m;
        }
    }

    /// Camera turn this frame in degrees (yaw, pitch).
    public static Vector2 LookDegrees(float mouseSensitivity)
    {
        Vector2 d = TouchLookDegrees;
        var mouse = Mouse.current;
        if (mouse != null && MouseLooking) d += mouse.delta.ReadValue() * mouseSensitivity;
        var pad = Gamepad.current;
        if (pad != null) d += pad.rightStick.ReadValue() * new Vector2(180f, -120f) * Time.deltaTime;
        return d;
    }

    public static bool Held(Btn b)
    {
        if (touchHeld[(int)b]) return true;
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        switch (b)
        {
            case Btn.Jump: return kb != null && kb.spaceKey.isPressed;
            case Btn.Descend: return kb != null && (kb.leftCtrlKey.isPressed || kb.cKey.isPressed);
            case Btn.Fly: return kb != null && kb.fKey.isPressed;
            case Btn.Boost: return kb != null && kb.leftShiftKey.isPressed;
            case Btn.Super: return kb != null && kb.rKey.isPressed;
            case Btn.Fire: return mouse != null && mouse.leftButton.isPressed && MouseLooking;
            case Btn.Punch: return kb != null && kb.qKey.isPressed;
            case Btn.Kick: return kb != null && kb.eKey.isPressed;
            case Btn.Grab: return kb != null && kb.gKey.isPressed;
            case Btn.Walk: return kb != null && kb.leftAltKey.isPressed;
        }
        return false;
    }

    /// Pressed this frame.
    public static bool Down(Btn b)
    {
        if (touchHeld[(int)b] && touchDownFrame[(int)b] == Time.frameCount) return true;
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        switch (b)
        {
            case Btn.Jump: return kb != null && kb.spaceKey.wasPressedThisFrame;
            case Btn.Descend: return kb != null && (kb.leftCtrlKey.wasPressedThisFrame || kb.cKey.wasPressedThisFrame);
            case Btn.Fly: return kb != null && kb.fKey.wasPressedThisFrame;
            case Btn.Boost: return kb != null && kb.leftShiftKey.wasPressedThisFrame;
            case Btn.Super: return kb != null && kb.rKey.wasPressedThisFrame;
            case Btn.Fire: return mouse != null && mouse.leftButton.wasPressedThisFrame && MouseLooking;
            case Btn.Punch: return kb != null && kb.qKey.wasPressedThisFrame;
            case Btn.Kick: return kb != null && kb.eKey.wasPressedThisFrame;
            case Btn.Grab: return kb != null && kb.gKey.wasPressedThisFrame;
            case Btn.Walk: return kb != null && kb.leftAltKey.wasPressedThisFrame;
        }
        return false;
    }
}
