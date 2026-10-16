using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Seamless flight from the city into space (scene objects are created by OverPeak > Setup Space):
///   - the sky darkens and stars come out as the hero climbs through a scattered cloud layer
///   - the city stays in view far up: it sits in a hole in the top of a giant Earth globe whose sea curves away
///   - in open space his flight speed scales up (40x) so the Moon is a short trip
///   - diving back into the atmosphere at speed sets him on fire (ReentryFx)
///   - he can land and walk anywhere on the Moon (low gravity): the Moon turns under his feet so the
///     spot he stands on is always its top
///   - the Sun always stays far away (it moves with the camera), so it can never be reached
///   - squadrons of space fighters fly patrols, sweep past him and attack; destroyed ones are replaced
/// Everything lives in one scene, so there is no loading screen.
/// </summary>
[DefaultExecutionOrder(-80)]
public class SpaceSystem : MonoBehaviour
{
    public static SpaceSystem Instance { get; private set; }

    [Header("Scene objects (filled by Setup Space)")]
    public Transform earth;
    public Transform moon;
    public Vector3 moonCenterLocal;
    public Vector3 earthCenterLocal;          // globe centre in the Earth object's own (unscaled) space
    public Transform crater;
    public Transform sunBody;
    public Renderer sunGlow;
    public Transform cloudDeck;
    public GameObject fighterTemplate;
    public GameObject[] cityRoots;
    public Material skyMaterial;
    public Material plasmaMaterial;

    [Header("Layout")]
    public float earthRadius = 24000f;
    public float moonRadius = 4000f;
    public float cloudHeight = 1200f;         // top of the sea of clouds
    public float cityHideHeight = 15000f;     // above this the city is switched off (it's a speck by then)
    public float reenterHeight = 3500f;       // fighters keep above this height over the Earth
    public float spaceStart = 1800f, spaceFull = 5000f;   // the sky goes from blue to black between these heights
    public float goldenElevation = 3f;        // sun height (degrees) above the clouds: golden hour
    public float cityHoleRadius = 10000f;     // the city's sea fills a hole this wide in the globe
    public float flatRadius = 3500f;          // the sea is flat this far out, then curves down with the Earth
    public float spaceBoundary = 90000f;
    public float sunDistance = 80000f;
    public float maxSpaceSpeed = 40f;
    public int squadrons = 3, squadronSize = 3;
    public Transform cloudSea;                // 3D cloud models (filled by Setup Space)
    public Color cityCutTint = new Color(0.02f, 0.045f, 0.085f);   // the globe fades to this around the city's sea

    public float Space01 { get; private set; }
    public bool InSpace => Space01 > 0.5f;
    public Vector3 CityCenter { get; private set; }
    public Vector3 MoonCenter => moon ? moon.TransformPoint(moonCenterLocal) : Vector3.zero;
    public bool OnMoon { get; private set; }

    HeroController hero;
    Camera cam;
    Light sun;
    float fogEnd0, fogStart0, near0, far0;
    Color fog0, ambientDay;
    float whiteFlash;
    readonly List<SpaceFighter> fleet = new List<SpaceFighter>();
    Vector3 sunDir;
    Quaternion sunDay, sunGolden;
    Color sunColor0, ambientGolden = new Color(0.55f, 0.45f, 0.45f);
    float sunIntensity0;
    Renderer earthGlow;
    Light earthshine;
    float earthGlowShow;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        hero = FindAnyObjectByType<HeroController>();
        cam = Camera.main;
        foreach (var l in FindObjectsByType<Light>())
            if (l.type == LightType.Directional) { sun = l; break; }
        if (sun)
        {
            RenderSettings.sun = sun;
            sunDay = sun.transform.rotation;
            sunGolden = Quaternion.Euler(goldenElevation, sun.transform.eulerAngles.y, 0f);
            sunColor0 = sun.color;
            sunIntensity0 = sun.intensity;
        }
        sunDir = sun ? -sun.transform.forward : Vector3.up;
        CityCenter = hero ? new Vector3(hero.MapRect.center.x, 0f, hero.MapRect.center.y) : Vector3.zero;

