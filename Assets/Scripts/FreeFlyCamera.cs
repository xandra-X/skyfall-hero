using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Test camera for flying around the city on Mac.
/// Hold right mouse to look, WASD to move, E/Space up, Q/Ctrl down, Shift = fast, scroll = change speed.
/// </summary>
public class FreeFlyCamera : MonoBehaviour
{
    public float speed = 20f;
    public float fastMultiplier = 4f;
    public float lookSensitivity = 0.15f;

    float yaw, pitch;
    float fps;

    void Start()
    {
        var e = transform.eulerAngles;
        yaw = e.y;
        pitch = e.x > 180f ? e.x - 360f : e.x;
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null) return;

        if (mouse.rightButton.isPressed)
        {
            Vector2 d = mouse.delta.ReadValue() * lookSensitivity;
            yaw += d.x;
            pitch = Mathf.Clamp(pitch - d.y, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        float scroll = mouse.scroll.ReadValue().y;
        if (scroll != 0f) speed = Mathf.Clamp(speed * (scroll > 0 ? 1.2f : 1f / 1.2f), 1f, 2000f);

        Vector3 move = Vector3.zero;
        if (kb.wKey.isPressed) move += transform.forward;
        if (kb.sKey.isPressed) move -= transform.forward;
        if (kb.dKey.isPressed) move += transform.right;
        if (kb.aKey.isPressed) move -= transform.right;
        if (kb.eKey.isPressed || kb.spaceKey.isPressed) move += Vector3.up;
        if (kb.qKey.isPressed || kb.leftCtrlKey.isPressed) move -= Vector3.up;

        float s = speed * (kb.leftShiftKey.isPressed ? fastMultiplier : 1f);
        transform.position += move.normalized * s * Time.deltaTime;

        fps = Mathf.Lerp(fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f), 0.05f);
    }

    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 600, 22),
            $"FPS {fps:0}   Height {transform.position.y:0} m   Speed {speed:0} m/s");
        GUI.Label(new Rect(10, 30, 600, 22),
            "Right mouse = look | WASD = move | E/Space up | Q/Ctrl down | Shift fast | Scroll speed");
    }
}
