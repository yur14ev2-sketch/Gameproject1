using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class MultiPaintingEntrySetup
{
    private const string ScenePath = "Assets/Scenes/3D.unity";

    static MultiPaintingEntrySetup()
    {
        EditorApplication.delayCall += ApplyExposureOnce;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += ApplyExposureOnce;
        };
    }

    [DidReloadScripts]
    private static void RestoreRoomModeAfterHotReload()
    {
        if (!EditorApplication.isPlaying)
            return;

        EditorApplication.delayCall += () =>
        {
            GameModeManager manager = Object.FindObjectOfType<GameModeManager>();
            if (manager != null)
                manager.ForceRoomMode();
        };
    }

    private static void ApplyExposureOnce()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != ScenePath)
            return;

        if (FindSceneObject("Painting_Cameras") == null)
            ApplySetup();
        else if (FindSceneObject("Painting_PlayerSpawns") == null)
            ConfigurePlayerSpawns();
    }

    [MenuItem("Tools/Paintings/Configure Independent Painting Views")]
    public static void ApplySetup()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            throw new System.InvalidOperationException($"Open '{ScenePath}' before configuring painting views.");

        GameObject paintCollection = FindSceneObject("Paint");
        GameObject table = FindSceneObject("Table");
        GameObject managerObject = FindSceneObject("GameModeManager");
        if (paintCollection == null || table == null || managerObject == null)
            throw new MissingReferenceException("Paint, Table, or GameModeManager is missing.");

        var paintRoots = new[]
        {
            paintCollection.transform.Find("paint1"),
            paintCollection.transform.Find("paint2"),
            paintCollection.transform.Find("paint3")
        };

        foreach (Transform paintRoot in paintRoots)
        {
            if (paintRoot == null)
                throw new MissingReferenceException("paint1, paint2, or paint3 is missing under Paint.");
        }

        GameObject paint1System = EnsureStableSystem(FindSceneObject("2D_Playersystem_Paint1"), "2D_Playersystem_Paint1");
        GameObject paint2System = EnsureStableSystem(FindSceneObject("2D_Playersystem_Paint2"), "2D_Playersystem_Paint2");
        if (paint1System == null || paint2System == null)
            throw new MissingReferenceException("Paint 1 or Paint 2 player system is missing.");

        GameObject paint3System = FindSceneObject("2D_Playersystem_Paint3");
        if (paint3System == null)
        {
            paint3System = Object.Instantiate(paint1System);
            Undo.RegisterCreatedObjectUndo(paint3System, "Create Paint 3 player system");
            paint3System.name = "2D_Playersystem_Paint3";

            Bounds paint1Bounds = CalculateBounds(paintRoots[0]);
            Bounds paint3Bounds = CalculateBounds(paintRoots[2]);
            Transform physics = FindChildComponent<PlayerMovement>(paint3System.transform)?.transform;
            if (physics != null)
                physics.position += paint3Bounds.center - paint1Bounds.center;
        }

        var systems = new[] { paint1System, paint2System, paint3System };
        Bounds tableBounds = CalculateBounds(table.transform);
        Transform cameraGroup = GetOrCreateRoot("Painting_Cameras");
        var cameras = new Camera[paintRoots.Length];

        for (int i = 0; i < paintRoots.Length; i++)
        {
            Transform entryPoint = GetOrCreateEntryPoint(paintRoots[i]);
            ConfigureCameraAndMovement(paintRoots[i], systems[i], tableBounds.center);
            cameras[i] = systems[i].GetComponentInChildren<Camera>(true);
            cameras[i].name = $"Camera_Paint{i + 1}";
            cameras[i].transform.SetParent(cameraGroup, true);
            cameras[i].gameObject.SetActive(false);
            systems[i].SetActive(false);
        }

        GameModeManager manager = managerObject.GetComponent<GameModeManager>();
        if (manager == null)
            throw new MissingComponentException("GameModeManager component is missing.");

        SerializedObject managerProperties = new SerializedObject(manager);
        SerializedProperty entries = managerProperties.FindProperty("paintings");
        entries.arraySize = paintRoots.Length;

        for (int i = 0; i < paintRoots.Length; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("paintingName").stringValue = $"Paint {i + 1}";
            entry.FindPropertyRelative("interactionPoint").objectReferenceValue =
                paintRoots[i].Find("PaintingEntryPoint");
            entry.FindPropertyRelative("playerSystem").objectReferenceValue = systems[i];
            entry.FindPropertyRelative("paintingCamera").objectReferenceValue = cameras[i];
            entry.FindPropertyRelative("playerSpawnPoint").objectReferenceValue =
                GetOrCreateSpawnPoint(i, paintRoots[i], systems[i], cameras[i]);
        }

        managerProperties.FindProperty("interactionDistance").floatValue = 170f;
        managerProperties.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = managerObject;
        Debug.Log("[Paintings] Camera_Paint1/2/3 are exposed under Painting_Cameras for manual adjustment.");
    }

    [MenuItem("Tools/Paintings/Create Adjustable Player Spawn Points")]
    public static void ConfigurePlayerSpawns()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            throw new System.InvalidOperationException($"Open '{ScenePath}' before creating painting spawn points.");

        GameObject paintCollection = FindSceneObject("Paint");
        GameObject managerObject = FindSceneObject("GameModeManager");
        GameModeManager manager = managerObject != null ? managerObject.GetComponent<GameModeManager>() : null;
        if (paintCollection == null || manager == null)
            throw new MissingReferenceException("Paint or GameModeManager is missing.");

        SerializedObject managerProperties = new SerializedObject(manager);
        SerializedProperty entries = managerProperties.FindProperty("paintings");

        for (int i = 0; i < entries.arraySize; i++)
        {
            Transform paintRoot = paintCollection.transform.Find($"paint{i + 1}");
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            GameObject system = entry.FindPropertyRelative("playerSystem").objectReferenceValue as GameObject;
            Camera camera = entry.FindPropertyRelative("paintingCamera").objectReferenceValue as Camera;
            if (paintRoot == null || system == null || camera == null)
                continue;

            entry.FindPropertyRelative("playerSpawnPoint").objectReferenceValue =
                GetOrCreateSpawnPoint(i, paintRoot, system, camera);
        }

        managerProperties.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Paintings] Adjustable PlayerSpawn_Paint1/2/3 points created and assigned.");
    }

    private static Transform GetOrCreateSpawnPoint(int index, Transform paintRoot, GameObject system, Camera camera)
    {
        Transform group = GetOrCreateRoot("Painting_PlayerSpawns");
        string spawnName = $"PlayerSpawn_Paint{index + 1}";
        Transform spawn = group.Find(spawnName);
        if (spawn == null)
        {
            GameObject spawnObject = new GameObject(spawnName);
            Undo.RegisterCreatedObjectUndo(spawnObject, $"Create {spawnName}");
            spawn = spawnObject.transform;
            spawn.SetParent(group, false);
        }

        Bounds paintingBounds = CalculateBounds(paintRoot);
        PlayerMovement movement = system.GetComponentInChildren<PlayerMovement>(true);
        Collider playerCollider = movement != null ? movement.GetComponent<Collider>() : null;
        float playerHalfHeight = playerCollider != null ? playerCollider.bounds.extents.y : 1f;

        Vector3 position = paintingBounds.center + camera.transform.forward * 2f;
        position.y = paintingBounds.min.y + paintingBounds.size.y * 0.12f + playerHalfHeight;
        spawn.SetPositionAndRotation(position, Quaternion.identity);
        spawn.localScale = Vector3.one;
        return spawn;
    }

    private static GameObject EnsureStableSystem(GameObject system, string finalName)
    {
        if (system == null || system.GetComponent<PlayerMovement>() == null)
            return system;

        bool wasActive = system.activeSelf;
        Camera camera = system.GetComponentInChildren<Camera>(true);
        GameObject stableRoot = new GameObject(finalName + "_StableRoot");
        Undo.RegisterCreatedObjectUndo(stableRoot, "Create stable painting player root");
        stableRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        system.name = "PlayerPhysics";
        system.SetActive(true);
        system.transform.SetParent(stableRoot.transform, true);
        if (camera != null)
            camera.transform.SetParent(stableRoot.transform, true);

        stableRoot.name = finalName;
        stableRoot.SetActive(wasActive);
        return stableRoot;
    }

    private static Transform GetOrCreateEntryPoint(Transform paintRoot)
    {
        Transform point = paintRoot.Find("PaintingEntryPoint");
        if (point == null)
        {
            GameObject pointObject = new GameObject("PaintingEntryPoint");
            Undo.RegisterCreatedObjectUndo(pointObject, "Create painting entry point");
            point = pointObject.transform;
            point.SetParent(paintRoot, true);
        }

        point.position = CalculateBounds(paintRoot).center;
        point.rotation = paintRoot.rotation;
        point.localScale = Vector3.one;
        return point;
    }

    private static void ConfigureCameraAndMovement(Transform paintRoot, GameObject system, Vector3 roomTarget)
    {
        Camera camera = system.GetComponentInChildren<Camera>(true);
        PlayerMovement movement = FindChildComponent<PlayerMovement>(system.transform);
        if (camera == null || movement == null)
            throw new MissingComponentException($"{system.name} requires a Camera and PlayerMovement.");

        Bounds paintingBounds = CalculateBounds(paintRoot);
        Vector3 viewDirection = roomTarget - paintingBounds.center;
        viewDirection.y = 0f;
        if (viewDirection.sqrMagnitude < 0.01f)
            viewDirection = paintRoot.forward;
        viewDirection.Normalize();

        camera.transform.SetPositionAndRotation(
            paintingBounds.center - viewDirection * 2f,
            Quaternion.LookRotation(viewDirection, Vector3.up));
        camera.transform.localScale = Vector3.one;
        camera.orthographic = true;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 1000f;
        camera.cullingMask = ~0;

        Vector3 right = camera.transform.right;
        float halfWidth = 0f;
        float halfHeight = 0f;
        foreach (Vector3 corner in GetBoundsCorners(paintingBounds))
        {
            Vector3 offset = corner - paintingBounds.center;
            halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(offset, right)));
            halfHeight = Mathf.Max(halfHeight, Mathf.Abs(Vector3.Dot(offset, Vector3.up)));
        }

        const float targetAspect = 16f / 9f;
        camera.orthographicSize = Mathf.Max(1f, Mathf.Max(halfHeight, halfWidth / targetAspect) * 0.98f);

        SerializedObject movementProperties = new SerializedObject(movement);
        int movementPlane = Mathf.Abs(right.x) >= Mathf.Abs(right.z) ? 0 : 1;
        movementProperties.FindProperty("movementPlane").intValue = movementPlane;
        movementProperties.ApplyModifiedPropertiesWithoutUndo();

        Paint2ObservationController oldController = camera.GetComponent<Paint2ObservationController>();
        if (oldController != null)
            oldController.enabled = false;
    }

    private static Bounds CalculateBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return new Bounds(root.position, Vector3.one * 10f);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static IEnumerable<Vector3> GetBoundsCorners(Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        for (int x = 0; x <= 1; x++)
        for (int y = 0; y <= 1; y++)
        for (int z = 0; z <= 1; z++)
            yield return new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
    }

    private static T FindChildComponent<T>(Transform root) where T : Component
    {
        return root.GetComponentInChildren<T>(true);
    }

    private static Transform GetOrCreateRoot(string objectName)
    {
        GameObject existing = FindSceneObject(objectName);
        if (existing != null)
            return existing.transform;

        GameObject root = new GameObject(objectName);
        Undo.RegisterCreatedObjectUndo(root, $"Create {objectName}");
        return root.transform;
    }

    private static GameObject FindSceneObject(string objectName)
    {
        foreach (Transform candidate in Object.FindObjectsOfType<Transform>(true))
        {
            if (candidate.gameObject.scene.IsValid() && candidate.name == objectName)
                return candidate.gameObject;
        }
        return null;
    }
}
