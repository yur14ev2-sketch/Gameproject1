using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Paint2StageTwoSetup
{
    private const string ScenePath = "Assets/Scenes/3D.unity";

    [MenuItem("Tools/Paint 2 Puzzle/Apply Stage 2 Observation Setup")]
    public static void ApplyStageTwoSetup()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            throw new System.InvalidOperationException($"Open '{ScenePath}' before applying Stage 2.");

        PlayerMovement player = Object.FindObjectOfType<PlayerMovement>();
        Camera mainCamera = Camera.main;
        GameObject paint = FindRoot(scene, "Paint");
        Transform paint2 = paint.transform.Find("paint2");
        Transform puzzleRoot = paint2 != null ? paint2.Find("PuzzleRoot") : null;
        Transform viewPoint = puzzleRoot != null ? puzzleRoot.Find("ViewPoint") : null;
        Transform interactionPoint = puzzleRoot != null ? puzzleRoot.Find("InteractionPoint") : null;

        if (player == null || mainCamera == null || viewPoint == null || interactionPoint == null)
            throw new MissingReferenceException("Stage 2 requires PlayerMovement, Main Camera, ViewPoint, and InteractionPoint.");

        if (viewPoint.localPosition == Vector3.zero)
        {
            Undo.RecordObject(viewPoint, "Place Paint 2 view point");
            viewPoint.localPosition = new Vector3(0f, 0f, -40f);
            viewPoint.localRotation = Quaternion.identity;
        }

        Paint2ObservationController controller = mainCamera.GetComponent<Paint2ObservationController>();
        if (controller == null)
            controller = Undo.AddComponent<Paint2ObservationController>(mainCamera.gameObject);

        SerializedObject serializedController = new SerializedObject(controller);
        serializedController.FindProperty("playerMovement").objectReferenceValue = player;
        serializedController.FindProperty("viewPoint").objectReferenceValue = viewPoint;
        serializedController.FindProperty("interactionPoint").objectReferenceValue = interactionPoint;
        serializedController.FindProperty("toggleKey").intValue = (int)KeyCode.E;
        serializedController.FindProperty("interactionDistance").floatValue = 75f;
        serializedController.FindProperty("transitionDuration").floatValue = 0.35f;
        serializedController.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = mainCamera.gameObject;
        Debug.Log("[Paint 2 Puzzle] Stage 2 observation setup applied and 3D scene saved.");
    }

    private static GameObject FindRoot(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == objectName)
                return root;
        }

        throw new MissingReferenceException($"Root object '{objectName}' was not found.");
    }
}
