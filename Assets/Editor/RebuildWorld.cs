using UnityEditor;
using UnityEngine;

/// <summary>
/// Menu: OverPeak > Rebuild Whole World
/// Recreates the complete City scene from the project's assets, in order:
/// town, hero (Superman), downtown, islands + sea + bridge, destruction, people + traffic.
/// </summary>
public static class RebuildWorld
{
    [MenuItem("OverPeak/Rebuild Whole World")]
    static void FromMenu()
    {
        if (!EditorUtility.DisplayDialog("Rebuild Whole World?",
                "Rebuilds City.unity with everything: town, Superman, downtown, sea and bridge, destruction, people and traffic, space.\n\nTakes about a minute.",
                "Rebuild", "Cancel")) return;
        Run();
    }

    public static void Run()
    {
        Step("town", 0.0f); TownSceneBuilder.Build();
        Step("hero", 0.15f); HeroSceneSetup.Run();
        Step("Superman", 0.3f); SupermanSetup.Run(); CapeSetup.Run();
        Step("downtown", 0.45f); DowntownSetup.Run();
        Step("islands, sea and bridge", 0.6f); WorldBuilder.Run();
        Step("destruction", 0.75f); DestructionSetup.Run();
        Step("people and traffic", 0.85f); LifeSetup.Run();
        Step("space", 0.93f); SpaceSetup.Run();
        EditorUtility.ClearProgressBar();
        Debug.Log("[Rebuild] DONE - whole world rebuilt");
    }

    static void Step(string what, float progress)
    {
        Debug.Log("[Rebuild] " + what);
        if (!Application.isBatchMode) EditorUtility.DisplayProgressBar("Rebuilding the world", what, progress);
    }
}
