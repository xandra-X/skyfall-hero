using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// G: pick up the nearest person / car (or plane, when flying near one) and hold it overhead.
/// Left mouse while holding: throw it where the camera aims. G again: put it down.
/// </summary>
[RequireComponent(typeof(HeroController))]
[DefaultExecutionOrder(-50)]          // place the hold point before the held object follows it
public class HeroGrab : MonoBehaviour
{
    public float grabRange = 3.5f;
    public float planeGrabRange = 30f;
    public float releaseDelay = 0.15f;      // when the arms come forward in the throw animation

    public Grabbable Held { get; private set; }

    HeroController hero;
    Transform holdPoint;
    float releaseAt = -1f;

    void Start()
    {
        hero = GetComponent<HeroController>();
    }

    Transform HoldPoint()
    {
        if (holdPoint) return holdPoint;
        holdPoint = new GameObject("HoldPoint").transform;
        holdPoint.SetParent(transform, false);
        holdPoint.localPosition = new Vector3(0f, 2.25f, 0.1f);
        return holdPoint;
    }

    /// The load sits on his hands: overhead when standing, out in front when he flies
    /// (his arms point the way his body points, so it is never behind his back).
    void LateUpdate()
    {
        if (!holdPoint) return;
        var a = hero.animator;
        var lh = a ? a.GetBoneTransform(HumanBodyBones.LeftHand) : null;
        var rh = a ? a.GetBoneTransform(HumanBodyBones.RightHand) : null;
        var hips = a ? a.GetBoneTransform(HumanBodyBones.Hips) : null;
        var head = a ? a.GetBoneTransform(HumanBodyBones.Head) : null;
        if (!lh || !rh || !hips || !head) return;
        Vector3 hands = (lh.position + rh.position) * 0.5f;
        Vector3 bodyUp = (head.position - hips.position).normalized;       // along the spine: up when standing, forward when flying
        // flying (body level): put it out past his fists, not on top of his head
        float level = 1f - Mathf.Abs(bodyUp.y);
        holdPoint.position = hands + bodyUp * (0.9f * level);
        holdPoint.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
    }

    void Update()
    {
        if (Held && !Held.Held) { Held = null; hero.HoldPose = false; }     // it died / exploded in our hands
        var kb = Keyboard.current; var mouse = Mouse.current;
        if (GameInput.Down(GameInput.Btn.Grab))
        {
            if (Held) Drop(); else TryGrab();
        }
        if (Held && releaseAt < 0f && GameInput.Down(GameInput.Btn.Fire))
            StartThrow();
        if (releaseAt > 0f && Time.time >= releaseAt) Release();
    }

    /// Is there anything in reach to pick up right now? (for the on-screen GRAB button)
    public bool HasTargetNearby()
    {
        if (Held) return true;
        Vector3 chest = transform.position + Vector3.up * 1.2f;
        Grabbable best = null; float bestD = float.MaxValue;
        foreach (var c in Physics.OverlapSphere(chest + transform.forward * 1.2f, grabRange + 0.5f, ~0, QueryTriggerInteraction.Ignore))
            Consider(c, chest, ref best, ref bestD);
        if (!best && hero.Current == HeroController.State.Fly)
            foreach (var c in Physics.OverlapSphere(chest, planeGrabRange, ~0, QueryTriggerInteraction.Ignore))
                if (c.GetComponentInParent<Airplane>() || c.GetComponentInParent<SpaceFighter>()) Consider(c, chest, ref best, ref bestD);
        return best;
    }

    public bool TryGrab()
    {
        Vector3 chest = transform.position + Vector3.up * 1.2f;
        Grabbable best = null; float bestD = float.MaxValue;
        foreach (var c in Physics.OverlapSphere(chest + transform.forward * 1.2f, grabRange, ~0, QueryTriggerInteraction.Ignore))
            Consider(c, chest, ref best, ref bestD);
        if (!best && hero.Current == HeroController.State.Fly)
            foreach (var c in Physics.OverlapSphere(chest, planeGrabRange, ~0, QueryTriggerInteraction.Ignore))
                if (c.GetComponentInParent<Airplane>() || c.GetComponentInParent<SpaceFighter>()) Consider(c, chest, ref best, ref bestD);
        if (!best) return false;
        Held = best;
        best.Grab(HoldPoint());
        hero.HoldPose = true;
        Sfx.Play(Sfx.Whoosh, chest, 0.6f, 0.7f, 0.2f);
        return true;
    }

    static void Consider(Collider c, Vector3 from, ref Grabbable best, ref float bestD)
    {
        var g = c.GetComponentInParent<Grabbable>();
        if (!g || !g.CanGrab || g.Held) return;
        float d = (c.ClosestPoint(from) - from).sqrMagnitude;
        if (d < bestD) { bestD = d; best = g; }
    }

    public void StartThrow()
    {
        if (!Held) return;
        hero.PlayThrow();
        releaseAt = Time.time + releaseDelay;
    }

    void Release()
    {
        releaseAt = -1f;
        hero.HoldPose = false;
        if (!Held) return;
        var g = Held;
        Held = null;
        Vector3 dir = AimDir();
        g.Throw(dir * g.ThrowSpeed + hero.Velocity * 0.5f);
        Sfx.Play(Sfx.Whoosh, transform.position + Vector3.up * 2f, 0.9f, 0.55f, 0.2f);
    }

    void Drop()
    {
        var g = Held;
        Held = null;
        hero.HoldPose = false;
        releaseAt = -1f;
        Vector3 front = transform.position + transform.forward * 2.2f + Vector3.up * 2f;
        Vector3 ground = Physics.Raycast(front, Vector3.down, out var h, 30f, FxUtil.WorldMask, QueryTriggerInteraction.Ignore)
            ? h.point : transform.position + transform.forward * 2.2f;
        g.Drop(ground);
    }

    /// Throw toward whatever the camera is looking at (slightly upward so it carries).
    Vector3 AimDir()
    {
        var cam = Camera.main;
        Vector3 from = HoldPoint().position;
        if (!cam) return transform.forward;
        Vector3 o = cam.transform.position, f = cam.transform.forward;
        float skip = Mathf.Max(0f, Vector3.Dot(from - o, f));
        Vector3 target = Physics.Raycast(o + f * skip, f, out var h, 400f, FxUtil.WorldMask, QueryTriggerInteraction.Ignore)
            ? h.point : o + f * (skip + 400f);
        Vector3 d = (target - from).normalized;
        return (d + Vector3.up * 0.06f).normalized;
    }
}
