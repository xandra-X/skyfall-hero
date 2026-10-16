using System.Collections.Generic;
using UnityEngine;

public enum HitKind { Punch, Laser, Impact, Explosion }

/// <summary>Anything the hero's punches, heat vision, ramming and explosions can hurt (people, cars, planes).</summary>
public interface IHittable
{
    /// velocity: how hard / which way the hit throws it. damage: for heat vision this is damage per frame.
    void Hit(Vector3 point, Vector3 velocity, float damage, HitKind kind);
}

/// <summary>Something the hero can pick up (G), hold overhead and throw (left mouse).</summary>
public abstract class Grabbable : MonoBehaviour
{
    public bool Held { get; private set; }
    public virtual bool CanGrab => true;
    /// Where the object sits relative to the hold point above the hero's head.
    public abstract Vector3 HoldOffset { get; }
    public abstract float ThrowSpeed { get; }

    Transform holdPoint;
    float settle;
    Vector3 restOffset;

    /// Lowest point of all the visible parts, in this object's space turned by `rot`.
    float LowestPoint(Quaternion rot)
    {
        float min = float.MaxValue;
        var toLocal = transform.worldToLocalMatrix;
        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            if (!mf.sharedMesh) continue;
            var b = mf.sharedMesh.bounds;
            var m = toLocal * mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                min = Mathf.Min(min, (rot * m.MultiplyPoint3x4(c)).y);
            }
        }
        return min == float.MaxValue ? 0f : min;
    }

    public void Grab(Transform point)
    {
        Held = true;
        holdPoint = point;
        settle = 0f;
        // rest it right on the hands: its lowest point (in the held orientation) sits on the hold point
        Vector3 off = HoldOffset;
        restOffset = new Vector3(off.x, 0.03f - LowestPoint(HoldRotation), off.z);
        OnGrabbed();
        transform.SetParent(point, true);
    }

    public void Throw(Vector3 velocity)
    {
        if (!Held) return;
        Held = false;
        transform.SetParent(null, true);
        holdPoint = null;
        OnThrown(velocity);
    }

    /// Put it down gently in front of the hero.
    public void Drop(Vector3 groundPoint)
    {
        if (!Held) return;
        Held = false;
        transform.SetParent(null, true);
        holdPoint = null;
        OnDropped(groundPoint);
    }

    /// It was destroyed / killed while being held: the hero lets go.
    protected void ForceRelease()
    {
        Held = false;
        holdPoint = null;
    }

    protected virtual void LateUpdate()
    {
        if (!Held || !holdPoint) return;
        // glide into the hands, then stay there
        settle = Mathf.Min(1f, settle + Time.deltaTime * 6f);
        transform.localPosition = Vector3.Lerp(transform.localPosition, restOffset, settle);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, HoldRotation, settle);
    }

    protected virtual Quaternion HoldRotation => Quaternion.identity;
    protected abstract void OnGrabbed();
    protected abstract void OnThrown(Vector3 velocity);
    protected abstract void OnDropped(Vector3 groundPoint);
}

/// <summary>Shared materials so hundreds of people and cars stay cheap to draw.</summary>
public static class Palette
{
    static readonly Dictionary<Color, Material> lit = new Dictionary<Color, Material>();
    static readonly Dictionary<Color, Material> glow = new Dictionary<Color, Material>();

    public static Material Lit(Color c, float smooth = 0.25f)
    {
        if (lit.TryGetValue(c, out var m) && m) return m;
        m = FxUtil.Lit(c, smooth);
        m.enableInstancing = true;             // hundreds of people / cars share these: draw them in batches
        lit[c] = m;
        return m;
    }

    public static Material Glow(Color c)
    {
        if (glow.TryGetValue(c, out var m) && m) return m;
        m = FxUtil.Lit(c * 0.3f, 0.5f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * 3f);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        glow[c] = m;
        return m;
    }

    static Material charred;
    public static Material Charred => charred ? charred : charred = FxUtil.Lit(new Color(0.06f, 0.05f, 0.045f), 0.05f);

    /// A part made from a primitive: no collider, given material, parented.
    public static Transform Part(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Material mat, string name, Quaternion? rot = null)
    {
        var g = GameObject.CreatePrimitive(type);
        g.name = name;
        Object.DestroyImmediate(g.GetComponent<Collider>());   // the body uses its own colliders
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localRotation = rot ?? Quaternion.identity;
        g.transform.localScale = scale;
        g.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return g.transform;
    }

    public static Transform Pivot(Transform parent, Vector3 localPos, string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = localPos;
        return t;
    }
}