        fog0 = RenderSettings.fogColor;
        fogEnd0 = RenderSettings.fogEndDistance;
        fogStart0 = RenderSettings.fogStartDistance;
        near0 = cam ? cam.nearClipPlane : 0.1f;
        far0 = cam ? cam.farClipPlane : 4000f;
        ambientDay = new Color(0.55f, 0.6f, 0.68f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        if (skyMaterial) RenderSettings.skybox = skyMaterial;
        if (fighterTemplate) fighterTemplate.SetActive(false);
        if (earth) earth.gameObject.SetActive(true);
        if (sunBody && !sunGlow)
        {
            // big soft glow around the Sun
            var q = FxUtil.Quad("SunGlow", sunBody.position, Quaternion.identity, 26000f,
                FxUtil.Transparent(new Color(2.2f, 1.5f, 0.8f, 1f), FxUtil.Soft, true));
            sunGlow = q;
        }
        if (hero && plasmaMaterial && !hero.GetComponent<ReentryFx>())
            hero.gameObject.AddComponent<ReentryFx>().plasmaMaterial = plasmaMaterial;
        float drop = earthRadius - Mathf.Sqrt(earthRadius * earthRadius - flatRadius * flatRadius);
        Shader.SetGlobalVector("_EarthBend", new Vector4(earthRadius, flatRadius, drop, 0f));
        earthCentre = CityCenter + Vector3.down * (earthRadius - drop);
        if (sunBody) sunRenderers = sunBody.GetComponentsInChildren<Renderer>();
        prevHeroAlt = Altitude(hero ? hero.transform.position : Vector3.zero);
        Shader.SetGlobalColor("_CityCutTint", cityCutTint);

        BuildEarthGlow();
        for (int i = 0; i < squadrons; i++) SpawnSquadron(false);
        Apply(true);
    }

    void LateUpdate() => Apply(false);

