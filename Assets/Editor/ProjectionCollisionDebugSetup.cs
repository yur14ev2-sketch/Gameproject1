using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ProjectionCollisionDebugSetup
{
    private const string SessionKey = "ProjectionCollisionDebugSetup.v1";
    private const string PlayerPrefabPath = "Assets/Player_2D.prefab";

    static ProjectionCollisionDebugSetup()
    {
        EditorApplication.delayCall += RunOnce;
    }

    [MenuItem("Tools/Painting Projection/Ensure Collision Debuggers")]
    public static void EnsureCollisionDebuggers()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        bool sceneChanged = false;
        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid() && scene.name == "3D")
        {
            GameObject manager = GameObject.Find("GameModeManager");
            if (manager != null && manager.GetComponent<ProjectionCollisionDebugger>() == null)
            {
                Undo.AddComponent<ProjectionCollisionDebugger>(manager);
                EditorUtility.SetDirty(manager);
                sceneChanged = true;
            }
        }

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        if (prefabRoot != null)
        {
            if (prefabRoot.GetComponent<ProjectionCollisionContactProbe>() == null)
            {
                prefabRoot.AddComponent<ProjectionCollisionContactProbe>();
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PlayerPrefabPath);
            }
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        if (sceneChanged)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[ProjectionCollisionDebug] Persistent collision debuggers installed.");
    }

    private static void RunOnce()
    {
        if (SessionState.GetBool(SessionKey, false))
            return;

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            return;
        }

        SessionState.SetBool(SessionKey, true);
        EnsureCollisionDebuggers();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode)
            return;

        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        SessionState.SetBool(SessionKey, false);
        EditorApplication.delayCall += RunOnce;
    }
}
