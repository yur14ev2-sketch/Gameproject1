using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class Paint2DedicatedSystemSetup
{
    private const string ScenePath = "Assets/Scenes/3D.unity";
    private const string SessionKey = "Paint2DedicatedSystemSetup.Paint2PhysicsRepairV2";

    static Paint2DedicatedSystemSetup()
    {
        EditorApplication.delayCall += ApplyOnceAfterCompile;
    }

    private static void ApplyOnceAfterCompile()
    {
        if (SessionState.GetBool(SessionKey, false) || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (SceneManager.GetActiveScene().path != ScenePath)
            return;

        SessionState.SetBool(SessionKey, true);
        GameObject paint2System = FindSceneObject("2D_Playersystem_Paint2");
        if (paint2System != null && paint2System.GetComponent<PlayerMovement>() != null)
            RepairPlayerPhysicsAndCamera();
    }

    [MenuItem("Tools/Paint 2 Puzzle/Create Dedicated 2D Player System")]
    public static void ApplySetup()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            throw new System.InvalidOperationException($"Open '{ScenePath}' before applying this setup.");

        GameObject paintRoot = FindSceneObject("Paint");
        GameObject managerObject = FindSceneObject("GameModeManager");
        GameObject paint1System = FindSceneObject("2D_Playersystem_Paint1") ?? FindSceneObject("2D_Playersystem");
        GameObject paint2System = FindSceneObject("2D_Playersystem_Paint2");
        Transform paint2 = paintRoot != null ? paintRoot.transform.Find("paint2") : null;
        Transform puzzleRoot = paint2 != null ? paint2.Find("PuzzleRoot") : null;

        if (paint1System == null || managerObject == null || puzzleRoot == null)
            throw new MissingReferenceException("Paint 1 system, GameModeManager, or Paint 2 PuzzleRoot is missing.");

        paint1System.name = "2D_Playersystem_Paint1";

        Transform spawnPoint = puzzleRoot.Find("PlayerSpawnPoint");
        if (spawnPoint == null)
        {
            GameObject spawnObject = new GameObject("PlayerSpawnPoint");
            Undo.RegisterCreatedObjectUndo(spawnObject, "Create Paint 2 player spawn point");
            spawnPoint = spawnObject.transform;
            spawnPoint.SetParent(puzzleRoot, false);
            spawnPoint.localPosition = new Vector3(0f, -25f, -1f);
            spawnPoint.localRotation = Quaternion.identity;
        }

        if (paint2System == null)
        {
            paint2System = Object.Instantiate(paint1System);
            Undo.RegisterCreatedObjectUndo(paint2System, "Create Paint 2 player system");
            paint2System.name = "2D_Playersystem_Paint2";
            paint2System.transform.SetParent(null, true);
        }

        paint2System.transform.SetPositionAndRotation(spawnPoint.position, paint2.rotation);

        PlayerMovement paint2Movement = paint2System.GetComponent<PlayerMovement>();
        if (paint2Movement == null)
            throw new MissingComponentException("The cloned Paint 2 system has no PlayerMovement component.");

        SerializedObject movementProperties = new SerializedObject(paint2Movement);
        movementProperties.FindProperty("movementPlane").intValue = 1;
        movementProperties.ApplyModifiedPropertiesWithoutUndo();

        Camera paint2Camera = paint2System.GetComponentInChildren<Camera>(true);
        if (paint2Camera == null)
            throw new MissingComponentException("The cloned Paint 2 system has no Camera component.");

        float cameraDistance = 61.5f;
        paint2Camera.transform.SetPositionAndRotation(
            spawnPoint.position - paint2.forward * cameraDistance,
            paint2.rotation);

        paint2System.SetActive(false);

        GameModeManager manager = managerObject.GetComponent<GameModeManager>();
        if (manager == null)
            throw new MissingComponentException("GameModeManager component is missing.");

        SerializedObject managerProperties = new SerializedObject(manager);
        SerializedProperty legacyPaintingSystem = managerProperties.FindProperty("paintingPlayerSystem");
        if (legacyPaintingSystem != null)
            legacyPaintingSystem.objectReferenceValue = paint2System;
        managerProperties.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = paint2System;
        Debug.Log("[Paint 2 Puzzle] Dedicated Paint 2 player system created and linked.");
    }

    [MenuItem("Tools/Paint 2 Puzzle/Repair Player Physics And Camera")]
    public static void RepairPlayerPhysicsAndCamera()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        GameObject oldSystem = FindSceneObject("2D_Playersystem_Paint2");
        GameObject managerObject = FindSceneObject("GameModeManager");
        GameObject paintRoot = FindSceneObject("Paint");
        Transform paint2 = paintRoot != null ? paintRoot.transform.Find("paint2") : null;
        Transform puzzleRoot = paint2 != null ? paint2.Find("PuzzleRoot") : null;

        if (oldSystem == null || managerObject == null || puzzleRoot == null)
            throw new MissingReferenceException("Paint 2 player system, GameModeManager, or Paint 2 PuzzleRoot is missing.");

        PlayerMovement movement = oldSystem.GetComponent<PlayerMovement>();
        Rigidbody body = oldSystem.GetComponent<Rigidbody>();
        BoxCollider playerCollider = oldSystem.GetComponent<BoxCollider>();
        Camera paint2Camera = oldSystem.GetComponentInChildren<Camera>(true);

        if (movement == null || body == null || playerCollider == null || paint2Camera == null)
            throw new MissingComponentException("Paint 2 player physics or camera components are incomplete.");

        GameObject stableSystem = FindSceneObject("2D_Playersystem_Paint2_Root");
        if (stableSystem == null)
        {
            stableSystem = new GameObject("2D_Playersystem_Paint2_Root");
            Undo.RegisterCreatedObjectUndo(stableSystem, "Create stable Paint 2 system root");
            stableSystem.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        bool wasActive = oldSystem.activeSelf;
        stableSystem.SetActive(true);
        oldSystem.name = "PlayerPhysics";
        oldSystem.transform.SetParent(stableSystem.transform, true);
        paint2Camera.transform.SetParent(stableSystem.transform, true);
        stableSystem.name = "2D_Playersystem_Paint2";

        Transform existingGround = puzzleRoot.Find("Paint2GameplayGround");
        GameObject groundObject;
        if (existingGround == null)
        {
            groundObject = new GameObject("Paint2GameplayGround");
            Undo.RegisterCreatedObjectUndo(groundObject, "Create Paint 2 gameplay ground");
            groundObject.transform.SetParent(puzzleRoot, true);
            groundObject.AddComponent<BoxCollider>();
        }
        else
        {
            groundObject = existingGround.gameObject;
        }

        Bounds playerBounds = playerCollider.bounds;
        float groundThickness = 4f;
        groundObject.transform.SetPositionAndRotation(
            new Vector3(playerBounds.center.x, playerBounds.min.y - groundThickness * 0.5f, playerBounds.center.z),
            Quaternion.identity);
        groundObject.transform.localScale = Vector3.one;

        BoxCollider groundCollider = groundObject.GetComponent<BoxCollider>();
        if (groundCollider == null)
            groundCollider = groundObject.AddComponent<BoxCollider>();
        groundCollider.isTrigger = false;
        groundCollider.center = Vector3.zero;
        groundCollider.size = new Vector3(8f, groundThickness, 180f);

        GameModeManager manager = managerObject.GetComponent<GameModeManager>();
        SerializedObject managerProperties = new SerializedObject(manager);
        SerializedProperty legacyPaintingSystem = managerProperties.FindProperty("paintingPlayerSystem");
        if (legacyPaintingSystem != null)
            legacyPaintingSystem.objectReferenceValue = stableSystem;
        managerProperties.ApplyModifiedPropertiesWithoutUndo();

        oldSystem.SetActive(true);
        stableSystem.SetActive(wasActive);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = stableSystem;
        Debug.Log("[Paint 2 Puzzle] Repaired hierarchy: camera is stable and Paint2GameplayGround now supports PlayerPhysics.");
    }

    private static GameObject FindSceneObject(string objectName)
    {
        foreach (Transform transform in Object.FindObjectsOfType<Transform>(true))
        {
            if (transform.gameObject.scene.IsValid() && transform.name == objectName)
                return transform.gameObject;
        }

        return null;
    }
}
