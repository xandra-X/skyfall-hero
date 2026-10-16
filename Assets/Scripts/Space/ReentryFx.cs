using UnityEngine;

/// <summary>
/// Diving fast from space back into the atmosphere wraps the hero in fire: a glowing shell of hot air in front
/// of him, flames streaming back, a long burning trail across the sky, an orange light, roar and shake.
/// Added to the hero by SpaceSystem.
/// </summary>
public class ReentryFx : MonoBehaviour
{
    public Material plasmaMaterial;
    public float Intensity { get; private set; }

    HeroController hero;
    Transform rig, shell;
    Material shellMat;
    ParticleSystem flames, trail;
    Light glow;
    AudioSource roar;
    static Material flameMat, trailMat;

    void Start()
    {
        hero = GetComponent<HeroController>();
        rig = new GameObject("ReentryFx").transform;
        rig.SetParent(transform, false);
        rig.localPosition = Vector3.up * 1.1f;

        // shell: a stretched sphere, its nose just in front of him
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(s.GetComponent<Collider>());
        s.name = "PlasmaShell";
        s.layer = FxUtil.HeroLayer;
        shell = s.transform;
        shell.SetParent(rig, false);
        shell.localScale = new Vector3(3.2f, 3.2f, 7.5f);
        shell.localPosition = new Vector3(0f, 0f, -1.6f);
        shellMat = new Material(plasmaMaterial);
        var r = s.GetComponent<MeshRenderer>();
        r.sharedMaterial = shellMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        // flames licking back around him (move with him)
        flameMat ??= FxUtil.Transparent(new Color(3f, 1.2f, 0.3f, 1f), FxUtil.Soft, true);
        flames = FxUtil.System("ReentryFlames", flameMat, 400, rig);
        flames.transform.localPosition = new Vector3(0f, 0f, 1.6f);
        var m = flames.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(-40f, -20f);
        m.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
        m.simulationSpace = ParticleSystemSimulationSpace.Local;
        var sh = flames.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 6f; sh.radius = 1.4f;
        var c = flames.colorOverLifetime; c.enabled = true; c.color = FireGradient();
        var sz = flames.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.6f));
        var e = flames.emission; e.rateOverTime = 0f;

        // the long burning trail left in the sky
        trailMat ??= FxUtil.Transparent(new Color(2.4f, 0.9f, 0.3f, 1f), FxUtil.Soft, true);
        trail = FxUtil.System("ReentryTrail", trailMat, 600, rig);
        var tm = trail.main;
        tm.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
        tm.startSpeed = 0f;
        tm.startSize = new ParticleSystem.MinMaxCurve(6f, 12f);
        tm.simulationSpace = ParticleSystemSimulationSpace.World;
        var tc = trail.colorOverLifetime; tc.enabled = true; tc.color = TrailGradient();
        var ts = trail.sizeOverLifetime; ts.enabled = true; ts.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.7f, 1, 2.2f));
        var te = trail.emission; te.rateOverTime = 0f; te.rateOverDistance = 0f;

        glow = new GameObject("ReentryLight").AddComponent<Light>();
        glow.transform.SetParent(rig, false);
        glow.transform.localPosition = Vector3.forward * 2.5f;
        glow.type = LightType.Point; glow.color = new Color(1f, 0.55f, 0.2f); glow.range = 45f; glow.intensity = 0f;

        roar = Sfx.Loop(gameObject, Sfx.Rush, 0f);
        roar.pitch = 0.55f;
        roar.Play();
        Apply(0f);
    }

    void LateUpdate()
    {
        if (!hero) return;
        Vector3 v = hero.Velocity;
        float speed = v.magnitude;
        var ss = SpaceSystem.Instance;
        float y = ss ? ss.Altitude(transform.position) : transform.position.y;     // height above the curved Earth
        float target = 0f;
        bool nearMoon = ss && ss.moon && Vector3.Distance(transform.position, ss.MoonCenter) < ss.moonRadius + 6000f;
        if (hero.Current == HeroController.State.Fly && speed > 1f && !nearMoon)
        {
            float fast = Mathf.Clamp01((speed - 250f) / 900f);
            Vector3 up = ss ? (transform.position - (ss.CityCenter + Vector3.down * ss.earthRadius)).normalized : Vector3.up;
            float diving = Mathf.Clamp01((-Vector3.Dot(v, up) / speed - 0.2f) / 0.45f);
            float air = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(900f, 3000f, y)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(9000f, 14000f, y)));   // the air ends lower than the trip to the Moon goes
            target = fast * diving * air;
        }
        // flares up quickly, dies down a little slower
        Intensity = Mathf.MoveTowards(Intensity, target, Time.deltaTime * (target > Intensity ? 2.5f : 1.2f));
        if (speed > 1f) rig.rotation = Quaternion.Slerp(rig.rotation, Quaternion.LookRotation(v / speed), 1f - Mathf.Exp(-12f * Time.deltaTime));
        Apply(Intensity);
    }

    void Apply(float k)
    {
        bool on = k > 0.01f;
        shell.gameObject.SetActive(on);
        if (on) shellMat.SetFloat("_Intensity", k * 0.75f * (0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 9f, 0.3f)));
        var e = flames.emission; e.rateOverTime = 500f * k;
        var te = trail.emission; te.rateOverTime = 120f * k;
        glow.intensity = 14f * k;
        roar.volume = 0.9f * k;
        if (k > 0.2f) ThirdPersonCamera.Shake(0.18f * k, 0.12f);
    }

    static Gradient FireGradient()
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.8f), 0f), new GradientColorKey(new Color(1f, 0.55f, 0.15f), 0.35f), new GradientColorKey(new Color(0.8f, 0.15f, 0.05f), 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    static Gradient TrailGradient()
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.7f, 0.3f), 0f), new GradientColorKey(new Color(0.9f, 0.3f, 0.08f), 0.3f), new GradientColorKey(new Color(0.25f, 0.2f, 0.2f), 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.05f), new GradientAlphaKey(0.3f, 0.5f), new GradientAlphaKey(0f, 1f) });
        return g;
    }
}
