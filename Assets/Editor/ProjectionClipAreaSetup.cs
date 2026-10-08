using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ProjectionClipAreaSetup
{
    private const string SessionKey = "ProjectionClipAreaSetup.v6";

    static ProjectionClipAreaSetup()
    {
        EditorApplication.delayCall += RunOnce;
    }

    [MenuItem("Tools/Painting Projection/Ensure Persistent Clip Areas")]
    public static void EnsurePersistentClipAreas()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[Projection Setup] Exit Play Mode before creating persistent clip areas.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "3D")
            return;

        bool changed = false;
        changed |= EnsurePersistentCaptureCamera();
        changed |= EnsurePainting("paint2", "Camera_Paint2");
        changed |= EnsurePainting("paint4", "Camera_Paint4");
        changed |= BindControllerReferences();

        if (!changed)
            return;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Projection Setup] Persistent ProjectionClipArea and output hierarchy saved for Paint 2/4.");
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
        EnsurePersistentClipAreas();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode)
            return;

        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        SessionState.SetBool(SessionKey, false);
        EditorApplication.delayCall += RunOnce;
    }

    private static bool EnsurePainting(string paintingName, string cameraName)
    {
        Transform painting = FindSceneTransform(paintingName);
        Camera paintingCamera = FindSceneCamera(cameraName);
        if (painting == null || paintingCamera == null)
            return false;

        bool changed = EnsureOutputHierarchy(painting, paintingName == "paint4");
        Transform existing = painting.Find("ProjectionClipArea");
        if (existing != null)
        {
            if (existing.GetComponent<BoxCollider>() == null)
                existing.gameObject.AddComponent<BoxCollider>().isTrigger = true;
            if (existing.GetComponent<ProjectionClipAreaHandle>() == null)
                existing.gameObject.AddComponent<ProjectionClipAreaHandle>();
            return changed;
        }

        Transform model = painting.Find("Model");
        if (model == null)
            model = painting;

        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return changed;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        GetCameraExtents(
            renderers,
            paintingCamera,
            bounds.center,
            out float minRight,
            out float maxRight,
            out float minUp,
            out float maxUp,
            out float nearestDepth);

        GameObject clipObject = new GameObject("ProjectionClipArea");
        Undo.RegisterCreatedObjectUndo(clipObject, "Create Projection Clip Area");
        clipObject.transform.SetParent(painting, true);
        clipObject.transform.rotation = paintingCamera.transform.rotation;

        float centerDepth = Vector3.Dot(
            bounds.center - paintingCamera.transform.position,
            paintingCamera.transform.forward);
        clipObject.transform.position = bounds.center + paintingCamera.transform.forward *
            (nearestDepth + 0.03f - centerDepth);
        clipObject.transform.localScale = Vector3.one;

        BoxCollider collider = clipObject.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = new Vector3(
            Mathf.Max(0.01f, (maxRight - minRight) * 0.78f),
            Mathf.Max(0.01f, (maxUp - minUp) * 0.78f),
            0.02f);
        clipObject.AddComponent<ProjectionClipAreaHandle>();
        return true;
    }

    private static bool EnsurePersistentCaptureCamera()
    {
        Transform manager = FindSceneTransform("GameModeManager");
        if (manager == null)
            return false;

        Transform existing = manager.Find("Projection_SilhouetteCamera");
        if (existing != null)
        {
            Camera existingCamera = existing.GetComponent<Camera>();
            if (existingCamera != null)
                existingCamera.enabled = false;
            return false;
        }

        GameObject cameraObject = new GameObject("Projection_SilhouetteCamera");
        Undo.RegisterCreatedObjectUndo(cameraObject, "Create Projection Silhouette Camera");
        cameraObject.transform.SetParent(manager, false);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        return true;
    }

    private static bool EnsureOutputHierarchy(Transform painting, bool addPaint4TopSupports)
    {
        bool changed = false;
        Transform puzzleRoot = painting.Find("PuzzleRoot");
        if (puzzleRoot == null)
        {
            puzzleRoot = new GameObject("PuzzleRoot").transform;
            puzzleRoot.SetParent(painting, false);
            changed = true;
        }

        Transform projectionArea = puzzleRoot.Find("ProjectionArea");
        if (projectionArea == null)
        {
            projectionArea = new GameObject("ProjectionArea").transform;
            projectionArea.SetParent(puzzleRoot, false);
            changed = true;
        }

        Transform outputRoot = projectionArea.Find("ProjectedObjects");
        if (outputRoot == null)
        {
            outputRoot = new GameObject("ProjectedObjects").transform;
            outputRoot.SetParent(projectionArea, false);
            changed = true;
        }

        changed |= EnsurePersistentOutputSlots(outputRoot, addPaint4TopSupports);

        return changed;
    }

    private static bool EnsurePersistentOutputSlots(
        Transform outputRoot,
        bool addPaint4TopSupports)
    {
        bool changed = false;
        Transform surface = outputRoot.Find("ProjectionSurface");
        if (surface == null)
        {
            surface = new GameObject("ProjectionSurface").transform;
            surface.SetParent(outputRoot, false);
            surface.gameObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = surface.gameObject.AddComponent<MeshRenderer>();
            renderer.enabled = false;
            changed = true;
        }

        const int collisionSlotCount = 16;
        for (int i = 0; i < collisionSlotCount; i++)
        {
            string slotName = $"ProjectionCollision_{i:00}";
            Transform slot = outputRoot.Find(slotName);
            if (slot == null)
            {
                slot = new GameObject(slotName).transform;
                slot.SetParent(outputRoot, false);
                changed = true;
            }

            MeshCollider collider = slot.GetComponent<MeshCollider>();
            if (collider == null)
            {
                collider = slot.gameObject.AddComponent<MeshCollider>();
                changed = true;
            }

            collider.enabled = false;
            collider.isTrigger = false;
            collider.convex = false;
            collider.cookingOptions =
                MeshColliderCookingOptions.CookForFasterSimulation |
                MeshColliderCookingOptions.EnableMeshCleaning |
                MeshColliderCookingOptions.WeldColocatedVertices;

            if (addPaint4TopSupports)
            {
                BoxCollider support = slot.GetComponent<BoxCollider>();
                if (support == null)
                {
                    support = slot.gameObject.AddComponent<BoxCollider>();
                    changed = true;
                }

                support.enabled = false;
                support.isTrigger = false;
            }
        }

        return changed;
    }

    private static bool BindControllerReferences()
    {
        Transform manager = FindSceneTransform("GameModeManager");
        Paint2ProjectionController controller =
            manager != null ? manager.GetComponent<Paint2ProjectionController>() : null;
        Transform paint2 = FindSceneTransform("paint2");
        Transform paint4 = FindSceneTransform("paint4");
        if (controller == null || paint2 == null || paint4 == null)
            return false;

        BoxCollider paint2Area =
            paint2.Find("ProjectionClipArea")?.GetComponent<BoxCollider>();
        BoxCollider paint4Area =
            paint4.Find("ProjectionClipArea")?.GetComponent<BoxCollider>();
        if (paint2Area == null || paint4Area == null)
            return false;

        SerializedObject serializedController = new SerializedObject(controller);
        SerializedProperty paint2Property =
            serializedController.FindProperty("paint2ClipArea");
        SerializedProperty paint4Property =
            serializedController.FindProperty("paint4ClipArea");
        bool changed =
            paint2Property.objectReferenceValue != paint2Area ||
            paint4Property.objectReferenceValue != paint4Area;

        if (!changed)
            return false;

        paint2Property.objectReferenceValue = paint2Area;
        paint4Property.objectReferenceValue = paint4Area;
        serializedController.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(controller);
        return true;
    }

    private static void GetCameraExtents(
        Renderer[] renderers,
        Camera camera,
        Vector3 center,
        out float minRight,
        out float maxRight,
        out float minUp,
        out float maxUp,
        out float nearestDepth)
    {
        minRight = minUp = nearestDepth = float.PositiveInfinity;
        maxRight = maxUp = float.NegativeInfinity;

        foreach (Renderer renderer in renderers)
        {
            Bounds bounds = renderer.bounds;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            for (int x = 0; x <= 1; x++)
            for (int y = 0; y <= 1; y++)
            for (int z = 0; z <= 1; z++)
            {
                Vector3 corner = new Vector3(
                    x == 0 ? min.x : max.x,
                    y == 0 ? min.y : max.y,
                    z == 0 ? min.z : max.z);
                Vector3 fromCenter = corner - center;
                minRight = Mathf.Min(minRight, Vector3.Dot(fromCenter, camera.transform.right));
                maxRight = Mathf.Max(maxRight, Vector3.Dot(fromCenter, camera.transform.right));
                minUp = Mathf.Min(minUp, Vector3.Dot(fromCenter, camera.transform.up));
                maxUp = Mathf.Max(maxUp, Vector3.Dot(fromCenter, camera.transform.up));
                nearestDepth = Mathf.Min(
                    nearestDepth,
                    Vector3.Dot(corner - camera.transform.position, camera.transform.forward));
            }
        }
    }

    private static Transform FindSceneTransform(string objectName)
    {
        foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (candidate.gameObject.scene.IsValid() && candidate.name == objectName)
                return candidate;
        }
        return null;
    }

    private static Camera FindSceneCamera(string cameraName)
    {
        foreach (Camera candidate in Resources.FindObjectsOfTypeAll<Camera>())
        {
            if (candidate.gameObject.scene.IsValid() && candidate.name == cameraName)
                return candidate;
        }
        return null;
    }
}
