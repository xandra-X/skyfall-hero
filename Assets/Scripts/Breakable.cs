using UnityEngine;

/// <summary>
/// A street prop (lamp, pole, tree, sign, bench...) that tears loose and flies when hit.
/// Created by OverPeak > Setup Destruction, which splits the town's merged props into separate objects.
/// </summary>
public class Breakable : MonoBehaviour
{
    [Tooltip("Whole prop, local space (the static collider only covers the base / trunk)")]
    public Vector3 fullCenter, fullSize = Vector3.one;
    public bool isPole, hasLight;

    [Tooltip("Seconds after breaking before it grows back in its place")]
    public float respawnDelay = 12f;

    public bool Broken { get; private set; }
    Rigidbody rb;
    float heat;
    Vector3 homePos, homeScale, boxCenter0, boxSize0;
    Quaternion homeRot;
    int layer0;
    bool homeSaved;

    void SaveHome()
    {
        if (homeSaved) return;
        homeSaved = true;
        homePos = transform.position; homeRot = transform.rotation; homeScale = transform.localScale;
        layer0 = gameObject.layer;
        var box = GetComponent<BoxCollider>();
        if (box) { boxCenter0 = box.center; boxSize0 = box.size; }
    }

    void Awake() => SaveHome();

    public Vector3 Top => transform.TransformPoint(fullCenter + Vector3.up * fullSize.y * 0.5f);

    /// velocity = how fast the hit point is sent flying
    public void Break(Vector3 point, Vector3 velocity)
    {
        if (Broken)
        {
            if (rb) rb.AddForceAtPosition(velocity * rb.mass * 0.6f, point, ForceMode.Impulse);
            return;
        }
        SaveHome();
        Broken = true;
        foreach (var t in GetComponentsInChildren<Transform>()) t.gameObject.layer = FxUtil.HeroLayer;
        var box = GetComponent<BoxCollider>();
        if (!box) box = gameObject.AddComponent<BoxCollider>();
        box.center = fullCenter;
        box.size = Vector3.Max(fullSize, Vector3.one * 0.15f);

        rb = gameObject.AddComponent<Rigidbody>();
        rb.mass = Mathf.Clamp(fullSize.x * fullSize.y * fullSize.z * 40f, 25f, 500f);
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.AddForceAtPosition(velocity * rb.mass, point, ForceMode.Impulse);
        rb.AddTorque(Random.insideUnitSphere * rb.mass * 4f, ForceMode.Impulse);

        Vector3 basePos = transform.position;
        Sfx.Play(Sfx.Clank, point, 0.9f, Random.Range(0.8f, 1.15f));
        FxUtil.Burst("PropDust", FxUtil.Dust, basePos + Vector3.up * 0.2f, Quaternion.Euler(-90, 0, 0), 14,
            new Vector2(0.6f, 1.4f), new Vector2(1f, 4f), new Vector2(0.5f, 1.4f), new Color(0.65f, 0.62f, 0.57f, 0.55f), -0.05f, 70f, 0.3f, false, 0.2f, 2f);
        for (int i = 0; i < 4; i++)
            FxUtil.Chunk(basePos + Random.insideUnitSphere * 0.3f + Vector3.up * 0.2f, Random.Range(0.08f, 0.22f), null,
                velocity.normalized * Random.Range(2f, 6f) + Vector3.up * Random.Range(2f, 5f), Random.Range(3f, 5f));
        if (hasLight || isPole)
            FxUtil.Burst("Sparks", FxUtil.Spark, Top, Quaternion.LookRotation(Vector3.up), hasLight ? 60 : 30,
                new Vector2(0.3f, 0.9f), new Vector2(3f, 9f), new Vector2(0.04f, 0.09f), Color.white, 1.5f, 80f, 0.2f, true);

        StartCoroutine(Respawn());
    }

    /// Shrink away where it landed, then grow back in its original spot.
    System.Collections.IEnumerator Respawn()
    {
        yield return new WaitForSeconds(respawnDelay);
        Vector3 s0 = transform.localScale;
        for (float t = 0f; t < 0.5f; t += Time.deltaTime) { transform.localScale = s0 * (1f - t / 0.5f); yield return null; }
        if (rb) { rb.isKinematic = true; Destroy(rb); rb = null; }
        yield return null;
        transform.SetPositionAndRotation(homePos, homeRot);
        var box = GetComponent<BoxCollider>();
        if (box) { box.center = boxCenter0; box.size = boxSize0; }
        foreach (var t in GetComponentsInChildren<Transform>()) t.gameObject.layer = layer0;
        for (float t = 0f; t < 0.5f; t += Time.deltaTime) { transform.localScale = homeScale * (t / 0.5f); yield return null; }
        transform.localScale = homeScale;
        heat = 0f;
        Broken = false;
    }

    /// Heat vision on a prop: after a moment it bursts free in flames.
    public void Heat(float dt, Vector3 point, Vector3 beamDir)
    {
        if (Broken) { if (rb) rb.AddForceAtPosition(beamDir * rb.mass * 8f * dt, point, ForceMode.Impulse); return; }
        heat += dt;
        if (heat < 0.15f) return;
        Break(point, beamDir * 14f + Vector3.up * 6f);
        FxUtil.Burst("Flare", FxUtil.Spark, point, Quaternion.LookRotation(-beamDir), 50,
            new Vector2(0.3f, 0.8f), new Vector2(4f, 10f), new Vector2(0.05f, 0.12f), Color.white, 1.2f, 70f, 0.15f, true);
    }
}
