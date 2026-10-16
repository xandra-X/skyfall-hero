using UnityEngine;

/// <summary>On-screen crosshair and the damage bar of the building being hit.</summary>
public class HeroHUD : MonoBehaviour
{
    HeroController hero;
    GUIStyle label;

    void Start() => hero = GetComponent<HeroController>();

    void OnGUI()
    {
        if (!hero) return;
        label ??= new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        float scale = Mathf.Max(1f, Screen.height / 900f);
        label.fontSize = Mathf.RoundToInt(13 * scale);
        DrawCrosshair(scale);
        DrawIntegrity(scale);
    }

    void DrawCrosshair(float scale)
    {
        if (Cursor.lockState != CursorLockMode.Locked && !GameInput.TouchActive && !hero.Blasting) return;
        Color c = hero.Blasting ? new Color(1f, 0.25f, 0.15f, 0.95f) : new Color(1f, 1f, 1f, 0.75f);
        float cx = Screen.width / 2f, cy = Screen.height / 2f, len = 7f * scale, gap = 5f * scale, t = Mathf.Max(1f, 1.5f * scale);
        Draw(new Rect(cx - gap - len, cy - t / 2, len, t), c);
        Draw(new Rect(cx + gap, cy - t / 2, len, t), c);
        Draw(new Rect(cx - t / 2, cy - gap - len, t, len), c);
        Draw(new Rect(cx - t / 2, cy + gap, t, len), c);
        Draw(new Rect(cx - t / 2, cy - t / 2, t, t), c);
    }

    void DrawIntegrity(float scale)
    {
        var ds = DamageSystem.Instance;
        if (!ds || !ds.LastDamaged || Time.time - ds.LastDamageTime > 3f) return;
        var d = ds.LastDamaged;
        float w = 300f * scale, h = 10f * scale;
        var bar = new Rect((Screen.width - w) / 2f, 70f * scale, w, h);
        Draw(new Rect(bar.x - 2, bar.y - 2, bar.width + 4, bar.height + 4), new Color(0f, 0f, 0f, 0.55f));
        Draw(bar, new Color(0.15f, 0.1f, 0.1f, 0.85f));
        Color fill = Color.Lerp(new Color(1f, 0.15f, 0.1f), new Color(1f, 0.85f, 0.3f), d.Health01);
        Draw(new Rect(bar.x, bar.y, bar.width * d.Health01, bar.height), fill);
        string text = d.Collapsing ? (Mathf.PingPong(Time.time * 4f, 1f) > 0.5f ? "COLLAPSING!" : "") : $"STRUCTURE  {d.Health01 * 100f:0}%";
        GUI.Label(new Rect(bar.x, bar.y - 22 * scale, bar.width, 20 * scale), text, label);
    }

    static void Draw(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
