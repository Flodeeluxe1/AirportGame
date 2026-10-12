
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

public class GameMenuUIController : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private UIDocument uiDocument;

    [SerializeField]
    private GameTimeManager gameTimeManager;

    [Header("Menu Actions")]
    [SerializeField] private UnityEvent onNewRequested = new UnityEvent();
    [SerializeField] private UnityEvent onSaveRequested = new UnityEvent();
    [SerializeField] private UnityEvent onLoadRequested = new UnityEvent();
    [SerializeField] private UnityEvent onOptionsRequested = new UnityEvent();

    [Header("Appearance")]
    [SerializeField] private string menuButtonText = "MENU";
    [SerializeField] private float menuButtonWidth = 100f;
    [SerializeField] private float menuButtonHeight = 42f;
    [SerializeField] private float menuPanelWidth = 340f;

    private VisualElement root;
    private VisualElement overlay;
    private VisualElement menuPanel;
    private Button menuButton;

    private bool isMenuOpen;
    private bool pauseStateCaptured;

    private float previousGameSpeedMultiplier;
    private float previousTimeScale;

    private void Start()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (gameTimeManager == null)
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();

        if (uiDocument == null)
        {
            Debug.LogError(
                "GameMenuUIController: UIDocument is missing. " +
                "Attach this script to the GameObject containing UIDocument " +
                "or assign the reference in the Inspector.",
                this
            );

            enabled = false;
            return;
        }

        root = uiDocument.rootVisualElement;

        CreateMenuButton();

        // Escape closes the menu while it has keyboard focus.
        root.RegisterCallback<KeyDownEvent>(
            OnRootKeyDown,
            TrickleDown.TrickleDown
        );
    }

    private void OnDisable()
    {
        if (root != null)
        {
            root.UnregisterCallback<KeyDownEvent>(
                OnRootKeyDown,
                TrickleDown.TrickleDown
            );
        }

        if (isMenuOpen)
            CloseMenu();

        if (menuButton != null)
            menuButton.RemoveFromHierarchy();

        root = null;
        menuButton = null;
    }

    private void CreateMenuButton()
    {
        menuButton = new Button(ToggleMenu)
        {
            text = menuButtonText
        };

        menuButton.name = "game-menu-button";
        menuButton.tooltip = "Open the game menu";

        menuButton.style.position = Position.Absolute;
        menuButton.style.left = 16f;
        menuButton.style.top = 16f;
        menuButton.style.width = menuButtonWidth;
        menuButton.style.height = menuButtonHeight;

        menuButton.style.backgroundColor =
            (Color)new Color32(38, 49, 64, 255);

        menuButton.style.color = Color.white;
        menuButton.style.fontSize = 15;
        menuButton.style.unityFontStyleAndWeight = FontStyle.Bold;
        menuButton.style.unityTextAlign = TextAnchor.MiddleCenter;

        SetBorder(menuButton, 1f, new Color32(95, 111, 132, 255));
        SetRadius(menuButton, 7f);

        root.Add(menuButton);
    }

    public void ToggleMenu()
    {
        if (isMenuOpen)
            CloseMenu();
        else
            OpenMenu();
    }

    public void OpenMenu()
    {
        if (isMenuOpen || root == null)
            return;

        CapturePauseState();
        PauseGame();

        CreateMenuOverlay();

        isMenuOpen = true;

        // Give the menu keyboard focus so Escape can close it.
        overlay.focusable = true;
        overlay.Focus();
    }

    public void CloseMenu()
    {
        if (!isMenuOpen && !pauseStateCaptured)
            return;

        isMenuOpen = false;

        if (overlay != null)
        {
            overlay.RemoveFromHierarchy();
            overlay = null;
            menuPanel = null;
        }

        RestorePauseState();

        if (menuButton != null && menuButton.panel != null)
            menuButton.Focus();
    }

    private void CapturePauseState()
    {
        if (pauseStateCaptured)
            return;

        previousTimeScale = Time.timeScale;

        previousGameSpeedMultiplier =
            gameTimeManager != null
                ? gameTimeManager.CurrentSpeedMultiplier
                : 1f;

        pauseStateCaptured = true;
    }

    private void PauseGame()
    {
        // Pause the game clock, including systems driven by its speed.
        if (gameTimeManager != null)
            gameTimeManager.Pause();

        // Also pause scripts and physics using Unity's scaled time.
        Time.timeScale = 0f;
    }

    private void RestorePauseState()
    {
        if (!pauseStateCaptured)
            return;

        // Restore the original Unity time scale and game clock speed,
        // including an original paused state.
        Time.timeScale = previousTimeScale;

        if (gameTimeManager != null)
        {
            gameTimeManager.SetSpeed(
                Mathf.RoundToInt(previousGameSpeedMultiplier)
            );
        }

        pauseStateCaptured = false;
    }

    private void CreateMenuOverlay()
    {
        overlay = new VisualElement();
        overlay.name = "game-menu-overlay";

        overlay.style.position = Position.Absolute;
        overlay.style.left = 0;
        overlay.style.right = 0;
        overlay.style.top = 0;
        overlay.style.bottom = 0;

        overlay.style.backgroundColor =
            new Color(0f, 0f, 0f, 0.68f);

        overlay.style.alignItems = Align.Center;
        overlay.style.justifyContent = Justify.Center;

        overlay.pickingMode = PickingMode.Position;

        root.Add(overlay);

        menuPanel = new VisualElement();
        menuPanel.name = "game-menu-panel";

        menuPanel.style.width = menuPanelWidth;
        menuPanel.style.maxWidth =
            new Length(92f, LengthUnit.Percent);

        menuPanel.style.paddingLeft = 22f;
        menuPanel.style.paddingRight = 22f;
        menuPanel.style.paddingTop = 20f;
        menuPanel.style.paddingBottom = 22f;

        menuPanel.style.backgroundColor =
            (Color)new Color32(27, 35, 47, 255);

        menuPanel.style.flexDirection = FlexDirection.Column;

        SetBorder(menuPanel, 1f, new Color32(82, 98, 119, 255));
        SetRadius(menuPanel, 12f);

        overlay.Add(menuPanel);

        CreateMenuHeader();
        CreateActionButton("New", OnNewClicked);
        CreateActionButton("Save", OnSaveClicked);
        CreateActionButton("Load", OnLoadClicked);
        CreateActionButton("Options", OnOptionsClicked);

        CreateActionButton(
            "Exit",
            OnExitClicked,
            new Color32(133, 48, 51, 255)
        );

        CreateResumeButton();
    }

    private void CreateMenuHeader()
    {
        VisualElement header = new VisualElement();

        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.justifyContent = Justify.SpaceBetween;

        header.style.marginBottom = 20f;

        menuPanel.Add(header);

        Label title = new Label("GAME MENU");

        title.style.color = Color.white;
        title.style.fontSize = 23;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;

        header.Add(title);

        Button closeButton = new Button(CloseMenu)
        {
            text = "X"
        };

        closeButton.tooltip = "Resume the game";
        closeButton.style.width = 38f;
        closeButton.style.height = 34f;
        closeButton.style.color = Color.white;
        closeButton.style.backgroundColor =
            (Color)new Color32(51, 63, 79, 255);

        SetRadius(closeButton, 6f);

        header.Add(closeButton);

        Label subtitle = new Label(
            "Manage your airport"
        );

        subtitle.style.color =
            (Color)new Color32(176, 188, 202, 255);

        subtitle.style.fontSize = 13;
        subtitle.style.marginTop = 3f;
        subtitle.style.marginBottom = 16f;

        menuPanel.Add(subtitle);
    }

    private void CreateActionButton(
        string text,
        System.Action callback,
        Color? backgroundColor = null)
    {
        Button button = new Button(callback)
        {
            text = text
        };

        button.name = $"game-menu-{text.ToLowerInvariant()}";

        button.style.height = 46f;
        button.style.marginBottom = 9f;

        button.style.backgroundColor =
            backgroundColor ??
            new Color32(48, 61, 78, 255);

        button.style.color = Color.white;
        button.style.fontSize = 16;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;

        SetBorder(button, 1f, new Color32(81, 96, 115, 255));
        SetRadius(button, 7f);

        menuPanel.Add(button);
    }

    private void CreateResumeButton()
    {
        Button resumeButton = new Button(CloseMenu)
        {
            text = "Resume Game"
        };

        resumeButton.name = "game-menu-resume";

        resumeButton.style.height = 42f;
        resumeButton.style.marginTop = 10f;
        resumeButton.style.color = Color.white;
        resumeButton.style.backgroundColor =
            (Color)new Color32(40, 112, 85, 255);

        resumeButton.style.fontSize = 15;
        resumeButton.style.unityFontStyleAndWeight = FontStyle.Bold;

        SetRadius(resumeButton, 7f);

        menuPanel.Add(resumeButton);
    }

    private void OnNewClicked()
    {
        InvokeMenuAction("New", onNewRequested);
    }

    private void OnSaveClicked()
    {
        InvokeMenuAction("Save", onSaveRequested);
    }

    private void OnLoadClicked()
    {
        InvokeMenuAction("Load", onLoadRequested);
    }

    private void OnOptionsClicked()
    {
        InvokeMenuAction("Options", onOptionsRequested);
    }

    private void InvokeMenuAction(
        string actionName,
        UnityEvent action)
    {
        if (action == null || action.GetPersistentEventCount() == 0)
        {
            Debug.Log(
                $"Game menu: {actionName} clicked. " +
                "No action has been connected yet.",
                this
            );
        }

        // Keep the menu open and the game paused while the action runs.
        action?.Invoke();
    }

    private void OnExitClicked()
    {
        CloseMenu();

        Debug.Log("Game menu: Exit requested.", this);

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnRootKeyDown(KeyDownEvent evt)
    {
        if (!isMenuOpen || evt.keyCode != KeyCode.Escape)
            return;

        evt.StopPropagation();
        CloseMenu();
    }

    private static void SetBorder(
        VisualElement element,
        float width,
        Color color)
    {
        element.style.borderTopWidth = width;
        element.style.borderBottomWidth = width;
        element.style.borderLeftWidth = width;
        element.style.borderRightWidth = width;

        element.style.borderTopColor = color;
        element.style.borderBottomColor = color;
        element.style.borderLeftColor = color;
        element.style.borderRightColor = color;
    }

    private static void SetRadius(
        VisualElement element,
        float radius)
    {
        element.style.borderTopLeftRadius = radius;
        element.style.borderTopRightRadius = radius;
        element.style.borderBottomLeftRadius = radius;
        element.style.borderBottomRightRadius = radius;
    }
}