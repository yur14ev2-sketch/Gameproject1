using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class PaintingSceneRepair
{
    private const string ScenePath = "Assets/Scenes/3D.unity";
    private const string PlayerPrefabPath = "Assets/Player_2D.prefab";

    static PaintingSceneRepair()
    {
        EditorApplication.delayCall += RepairIfNeeded;
        EditorSceneManager.sceneOpened += OnSceneOpened;
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        if (scene.path == ScenePath)
            EditorApplication.delayCall += RepairIfNeeded;
    }

    [MenuItem("Tools/Paintings/Repair Painting Players")]
    public static void RepairIfNeeded()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != ScenePath)
            return;

        GameModeManager manager = Object.FindObjectOfType<GameModeManager>();
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (manager == null || playerPrefab == null)
            return;

        SerializedObject managerData = new SerializedObject(manager);
        bool changed = false;
        Camera roomCamera = null;
        SerializedProperty roomSystemProperty = managerData.FindProperty("roomPlayerSystem");
        GameObject roomSystem = roomSystemProperty.objectReferenceValue as GameObject;
        if (roomSystem != null)
        {
            roomCamera = roomSystem.GetComponentInChildren<Camera>(true);
            if (roomCamera != null)
            {
                SerializedProperty roomCameraProperty = managerData.FindProperty("roomCamera");
                if (roomCameraProperty.objectReferenceValue != roomCamera)
                {
                    roomCameraProperty.objectReferenceValue = roomCamera;
                    changed = true;
                }

                if (!roomCamera.CompareTag("MainCamera"))
                {
                    roomCamera.tag = "MainCamera";
                    changed = true;
                }
            }
        }

        SerializedProperty paintings = managerData.FindProperty("paintings");

        for (int i = 0; i < paintings.arraySize; i++)
        {
            SerializedProperty entry = paintings.GetArrayElementAtIndex(i);
            Transform interactionPoint = entry.FindPropertyRelative("interactionPoint").objectReferenceValue as Transform;
            Camera paintingCamera = entry.FindPropertyRelative("paintingCamera").objectReferenceValue as Camera;
            if (interactionPoint == null || paintingCamera == null || interactionPoint.parent == null)
                continue;

            if (!paintingCamera.CompareTag("Untagged"))
            {
                paintingCamera.tag = "Untagged";
                changed = true;
            }

            Transform paintingRoot = interactionPoint.parent;
            PaintingPlayer2DController player = paintingRoot.GetComponentInChildren<PaintingPlayer2DController>(true);
            if (player == null)
            {
                GameObject instance = PrefabUtility.InstantiatePrefab(playerPrefab, paintingRoot) as GameObject;
                if (instance == null)
                    continue;

                instance.name = "Player_2D";
                player = instance.GetComponent<PaintingPlayer2DController>();
                changed = true;
            }

            Transform spawn = paintingRoot.Find("PlayerSpawnPoint");
            if (spawn == null)
            {
                GameObject spawnObject = new GameObject("PlayerSpawnPoint");
                spawn = spawnObject.transform;
                spawn.SetParent(paintingRoot, false);
                changed = true;
            }

            Bounds paintingBounds = CalculatePaintingBounds(paintingRoot, player.transform, spawn);
            Collider playerCollider = player.GetComponent<Collider>();
            float halfHeight = playerCollider != null ? playerCollider.bounds.extents.y : 0.5f;
            Vector3 spawnPosition = paintingBounds.center + paintingCamera.transform.forward * 0.5f;
            spawnPosition.y = paintingBounds.min.y + halfHeight + 0.05f;
            spawn.SetPositionAndRotation(spawnPosition, Quaternion.identity);
            spawn.localScale = Vector3.one;

            player.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            player.gameObject.SetActive(false);

            SerializedProperty playerSystem = entry.FindPropertyRelative("playerSystem");
            SerializedProperty spawnPoint = entry.FindPropertyRelative("playerSpawnPoint");
            if (playerSystem.objectReferenceValue != player.gameObject || spawnPoint.objectReferenceValue != spawn)
            {
                playerSystem.objectReferenceValue = player.gameObject;
                spawnPoint.objectReferenceValue = spawn;
                changed = true;
            }
        }

        if (!changed)
            return;

        managerData.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[Paintings] Repaired missing Player_2D instances and spawn points for configured paintings.");
    }

    private static Bounds CalculatePaintingBounds(Transform root, Transform player, Transform spawn)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool initialized = false;
        Bounds bounds = new Bounds(root.position, Vector3.one);

        foreach (Renderer renderer in renderers)
        {
            if (renderer.transform == player || renderer.transform.IsChildOf(player) ||
                renderer.transform == spawn || renderer.transform.IsChildOf(spawn))
                continue;

            if (!initialized)
            {
                bounds = renderer.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return bounds;
    }

}
