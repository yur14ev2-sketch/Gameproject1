using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class Paint1NpcSetup
{
    private const string ScenePath = "Assets/Scenes/3D.unity";

    [MenuItem("Tools/Game Project/Setup Paint 1 NPC Sequence")]
    public static void Setup()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Transform paint1 = FindSceneTransform("paint1");
        GameModeManager manager = Object.FindObjectOfType<GameModeManager>();
        Camera paint1Camera = FindCamera("Camera_Paint1");

        if (paint1 == null || manager == null || paint1Camera == null)
        {
            Debug.LogError("[Paint1 NPC Setup] paint1, GameModeManager, or Camera_Paint1 is missing.");
            return;
        }

        Transform sequenceRoot = GetOrCreateChild(paint1, "Paint1_NPC_Sequence");
        sequenceRoot.localPosition = Vector3.zero;
        sequenceRoot.localRotation = Quaternion.identity;
        sequenceRoot.localScale = Vector3.one;

        Transform initialAnchor = GetOrCreateChild(sequenceRoot, "NPC_Initial_Anchor");
        initialAnchor.localPosition = new Vector3(-28f, -29.5f, -0.2f);
        initialAnchor.localRotation = Quaternion.identity;

        Transform victoryAnchor = GetOrCreateChild(sequenceRoot, "NPC_Victory_Anchor");
        victoryAnchor.localPosition = new Vector3(-28f, 13f, -0.2f);
        victoryAnchor.localRotation = Quaternion.identity;

        Transform npc = GetOrCreateNpcPlaceholder(sequenceRoot);
        npc.SetPositionAndRotation(initialAnchor.position, initialAnchor.rotation);
        npc.gameObject.SetActive(false);

        Transform triggerTransform = GetOrCreateChild(sequenceRoot, "Paint1_Victory_Trigger");
        triggerTransform.localPosition = new Vector3(0f, 15f, 0f);
        triggerTransform.localRotation = Quaternion.identity;
        triggerTransform.localScale = Vector3.one;

        BoxCollider trigger = triggerTransform.GetComponent<BoxCollider>();
        if (trigger == null)
            trigger = triggerTransform.gameObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = Vector3.zero;
        trigger.size = new Vector3(56f, 12f, 4f);

        Paint1NpcSequence2D sequence = sequenceRoot.GetComponent<Paint1NpcSequence2D>();
        if (sequence == null)
            sequence = sequenceRoot.gameObject.AddComponent<Paint1NpcSequence2D>();

        foreach (Paint1VictoryTrigger2D existingTrigger in
                 triggerTransform.GetComponents<Paint1VictoryTrigger2D>())
        {
            Object.DestroyImmediate(existingTrigger);
        }

        Paint1VictoryTrigger2D victoryTrigger =
            triggerTransform.gameObject.AddComponent<Paint1VictoryTrigger2D>();
        victoryTrigger.Configure(sequence);

        SerializedObject serializedSequence = new SerializedObject(sequence);
        serializedSequence.FindProperty("gameModeManager").objectReferenceValue = manager;
        serializedSequence.FindProperty("paint1Camera").objectReferenceValue = paint1Camera;
        serializedSequence.FindProperty("npcPlaceholder").objectReferenceValue = npc;
        serializedSequence.FindProperty("initialNpcAnchor").objectReferenceValue = initialAnchor;
        serializedSequence.FindProperty("victoryNpcAnchor").objectReferenceValue = victoryAnchor;
        serializedSequence.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(sequence);
        EditorUtility.SetDirty(victoryTrigger);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Selection.activeGameObject = sequenceRoot.gameObject;
        Debug.Log("[Paint1 NPC Setup] Flat NPC, speech bubble dialogue, and automatic progression installed.");
    }

    private static Transform GetOrCreateNpcPlaceholder(Transform parent)
    {
        Transform existing = parent.Find("NPC_Placeholder");
        GameObject npc = existing != null
            ? existing.gameObject
            : new GameObject("NPC_Placeholder");
        npc.transform.SetParent(parent, false);

        while (npc.transform.childCount > 0)
            Object.DestroyImmediate(npc.transform.GetChild(0).gameObject);

        GameObject visual = new GameObject(
            "NPC_2D_Visual",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler));
        visual.transform.SetParent(npc.transform, false);
        RectTransform visualRect = visual.GetComponent<RectTransform>();
        visualRect.localPosition = new Vector3(0f, 1.15f, -0.08f);
        visualRect.localRotation = Quaternion.identity;
        visualRect.localScale = Vector3.one * 0.01f;
        visualRect.sizeDelta = new Vector2(100f, 230f);

        Canvas canvas = visual.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 240;

        GameObject body = CreateImage("Flat Body", visual.transform);
        RectTransform bodyRect = body.GetComponent<RectTransform>();
        bodyRect.anchorMin = new Vector2(0.5f, 0f);
        bodyRect.anchorMax = new Vector2(0.5f, 0f);
        bodyRect.pivot = new Vector2(0.5f, 0f);
        bodyRect.anchoredPosition = Vector2.zero;
        bodyRect.sizeDelta = new Vector2(72f, 142f);
        body.GetComponent<Image>().color = new Color(0.1f, 0.78f, 0.9f, 1f);

        GameObject head = CreateImage("Flat Head", visual.transform);
        RectTransform headRect = head.GetComponent<RectTransform>();
        headRect.anchorMin = new Vector2(0.5f, 0f);
        headRect.anchorMax = new Vector2(0.5f, 0f);
        headRect.pivot = new Vector2(0.5f, 0f);
        headRect.anchoredPosition = new Vector2(0f, 142f);
        headRect.sizeDelta = new Vector2(92f, 78f);
        head.GetComponent<Image>().color = new Color(0.18f, 0.9f, 1f, 1f);

        GameObject marker = CreateImage("Dialogue Marker", visual.transform);
        RectTransform markerRect = marker.GetComponent<RectTransform>();
        markerRect.anchorMin = new Vector2(0.5f, 0f);
        markerRect.anchorMax = new Vector2(0.5f, 0f);
        markerRect.pivot = new Vector2(0.5f, 0f);
        markerRect.anchoredPosition = new Vector2(0f, 224f);
        markerRect.sizeDelta = new Vector2(30f, 30f);
        markerRect.localRotation = Quaternion.Euler(0f, 0f, 45f);
        marker.GetComponent<Image>().color = new Color(1f, 0.76f, 0.12f, 1f);

        return npc.transform;
    }

    private static GameObject CreateImage(string objectName, Transform parent)
    {
        GameObject imageObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(Image));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.GetComponent<Image>();
        image.raycastTarget = false;
        return imageObject;
    }

    private static Transform GetOrCreateChild(Transform parent, string childName)
    {
        Transform existing = parent.Find(childName);
        if (existing != null)
            return existing;

        GameObject child = new GameObject(childName);
        child.transform.SetParent(parent, false);
        return child.transform;
    }

    private static Camera FindCamera(string objectName)
    {
        Transform transform = FindSceneTransform(objectName);
        return transform != null ? transform.GetComponent<Camera>() : null;
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
}
