using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Visual effects for the hero (added automatically by HeroController):
///   super speed  - afterimages, speed lines around the camera, dust trail at the feet
///   hard landing - cracked ground decal, crater, flying debris, dust shockwave, camera shake
/// All textures and materials are generated in code, no assets needed.
/// </summary>
public class SuperEffects : MonoBehaviour
{
    [Header("Afterimages")]
    public Color afterimageColor = new Color(0.45f, 0.65f, 1f, 0.6f);
    public float afterimageInterval = 0.045f;
    public float afterimageLife = 0.28f;

    [Header("Landing")]
    public int debrisCount = 22;
    public float crackLife = 10f;

    HeroController hero;
    SkinnedMeshRenderer[] skins;
    Material ghostMat, crackMat, craterMat, dustMat, lineMat, debrisMat;
    ParticleSystem speedLines, footDust;
    readonly Queue<Ghost> ghostPool = new Queue<Ghost>();
    float ghostTimer;

    class Ghost { public GameObject go; public Mesh[] meshes; public MeshRenderer[] renderers; public float age; }
    readonly List<Ghost> liveGhosts = new List<Ghost>();
    MaterialPropertyBlock mpb;

    static Texture2D crackTex, craterTex, softTex, ringTex, gridTex;
    Material ringMat, wallMat, sprayMat;
    ParticleSystem windWrap, waterSpray, wakeFoam, groundDust, groundDebris;
    Material foamMat, debrisBitMat;
    Animator anim;
    readonly MeshRenderer[] walls = new MeshRenderer[4];

    void Start()
    {
        hero = GetComponent<HeroController>();
        skins = GetComponentsInChildren<SkinnedMeshRenderer>();
        mpb = new MaterialPropertyBlock();
        Physics.IgnoreLayerCollision(2, 2, true);      // debris (layer 2) never blocks the hero (layer 2)

        softTex ??= MakeSoftDot(64);
        crackTex ??= MakeCrackTexture(512, 7);
        craterTex ??= MakeCraterTexture(256);

        ghostMat = Transparent(Color.white, null, additive: true);
        crackMat = Transparent(new Color(0.05f, 0.045f, 0.04f, 1f), crackTex, additive: false);
        craterMat = Transparent(new Color(0.12f, 0.1f, 0.09f, 1f), craterTex, additive: false);
        dustMat = Transparent(new Color(0.75f, 0.7f, 0.62f, 1f), softTex, additive: false);
        lineMat = Transparent(new Color(1f, 1f, 1f, 1f), softTex, additive: true);
        debrisMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        debrisMat.SetColor("_BaseColor", new Color(0.32f, 0.31f, 0.3f));
        debrisMat.SetFloat("_Smoothness", 0.1f);

        ringTex ??= MakeRingTexture(256);
        gridTex ??= MakeGridTexture(128);
        ringMat = Transparent(new Color(0.85f, 0.95f, 1f, 1f), ringTex, additive: true);
        wallMat = Transparent(new Color(0.3f, 0.8f, 1f, 1f), gridTex, additive: true);
        anim = GetComponentInChildren<Animator>();

        hero.HardLanded += OnHardLanded;
        hero.BoostStarted += OnBoostStarted;
        BuildSpeedLines();
        BuildFootDust();
        BuildWindWrap();
        BuildEdgeWalls();
        hero.SplashedIntoSea += OnSplash;
        sprayMat = Transparent(new Color(0.9f, 0.96f, 1f, 1f), softTex, additive: false);
        foamMat = Transparent(new Color(0.95f, 0.98f, 1f, 1f), softTex, additive: false);
        debrisBitMat = Transparent(new Color(0.25f, 0.23f, 0.21f, 1f), softTex, additive: false);
        BuildSurfaceRush();
        if (!GetComponent<HeroHUD>()) gameObject.AddComponent<HeroHUD>();
    }

    void OnDestroy()
    {
        if (!hero) return;
        hero.HardLanded -= OnHardLanded;
        hero.BoostStarted -= OnBoostStarted;
        hero.SplashedIntoSea -= OnSplash;
    }

    void OnSplash(Vector3 at)
    {
        DustBurst(at, Vector3.up, 0.6f, sprayMat, new Color(0.9f, 0.96f, 1f, 0.75f));
        ThirdPersonCamera.Shake(0.2f, 0.3f);
    }

