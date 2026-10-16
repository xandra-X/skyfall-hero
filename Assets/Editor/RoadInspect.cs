using System.Linq;
using Unity.AI.Navigation;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class RoadInspect
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/City.unity");
        foreach (var m in Object.FindObjectsByType<NavMeshModifier>())
        {
            var r = m.GetComponent<MeshRenderer>(); var mf = m.GetComponent<MeshFilter>();
            if (!r) { Debug.Log($"[Road] {m.name} (no renderer) ignore={m.ignoreFromBuild}"); continue; }
            var mats = string.Join(" | ", r.sharedMaterials.Select((x, i) => $"{(x ? x.name : "-")}[{(x && x.mainTexture ? x.mainTexture.name : "")}] tris {(mf && mf.sharedMesh && i < mf.sharedMesh.subMeshCount ? mf.sharedMesh.GetIndexCount(i) / 3 : 0)}"));
            Debug.Log($"[Road] {m.name} size {r.bounds.size} :: {mats}");
        }
    }
}
