using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Menu: OverPeak > Add Hero To City
/// Builds the hero's Animator Controller and puts a playable hero + follow camera into City.unity.
/// </summary>
public static class HeroSceneSetup
{
    const string HeroModel = "Assets/Characters/Hero/Hero.fbx";
    const string AnimDir = "Assets/Characters/Hero/Animations/";
    const string ControllerPath = "Assets/Characters/Hero/HeroAnimator.controller";
    const string MaskPath = "Assets/Characters/Hero/UpperBody.mask";
    const string ScenePath = "Assets/Scenes/City.unity";

    [MenuItem("OverPeak/Add Hero To City")]
    public static void Run()
    {
        var controller = BuildController();

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var old = GameObject.Find("Hero");
        if (old) Object.DestroyImmediate(old);

        // Root: physics + controller. Child "Visual": the animated model.
        var hero = new GameObject("Hero");
        hero.layer = 2;                                   // Ignore Raycast: the camera won't collide with him
        hero.transform.position = new Vector3(0f, 1.5f, -6f);
        var cc = hero.AddComponent<CharacterController>();
        cc.height = 1.85f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.95f, 0f);
        cc.stepOffset = 0.4f; cc.slopeLimit = 50f; cc.skinWidth = 0.05f;

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(HeroModel);
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, hero.transform);
        visual.name = "Visual";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        foreach (var t in visual.GetComponentsInChildren<Transform>()) t.gameObject.layer = 2;
        var animator = (visual.TryGetComponent<Animator>(out var existing8) ? existing8 : visual.AddComponent<Animator>());
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.avatar = AssetDatabase.LoadAllAssetsAtPath(HeroModel).OfType<Avatar>().First();

        var hc = hero.AddComponent<HeroController>();
        hc.animator = animator;
        hc.visual = visual.transform;

        // Camera: replace the free-fly test camera with the follow camera
        var cam = Camera.main;
        var fly = cam.GetComponent<FreeFlyCamera>();
        if (fly) Object.DestroyImmediate(fly);
        var tpc = (cam.TryGetComponent<ThirdPersonCamera>(out var existing9) ? existing9 : cam.gameObject.AddComponent<ThirdPersonCamera>());
        tpc.hero = hc;
        hc.cameraTransform = cam.transform;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 3000f;
        cam.transform.position = hero.transform.position + new Vector3(0f, 2.5f, -5f);
        cam.transform.LookAt(hero.transform.position + Vector3.up * 1.5f);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = hero;
        Debug.Log("[HeroSetup] DONE - hero added to City. Press Play.");
    }

    static AnimationClip Clip(string file, string name = null)
    {
        name ??= file;
        var clip = AssetDatabase.LoadAllAssetsAtPath(AnimDir + file + ".fbx").OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == name);
        if (!clip) Debug.LogError($"[HeroSetup] clip {name} not found in {file}.fbx");
        return clip;
    }

    static AnimatorController BuildController()
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);

        // ---- Base layer: full body
        var sm = ctrl.layers[0].stateMachine;
        var loco = ctrl.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(Clip("Idle"), 0f);
        tree.AddChild(Clip("Walk"), 1.7f);
        tree.AddChild(Clip("Run"), 5.9f);
        tree.AddChild(Clip("Sprint"), 9f);
        var children = tree.children;
        children[3].timeScale = 1.5f;                 // sprint legs move faster to match 9 m/s
        tree.children = children;
        sm.defaultState = loco;

        foreach (var (state, file, clipName) in new[] {
                     ("JumpUp", "JumpUp", "JumpUp"), ("Fall", "Fall", "Fall"), ("Land", "Land", "Land"),
                     ("Fly", "Fly", "Fly"), ("Hover", "HeroCustom", "Hover") })
        {
            var s = sm.AddState(state);
            s.motion = Clip(file, clipName);
        }

        // ---- Upper body layer: powers that work while walking or flying
        AssetDatabase.DeleteAsset(MaskPath);
        var mask = new AvatarMask();
        for (var p = AvatarMaskBodyPart.Root; p < AvatarMaskBodyPart.LastBodyPart; p++)
            mask.SetHumanoidBodyPartActive(p, p == AvatarMaskBodyPart.Body || p == AvatarMaskBodyPart.Head ||
                                              p == AvatarMaskBodyPart.LeftArm || p == AvatarMaskBodyPart.RightArm ||
                                              p == AvatarMaskBodyPart.LeftFingers || p == AvatarMaskBodyPart.RightFingers);
        AssetDatabase.CreateAsset(mask, MaskPath);

        ctrl.AddLayer("UpperBody");
        var layers = ctrl.layers;
        layers[1].avatarMask = mask;
        layers[1].defaultWeight = 0f;
        ctrl.layers = layers;
        var upper = ctrl.layers[1].stateMachine;
        upper.defaultState = upper.AddState("Empty");
        upper.AddState("Blast").motion = Clip("HeroCustom", "Blast");
        upper.AddState("Punch").motion = Clip("Punch");

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        Debug.Log("[HeroSetup] animator controller built");
        return ctrl;
    }
}
