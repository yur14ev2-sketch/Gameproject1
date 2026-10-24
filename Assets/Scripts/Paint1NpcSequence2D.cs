using System;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class Paint1NpcSequence2D : MonoBehaviour
{
    public enum AdvanceMode
    {
        Timed,
        PlayerMoved,
        PlayerJumped,
        QuestionBlockActivated,
        CoinCollected,
        ReachedTarget
    }

    [Serializable]
    public sealed class DialogueLine
    {
        [TextArea(2, 6)] public string text = "";
        public AdvanceMode advanceMode = AdvanceMode.Timed;
        [Min(0.1f)] public float duration = 3f;
        public Transform target;
        [Min(0.1f)] public float targetDistance = 1.5f;
    }

    private enum SequenceState
    {
        WaitingForFirstEntry,
        TutorialDialogue,
        TutorialFinished,
        VictoryDialogue,
        VictoryFinished
    }

    private enum PresentationMode
    {
        PaintBubble,
        RoomHud
    }

    [Header("Scene References")]
    [SerializeField] private GameModeManager gameModeManager;
    [SerializeField] private Camera paint1Camera;
    [SerializeField] private Transform npcPlaceholder;
    [SerializeField] private Transform initialNpcAnchor;
    [SerializeField] private Transform victoryNpcAnchor;

    [Header("Paint 1 Dialogue")]
    [Tooltip("Each line advances after its selected gameplay condition is met.")]
    [SerializeField] private DialogueLine[] tutorialLines = Array.Empty<DialogueLine>();
    [SerializeField] private DialogueLine[] victoryLines = Array.Empty<DialogueLine>();

    [Header("Future 3D Remote Dialogue")]
    [Tooltip("Optional lines shown in the top-right HUD when BeginRoomRemoteDialogue is called.")]
    [SerializeField] private DialogueLine[] roomRemoteLines = Array.Empty<DialogueLine>();

    private SequenceState state = SequenceState.WaitingForFirstEntry;
    private DialogueLine[] activeLines;
    private int activeLineIndex = -1;
    private float lineElapsed;
    private Vector3 playerPositionAtLineStart;
    private float playerHeightAtLineStart;
    private int poppedBlocksAtLineStart;
    private int collectedCoinsAtLineStart;
    private PresentationMode presentationMode;
    private GameObject paintBubble;
    private Text paintBubbleText;
    private GameObject roomHud;
    private Text roomHudText;
    private bool dialogueActive;
    private bool wasInsidePaint1;

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

        BuildPaintBubble();
        BuildRoomHud();
        SetNpcVisible(false);
        SetPresentationVisible(false);
    }

    private void Update()
    {
        bool insidePaint1 = IsInsidePaint1;

        if (insidePaint1 && !wasInsidePaint1)
            HandlePaint1Entered();
        else if (!insidePaint1 && wasInsidePaint1)
            HandlePaint1Exited();

        wasInsidePaint1 = insidePaint1;

        if (!dialogueActive)
            return;
        if (presentationMode == PresentationMode.PaintBubble && !insidePaint1)
            return;

        lineElapsed += Time.deltaTime;
        if (activeLineIndex >= 0 && activeLineIndex < activeLines.Length &&
            IsCurrentLineComplete(activeLines[activeLineIndex]))
        {
            ShowNextLine();
        }
    }

    public void NotifyVictoryReached()
    {
        if (state == SequenceState.VictoryDialogue || state == SequenceState.VictoryFinished)
            return;

        state = SequenceState.VictoryDialogue;
        MoveNpcTo(victoryNpcAnchor);
        SetNpcVisible(IsInsidePaint1);
        BeginDialogue(victoryLines, PresentationMode.PaintBubble);
    }

    public void BeginRoomRemoteDialogue()
    {
        SetNpcVisible(false);
        BeginDialogue(roomRemoteLines, PresentationMode.RoomHud);
    }

    public void StopRoomRemoteDialogue()
    {
        if (presentationMode == PresentationMode.RoomHud)
            EndDialogue();
    }

    private void HandlePaint1Entered()
    {
        switch (state)
        {
            case SequenceState.WaitingForFirstEntry:
                state = SequenceState.TutorialDialogue;
                MoveNpcTo(initialNpcAnchor);
                SetNpcVisible(true);
                BeginDialogue(tutorialLines, PresentationMode.PaintBubble);
                break;
            case SequenceState.TutorialDialogue:
                MoveNpcTo(initialNpcAnchor);
                SetNpcVisible(true);
                SetPresentationVisible(true);
                break;
            case SequenceState.TutorialFinished:
                SetNpcVisible(false);
                break;
            case SequenceState.VictoryDialogue:
                MoveNpcTo(victoryNpcAnchor);
                SetNpcVisible(true);
                SetPresentationVisible(true);
                break;
            case SequenceState.VictoryFinished:
                MoveNpcTo(victoryNpcAnchor);
                SetNpcVisible(true);
                break;
        }
    }

    private void HandlePaint1Exited()
    {
        if (presentationMode == PresentationMode.PaintBubble)
            SetPresentationVisible(false);
        SetNpcVisible(false);
    }

    private void BeginDialogue(DialogueLine[] lines, PresentationMode mode)
    {
        activeLines = lines ?? Array.Empty<DialogueLine>();
        activeLineIndex = -1;
        presentationMode = mode;
        dialogueActive = true;
        ShowNextLine();
    }

    private void ShowNextLine()
    {
        activeLineIndex++;

        if (activeLines == null || activeLines.Length == 0)
        {
            activeLineIndex = -1;
            SetDisplayedText(string.Empty);
            SetPresentationVisible(true);
            return;
        }

        if (activeLineIndex >= activeLines.Length)
        {
            EndDialogue();
            return;
        }

        DialogueLine line = activeLines[activeLineIndex];
        SetDisplayedText(line != null ? line.text : string.Empty);
        SetPresentationVisible(true);
        CaptureLineStartState();
    }

    private void EndDialogue()
    {
        dialogueActive = false;
        SetPresentationVisible(false);

        if (state == SequenceState.TutorialDialogue)
        {
            state = SequenceState.TutorialFinished;
            SetNpcVisible(false);
        }
        else if (state == SequenceState.VictoryDialogue)
        {
            state = SequenceState.VictoryFinished;
            SetNpcVisible(IsInsidePaint1);
        }
    }

    private void CaptureLineStartState()
    {
        lineElapsed = 0f;
        Transform player = FindTrackedPlayer();
        playerPositionAtLineStart = player != null ? player.position : Vector3.zero;
        playerHeightAtLineStart = player != null ? player.position.y : 0f;
        poppedBlocksAtLineStart = GetTotalPoppedBlocks();
        collectedCoinsAtLineStart = GetTotalCollectedCoins();
    }

    private bool IsCurrentLineComplete(DialogueLine line)
    {
        if (line == null)
            return true;

        Transform player = FindTrackedPlayer();
        switch (line.advanceMode)
        {
            case AdvanceMode.Timed:
                return lineElapsed >= line.duration;
            case AdvanceMode.PlayerMoved:
                if (player == null)
                    return false;
                Vector3 offset = player.position - playerPositionAtLineStart;
                offset.y = 0f;
                return offset.sqrMagnitude >= 0.25f;
            case AdvanceMode.PlayerJumped:
                if (player == null)
                    return false;
                Rigidbody body = player.GetComponent<Rigidbody>();
                return player.position.y >= playerHeightAtLineStart + 0.15f ||
                       (body != null && body.velocity.y > 0.5f);
            case AdvanceMode.QuestionBlockActivated:
                return GetTotalPoppedBlocks() > poppedBlocksAtLineStart;
            case AdvanceMode.CoinCollected:
                return GetTotalCollectedCoins() > collectedCoinsAtLineStart;
            case AdvanceMode.ReachedTarget:
                return player != null && line.target != null &&
                       Vector3.Distance(player.position, line.target.position) <= line.targetDistance;
            default:
                return false;
        }
    }

    private Transform FindTrackedPlayer()
    {
        GameObject paintingPlayer = GameObject.Find("Player_2D_Runtime");
        if (paintingPlayer != null)
            return paintingPlayer.transform;
        return gameModeManager != null ? gameModeManager.RoomPlayer : null;
    }

    private static int GetTotalPoppedBlocks()
    {
        int total = 0;
        foreach (CoinSystem system in FindObjectsOfType<CoinSystem>(true))
            total += system.PoppedCoinCount;
        return total;
    }

    private static int GetTotalCollectedCoins()
    {
        int total = 0;
        foreach (CoinSystem system in FindObjectsOfType<CoinSystem>(true))
            total += system.CollectedCoinCount;
        return total;
    }

    private void MoveNpcTo(Transform anchor)
    {
        if (npcPlaceholder != null && anchor != null)
            npcPlaceholder.SetPositionAndRotation(anchor.position, anchor.rotation);
    }

    private void SetNpcVisible(bool visible)
    {
        if (npcPlaceholder != null)
            npcPlaceholder.gameObject.SetActive(visible);
    }

    private void SetDisplayedText(string text)
    {
        if (paintBubbleText != null)
            paintBubbleText.text = text ?? string.Empty;
        if (roomHudText != null)
            roomHudText.text = text ?? string.Empty;
    }

    private void SetPresentationVisible(bool visible)
    {
        if (paintBubble != null)
            paintBubble.SetActive(visible && presentationMode == PresentationMode.PaintBubble);
        if (roomHud != null)
            roomHud.SetActive(visible && presentationMode == PresentationMode.RoomHud);
    }

    private void BuildPaintBubble()
    {
        if (npcPlaceholder == null)
            return;

        Font font = LoadTextFont();
        paintBubble = new GameObject(
            "Speech_Bubble_2D",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler));
        paintBubble.transform.SetParent(npcPlaceholder, false);

        RectTransform canvasRect = paintBubble.GetComponent<RectTransform>();
        canvasRect.localPosition = new Vector3(2.7f, 3.1f, -0.12f);
        canvasRect.localRotation = Quaternion.identity;
        canvasRect.localScale = Vector3.one * 0.01f;
        canvasRect.sizeDelta = new Vector2(420f, 170f);

        Canvas canvas = paintBubble.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = paint1Camera;
        canvas.sortingOrder = 250;

        GameObject panelObject = CreateUiObject<Image>("Bubble", paintBubble.transform);
        RectTransform panel = panelObject.GetComponent<RectTransform>();
        panel.anchorMin = Vector2.zero;
        panel.anchorMax = Vector2.one;
        panel.offsetMin = Vector2.zero;
        panel.offsetMax = Vector2.zero;
        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = new Color(0.97f, 0.98f, 1f, 0.96f);
        panelImage.raycastTarget = false;

        GameObject tailObject = CreateUiObject<Image>("Bubble Tail", paintBubble.transform);
        RectTransform tail = tailObject.GetComponent<RectTransform>();
        tail.anchorMin = Vector2.zero;
        tail.anchorMax = Vector2.zero;
        tail.pivot = new Vector2(0.5f, 0.5f);
        tail.anchoredPosition = new Vector2(18f, -16f);
        tail.sizeDelta = new Vector2(44f, 44f);
        tail.localRotation = Quaternion.Euler(0f, 0f, 45f);
        Image tailImage = tailObject.GetComponent<Image>();
        tailImage.color = panelImage.color;
        tailImage.raycastTarget = false;

        GameObject textObject = CreateUiObject<Text>("Bubble Text", panel);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(24f, 18f);
        textRect.offsetMax = new Vector2(-24f, -18f);
        paintBubbleText = textObject.GetComponent<Text>();
        paintBubbleText.font = font;
        paintBubbleText.fontSize = 28;
        paintBubbleText.alignment = TextAnchor.MiddleLeft;
        paintBubbleText.color = new Color(0.055f, 0.075f, 0.11f, 1f);
        paintBubbleText.horizontalOverflow = HorizontalWrapMode.Wrap;
        paintBubbleText.verticalOverflow = VerticalWrapMode.Truncate;
        paintBubbleText.raycastTarget = false;
    }

    private void BuildRoomHud()
    {
        Font font = LoadTextFont();
        roomHud = new GameObject(
            "NPC Remote Dialogue HUD",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        roomHud.transform.SetParent(transform, false);

        Canvas canvas = roomHud.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 210;

        CanvasScaler scaler = roomHud.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject panelObject = CreateUiObject<Image>("Remote Dialogue", roomHud.transform);
        RectTransform panel = panelObject.GetComponent<RectTransform>();
        panel.anchorMin = Vector2.one;
        panel.anchorMax = Vector2.one;
        panel.pivot = Vector2.one;
        panel.anchoredPosition = new Vector2(-34f, -34f);
        panel.sizeDelta = new Vector2(520f, 170f);
        panelObject.GetComponent<Image>().color = new Color(0.035f, 0.055f, 0.09f, 0.9f);

        GameObject labelObject = CreateUiObject<Text>("Remote NPC Label", panel);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 1f);
        labelRect.anchorMax = new Vector2(0f, 1f);
        labelRect.pivot = new Vector2(0f, 1f);
        labelRect.anchoredPosition = new Vector2(22f, -14f);
        labelRect.sizeDelta = new Vector2(140f, 34f);
        Text label = labelObject.GetComponent<Text>();
        label.font = font;
        label.fontSize = 22;
        label.fontStyle = FontStyle.Bold;
        label.color = new Color(0.25f, 0.85f, 1f, 1f);
        label.text = "NPC";

        GameObject textObject = CreateUiObject<Text>("Remote Dialogue Text", panel);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(22f, 18f);
        textRect.offsetMax = new Vector2(-22f, -52f);
        roomHudText = textObject.GetComponent<Text>();
        roomHudText.font = font;
        roomHudText.fontSize = 24;
        roomHudText.alignment = TextAnchor.UpperLeft;
        roomHudText.color = Color.white;
        roomHudText.raycastTarget = false;
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
            // Use an installed CJK-capable font below when needed.
        }

        if (font == null)
        {
            font = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial" },
                28);
        }

        return font;
    }

    private void OnDestroy()
    {
        if (paintBubble != null)
            Destroy(paintBubble);
        if (roomHud != null)
            Destroy(roomHud);
    }
}
