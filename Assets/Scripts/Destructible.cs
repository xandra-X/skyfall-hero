using UnityEngine;

/// <summary>A building that can be holed and collapsed. Added by OverPeak > Setup Destruction.</summary>
public class Destructible : MonoBehaviour
{
    public float maxHealth = 200f;
    public float Health { get; private set; }
    public float Health01 => maxHealth > 0f ? Mathf.Clamp01(Health / maxHealth) : 0f;
    public bool Collapsing { get; private set; }
    public bool Collapsed { get; private set; }
    public Bounds WorldBounds { get; private set; }
    public Renderer[] Renderers { get; private set; }

    bool ready;

    public void Init()
    {
        if (ready) return;
        ready = true;
        Renderers = GetComponentsInChildren<Renderer>(true);
        RecalculateBounds();
        // bigger buildings take more punishment
        Vector3 s = WorldBounds.size;
        maxHealth = Mathf.Clamp(Mathf.Sqrt(s.x * s.y * s.z) * 2.5f, 160f, 1200f);
        Health = maxHealth;
    }

    public void RecalculateBounds()
    {
        bool any = false;
        var b = new Bounds(transform.position, Vector3.zero);
        foreach (var r in Renderers ?? GetComponentsInChildren<Renderer>(true))
        {
            if (!r) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        WorldBounds = b;
    }

    /// Returns true if this hit made the building collapse.
    public bool Damage(float amount, Vector3 point)
    {
        Init();
        if (Collapsing) return false;
        // buildings only get holes now (they heal); they never collapse
        Health = Mathf.Max(0f, Health - amount);
        var ds = DamageSystem.Instance;
        if (ds) ds.NoteDamage(this);
        return false;
    }

    public void MarkCollapsed() => Collapsed = true;
}
