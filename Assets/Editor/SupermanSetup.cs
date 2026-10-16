using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Menu: OverPeak > Use Superman
/// Imports Superman.fbx as Humanoid (with finger bones), builds his materials and Animator Controller,
/// and swaps him in as the playable hero in City.unity. Hero controller + camera stay the same.
/// </summary>
public static class SupermanSetup
{
    const string Dir = "Assets/Characters/Superman/";
    const string Model = Dir + "Superman.fbx";
    const string ControllerPath = Dir + "SupermanAnimator.controller";
    const string MaskPath = Dir + "UpperBody.mask";
    const string MixamoDir = "Assets/Characters/Hero/Animations/";
    const string ScenePath = "Assets/Scenes/City.unity";

    static readonly string[] Loops = { "SM_Idle", "SM_Hover", "SM_Fly", "SM_FlyFast", "SM_HeatVision", "SM_Lift" };

    [MenuItem("OverPeak/Use Superman")]
    public static void Run()
    {
        var mats = BuildMaterials();
        SetupModel(mats);
        var ctrl = BuildController();
        SwapIntoScene(ctrl);
        Debug.Log("[Superman] DONE - press Play");
    }

    // ------------------------------------------------------------------ materials
    static Dictionary<string, Material> BuildMaterials()
    {
        var result = new Dictionary<string, Material>();
        foreach (var part in new[] { "SM_Body", "SM_FaceAcc", "SM_Head" })
        {
            string d = Dir + "Textures/" + part + "_D.png", n = Dir + "Textures/" + part + "_N.png";
            var ti = (TextureImporter)AssetImporter.GetAtPath(d); ti.sRGBTexture = true; ti.SaveAndReimport();
            var ni = (TextureImporter)AssetImporter.GetAtPath(n); ni.textureType = TextureImporterType.NormalMap; ni.SaveAndReimport();
            string path = Dir + part + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(d));
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(n));
            m.EnableKeyword("_NORMALMAP");
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", part == "SM_Body" ? 0.45f : 0.3f);
            EditorUtility.SetDirty(m);
            result[part] = m;
        }
        AssetDatabase.SaveAssets();
        return result;
    }

    // ------------------------------------------------------------------ model + humanoid mapping
    static readonly (HumanBodyBones bone, string name)[] BoneMap = BuildBoneMap();

    static (HumanBodyBones, string)[] BuildBoneMap()
    {
        var m = new List<(HumanBodyBones, string)>
        {
            (HumanBodyBones.Hips, "pelvis"), (HumanBodyBones.Spine, "spine_01"), (HumanBodyBones.Chest, "spine_03"),
            (HumanBodyBones.UpperChest, "spine_05"), (HumanBodyBones.Neck, "neck_01"), (HumanBodyBones.Head, "head"),
        };
        foreach (var (L, s) in new[] { (true, "l"), (false, "r") })
        {
            HumanBodyBones B(string left, string right) => (HumanBodyBones)System.Enum.Parse(typeof(HumanBodyBones), L ? left : right);
            m.Add((B("LeftShoulder", "RightShoulder"), "clavicle_" + s));
            m.Add((B("LeftUpperArm", "RightUpperArm"), "upperarm_" + s));
            m.Add((B("LeftLowerArm", "RightLowerArm"), "lowerarm_" + s));
            m.Add((B("LeftHand", "RightHand"), "hand_" + s));
            m.Add((B("LeftUpperLeg", "RightUpperLeg"), "thigh_" + s));
            m.Add((B("LeftLowerLeg", "RightLowerLeg"), "calf_" + s));
            m.Add((B("LeftFoot", "RightFoot"), "foot_" + s));
            m.Add((B("LeftToes", "RightToes"), "ball_" + s));
            foreach (var (unity, fbx) in new[] { ("Thumb", "thumb"), ("Index", "index"), ("Middle", "middle"), ("Ring", "ring"), ("Little", "pinky") })
            {
                m.Add((B($"Left{unity}Proximal", $"Right{unity}Proximal"), $"{fbx}_01_{s}"));
                m.Add((B($"Left{unity}Intermediate", $"Right{unity}Intermediate"), $"{fbx}_02_{s}"));
                m.Add((B($"Left{unity}Distal", $"Right{unity}Distal"), $"{fbx}_03_{s}"));
            }
        }
        return m.ToArray();
    }

    static void SetupModel(Dictionary<string, Material> mats)
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(Model);
        imp.bakeAxisConversion = true;
        imp.importCameras = false;
        imp.importLights = false;
        imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        foreach (var kv in mats)
            imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);

        // bones use standard names -> Unity maps the Humanoid (fingers included) automatically.
        // Clear any old hand-made mapping first.
        imp.animationType = ModelImporterAnimationType.Generic;
        imp.humanDescription = new HumanDescription();
        imp.SaveAndReimport();
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        imp.importAnimation = true;
        imp.SaveAndReimport();

        var clips = imp.defaultClipAnimations;
        foreach (var c in clips)
        {
            c.name = c.takeName.Split('|').Last();
            bool loop = Loops.Contains(c.name);
            c.loopTime = loop; c.loopPose = loop;
            c.lockRootRotation = true; c.keepOriginalOrientation = true;
            c.lockRootHeightY = true; c.keepOriginalPositionY = true;
            c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
            c.rotationOffset = 180f;              // his own clips were authored facing the other way
        }
        imp.clipAnimations = clips;
        imp.SaveAndReimport();

        var avatar = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<Avatar>().FirstOrDefault();
        Debug.Log($"[Superman] avatar valid={avatar && avatar.isValid} human={avatar && avatar.isHuman}");
        if (avatar)
            foreach (var hb in avatar.humanDescription.human)
                if (hb.humanName.Contains("Index") || hb.humanName.Contains("Hips") || hb.humanName.Contains("Toes"))
                    Debug.Log($"[Superman] map {hb.humanName} -> {hb.boneName}");
        foreach (var c in AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")))
            Debug.Log($"[Superman] clip {c.name} {c.length:0.00}s loop={c.isLooping} human={c.humanMotion}");
    }

    // ------------------------------------------------------------------ animator
    static AnimationClip Clip(string assetPath, string name) =>
        AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == name)
        ?? throw new System.Exception($"clip {name} missing in {assetPath}");
    static AnimationClip SM(string name) => Clip(Model, name);
    static AnimationClip Mixamo(string name) => Clip(MixamoDir + name + ".fbx", name);

    static AnimatorController BuildController()
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        var sm = ctrl.layers[0].stateMachine;

        var loco = ctrl.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
        tree.blendType = BlendTreeType.Simple1D; tree.blendParameter = "Speed"; tree.useAutomaticThresholds = false;
        tree.AddChild(SM("SM_Idle"), 0f);
        tree.AddChild(Mixamo("Walk"), 1.7f);
        tree.AddChild(Mixamo("Run"), 5.9f);
        tree.AddChild(Mixamo("Sprint"), 9f);
        var ch = tree.children; ch[3].timeScale = 1.5f; tree.children = ch;
        sm.defaultState = loco;

        sm.AddState("JumpUp").motion = Mixamo("JumpUp");
        sm.AddState("Fall").motion = Mixamo("Fall");
        sm.AddState("Land").motion = Mixamo("Land");
        sm.AddState("TakeOff").motion = SM("SM_TakeOff");
        sm.AddState("Hover").motion = SM("SM_Hover");
        sm.AddState("Fly").motion = SM("SM_Fly");
        sm.AddState("FlyFast").motion = SM("SM_FlyFast");
        sm.AddState("Kick").motion = SM("SM_Kick");

        AssetDatabase.DeleteAsset(MaskPath);
        var mask = new AvatarMask();
        for (var p = AvatarMaskBodyPart.Root; p < AvatarMaskBodyPart.LastBodyPart; p++)
            mask.SetHumanoidBodyPartActive(p, p == AvatarMaskBodyPart.Body || p == AvatarMaskBodyPart.Head ||
                                              p == AvatarMaskBodyPart.LeftArm || p == AvatarMaskBodyPart.RightArm ||
                                              p == AvatarMaskBodyPart.LeftFingers || p == AvatarMaskBodyPart.RightFingers);
        AssetDatabase.CreateAsset(mask, MaskPath);
        ctrl.AddLayer("UpperBody");
        var layers = ctrl.layers; layers[1].avatarMask = mask; layers[1].defaultWeight = 0f; ctrl.layers = layers;
        var upper = ctrl.layers[1].stateMachine;
        upper.defaultState = upper.AddState("Empty");
        upper.AddState("Blast").motion = SM("SM_HeatVision");     // left mouse = heat vision
        upper.AddState("Punch").motion = Mixamo("Punch");
        upper.AddState("Lift").motion = SM("SM_Lift");          // holding a person / car / plane overhead
        upper.AddState("Throw").motion = SM("SM_Throw");

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        Debug.Log("[Superman] animator controller built");
        return ctrl;
    }

    // ------------------------------------------------------------------ scene
    static void SwapIntoScene(AnimatorController ctrl)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var hero = GameObject.Find("Hero");
        if (!hero) { HeroSceneSetup.Run(); scene = EditorSceneManager.OpenScene(ScenePath); hero = GameObject.Find("Hero"); }
        var oldVisual = hero.transform.Find("Visual");
        if (oldVisual) Object.DestroyImmediate(oldVisual.gameObject);

        var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Model), hero.transform);
        visual.name = "Visual";
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        foreach (var t in visual.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;
        var animator = (visual.TryGetComponent<Animator>(out var existing6) ? existing6 : visual.AddComponent<Animator>());
        animator.runtimeAnimatorController = ctrl;
        animator.avatar = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<Avatar>().First();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        var hc = hero.GetComponent<HeroController>();
        hc.animator = animator;
        hc.visual = visual.transform;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = hero;
    }
}