    void Apply(bool force)
    {
        if (!hero || !cam) return;
        if (moon) MoonWalk();
        ReturnTowardCity();
        KeepAboveEarth();
        Vector3 hp = hero.transform.position;
        // heights are measured from the curved Earth (same as plain Y over the city), so the sky, haze and
        // clouds stay right wherever he comes back down
        float y = Altitude(cam.transform.position);
        float heroAlt = Altitude(hp);

        // ---- golden hour above the clouds: the sun sinks low and warm as he rises out of the city
        // (only around the cloud sea: higher up, and in space, it is full daylight again)
        float golden = Smooth(450f, cloudHeight + 200f, y) * (1f - Smooth(spaceStart + 600f, spaceStart + 2400f, y));
        float goldenTint = golden;
        if (sun)
        {
            sun.transform.rotation = Quaternion.Slerp(sunDay, sunGolden, golden);
            sun.color = Color.Lerp(sunColor0, new Color(1f, 0.66f, 0.38f), goldenTint);
            sun.intensity = sunIntensity0 * (1f + 0.25f * goldenTint);
            sunDir = -sun.transform.forward;
        }
        Shader.SetGlobalFloat("_Golden", goldenTint);
        // the sky draws the evening sun right on the cloud horizon (this little world's horizon dips a lot)
        float dip = Mathf.Acos(earthRadius / (earthRadius + Mathf.Max(0f, y - cloudHeight + 150f))) * Mathf.Rad2Deg;
        Shader.SetGlobalVector("_SkySunDir", Quaternion.Euler(1.5f - dip, sunGolden.eulerAngles.y, 0f) * Vector3.back);

        // ---- sky, haze, light
        Space01 = Smooth(spaceStart, spaceFull, y);
        float stars = Smooth(spaceStart + 500f, spaceFull - 500f, y);
        Shader.SetGlobalFloat("_SpaceBlend", Mathf.Pow(Space01, 0.8f));
        Shader.SetGlobalFloat("_StarBlend", stars);
        Shader.SetGlobalFloat("_AtmoVisible", Smooth(2500f, 9000f, y));
        Shader.SetGlobalFloat("_SeaGlobeBlend", Smooth(2500f, 11000f, y) * 0.92f);
        Shader.SetGlobalFloat("_CloudSeaOpacity", 1f - Smooth(spaceFull, spaceFull + 4000f, y));
        // the haze thins out as he climbs so the city below gets sharp; warm haze over the cloud sea at golden hour
        float clear = Smooth(300f, 4000f, y);
        Color haze = Color.Lerp(new Color(0.36f, 0.5f, 0.68f), new Color(0.86f, 0.62f, 0.46f), goldenTint);
        RenderSettings.fog = y < cityHideHeight + 3000f;
        RenderSettings.fogColor = Color.Lerp(fog0, haze, Smooth(300f, 1500f, y));
        RenderSettings.fogStartDistance = Mathf.Lerp(fogStart0, 6000f, clear);
        RenderSettings.fogEndDistance = Mathf.Lerp(fogEnd0, 40000f, clear) + Smooth(7000f, 16000f, y) * 60000f;
        Color ambLow = Color.Lerp(ambientDay, ambientGolden, goldenTint);
        RenderSettings.ambientLight = Color.Lerp(ambLow, new Color(0.07f, 0.08f, 0.11f), Space01);

        // ---- camera range: far away things (city from above, Earth, Moon) need a deep view
        cam.nearClipPlane = Mathf.Lerp(near0, 1.2f, Smooth(2000f, 6000f, y));
        cam.farClipPlane = y > 150f ? 250000f : far0;

        // ---- the city always stays switched on (switching thousands of objects on/off made a hitch);
        // only its small details stop drawing once it is far away (it's a speck by then)
        float camToCity = Vector3.Distance(cam.transform.position, CityCenter);
        if (cityDetailShown ? camToCity > 38000f : camToCity < 34000f) ShowCityDetail(!cityDetailShown);
        Shader.SetGlobalVector("_CityCut", new Vector4(CityCenter.x, 0f, CityCenter.z, cityHoleRadius));
        if (cloudDeck)
        {
            var r = cloudDeck.GetComponent<Renderer>();
            float o = 1f - Smooth(spaceFull, spaceFull + 5000f, y);
            r.enabled = o > 0.01f;
            if (r.enabled) r.sharedMaterial.SetFloat("_Opacity", o);
            cloudDeck.position = new Vector3(cam.transform.position.x, cloudHeight, cam.transform.position.z);
        }

        // ---- the Sun: always the same distance away along the sunlight
        if (sunBody)
        {
            sunBody.position = cam.transform.position + sunDir * sunDistance;
            sunBody.Rotate(Vector3.up, 2f * Time.deltaTime, Space.Self);
            bool showSun = Space01 > 0.2f;
            foreach (var r in sunRenderers) r.enabled = showSun;
            if (sunGlow)
            {
                sunGlow.enabled = showSun;
                sunGlow.transform.position = sunBody.position;
                sunGlow.transform.rotation = Quaternion.LookRotation(cam.transform.forward);
            }
        }

        // ---- hero: speed, gravity, landing
        float toMoonSurface = moon ? Vector3.Distance(hp, MoonCenter) - moonRadius : 1e9f;
        bool nearMoon = toMoonSurface < 2500f;
        float horiz = new Vector2(hp.x - CityCenter.x, hp.z - CityCenter.z).magnitude;
        float open = Smooth(cloudHeight + 300f, spaceFull - 500f, heroAlt) * Mathf.Clamp01((toMoonSurface - 150f) / 2500f);
        hero.SpeedMultiplier = 1f + (maxSpaceSpeed - 1f) * open;
        hero.GravityScale = nearMoon ? 1f / 6f : 1f;
        hero.CanStopFlying = heroAlt < 40000f || nearMoon;          // LAND works anywhere in the sky
        GuidedLanding(heroAlt, horiz, nearMoon);
        // the walls round the city only matter when he is really low (measured over the curved Earth)
        hero.MapEdgeCeiling = heroAlt < 1500f ? 1500f : float.NegativeInfinity;

        // ---- outer boundary of space
        Vector3 fromCity = hp - CityCenter;
        if (fromCity.magnitude > spaceBoundary) Teleport(CityCenter + fromCity.normalized * spaceBoundary);

        UpdateEarthGlow(nearMoon);
        hero.SeaNearby = heroAlt < 3000f && horiz < cityHoleRadius;

        whiteFlash = Mathf.MoveTowards(whiteFlash, 0f, Time.unscaledDeltaTime * 1.5f);
        MaintainFleet();
    }

    // ================================================================== coming home
    Vector3 earthCentre;
    float prevHeroAlt;
    Renderer[] sunRenderers = new Renderer[0];
    bool cityDetailShown = true;
    readonly List<Renderer> hiddenCityDetail = new List<Renderer>();

