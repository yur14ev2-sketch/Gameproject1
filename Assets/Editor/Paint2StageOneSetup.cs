using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Paint2StageOneSetup
{
    private const string ScenePath = "Assets/Scenes/3D.unity";

    [MenuItem("Tools/Paint 2 Puzzle/Apply Stage 1 Structure")]
    public static void ApplyStageOneStructure()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            throw new System.InvalidOperationException($"Open '{ScenePath}' before applying Stage 1.");
        }

        GameObject paintRoot = FindRoot(scene, "Paint");
        GameObject table = FindRoot(scene, "Table");
        Transform paint2 = FindDirectChild(paintRoot.transform, "paint2");

        Transform paintModel = GetOrCreateChild(paint2, "Model");
        Transform puzzleRoot = GetOrCreateChild(paint2, "PuzzleRoot");

        MoveExistingChildren(paint2, paintModel, puzzleRoot);
        GetOrCreateChild(puzzleRoot, "ViewPoint");
        GetOrCreateChild(puzzleRoot, "ProjectionArea");
        GetOrCreateChild(puzzleRoot, "InteractionPoint");

        Transform tableModel = GetOrCreateChild(table.transform, "Model");
        Transform puzzleAnchor = GetOrCreateChild(table.transform, "PuzzleAnchor");
        MoveExistingChildren(table.transform, tableModel, puzzleAnchor);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = paint2.gameObject;

        Debug.Log("[Paint 2 Puzzle] Stage 1 structure applied and 3D scene saved.");
    }

    private static GameObject FindRoot(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == objectName)
            {
                return root;
            }
        }

        throw new MissingReferenceException($"Root object '{objectName}' was not found in {ScenePath}.");
    }

    private static Transform FindDirectChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child == null)
        {
            throw new MissingReferenceException($"Child object '{childName}' was not found under '{parent.name}'.");
        }

        return child;
    }

    private static Transform GetOrCreateChild(Transform parent, string childName)
    {
        Transform existing = parent.Find(childName);
        if (existing != null)
        {
            return existing;
        }

        GameObject child = new GameObject(childName);
        Undo.RegisterCreatedObjectUndo(child, $"Create {childName}");
        child.transform.SetParent(parent, false);
        child.transform.localPosition = Vector3.zero;
        child.transform.localRotation = Quaternion.identity;
        child.transform.localScale = Vector3.one;
        return child.transform;
    }

    private static void MoveExistingChildren(Transform source, Transform model, Transform logicRoot)
    {
        var childrenToMove = new List<Transform>();

        foreach (Transform child in source)
        {
            if (child != model && child != logicRoot)
            {
                childrenToMove.Add(child);
            }
        }

        foreach (Transform child in childrenToMove)
        {
            Undo.SetTransformParent(child, model, "Organize puzzle model hierarchy");
        }
    }
}
