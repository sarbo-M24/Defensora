
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Build-mode HUD for ObjectPlacementManager. Builds its own overlay canvas
/// at runtime, so it only needs to be added to a GameObject in the scene.
///
/// While placing, shows one card per turret with its number key and name,
/// highlighting the selected one.
/// </summary>
public class TurretPlacementUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Found automatically if left empty.")]
    [SerializeField] private ObjectPlacementManager placementManager;

    [Header("Style")]
    [SerializeField] private Color panelColor = new Color(0.06f, 0.06f, 0.09f, 0.82f);
    [SerializeField] private Color selectedColor = new Color(1f, 0.62f, 0.11f, 1f);
    [SerializeField] private Color textColor = new Color(0.94f, 0.94f, 0.96f, 1f);
    [SerializeField] private Color mutedTextColor = new Color(0.65f, 0.66f, 0.72f, 1f);
    [SerializeField, Min(0.01f)] private float fadeSpeed = 10f;

    private const float SlotWidth = 200f;
    private const float SlotHeight = 56f;
    private const float SlotSpacing = 12f;
    private const float SlotBaseY = 24f;
    private const float SlotLift = 6f;

    private Font font;

    private CanvasGroup placementGroup;

    private Image[] slotBackgrounds;
    private Image[] slotOutlines;
    private Text[] slotKeyLabels;

    private int shownSelection = -1;

    private void Awake()
    {
        if (placementManager == null)
            placementManager = FindFirstObjectByType<ObjectPlacementManager>();

        if (placementManager == null)
        {
            Debug.LogError(
                "TurretPlacementUI: No ObjectPlacementManager in the scene.",
                this
            );
            enabled = false;
            return;
        }

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private void Start()
    {
        BuildUI();
    }

    private void Update()
    {
        bool placing = placementManager.IsPlacing;

        placementGroup.alpha = Mathf.MoveTowards(
            placementGroup.alpha,
            placing ? 1f : 0f,
            fadeSpeed * Time.unscaledDeltaTime
        );

        if (placing && shownSelection != placementManager.SelectedIndex)
            RefreshSlots();
    }

    private void RefreshSlots()
    {
        shownSelection = placementManager.SelectedIndex;

        for (int i = 0; i < slotBackgrounds.Length; i++)
        {
            bool selected = i == shownSelection;

            slotOutlines[i].color = selected ? selectedColor : new Color(1f, 1f, 1f, 0.08f);
            slotBackgrounds[i].color = selected
                ? Color.Lerp(panelColor, selectedColor, 0.1f)
                : panelColor;
            slotKeyLabels[i].color = selected ? selectedColor : mutedTextColor;

            // Lift the selected card slightly.
            RectTransform rect = slotOutlines[i].rectTransform;
            Vector2 pos = rect.anchoredPosition;
            pos.y = selected ? SlotBaseY + SlotLift : SlotBaseY;
            rect.anchoredPosition = pos;
        }
    }

    // ---------------------------------------------------------------- Build

    private void BuildUI()
    {
        GameObject canvasObject = new GameObject("TurretPlacementCanvas");
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform root = canvasObject.GetComponent<RectTransform>();

        int count = placementManager.TurretCount;
        float hotbarWidth = count * SlotWidth + (count - 1) * SlotSpacing;

        RectTransform panel = CreateRect("PlacementPanel", root);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0f);
        panel.pivot = new Vector2(0.5f, 0f);
        panel.sizeDelta = new Vector2(hotbarWidth, SlotBaseY + SlotLift + SlotHeight);
        panel.anchoredPosition = Vector2.zero;

        placementGroup = panel.gameObject.AddComponent<CanvasGroup>();
        placementGroup.alpha = 0f;

        slotBackgrounds = new Image[count];
        slotOutlines = new Image[count];
        slotKeyLabels = new Text[count];

        for (int i = 0; i < count; i++)
        {
            float x = -hotbarWidth * 0.5f + SlotWidth * 0.5f + i * (SlotWidth + SlotSpacing);
            BuildSlot(panel, i, x);
        }

        RefreshSlots();
    }

    private void BuildSlot(RectTransform parent, int index, float x)
    {
        GameObject prefab = placementManager.GetTurretPrefab(index);
        TurretController turret = prefab != null
            ? prefab.GetComponentInChildren<TurretController>(true)
            : null;

        // The outline is the card's border: a slightly larger image behind it.
        Image outline = CreatePanel($"Slot{index + 1}", parent, Color.white);
        RectTransform outlineRect = outline.rectTransform;
        outlineRect.anchorMin = outlineRect.anchorMax = new Vector2(0.5f, 0f);
        outlineRect.pivot = new Vector2(0.5f, 0f);
        outlineRect.sizeDelta = new Vector2(SlotWidth, SlotHeight);
        outlineRect.anchoredPosition = new Vector2(x, SlotBaseY);

        Image background = CreatePanel("Background", outlineRect, panelColor);
        Stretch(background.rectTransform, new Vector2(2f, 2f), new Vector2(-2f, -2f));

        RectTransform card = background.rectTransform;

        Text key = CreateText("Key", card, (index + 1).ToString(), 26, mutedTextColor, TextAnchor.MiddleCenter);
        key.fontStyle = FontStyle.Bold;
        key.rectTransform.anchorMin = new Vector2(0f, 0f);
        key.rectTransform.anchorMax = new Vector2(0f, 1f);
        key.rectTransform.pivot = new Vector2(0f, 0.5f);
        key.rectTransform.sizeDelta = new Vector2(44f, 0f);
        key.rectTransform.anchoredPosition = new Vector2(4f, 0f);

        string title = turret != null
            ? turret.Type + " Turret"
            : (prefab != null ? prefab.name : "Not assigned");

        Text titleText = CreateText("Title", card, title, 20, textColor, TextAnchor.MiddleLeft);
        titleText.fontStyle = FontStyle.Bold;
        Stretch(titleText.rectTransform, new Vector2(50f, 0f), new Vector2(-8f, 0f));

        slotOutlines[index] = outline;
        slotBackgrounds[index] = background;
        slotKeyLabels[index] = key;
    }

    // -------------------------------------------------------------- Helpers

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image CreatePanel(string name, Transform parent, Color color)
    {
        Image image = CreateRect(name, parent).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private Text CreateText(string name, Transform parent, string content, int size, Color color, TextAnchor alignment)
    {
        Text text = CreateRect(name, parent).gameObject.AddComponent<Text>();
        text.font = font;
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}