    /// Height above the (curved) Earth's surface; over the city this equals the plain Y height.
    public float Altitude(Vector3 p) => Vector3.Distance(p, earthCentre) - (CityCenter.y - earthCentre.y);

    /// Diving back to Earth far from the city: as he comes down, the planet turns smoothly under him so he
    /// arrives over the city (the only place with streets). No teleport, no flash: the turn is spread over
    /// the whole descent, and how far from the city he may be shrinks with height.
    void ReturnTowardCity()
    {
        Vector3 rel = hero.transform.position - earthCentre;
        float alt = Altitude(hero.transform.position);
        float descended = prevHeroAlt - alt;
        prevHeroAlt = alt;
        // (a jump of kilometres in one frame is a placement, not a dive: ignore it)
        if (OnMoon || alt > 40000f || descended <= 0f || descended > 2000f || (moon && Vector3.Distance(hero.transform.position, MoonCenter) < moonRadius + 3000f)) return;

        float R = CityCenter.y - earthCentre.y;
        float allowed = Mathf.Max(700f, 700f + (alt - 2500f) * 2.2f);                 // metres from the city
        float allowedAngle = Mathf.Asin(Mathf.Clamp01(allowed / R)) * Mathf.Rad2Deg;
        float angle = Vector3.Angle(rel, Vector3.up);
        if (angle <= allowedAngle) return;

        if (hero.Velocity.sqrMagnitude > 1f && Vector3.Dot(hero.Velocity.normalized, -rel.normalized) < 0.45f) return;
        // heading for the Moon (which sits out beside the Earth) is a trip, not a homecoming
        if (moon && hero.Velocity.sqrMagnitude > 1f && Vector3.Angle(hero.Velocity, MoonCenter - hero.transform.position) < 35f) return;
        float k = Mathf.Clamp01(descended / Mathf.Max(alt - 2500f, 400f));
        float turn = (angle - allowedAngle) * k;
        Vector3 axis = Vector3.Cross(rel, Vector3.up);
        if (axis.sqrMagnitude < 1e-6f) return;
        Quaternion q = Quaternion.AngleAxis(turn, axis.normalized);
        hero.transform.position = earthCentre + q * rel;
        hero.Velocity = q * hero.Velocity;
        Physics.SyncTransforms();
    }

    /// Away from the city the planet is a soft floor: he skims over it at cloud-top height instead of sinking
    /// into it (only the city and its sea are real ground).
    /// After LAND high up he doesn't drift down for a minute: he drops fast, curving in over the city, and
    /// slows just enough near the ground for a superhero landing.
    void GuidedLanding(float alt, float horiz, bool nearMoon)
    {
        if (nearMoon || hero.Current != HeroController.State.Air || alt < 120f) return;
        float target = Mathf.Clamp(alt * 0.35f, 45f, 700f);             // metres per second downward
        Vector3 v = hero.Velocity;
        Vector3 down = (earthCentre - hero.transform.position).normalized;
        float fall = Vector3.Dot(v, down);
        if (fall < target) v += down * Mathf.Min(target - fall, 400f * Time.deltaTime);
        // drift in over the streets so he lands in the city, not in the sea
        Vector3 toCity = new Vector3(CityCenter.x - hero.transform.position.x, 0f, CityCenter.z - hero.transform.position.z);
        if (horiz > 300f) v += toCity.normalized * Mathf.Min(horiz, 400f) * 0.5f * Time.deltaTime;
        hero.Velocity = v;
    }

    void KeepAboveEarth()
    {
        if (hero.Current != HeroController.State.Fly) return;           // falling to land: the city pull handles it
        Vector3 p = hero.transform.position;
        float horiz = new Vector2(p.x - CityCenter.x, p.z - CityCenter.z).magnitude;
        if (horiz < cityHoleRadius * 0.9f && p.y > earthCentre.y) return;            // over the city: normal ground
        float R = CityCenter.y - earthCentre.y;
        const float floorAlt = 2500f;
        Vector3 rel = p - earthCentre;
        if (rel.magnitude >= R + floorAlt) return;
        Vector3 n = rel.normalized;
        hero.transform.position = earthCentre + n * (R + floorAlt);
        float inward = Vector3.Dot(hero.Velocity, n);
        if (inward < 0f) hero.Velocity -= n * inward;                                // slide along, don't sink
        Physics.SyncTransforms();
        prevHeroAlt = floorAlt;
    }

