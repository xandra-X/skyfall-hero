using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Batch test: poses the hero with every clip of his Animator and renders him from behind and from the side.
/// The hero root faces +Z (the direction he moves when you press W with the camera behind him).
/// Run: Unity -batchmode -executeMethod HeroPoseTest.Run
/// </summary>
public static class HeroPoseTest
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/City.unity");
        var hero = GameObject.Find("Hero");
        var animator = hero.GetComponentInChildren<Animator>();
        var visual = animator.transform;
        hero.transform.SetPositionAndRotation(new Vector3(0, 20, 0), Quaternion.identity);
        visual.localRotation = Quaternion.identity;

        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var cam = new GameObject("TestCam").AddComponent<Camera>();
        cam.fieldOfView = 40;
        Directory.CreateDirectory("Screenshots/pose");

        var clips = animator.runtimeAnimatorController.animationClips.Distinct().ToList();
        foreach (var clip in clips)
        {
            var graph = PlayableGraph.Create();
            var output = AnimationPlayableOutput.Create(graph, "out", animator);
            var play = AnimationClipPlayable.Create(graph, clip);
            output.SetSourcePlayable(play);
            play.SetTime(clip.length * 0.3f);
            graph.Evaluate();

            // which way is the body facing? (cross of up and the right->left hip line)
            var l = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position;
            var r = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg).position;
            var faceDir = Vector3.Cross(Vector3.up, l - r).normalized;
            var head = animator.GetBoneTransform(HumanBodyBones.Head).position;
            Debug.Log($"[PoseTest] {clip.name,-14} body forward {faceDir:F2}  hips->head {(head - hips.position):F2}");

            // refresh the skinned mesh: bake it into a static copy for the pictures
            var smrs = animator.GetComponentsInChildren<SkinnedMeshRenderer>();
            var copies = new System.Collections.Generic.List<GameObject>();
            foreach (var smr in smrs)
            {
                var m = new Mesh(); smr.BakeMesh(m, true);
                var g = new GameObject("bake"); g.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                g.AddComponent<MeshFilter>().sharedMesh = m; g.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                smr.enabled = false; copies.Add(g);
            }
            Vector3 c = hips.position;
            Shot(cam, c + new Vector3(0, 0.6f, -4.5f), c, $"Screenshots/pose/{clip.name}_behind.png");
            Shot(cam, c + new Vector3(4.5f, 0.6f, 0), c, $"Screenshots/pose/{clip.name}_side.png");
            foreach (var g in copies) Object.DestroyImmediate(g);
            foreach (var smr in smrs) smr.enabled = true;
            graph.Destroy();
        }
        Debug.Log("[PoseTest] DONE");
    }

    static void Shot(Camera cam, Vector3 pos, Vector3 target, string file)
    {
        cam.transform.position = pos;
        cam.transform.LookAt(target);
        var rt = new RenderTexture(400, 400, 24);
        cam.targetTexture = rt;
        cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(400, 400, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 400, 400), 0, 0);
        cam.targetTexture = null; RenderTexture.active = null;
        File.WriteAllBytes(file, tex.EncodeToPNG());
        Object.DestroyImmediate(rt);
    }
}

public static class HeroPoseTestBatch
{
    public static void Run()
    {
        SupermanSetup.Run();
        HeroPoseTest.Run();
    }
}
