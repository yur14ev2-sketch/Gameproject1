using System;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class CoinCounterUI : MonoBehaviour
{
    [SerializeField] private Sprite coinIcon;

    private GameObject canvasObject;
    private Text countText;
    private int currentCount;

    private void Awake()
    {
        try
        {
            BuildUi();
            UpdateCountText();
        }
        catch (Exception exception)
        {
            Debug.LogError($"Coin UI could not be created: {exception.Message}", this);
        }
    }

    public void SetCount(int count)
    {
        currentCount = Mathf.Max(0, count);
        UpdateCountText();
    }

    private void BuildUi()
    {
        Font textFont = LoadTextFont();

        canvasObject = new GameObject(
            "Coin UI",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject panelObject = CreateUiObject<Image>("Coin Counter", canvasObject.transform);
        RectTransform panel = panelObject.GetComponent<RectTransform>();
        panel.anchorMin = new Vector2(0f, 1f);
        panel.anchorMax = new Vector2(0f, 1f);
        panel.pivot = new Vector2(0f, 1f);
        panel.anchoredPosition = new Vector2(20f, -20f);
        panel.sizeDelta = new Vector2(180f, 54f);
        panelObject.GetComponent<Image>().color = new Color(0.03f, 0.04f, 0.09f, 0.8f);

        if (coinIcon != null)
            CreateSpriteIcon(panel, coinIcon);
        else
            CreateFallbackIcon(panel, textFont);

        GameObject countObject = CreateUiObject<Text>("Count", panel);
        RectTransform countRect = countObject.GetComponent<RectTransform>();
        countRect.anchorMin = Vector2.zero;
        countRect.anchorMax = Vector2.one;
        countRect.offsetMin = new Vector2(62f, 0f);
        countRect.offsetMax = new Vector2(-12f, 0f);

        countText = countObject.GetComponent<Text>();
        countText.font = textFont;
        countText.fontSize = 26;
        countText.fontStyle = FontStyle.Bold;
        countText.alignment = TextAnchor.MiddleLeft;
        countText.color = Color.white;
        countText.raycastTarget = false;
    }

    private static GameObject CreateUiObject<T>(string objectName, Transform parent)
        where T : Component
    {
        GameObject uiObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(T));
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
            // Older Unity versions may not expose LegacyRuntime.ttf.
        }

        if (font == null)
        {
            font = Font.CreateDynamicFontFromOSFont(
                new[] { "Arial", "Liberation Sans", "Segoe UI" },
                26);
        }

        if (font == null)
            throw new InvalidOperationException("No usable runtime font was found.");

        return font;
    }

    private static void CreateSpriteIcon(RectTransform parent, Sprite sprite)
    {
        GameObject iconObject = CreateUiObject<Image>("Coin Icon", parent);
        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        ConfigureIconRect(iconRect);

        Image image = iconObject.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
    }

    private static void CreateFallbackIcon(RectTransform parent, Font font)
    {
        GameObject iconObject = CreateUiObject<Text>("Coin Placeholder", parent);
        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        ConfigureIconRect(iconRect);

        Text icon = iconObject.GetComponent<Text>();
        icon.font = font;
        icon.fontSize = 34;
        icon.fontStyle = FontStyle.Bold;
        icon.alignment = TextAnchor.MiddleCenter;
        icon.color = new Color(1f, 0.78f, 0.12f, 1f);
        icon.text = "●";
        icon.raycastTarget = false;
    }

    private static void ConfigureIconRect(RectTransform iconRect)
    {
        iconRect.anchorMin = new Vector2(0f, 0.5f);
        iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(10f, 0f);
        iconRect.sizeDelta = new Vector2(42f, 42f);
    }

    private void UpdateCountText()
    {
        if (countText != null)
            countText.text = currentCount.ToString();
    }

    private void OnDestroy()
    {
        if (canvasObject != null)
            Destroy(canvasObject);
    }
}