    void ShowCityDetail(bool show)
    {
        cityDetailShown = show;
        if (!show)
        {
            hiddenCityDetail.Clear();
            foreach (var root in cityRoots)
            {
                if (!root) continue;
                foreach (var r in root.GetComponentsInChildren<Renderer>())
                {
                    // the sea and islands stay: they fill the hole in the globe
                    if (!r.enabled || r.name.StartsWith("Ocean") || r.name == "Islands") continue;
                    r.enabled = false;
                    hiddenCityDetail.Add(r);
                }
            }
        }
        else
        {
            foreach (var r in hiddenCityDetail) if (r) r.enabled = true;
            hiddenCityDetail.Clear();
        }
    }

    // ================================================================== the Earth seen from the Moon
    /// The Moon sits out beside the Earth, so standing on it the real Earth hangs above the horizon.
    /// Near the Moon a soft blue glow surrounds the Earth and its light (earthshine) brightens the Moon.
    void BuildEarthGlow()
    {
        if (!earth) return;
        var glowMat = FxUtil.Transparent(new Color(0.35f, 0.6f, 1.6f, 0.45f), FxUtil.Soft, true);
        earthGlow = FxUtil.Quad("EarthGlow", Vector3.zero, Quaternion.identity, 1f, glowMat, transform);
        earthGlow.enabled = false;
        earthshine = new GameObject("Earthshine").AddComponent<Light>();
        earthshine.transform.SetParent(transform, false);
        earthshine.type = LightType.Directional;
        earthshine.color = new Color(0.45f, 0.6f, 1f);
        earthshine.intensity = 0f;
        earthshine.shadows = LightShadows.None;
        earthshine.enabled = false;
    }

    void UpdateEarthGlow(bool nearMoon)
    {
        if (!earthGlow) return;
        earthGlowShow = Mathf.MoveTowards(earthGlowShow, nearMoon ? 1f : 0f, Time.deltaTime * 0.8f);
        bool show = earthGlowShow > 0.001f;
        earthGlow.enabled = show;
        earthshine.enabled = show;
        Shader.SetGlobalVector("_EarthshineDir", Vector4.zero);
        if (!show) return;
        float R = CityCenter.y - earthCentre.y;
        Vector3 toEarth = earthCentre - cam.transform.position;
        Vector3 dir = toEarth.normalized;
        // the glow quad sits just behind the Earth, facing the camera
        earthGlow.transform.position = earthCentre + dir * R * 1.1f;
        earthGlow.transform.rotation = Quaternion.LookRotation(dir);
        earthGlow.transform.localScale = Vector3.one * R * 3.0f;
        earthshine.transform.rotation = Quaternion.LookRotation(-dir);
        earthshine.intensity = 0.35f * earthGlowShow;
        Shader.SetGlobalVector("_EarthshineDir", new Vector4(dir.x, dir.y, dir.z, earthGlowShow));
        Shader.SetGlobalColor("_EarthshineColor", new Color(0.05f, 0.08f, 0.14f));
    }

    // ================================================================== walking on the Moon
    /// While he is on (or falling to) the Moon, the Moon is turned around its centre so that the point
    /// under him is always its top. Gravity stays "down", so he can walk all the way round.
    /// Landing on its side first swings the Moon (with him on it) round until he is on top.
    void MoonWalk()
    {
        Vector3 c = MoonCenter;
        Vector3 rel = hero.transform.position - c;
        float dist = rel.magnitude;
        OnMoon = hero.Current != HeroController.State.Fly && dist - moonRadius < 3000f;
        if (!OnMoon || dist < 1f) return;

        Quaternion q = Quaternion.FromToRotation(rel / dist, Vector3.up);
        float angle = Quaternion.Angle(Quaternion.identity, q);
        if (angle < 0.0001f) return;
        bool swinging = angle > 1.5f;
        if (swinging)
        {
            float k = Mathf.Max(1f - Mathf.Exp(-4f * Time.deltaTime), Mathf.Min(1f, 0.25f / angle));
            q = Quaternion.Slerp(Quaternion.identity, q, k);
            hero.Velocity = Vector3.zero;          // hold him while the Moon turns him upright
        }
        else hero.Velocity = q * hero.Velocity;

        moon.SetPositionAndRotation(c + q * (moon.position - c), q * moon.rotation);
        hero.transform.position = c + q * rel;
        Vector3 fwd = Vector3.ProjectOnPlane(q * hero.transform.forward, Vector3.up);
        if (fwd.sqrMagnitude > 1e-4f) hero.transform.rotation = Quaternion.LookRotation(fwd);
        Physics.SyncTransforms();
    }