    // ================================================================== super speed
    void LateUpdate()
    {
        float s = hero.SuperSpeed01;
        bool grounded = hero.Current == HeroController.State.Ground;

        // afterimages
        ghostTimer -= Time.deltaTime;
        if (s > 0.25f && ghostTimer <= 0f)
        {
            ghostTimer = afterimageInterval;
            SpawnGhost(s);
        }
        for (int i = liveGhosts.Count - 1; i >= 0; i--)
        {
            var g = liveGhosts[i];
            g.age += Time.deltaTime;
            float a = afterimageColor.a * (1f - g.age / afterimageLife);
            if (a <= 0f) { g.go.SetActive(false); ghostPool.Enqueue(g); liveGhosts.RemoveAt(i); continue; }
            mpb.SetColor("_BaseColor", new Color(afterimageColor.r * a, afterimageColor.g * a, afterimageColor.b * a, a));
            foreach (var r in g.renderers) r.SetPropertyBlock(mpb);
        }

        // speed lines (also when flying fast)
        float fly01 = hero.Current == HeroController.State.Fly
            ? Mathf.InverseLerp(hero.flySpeed * 1.2f, hero.boostSpeed, hero.Velocity.magnitude) : 0f;
        var em = speedLines.emission;
        em.rateOverTime = Mathf.Max(s, fly01) * 160f;
        if (Camera.main && speedLines.transform.parent != Camera.main.transform)
            AttachSpeedLines(Camera.main.transform);

        // dust kicked up at the feet
        var fe = footDust.emission;
        fe.rateOverTime = grounded ? s * (hero.OnWater ? 160f : 90f) : 0f;
        var fm = footDust.main;
        fm.startColor = hero.OnWater ? new Color(0.92f, 0.97f, 1f, 0.55f) : new Color(0.72f, 0.68f, 0.6f, 0.35f);   // white spray over water
        var fr = footDust.GetComponent<ParticleSystemRenderer>();
        fr.sharedMaterial = hero.OnWater ? sprayMat : dustMat;

        // wind wrapping around the body, aligned with the direction of travel
        float wind = Mathf.Max(s, fly01);
        var we = windWrap.emission;
        we.rateOverTime = wind * 260f;
        Vector3 v = hero.Velocity;
        if (v.sqrMagnitude > 1f) windWrap.transform.rotation = Quaternion.LookRotation(v.normalized);
        var hips = anim ? anim.GetBoneTransform(HumanBodyBones.Chest) : null;
        windWrap.transform.position = hips ? hips.position : transform.position + Vector3.up;

        UpdateEdgeWalls();
        UpdateSurfaceRush();
    }

    // ================================================================== low & fast over water / ground
    void BuildSurfaceRush()
    {
        // spray thrown up behind him (water)
        waterSpray = MakeSystem("WaterSpray", sprayMat, ParticleSystemSimulationSpace.World, 600);
        var m = waterSpray.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(5f, 13f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
        m.startColor = new Color(0.92f, 0.97f, 1f, 0.8f);
        m.gravityModifier = 1.4f;
        var sh = waterSpray.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 28f; sh.radius = 1.4f;
        var sol = waterSpray.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 2.2f));
        var col = waterSpray.colorOverLifetime; col.enabled = true; col.color = Fade(0.05f, 0.55f);
        var r = waterSpray.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f; r.lengthScale = 1.2f;

