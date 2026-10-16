using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Sets up the hero model and all hero animation FBX files as Unity Humanoid.
/// Menu: OverPeak > Setup Hero Humanoid
/// </summary>
public static class HeroImporter
{
    const string HeroPath = "Assets/Characters/Hero/Hero.fbx";
    const string AnimDir = "Assets/Characters/Hero/Animations";

    // Clips that repeat
    static readonly string[] Loops = { "Idle", "Walk", "Run", "Sprint", "Fall", "Fly", "Hover", "Blast" };
    // Clips whose vertical movement should not be baked into the body (the jump height comes from code)
    static readonly string[] FreeY = { "JumpUp" };

    [MenuItem("OverPeak/Setup Hero Humanoid")]
    public static void Setup()
    {
        // 1. Hero model
        var imp = (ModelImporter)AssetImporter.GetAtPath(HeroPath);
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        imp.bakeAxisConversion = true;
        imp.importCameras = false;
        imp.importLights = false;
        imp.importAnimation = false;
        imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        imp.SaveAndReimport();
        var heroAvatar = AssetDatabase.LoadAllAssetsAtPath(HeroPath).OfType<Avatar>().FirstOrDefault();
        Debug.Log($"[Hero] model avatar valid={heroAvatar && heroAvatar.isValid} human={heroAvatar && heroAvatar.isHuman}");
        ReportBones(heroAvatar);

        // 2. Animation files
        foreach (var path in Directory.GetFiles(AnimDir, "*.fbx"))
        {
            var p = path.Replace('\\', '/');
            var file = Path.GetFileNameWithoutExtension(p);
            var ai = (ModelImporter)AssetImporter.GetAtPath(p);
            ai.animationType = ModelImporterAnimationType.Human;
            ai.importAnimation = true;
            ai.importCameras = false;
            ai.importLights = false;
            ai.materialImportMode = ModelImporterMaterialImportMode.None;
            // Each file gets its own avatar (Mixamo skeleton, or our skeleton without the mesh);
            // Humanoid retargeting maps them all onto the hero.
            ai.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            ai.SaveAndReimport();

            var clips = ai.defaultClipAnimations;
            foreach (var c in clips)
            {
                // Mixamo takes are all called "mixamo.com": name them after the file
                string name = file == "HeroCustom" ? c.takeName.Split('|').Last() : file;
                c.name = name;
                bool loop = Loops.Contains(name);
                c.loopTime = loop;
                c.loopPose = loop;
                c.lockRootRotation = true;                    // body keeps facing where the controller faces
                c.keepOriginalOrientation = true;
                c.lockRootHeightY = !FreeY.Contains(name);    // keep the body height from the animation
                c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = false;                 // forward travel goes to the root and is ignored
            }
            ai.clipAnimations = clips;
            ai.SaveAndReimport();

            var av = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Avatar>().FirstOrDefault();
            var made = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview"));
            foreach (var clip in made)
                Debug.Log($"[Hero] clip {clip.name,-10} {clip.length:0.00}s loop={clip.isLooping} human={clip.humanMotion} " +
                          $"avatar valid={av && av.isValid}");
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[Hero] DONE");
    }

    static void ReportBones(Avatar avatar)
    {
        if (!avatar) return;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(HeroPath);
        var go = Object.Instantiate(model);
        var anim = (go.TryGetComponent<Animator>(out var existing7) ? existing7 : go.AddComponent<Animator>());
        anim.avatar = avatar;
        var missing = new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Neck,
            HumanBodyBones.Head, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot }
            .Where(b => anim.GetBoneTransform(b) == null).ToList();
        Debug.Log(missing.Count == 0 ? "[Hero] all main bones mapped" : "[Hero] MISSING bones: " + string.Join(", ", missing));
        Object.DestroyImmediate(go);
    }
}
