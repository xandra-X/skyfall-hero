using System.Linq;
using System.IO;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>Batch test: fakes a hard landing and a super-speed run in edit mode and renders pictures.</summary>
public static class SuperEffectsTest
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/City.unity");
        var hero = GameObject.Find("Hero");
        var hc = hero.GetComponent<HeroController>();
        var animator = hero.GetComponentInChildren<Animator>();
        var fx = (hero.TryGetComponent<SuperEffects>(out var existing1) ? existing1 : hero.AddComponent<SuperEffects>());
        hc.GetType().GetMethod("Awake", F).Invoke(hc, null);
        fx.GetType().GetMethod("Start", F).Invoke(fx, null);
        Directory.CreateDirectory("Screenshots/fx");
        var cam = new GameObject("TestCam").AddComponent<Camera>();
        cam.fieldOfView = 55;

        // ---- 1. landing impact on the street
        Vector3 p = new Vector3(0f, 0.25f, -30f);
        if (Physics.Raycast(p + Vector3.up * 5, Vector3.down, out var hit, 20f)) p = hit.point;
        hero.transform.position = p;
        Pose(animator, "Land", 0.25f);
        fx.GetType().GetMethod("OnHardLanded", F).Invoke(fx, new object[] { p, Vector3.up, 45f });
        var oldMode = Physics.simulationMode;
        Physics.simulationMode = SimulationMode.Script;      // restored below: this setting is saved with the project
        for (int i = 0; i < 15; i++) Physics.Simulate(0.02f);          // debris flies out
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>())
            if (ps.name == "ImpactDust") ps.Simulate(0.35f, true, true);
        Shot(cam, p + new Vector3(-5f, 3.5f, -7f), p + Vector3.up * 0.6f, "Screenshots/fx/landing.png");
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>())
            if (ps.name == "ImpactDust") ps.gameObject.SetActive(false);
        for (int i = 0; i < 150; i++) Physics.Simulate(0.02f);         // debris settles
        Shot(cam, p + new Vector3(0f, 9f, -4f), p, "Screenshots/fx/crack_top.png");
        Physics.simulationMode = oldMode;

        // ---- 2. super speed: afterimages along a run
        var spawn = fx.GetType().GetMethod("SpawnGhost", F);
        Vector3 start = new Vector3(-8f, p.y, 10f);
        hero.transform.rotation = Quaternion.LookRotation(Vector3.right);
        var visual = animator.transform;
        visual.localRotation = Quaternion.Euler(16f, 0, 0);
        for (int i = 0; i < 6; i++)
        {
            hero.transform.position = start + Vector3.right * (i * 1.6f);
            Pose(animator, "Sprint", (i * 0.09f) % 0.53f);
            spawn.Invoke(fx, new object[] { 1f });
        }
        // fade: older ghosts dimmer
        fx.GetType().GetMethod("LateUpdate", F).Invoke(fx, null);
        Shot(cam, start + new Vector3(5f, 1.5f, -6f), start + new Vector3(5f, 1f, 0f), "Screenshots/fx/superspeed.png");
        Debug.Log("[FxTest] DONE");
    }

    public static void RunFlight()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/City.unity");
        var hero = GameObject.Find("Hero");
        var hc = hero.GetComponent<HeroController>();
        var animator = hero.GetComponentInChildren<Animator>();
        var fx = (hero.TryGetComponent<SuperEffects>(out var existing2) ? existing2 : hero.AddComponent<SuperEffects>());
        hc.GetType().GetMethod("Awake", F).Invoke(hc, null);
        fx.GetType().GetMethod("Start", F).Invoke(fx, null);
        Directory.CreateDirectory("Screenshots/fx");
        var cam = new GameObject("TestCam").AddComponent<Camera>();
        cam.fieldOfView = 70;
        Debug.Log($"[FxTest] map rect {hc.MapRect}");
        if (System.Environment.GetCommandLineArgs().Contains("-rectOnly")) return;

        // flying fast along +Z at 40 m height
        hero.transform.SetPositionAndRotation(new Vector3(0, 40, -10), Quaternion.identity);
        typeof(HeroController).GetProperty("Current").SetValue(hc, HeroController.State.Fly);
        typeof(HeroController).GetField("velocity", F).SetValue(hc, new Vector3(0, 0, 110f));
        Pose(animator, "SM_FlyFast", 0.2f);
        fx.GetType().GetMethod("LateUpdate", F).Invoke(fx, null);
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>())
            if (ps.name == "WindWrap" || ps.name == "SpeedLines") ps.Simulate(0.5f, true, true);
        fx.GetType().GetMethod("OnBoostStarted", F).Invoke(fx, new object[] { Vector3.forward });
        var boom = GameObject.Find("SonicBoom"); if (boom) boom.transform.localScale = Vector3.one * 5f;
        var chest = animator.GetBoneTransform(HumanBodyBones.Chest).position;
        Shot(cam, chest + new Vector3(2.2f, 1.2f, -4.5f), chest, "Screenshots/fx/flyboost.png");
        if (boom) Object.DestroyImmediate(boom);

        // standing near the east edge of the map
        Rect m = hc.MapRect;
        hero.transform.SetPositionAndRotation(new Vector3(m.xMax - 3f, 0.3f, 0f), Quaternion.LookRotation(Vector3.right));
        typeof(HeroController).GetProperty("Current").SetValue(hc, HeroController.State.Ground);
        typeof(HeroController).GetField("velocity", F).SetValue(hc, Vector3.zero);
        Pose(animator, "SM_Idle", 0f);
        fx.GetType().GetMethod("LateUpdate", F).Invoke(fx, null);
        foreach (var w in (MeshRenderer[])fx.GetType().GetField("walls", F).GetValue(fx))
        {
            var blk = new MaterialPropertyBlock(); w.GetPropertyBlock(blk);
            Debug.Log($"[FxTest] wall {w.transform.position} rot {w.transform.eulerAngles} scale {w.transform.localScale} enabled {w.enabled} color {blk.GetColor("_BaseColor")} cull {w.sharedMaterial.GetFloat("_Cull")}");
        }
        Shot(cam, hero.transform.position + new Vector3(-6f, 3f, -3f), hero.transform.position + new Vector3(3f, 2f, 0f), "Screenshots/fx/edge.png");
        Shot(cam, hero.transform.position + new Vector3(0f, 6f, 0f), hero.transform.position + new Vector3(10f, 6f, 0f), "Screenshots/fx/edge_straight.png");
        var wm = ((MeshRenderer[])fx.GetType().GetField("walls", F).GetValue(fx))[1].sharedMaterial;
        Debug.Log($"[FxTest] wall mat shader {wm.shader.name} tex {wm.GetTexture("_BaseMap")} scale {wm.GetTextureScale("_BaseMap")} keywords {string.Join(",", wm.shaderKeywords)}");
        Debug.Log("[FxTest] FLIGHT DONE");
    }

    public static void RunSurface()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/City.unity");
        var hero = GameObject.Find("Hero");
        var hc = hero.GetComponent<HeroController>();
        var animator = hero.GetComponentInChildren<Animator>();
        var fx = (hero.TryGetComponent<SuperEffects>(out var existing3) ? existing3 : hero.AddComponent<SuperEffects>());
        hc.GetType().GetMethod("Awake", F).Invoke(hc, null);
        fx.GetType().GetMethod("Start", F).Invoke(fx, null);
        var cam = new GameObject("TestCam").AddComponent<Camera>();
        cam.fieldOfView = 65; cam.farClipPlane = 5000;
        Directory.CreateDirectory("Screenshots/fx");
        typeof(HeroController).GetProperty("Current").SetValue(hc, HeroController.State.Fly);
        var late = fx.GetType().GetMethod("LateUpdate", F);
        string[] names = { "WaterSpray", "WakeFoam", "GroundRush", "GroundGrit" };

        void Fly(Vector3 from, Vector3 vel, float height, string file)
        {
            foreach (var n in names) GameObject.Find(n).GetComponent<ParticleSystem>().Clear();
            Pose(animator, "SM_Fly", 0.3f);
            animator.transform.localRotation = Quaternion.Euler(0, 0, -25f);       // banking into a turn
            typeof(HeroController).GetField("velocity", F).SetValue(hc, vel);
            hero.transform.rotation = Quaternion.LookRotation(new Vector3(vel.x, 0, vel.z));
            const float dt = 1f / 30f;
            for (int i = 0; i < 45; i++)
            {
                Vector3 p = from + vel * (i * dt);
                float ground = Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out var h, 60f, ~(1 << 2)) ? h.point.y : p.y;
                hero.transform.position = new Vector3(p.x, ground + height, p.z);
                late.Invoke(fx, null);
                foreach (var n in names) GameObject.Find(n).GetComponent<ParticleSystem>().Simulate(dt, true, false, false);
            }
            var hp = hero.transform.position;
            Pose(animator, "SM_Fly", 0.3f);
            // move the posed body to where the hero is now (edit-mode posing leaves it at the start)
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            animator.transform.position += hp + Vector3.up * 0.9f - hips.position;
            Vector3 back = -vel.normalized;
            Vector3 side = Vector3.Cross(Vector3.up, back);
            Shot(cam, hp + back * 4f + side * 9f + Vector3.up * 2.5f, hp + back * 9f, file);
        }
        Fly(new Vector3(-170f, 0f, -120f), new Vector3(0f, 0f, 55f), 1.5f, "Screenshots/fx/fly_sea.png");
        var deck = GameObject.Find("World/Bridge/Deck").GetComponent<Renderer>().bounds;
        Vector3 road = new Vector3(deck.min.x, 0, deck.min.z), roadDir = new Vector3(deck.size.x, 0, deck.size.z).normalized;
        Fly(road + roadDir * 30f, roadDir * 55f, 1.5f, "Screenshots/fx/fly_road.png");
        // downtown street
        Fly(new Vector3(-60f, 0f, 1000f), new Vector3(55f, 0f, 0f), 1.5f, "Screenshots/fx/fly_street.png");
        Debug.Log("[FxTest] SURFACE DONE");
    }

    static void Pose(Animator animator, string clipName, float time)
    {
        foreach (var c in animator.runtimeAnimatorController.animationClips)
            if (c.name == clipName)
            {
                var g = PlayableGraph.Create();
                var o = AnimationPlayableOutput.Create(g, "o", animator);
                var pl = AnimationClipPlayable.Create(g, c);
                o.SetSourcePlayable(pl); pl.SetTime(time); g.Evaluate(); g.Destroy();
                return;
            }
    }

    static void Shot(Camera cam, Vector3 pos, Vector3 target, string file)
    {
        // refresh skinned meshes for edit-mode rendering
        var smrs = Object.FindObjectsByType<SkinnedMeshRenderer>();
        var tmp = new System.Collections.Generic.List<GameObject>();
        foreach (var smr in smrs)
        {
            var m = new Mesh(); smr.BakeMesh(m, true);
            var g = new GameObject("bake"); g.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
            g.AddComponent<MeshFilter>().sharedMesh = m; g.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
            smr.enabled = false; tmp.Add(g);
        }
        cam.transform.position = pos; cam.transform.LookAt(target);
        var rt = new RenderTexture(960, 540, 24);
        cam.targetTexture = rt; cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
        cam.targetTexture = null; RenderTexture.active = null;
        File.WriteAllBytes(file, tex.EncodeToPNG());
        foreach (var g in tmp) Object.DestroyImmediate(g);
        foreach (var smr in smrs) smr.enabled = true;
    }
}
