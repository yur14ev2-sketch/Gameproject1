#if false // Superseded by Paint1NpcSequence2D. Kept temporarily for migration reference.
using System;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class Paint1NpcSequence : MonoBehaviour
{
    private enum SequenceState
    {
        WaitingForFirstEntry,
        TutorialDialogue,
        TutorialFinished,
        VictoryReached
    }

    [Header("Scene References")]
    [SerializeField] private GameModeManager gameModeManager;
    [SerializeField] private Camera paint1Camera;
    [SerializeField] private Transform npcPlaceholder;
    [SerializeField] private Transform initialNpcAnchor;
    [SerializeField] private Transform victoryNpcAnchor;

    [Header("Dialogue - fill these in later")]
    [SerializeField, TextArea(3, 8)] private string tutorialText = "";
    [SerializeField, TextArea(3, 8)] private string victoryText = "";
    [SerializeField, Min(0.1f)] private float talkDistance = 4f;

    private SequenceState state = SequenceState.WaitingForFirstEntry;
    private GameObject dialogueCanvas;
    private Text dialogueBody;
    private bool dialogueOpen;
    private bool wasInsidePaint1;
    private bool waitForEntryKeyRelease;

    private bool IsInsidePaint1 =>
        gameModeManager != null &&
        gameModeManager.IsInsidePainting &&
        paint1Camera != null &&
        paint1Camera.gameObject.activeInHierarchy &&
        paint1Camera.enabled;

    private void Awake()
    {
        if (gameModeManager == null)
            gameModeManager = FindObjectOfType<GameModeManager>();

        BuildDialogueUi();
        SetNpcVisible(false);
    }

    private void Update()
    {
        bool insidePaint1 = IsInsidePaint1;

        if (insidePaint1 && !wasInsidePaint1)
            HandlePaint1Entered();
        else if (!insidePaint1 && wasInsidePaint1)
            HandlePaint1Exited();

        wasInsidePaint1 = insidePaint1;

        if (!insidePaint1)
            return;

        if (waitForEntryKeyRelease)
        {
            if (Input.GetKeyUp(KeyCode.E))
                waitForEntryKeyRelease = false;
            return;
        }

        if (state == SequenceState.TutorialDialogue &&
            dialogueOpen && Input.GetKeyDown(KeyCode.E))
        {
            FinishTutorial();
            return;
        }

        if (state == SequenceState.VictoryReached && Input.GetKeyDown(KeyCode.E))
        {
            if (dialogueOpen)
                SetDialogueVisible(false);
            else if (IsPlayerCloseToNpc())
                ShowDialogue(victoryText);
        }
    }

    public void NotifyVictoryReached()
    {
        if (state == SequenceState.VictoryReached)
            return;

        state = SequenceState.VictoryReached;
        SetDialogueVisible(false);
        MoveNpcTo(victoryNpcAnchor);
        SetNpcVisible(IsInsidePaint1);
    }

    private void HandlePaint1Entered()
    {
        waitForEntryKeyRelease = Input.GetKey(KeyCode.E);

        switch (state)
        {
            case SequenceState.WaitingForFirstEntry:
                state = SequenceState.TutorialDialogue;
                MoveNpcTo(initialNpcAnchor);
                SetNpcVisible(true);
                ShowDialogue(tutorialText);
                break;

            case SequenceState.TutorialDialogue:
                MoveNpcTo(initialNpcAnchor);
                SetNpcVisible(true);
                ShowDialogue(tutorialText);
                break;

            case SequenceState.TutorialFinished:
                SetNpcVisible(false);
                break;

            case SequenceState.VictoryReached:
                MoveNpcTo(victoryNpcAnchor);
                SetNpcVisible(true);
                break;
        }
    }

    private void HandlePaint1Exited()
    {
        SetDialogueVisible(false);
        SetNpcVisible(false);
    }

    private void FinishTutorial()
    {
        state = SequenceState.TutorialFinished;
        SetDialogueVisible(false);
        SetNpcVisible(false);
    }

    private void MoveNpcTo(Transform anchor)
    {
        if (npcPlaceholder == null || anchor == null)
            return;

        npcPlaceholder.SetPositionAndRotation(anchor.position, anchor.rotation);
    }

    private void SetNpcVisible(bool visible)
    {
        if (npcPlaceholder != null)
            npcPlaceholder.gameObject.SetActive(visible);
    }

    private bool IsPlayerCloseToNpc()
    {
        if (npcPlaceholder == null)
            return false;

        GameObject player = GameObject.Find("Player_2D_Runtime");
        return player != null &&
               Vector3.Distance(player.transform.position, npcPlaceholder.position) <= talkDistance;
    }

    private void ShowDialogue(string body)
    {
        if (dialogueBody != null)
            dialogueBody.text = body ?? string.Empty;

        SetDialogueVisible(true);
    }

    private void SetDialogueVisible(bool visible)
    {
        dialogueOpen = visible;
        if (dialogueCanvas != null)
            dialogueCanvas.SetActive(visible);

        PaintingPlayer2DController player = FindObjectOfType<PaintingPlayer2DController>();
        if (player != null && player.gameObject.name == "Player_2D_Runtime")
            player.SetControlEnabled(!visible && IsInsidePaint1, paint1Camera);
    }

    private void BuildDialogueUi()
    {
        Font font = LoadTextFont();

        dialogueCanvas = new GameObject(
            "Paint 1 NPC Dialogue",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        dialogueCanvas.transform.SetParent(transform, false);

        Canvas canvas = dialogueCanvas.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        CanvasScaler scaler = dialogueCanvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject panelObject = CreateUiObject<Image>("Dialogue Panel", dialogueCanvas.transform);
        RectTransform panel = panelObject.GetComponent<RectTransform>();
        panel.anchorMin = new Vector2(0.5f, 0f);
        panel.anchorMax = new Vector2(0.5f, 0f);
        panel.pivot = new Vector2(0.5f, 0f);
        panel.anchoredPosition = new Vector2(0f, 54f);
        panel.sizeDelta = new Vector2(1120f, 230f);
        panelObject.GetComponent<Image>().color = new Color(0.035f, 0.045f, 0.075f, 0.94f);

        GameObject nameObject = CreateUiObject<Text>("Speaker", panel);
        RectTransform nameRect = nameObject.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0f, 1f);
        nameRect.anchorMax = new Vector2(0f, 1f);
        nameRect.pivot = new Vector2(0f, 1f);
        nameRect.anchoredPosition = new Vector2(34f, -22f);
        nameRect.sizeDelta = new Vector2(260f, 45f);
        Text speaker = nameObject.GetComponent<Text>();
        speaker.font = font;
        speaker.fontSize = 28;
        speaker.fontStyle = FontStyle.Bold;
        speaker.color = new Color(0.35f, 0.88f, 1f, 1f);
        speaker.text = "NPC";

        GameObject bodyObject = CreateUiObject<Text>("Dialogue Text", panel);
        RectTransform bodyRect = bodyObject.GetComponent<RectTransform>();
        bodyRect.anchorMin = Vector2.zero;
        bodyRect.anchorMax = Vector2.one;
        bodyRect.offsetMin = new Vector2(34f, 52f);
        bodyRect.offsetMax = new Vector2(-34f, -68f);
        dialogueBody = bodyObject.GetComponent<Text>();
        dialogueBody.font = font;
        dialogueBody.fontSize = 25;
        dialogueBody.color = Color.white;
        dialogueBody.alignment = TextAnchor.UpperLeft;

        GameObject hintObject = CreateUiObject<Text>("Continue Hint", panel);
        RectTransform hintRect = hintObject.GetComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(1f, 0f);
        hintRect.anchorMax = new Vector2(1f, 0f);
        hintRect.pivot = new Vector2(1f, 0f);
        hintRect.anchoredPosition = new Vector2(-28f, 18f);
        hintRect.sizeDelta = new Vector2(260f, 36f);
        Text hint = hintObject.GetComponent<Text>();
        hint.font = font;
        hint.fontSize = 20;
        hint.alignment = TextAnchor.MiddleRight;
        hint.color = new Color(0.72f, 0.76f, 0.84f, 1f);
        hint.text = "按 E 继续";

        SetDialogueVisible(false);
    }

    private static GameObject CreateUiObject<T>(string objectName, Transform parent)
        where T : Component
    {
        GameObject uiObject = new GameObject(objectName, typeof(RectTransform), typeof(T));
        uiObject.transform.SetParent(parent, false);
        return uiObject;
    }

    private static Font LoadTextFont()
    {
        Font font = null;

        try
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        catch (ArgumentException)
        {
            // Some Unity versions expose Arial.ttf instead.
        }

        if (font == null)
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        return font;
    }

    private void OnDestroy()
    {
        if (dialogueCanvas != null)
            Destroy(dialogueCanvas);
    }
}

[DisallowMultipleComponent]
public sealed class Paint1VictoryTrigger : MonoBehaviour
{
    [SerializeField] private Paint1NpcSequence sequence;
    [SerializeField] private Color gizmoColor = new Color(0f, 0.75f, 1f, 0.28f);

    public void Configure(Paint1NpcSequence targetSequence)
    {
        sequence = targetSequence;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<PaintingPlayer2DController>() != null)
            sequence?.NotifyVictoryReached();
    }

    private void OnDrawGizmos()
    {
        BoxCollider area = GetComponent<BoxCollider>();
        if (area == null)
            return;

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = gizmoColor;
        Gizmos.DrawCube(area.center, area.size);
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.95f);
        Gizmos.DrawWireCube(area.center, area.size);
        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
}
#endif