    void Teleport(Vector3 p)
    {
        var cc = hero.GetComponent<CharacterController>();
        cc.enabled = false;
        hero.transform.position = p;
        cc.enabled = true;
    }

    void OnGUI()
    {
        if (whiteFlash <= 0.01f) return;
        GUI.color = new Color(1f, 1f, 1f, whiteFlash);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

    // ================================================================== fighters
    bool wasOnMoon;

    void MaintainFleet()
    {
        fleet.RemoveAll(f => !f || f.Destroyed);
        // landing on the Moon brings a squadron straight in (unless one is already close)
        if (OnMoon && !wasOnMoon && !fleet.Exists(f => Vector3.Distance(f.transform.position, hero.transform.position) < 6000f))
            SpawnSquadron(true);
        wasOnMoon = OnMoon;
        if (fleet.Count <= (squadrons - 1) * squadronSize && Time.frameCount % 90 == 0) SpawnSquadron(true);
    }

    void SpawnSquadron(bool nearHero)
    {
        if (!fighterTemplate) return;
        Vector3 pos;
        if (nearHero && InSpace)
        {
            Vector3 dir = Random.onUnitSphere; dir.y = Mathf.Abs(dir.y) * (OnMoon ? 1f : 0.5f) + (OnMoon ? 0.3f : 0f);
            pos = hero.transform.position + dir.normalized * Random.Range(4000f, 7000f);
        }
        else pos = RandomPatrolPoint();
        pos = AboveEarth(pos, reenterHeight + 4000f);
        if (moon && Vector3.Distance(pos, MoonCenter) < moonRadius + 1500f) pos = MoonCenter + (pos - MoonCenter).normalized * (moonRadius + 1500f);
        var rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        SpaceFighter leader = null;
        for (int i = 0; i < squadronSize; i++)
        {
            // V formation: leader in front, wingmen behind on each side
            int row = (i + 1) / 2;
            Vector3 slot = i == 0 ? Vector3.zero : new Vector3((i % 2 == 1 ? -1f : 1f) * 28f * row, -4f * row, -30f * row);
            var go = Instantiate(fighterTemplate, pos + rot * slot, rot);
            go.name = i == 0 ? "SpaceFighter" : "SpaceFighter (wing)";
            go.SetActive(true);
            var f = go.AddComponent<SpaceFighter>();
            f.leader = leader;
            f.slot = slot;
            if (i == 0) leader = f;
            fleet.Add(f);
        }
    }

    /// Somewhere he is likely to see it: around him when he is in space, otherwise between the Earth and the Moon.
    public Vector3 RandomPatrolPoint()
    {
        Vector3 q;
        if (hero && InSpace && Random.value < 0.6f)
            q = hero.transform.position + Random.onUnitSphere * Random.Range(1500f, 5000f);
        else
        {
            Vector3 a = CityCenter + Vector3.up * (reenterHeight + 4000f);
            Vector3 b = moon ? MoonCenter + Vector3.up * (moonRadius + 1500f) : a + Vector3.forward * 40000f;
            q = Vector3.Lerp(a, b, Random.value) + Random.insideUnitSphere * 9000f;
        }
        q = AboveEarth(q, reenterHeight + 3000f);
        if (moon && Vector3.Distance(q, MoonCenter) < moonRadius + 800f) q = MoonCenter + (q - MoonCenter).normalized * (moonRadius + 1500f);
        return q;
    }

    /// Lift a point so it is at least this high above the (curved) Earth.
    public Vector3 AboveEarth(Vector3 p, float minAltitude)
    {
        float R = CityCenter.y - earthCentre.y;
        Vector3 rel = p - earthCentre;
        return rel.magnitude < R + minAltitude ? earthCentre + rel.normalized * (R + minAltitude) : p;
    }

    public Vector3 EarthCentre => earthCentre;
    public Transform Hero => hero ? hero.transform : null;
}
