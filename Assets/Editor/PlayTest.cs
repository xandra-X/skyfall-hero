using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Runs PlayTestDriver in play mode. Command line: -batchmode -executeMethod PlayTest.Run (no -quit).</summary>
public static class PlayTest
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/City.unity", OpenSceneMode.Single);
        new GameObject("PlayTestDriver").AddComponent<PlayTestDriver>();
        EditorApplication.EnterPlaymode();
    }
}

public static class LifeTest
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/City.unity", OpenSceneMode.Single);
        new GameObject("LifeTestDriver").AddComponent<LifeTestDriver>();
        EditorApplication.EnterPlaymode();
    }
}

public static class SpaceTest
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/City.unity", OpenSceneMode.Single);
        new GameObject("SpaceTestDriver").AddComponent<SpaceTestDriver>();
        EditorApplication.EnterPlaymode();
    }
}

public static class RamTest
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/City.unity", OpenSceneMode.Single);
        new GameObject("RamTestDriver").AddComponent<RamTestDriver>();
        EditorApplication.EnterPlaymode();
    }
}

public static class CapeTest
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/City.unity", OpenSceneMode.Single);
        new GameObject("CapeTestDriver").AddComponent<CapeTestDriver>();
        EditorApplication.EnterPlaymode();
    }
}

public static class MobileTest
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/City.unity", OpenSceneMode.Single);
        new GameObject("MobileTestDriver").AddComponent<MobileTestDriver>();
        EditorApplication.EnterPlaymode();
    }
}
