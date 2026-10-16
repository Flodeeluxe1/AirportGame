
using UnityEngine;
using UnityEngine.UIElements;

public class GameTimeUIController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private GameTimeManager gameTimeManager;

    private Label clockLabel;

    private Button pauseButton;
    private Button normalButton;
    private Button doubleButton;
    private Button quadrupleButton;
    private Button octupleButton;

    private string lastDisplayedTime;

    private void Awake()
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }

        if (gameTimeManager == null)
        {
            gameTimeManager =
                FindFirstObjectByType<GameTimeManager>();
        }

        if (uiDocument == null)
        {
            Debug.LogError(
                "GameTimeUIController: UIDocument is missing."
            );

            enabled = false;
            return;
        }

        if (gameTimeManager == null)
        {
            Debug.LogError(
                "GameTimeUIController: GameTimeManager is missing."
            );

            enabled = false;
            return;
        }

        VisualElement root =
            uiDocument.rootVisualElement;

        clockLabel =
            root.Q<Label>("game-time-label");

        pauseButton =
            root.Q<Button>("time-pause-button");

        normalButton =
            root.Q<Button>("time-1x-button");

        doubleButton =
            root.Q<Button>("time-2x-button");

        quadrupleButton =
            root.Q<Button>("time-4x-button");

        octupleButton =
            root.Q<Button>("time-8x-button");

        if (clockLabel == null ||
            pauseButton == null ||
            normalButton == null ||
            doubleButton == null ||
            quadrupleButton == null ||
            octupleButton == null)
        {
            Debug.LogError(
                "GameTimeUIController: One or more UI elements " +
                "could not be found. Check their UXML names."
            );

            enabled = false;
            return;
        }

        pauseButton.clicked += OnPauseClicked;
        normalButton.clicked += OnNormalClicked;
        doubleButton.clicked += OnDoubleClicked;
        quadrupleButton.clicked += OnQuadrupleClicked;
        octupleButton.clicked += OnOctupleClicked;

        RefreshDisplay();
    }

    private void Update()
    {
        if (gameTimeManager == null)
            return;

        string currentTime =
            $"Day {gameTimeManager.CurrentDay} | " +
            gameTimeManager.FormattedTime;

        if (currentTime != lastDisplayedTime)
        {
            clockLabel.text = currentTime;
            lastDisplayedTime = currentTime;
        }

        UpdateSpeedButtonStates();
    }

    private void OnPauseClicked()
    {
        gameTimeManager.SetSpeed(0);
    }

    private void OnNormalClicked()
    {
        gameTimeManager.SetSpeed(1);
    }

    private void OnDoubleClicked()
    {
        gameTimeManager.SetSpeed(10);
    }

    private void OnQuadrupleClicked()
    {
        gameTimeManager.SetSpeed(100);
    }

    private void OnOctupleClicked()
    {
        gameTimeManager.SetSpeed(1000);
    }

    private void RefreshDisplay()
    {
        if (gameTimeManager == null)
            return;

        lastDisplayedTime =
            $"Day {gameTimeManager.CurrentDay} | " +
            gameTimeManager.FormattedTime;

        clockLabel.text = lastDisplayedTime;

        UpdateSpeedButtonStates();
    }

    private void UpdateSpeedButtonStates()
    {
        int speed =
            gameTimeManager.CurrentSpeedMultiplier;

        SetSelected(pauseButton, speed == 0);
        SetSelected(normalButton, speed == 1);
        SetSelected(doubleButton, speed == 10);
        SetSelected(quadrupleButton, speed == 100);
        SetSelected(octupleButton, speed == 1000);
    }

    private void SetSelected(
        Button button,
        bool selected)
    {
        if (selected)
        {
            button.AddToClassList(
                "time-speed-selected"
            );
        }
        else
        {
            button.RemoveFromClassList(
                "time-speed-selected"
            );
        }
    }

    private void OnDestroy()
    {
        if (pauseButton != null)
            pauseButton.clicked -= OnPauseClicked;

        if (normalButton != null)
            normalButton.clicked -= OnNormalClicked;

        if (doubleButton != null)
            doubleButton.clicked -= OnDoubleClicked;

        if (quadrupleButton != null)
            quadrupleButton.clicked -= OnQuadrupleClicked;

        if (octupleButton != null)
            octupleButton.clicked -= OnOctupleClicked;
    }
}