        // white foam left on the surface: a wake trail behind him
        wakeFoam = MakeSystem("WakeFoam", foamMat, ParticleSystemSimulationSpace.World, 500);
        m = wakeFoam.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
        m.startSize = new ParticleSystem.MinMaxCurve(2.5f, 5.5f);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m.startColor = new Color(0.95f, 0.98f, 1f, 0.7f);
        sh = wakeFoam.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 1.5f;
        sol = wakeFoam.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.7f, 1, 2.6f));
        col = wakeFoam.colorOverLifetime; col.enabled = true; col.color = Fade(0.05f, 0.3f);
        wakeFoam.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;   // lies flat on the water

        // dust blasted off the road
        groundDust = MakeSystem("GroundRush", dustMat, ParticleSystemSimulationSpace.World, 400);
        m = groundDust.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(2f, 8f);
        m.startSize = new ParticleSystem.MinMaxCurve(1.8f, 4f);
        m.startColor = new Color(0.62f, 0.59f, 0.55f, 0.8f);
        m.gravityModifier = -0.05f;
        sh = groundDust.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 60f; sh.radius = 1.6f;
        sol = groundDust.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.5f, 1, 2.4f));
        col = groundDust.colorOverLifetime; col.enabled = true; col.color = Fade(0.05f, 0.35f);

        // small bits of grit flying out
        groundDebris = MakeSystem("GroundGrit", debrisBitMat, ParticleSystemSimulationSpace.World, 300);
        m = groundDebris.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(4f, 11f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.16f);
        m.startColor = new Color(0.25f, 0.23f, 0.21f, 0.9f);
        m.gravityModifier = 2f;
        sh = groundDebris.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 40f; sh.radius = 1f;
    }

    ParticleSystem MakeSystem(string name, Material mat, ParticleSystemSimulationSpace space, int max)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.duration = 1f; m.loop = true; m.simulationSpace = space; m.maxParticles = max;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        var em = ps.emission; em.rateOverTime = 0f;
        ps.Play();
        return ps;
    }

    void UpdateSurfaceRush()
    {
        Vector3 v = hero.Velocity;
        float speed = v.magnitude;
        bool flying = hero.Current == HeroController.State.Fly;
        float waterRate = 0f, groundRate = 0f;
        Vector3 at = transform.position;
        bool overWater = false;

        // what is under him, and how close?
        if (Physics.Raycast(transform.position + Vector3.up, Vector3.down, out RaycastHit hit, 14f, ~(1 << 2), QueryTriggerInteraction.Ignore))
        {
            float height = hit.distance - 1f;
            overWater = hit.collider.name == "SeaSurface";
            at = hit.point;
            float low = Mathf.Clamp01(1f - height / 10f);                     // full effect when skimming, none above 10 m
            float fast = flying ? Mathf.InverseLerp(10f, 60f, speed) : 0f;
            float run = hero.OnWater ? hero.SuperSpeed01 : hero.SuperSpeed01 * 0.4f;   // running on the sea (or fast on the road)
            float k = Mathf.Max(low * fast, run);
            if (overWater) waterRate = k; else groundRate = flying ? low * fast : 0f;
        }

        Vector3 back = speed > 0.5f ? -v.normalized : -transform.forward;
        Vector3 flatBack = new Vector3(back.x, 0f, back.z).normalized;
        // spray points up and backwards, like a rooster tail behind a speed boat
        Quaternion upBack = Quaternion.LookRotation((Vector3.up + flatBack * 0.8f).normalized);
        foreach (var ps in new[] { waterSpray, wakeFoam, groundDust, groundDebris })
            ps.transform.position = at + Vector3.up * 0.05f;
        waterSpray.transform.rotation = upBack;
        groundDust.transform.rotation = Quaternion.LookRotation((Vector3.up * 0.6f + flatBack).normalized);
        groundDebris.transform.rotation = upBack;
        wakeFoam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        // particles per meter travelled keeps the trail dense at any speed, plus some per second when slow
        SetRate(waterSpray, waterRate * 60f, waterRate * 9f);
        SetRate(wakeFoam, waterRate * 15f, waterRate * 1.6f);
        SetRate(groundDust, groundRate * 40f, groundRate * 6f);
        SetRate(groundDebris, groundRate * 30f, groundRate * 3f);
    }

    static void SetRate(ParticleSystem ps, float perSecond, float perMeter)
    {
        var e = ps.emission; e.rateOverTime = perSecond; e.rateOverDistance = perMeter;
    }

    // ================================================================== boost burst + wind
    void OnBoostStarted(Vector3 dir)
    {
        // sonic-boom ring: expands behind him, facing the direction of travel
        var hips = anim ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
        Vector3 at = (hips ? hips.position : transform.position + Vector3.up) - dir * 0.6f;
        StartCoroutine(SonicBoom(at, dir));
        ThirdPersonCamera.Shake(0.25f, 0.3f);
    }

    IEnumerator SonicBoom(Vector3 at, Vector3 dir)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = "SonicBoom";
        Destroy(q.GetComponent<Collider>());
        q.layer = 2;
        q.transform.position = at;
        q.transform.rotation = Quaternion.LookRotation(dir);
        var r = q.GetComponent<MeshRenderer>();
        r.sharedMaterial = ringMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var block = new MaterialPropertyBlock();
        const float life = 0.45f;
        for (float t = 0; t < life; t += Time.deltaTime)
        {
            float k = t / life;
            q.transform.localScale = Vector3.one * Mathf.Lerp(1.5f, 14f, 1f - (1f - k) * (1f - k));
            float a = (1f - k) * 0.9f;
            block.SetColor("_BaseColor", new Color(0.85f * a, 0.95f * a, a, a));
            r.SetPropertyBlock(block);
            yield return null;
        }
        Destroy(q);
    }

    void BuildWindWrap()
    {
        var go = new GameObject("WindWrap");
        windWrap = go.AddComponent<ParticleSystem>();
        windWrap.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = windWrap.main;
        main.duration = 1f; main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.32f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
        main.startColor = new Color(0.9f, 0.97f, 1f, 0.55f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;     // streaks stay wrapped around him
        main.maxParticles = 400;
        var shape = windWrap.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;                 // opens around the front of the body
        shape.angle = 25f; shape.radius = 0.55f; shape.radiusThickness = 0.25f;
        shape.position = new Vector3(0f, 0f, 1.0f);
        var vel = windWrap.velocityOverLifetime;
        vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = 0f; vel.y = 0f; vel.z = -16f;                            // flow back over the body
        vel.orbitalZ = 9f;                                               // and spiral around it
        var col = windWrap.colorOverLifetime; col.enabled = true; col.color = Fade(0.15f, 0.5f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.06f; r.lengthScale = 1.5f;
        r.sharedMaterial = lineMat;
        var em = windWrap.emission; em.rateOverTime = 0f;
        windWrap.Play();
    }

    // ================================================================== map edge walls
    void BuildEdgeWalls()
    {
        Rect m = hero.MapRect;
        const float h = 600f;
        var specs = new[]
        {
            (new Vector3(m.xMin, h / 2, m.center.y), Quaternion.LookRotation(Vector3.left),  m.height),
            (new Vector3(m.xMax, h / 2, m.center.y), Quaternion.LookRotation(Vector3.right), m.height),
            (new Vector3(m.center.x, h / 2, m.yMin), Quaternion.LookRotation(Vector3.back),  m.width),
            (new Vector3(m.center.x, h / 2, m.yMax), Quaternion.LookRotation(Vector3.forward), m.width),
        };
        var block = new MaterialPropertyBlock();
        for (int i = 0; i < 4; i++)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "EdgeWall";
            Destroy(q.GetComponent<Collider>());
            q.layer = 2;
            q.transform.SetPositionAndRotation(specs[i].Item1, specs[i].Item2);
            q.transform.localScale = new Vector3(specs[i].Item3, h, 1f);
            var r = q.GetComponent<MeshRenderer>();
            // repeat the grid every 3 m by stretching the quad's UVs (the particle shader ignores tiling)
            var mesh = Instantiate(q.GetComponent<MeshFilter>().sharedMesh);
            var uv = mesh.uv;
            for (int k = 0; k < uv.Length; k++) uv[k] = new Vector2(uv[k].x * specs[i].Item3 / 3f, uv[k].y * h / 3f);
            mesh.uv = uv;
            q.GetComponent<MeshFilter>().sharedMesh = mesh;
            r.sharedMaterial = wallMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            block.SetColor("_BaseColor", Color.clear);
            r.SetPropertyBlock(block);
            r.enabled = false;
            walls[i] = r;
        }
    }

    void UpdateEdgeWalls()
    {
        Rect m = hero.MapRect;
        Vector3 p = hero.transform.position;
        float[] dist = { p.x - m.xMin, m.xMax - p.x, p.z - m.yMin, m.yMax - p.z };
        var block = new MaterialPropertyBlock();
        float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 6f);
        for (int i = 0; i < 4; i++)
        {
            float a = Mathf.Clamp01(1f - dist[i] / 12f);       // shows within 12 m
            a = Mathf.SmoothStep(0f, 1f, a) * 0.9f * pulse;
            walls[i].enabled = a > 0.01f;
            if (!walls[i].enabled) continue;
            walls[i].GetPropertyBlock(block);
            block.SetColor("_BaseColor", new Color(0.3f * a, 0.8f * a, a, a));
            walls[i].SetPropertyBlock(block);
        }
    }

    void SpawnGhost(float strength)
    {
        var g = ghostPool.Count > 0 ? ghostPool.Dequeue() : NewGhost();
        for (int i = 0; i < skins.Length; i++)
        {
            skins[i].BakeMesh(g.meshes[i], true);
            g.renderers[i].transform.SetPositionAndRotation(skins[i].transform.position, skins[i].transform.rotation);
        }
        g.age = 0f;
        g.go.SetActive(true);
        liveGhosts.Add(g);
    }

    Ghost NewGhost()
    {
        var g = new Ghost { go = new GameObject("Afterimage"), meshes = new Mesh[skins.Length], renderers = new MeshRenderer[skins.Length] };
        for (int i = 0; i < skins.Length; i++)
        {
            var part = new GameObject("part");
            part.transform.SetParent(g.go.transform, false);
            part.layer = 2;
            g.meshes[i] = new Mesh();
            part.AddComponent<MeshFilter>().sharedMesh = g.meshes[i];
            var r = part.AddComponent<MeshRenderer>();
            var mats = new Material[skins[i].sharedMaterials.Length];
            for (int m = 0; m < mats.Length; m++) mats[m] = ghostMat;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            g.renderers[i] = r;
        }
        return g;
    }

    void BuildSpeedLines()
    {
        var go = new GameObject("SpeedLines");
        speedLines = go.AddComponent<ParticleSystem>();
        speedLines.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = speedLines.main;
        main.duration = 1f; main.loop = true;
        main.startLifetime = 0.35f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
        main.startColor = new Color(1f, 1f, 1f, 0.35f);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 300;
        var shape = speedLines.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 4f; shape.radiusThickness = 0.35f;   // a ring around the view, center stays clear
        var vel = speedLines.velocityOverLifetime;
        vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = 0f; vel.y = 0f; vel.z = -55f;               // rush toward the camera
        var col = speedLines.colorOverLifetime;
        col.enabled = true;
        col.color = Fade(0.1f, 0.6f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.12f; r.lengthScale = 2f;
        r.sharedMaterial = lineMat;
        var em = speedLines.emission; em.rateOverTime = 0f;
        speedLines.Play();
    }

    void AttachSpeedLines(Transform cam)
    {
        speedLines.transform.SetParent(cam, false);
        speedLines.transform.localPosition = new Vector3(0f, 0f, 14f);
        speedLines.transform.localRotation = Quaternion.identity;
    }

    void BuildFootDust()
    {
        var go = new GameObject("FootDust");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0.1f, -0.3f);
        footDust = go.AddComponent<ParticleSystem>();
        footDust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = footDust.main;
        main.duration = 1f; main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
        main.startColor = new Color(0.72f, 0.68f, 0.6f, 0.35f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;   // stays behind as he races away
        main.gravityModifier = -0.05f;
        var shape = footDust.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere; shape.radius = 0.4f;
        var sol = footDust.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 2.2f));
        var col = footDust.colorOverLifetime; col.enabled = true; col.color = Fade(0.05f, 0.3f);
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = dustMat;
        var em = footDust.emission; em.rateOverTime = 0f;
        footDust.Play();
    }

    // ================================================================== hard landing
    void OnHardLanded(Vector3 point, Vector3 normal, float impactSpeed)
    {
        float k = Mathf.InverseLerp(hero.hardLandingSpeed, 60f, impactSpeed);   // 0 = light, 1 = huge
        float radius = Mathf.Lerp(1.6f, 5.5f, k);

        var root = new GameObject("Impact");
        root.transform.position = point + normal * 0.03f;
        root.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0, Random.Range(0f, 360f), 0);

        var crater = Quad("Crater", root.transform, radius * 0.9f, craterMat, 0.005f);
        var crack = Quad("Cracks", root.transform, radius * 2.4f, crackMat, 0.012f);
        StartCoroutine(FadeAndDestroy(root, new[] { crater, crack }, crackLife));

        SpawnDebris(point, normal, k);
        DustBurst(point, normal, k, dustMat, new Color(0.7f, 0.66f, 0.58f, 0.6f));
        ThirdPersonCamera.Shake(Mathf.Lerp(0.25f, 0.9f, k), Mathf.Lerp(0.35f, 0.8f, k));
    }

    MeshRenderer Quad(string name, Transform parent, float size, Material mat, float lift)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = name;
        Destroy(q.GetComponent<Collider>());
        q.layer = 2;
        q.transform.SetParent(parent, false);
        q.transform.localPosition = Vector3.up * lift;
        q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        q.transform.localScale = Vector3.one * size;
        var r = q.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return r;
    }

    IEnumerator FadeAndDestroy(GameObject root, MeshRenderer[] rs, float life)
    {
        var block = new MaterialPropertyBlock();
        float t = 0f;
        while (t < life)
        {
            t += Time.deltaTime;
            float a = Mathf.Clamp01((life - t) / 3f);       // fade over the last 3 seconds
            foreach (var r in rs)
            {
                r.GetPropertyBlock(block);
                var c = r.sharedMaterial.GetColor("_BaseColor"); c.a = a;
                block.SetColor("_BaseColor", c);
                r.SetPropertyBlock(block);
            }
            yield return null;
        }
        Destroy(root);
    }

    void SpawnDebris(Vector3 point, Vector3 normal, float k)
    {
        int n = Mathf.RoundToInt(debrisCount * Mathf.Lerp(0.5f, 1.3f, k));
        for (int i = 0; i < n; i++)
        {
            var d = GameObject.CreatePrimitive(PrimitiveType.Cube);
            d.name = "Debris";
            d.layer = 2;
            float size = Random.Range(0.08f, Mathf.Lerp(0.22f, 0.45f, k));
            d.transform.localScale = new Vector3(size, size * Random.Range(0.4f, 0.9f), size * Random.Range(0.6f, 1.2f));
            Vector3 dir = Quaternion.AngleAxis(Random.Range(0f, 360f), normal) * Vector3.ProjectOnPlane(Random.onUnitSphere, normal).normalized;
            d.transform.position = point + dir * Random.Range(0.2f, 1.2f) + normal * 0.25f;
            d.transform.rotation = Random.rotation;
            d.GetComponent<MeshRenderer>().sharedMaterial = debrisMat;
            var rb = d.AddComponent<Rigidbody>();
            rb.mass = size * 4f;
            rb.linearVelocity = (dir * Random.Range(2f, 7f) + normal * Random.Range(4f, 11f)) * Mathf.Lerp(0.7f, 1.4f, k);
            rb.angularVelocity = Random.insideUnitSphere * 15f;
            StartCoroutine(ShrinkAndDestroy(d.transform, Random.Range(3f, 5f)));
        }
    }

    IEnumerator ShrinkAndDestroy(Transform t, float life)
    {
        yield return new WaitForSeconds(life);
        Vector3 s0 = t.localScale;
        for (float f = 0; f < 1f; f += Time.deltaTime / 0.6f)
        {
            if (!t) yield break;
            t.localScale = s0 * (1f - f);
            yield return null;
        }
        if (t) Destroy(t.gameObject);
    }

    void DustBurst(Vector3 point, Vector3 normal, float k, Material mat, Color color)
    {
        var go = new GameObject("ImpactDust");
        go.transform.position = point + normal * 0.2f;
        go.transform.rotation = Quaternion.FromToRotation(Vector3.forward, normal);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 0.2f; main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 2.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(6f, Mathf.Lerp(10f, 22f, k));
        main.startSize = new ParticleSystem.MinMaxCurve(1.2f, Mathf.Lerp(2.5f, 4.5f, k));
        main.startColor = color;
        main.stopAction = ParticleSystemStopAction.Destroy;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;           // flat ring along the ground = shockwave
        shape.radius = 0.4f; shape.arc = 360f;
        var limit = ps.limitVelocityOverLifetime; limit.enabled = true; limit.limit = 1.5f; limit.dampen = 0.12f;
        var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.5f, 1, 2f));
        var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(0.02f, 0.4f);
        var em = ps.emission; em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Lerp(30, 80, k)) });
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        ps.Play();
    }

    // ================================================================== helpers
    static Gradient Fade(float fadeIn, float holdUntil)
    {
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(1f, holdUntil), new GradientAlphaKey(0f, 1f) });
        return g;
    }

    static Material Transparent(Color color, Texture tex, bool additive) => FxUtil.Transparent(color, tex, additive);

    static Texture2D MakeSoftDot(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude / (n / 2f);
                byte a = (byte)(Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d) * 255);
                px[y * n + x] = new Color32(255, 255, 255, a);
            }
        t.SetPixels32(px); t.Apply();
        return t;
    }

    /// Radial cracks: jagged lines from the center that branch and get thinner.
    static Texture2D MakeCrackTexture(int n, int seed)
    {
        var rng = new System.Random(seed);
        float R() => (float)rng.NextDouble();
        var alpha = new float[n * n];
        void Stamp(float cx, float cy, float rad, float a)
        {
            int r = Mathf.CeilToInt(rad) + 1;
            for (int y = (int)cy - r; y <= (int)cy + r; y++)
                for (int x = (int)cx - r; x <= (int)cx + r; x++)
                {
                    if (x < 0 || y < 0 || x >= n || y >= n) continue;
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    float v = Mathf.Clamp01(rad + 0.5f - d) * a;
                    int i = y * n + x;
                    if (v > alpha[i]) alpha[i] = v;
                }
        }
        void Crack(float x, float y, float angle, float length, float width, int depth)
        {
            float step = 3f;
            for (float l = 0; l < length; l += step)
            {
                // concrete cracks: mostly straight, with sharp zig-zag kinks
                angle += R() < 0.18f ? (R() - 0.5f) * 1.1f : (R() - 0.5f) * 0.06f;
                x += Mathf.Cos(angle) * step; y += Mathf.Sin(angle) * step;
                float w = width * (1f - l / length) * 0.75f + 0.5f;   // taper
                Stamp(x, y, w, 1f);
                if (depth < 2 && R() < 0.025f)                 // branch
                    Crack(x, y, angle + (R() < 0.5f ? -1 : 1) * (0.5f + R() * 0.6f), (length - l) * (0.4f + R() * 0.3f), w * 0.7f, depth + 1);
            }
        }
        float c = n / 2f;
        int main = 9 + rng.Next(5);
        for (int i = 0; i < main; i++)
        {
            float a = i * Mathf.PI * 2f / main + (R() - 0.5f) * 0.5f;
            Crack(c + Mathf.Cos(a) * n * 0.05f, c + Mathf.Sin(a) * n * 0.05f, a, n * (0.28f + R() * 0.17f), 3.2f + R() * 2f, 0);
        }
        // a broken ring around the crushed center
        for (float a = 0; a < Mathf.PI * 2f; a += 0.02f)
            if (Mathf.Sin(a * 7f + 1.3f) > -0.2f)
            {
                float rr = n * (0.11f + 0.015f * Mathf.Sin(a * 13f));
                Stamp(c + Mathf.Cos(a) * rr, c + Mathf.Sin(a) * rr, 1.6f, 0.9f);
            }
        var px = new Color32[n * n];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, (byte)(alpha[i] * 255));
        var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        t.SetPixels32(px); t.Apply();
        return t;
    }

    static Texture2D MakeRingTexture(int n)
    {
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = new Vector2(x - n / 2f, y - n / 2f).magnitude / (n / 2f);
                float a = Mathf.Exp(-Mathf.Pow((d - 0.82f) / 0.06f, 2f)) + 0.35f * Mathf.Exp(-Mathf.Pow((d - 0.7f) / 0.12f, 2f));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }
        var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        t.SetPixels32(px); t.Apply();
        return t;
    }

    static Texture2D MakeGridTexture(int n)
    {
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float gx = Mathf.Min(x, n - 1 - x) / (float)n, gy = Mathf.Min(y, n - 1 - y) / (float)n;
                float line = Mathf.Exp(-Mathf.Min(gx, gy) * 22f);           // glowing grid lines
                float a = Mathf.Clamp01(line + 0.18f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
        t.SetPixels32(px); t.Apply();
        return t;
    }

    /// Dark crushed center with a noisy edge.
    static Texture2D MakeCraterTexture(int n)
    {
        var px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x - n / 2f) / (n / 2f), dy = (y - n / 2f) / (n / 2f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx);
                float edge = 0.85f + 0.1f * Mathf.PerlinNoise(ang * 2f + 5f, 3.1f);
                float a = Mathf.Clamp01((edge - d) * 6f) * (0.55f + 0.45f * Mathf.PerlinNoise(x * 0.08f, y * 0.08f));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 220));
            }
        var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        t.SetPixels32(px); t.Apply();
        return t;
    }

}
