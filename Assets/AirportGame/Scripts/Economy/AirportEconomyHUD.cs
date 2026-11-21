
using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Displays the airport's current balance in a UI Toolkit HUD.
/// Attach this component to a GameObject and assign the main game
/// UIDocument and AirportEconomyManager in the Inspector.
/// </summary>
public class AirportEconomyHUD : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private AirportEconomyManager economyManager;

    [Header("Appearance")]
    [SerializeField, Min(12)] private int fontSize = 24;
    [SerializeField, Min(0)] private float topOffset = 20f;
    [SerializeField, Min(0)] private float rightOffset = 20f;

    public event Action BalanceClicked;

    private Label balanceLabel;
    private VisualElement root;

    private void OnEnable()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (economyManager == null)
        {
            economyManager =
                FindAnyObjectByType<AirportEconomyManager>();
        }

        if (uiDocument == null)
        {
            LogError(
                "AirportEconomyHUD: UIDocument is missing. " +
                "Assign the main game UI document in the Inspector.",
                this
            );

            return;
        }

        if (economyManager == null)
        {
            LogError(
                "AirportEconomyHUD: AirportEconomyManager was not found. " +
                "Assign it in the Inspector.",
                this
            );

            return;
        }

        root = uiDocument.rootVisualElement;

        if (root == null)
        {
            LogError(
                "AirportEconomyHUD: The UIDocument has no root element.",
                this
            );

            return;
        }

        CreateBalanceLabel();

        economyManager.OnBalanceChanged += HandleBalanceChanged;

        // Display the current balance immediately, including after loading.
        UpdateBalanceLabel(economyManager.Balance);
    }

    private void OnDisable()
    {
        if (economyManager != null)
            economyManager.OnBalanceChanged -= HandleBalanceChanged;

        if (root != null && balanceLabel != null)
        {
            balanceLabel.UnregisterCallback<ClickEvent>(HandleBalanceClicked);
            root.Remove(balanceLabel);
        }

        balanceLabel = null;
        root = null;
    }

    private void CreateBalanceLabel()
    {
        balanceLabel = new Label();

        balanceLabel.name = "airport-money-label";
        balanceLabel.pickingMode = PickingMode.Position;
        balanceLabel.tooltip = "Open financial reports";
        balanceLabel.RegisterCallback<ClickEvent>(HandleBalanceClicked);

        balanceLabel.style.position = Position.Absolute;
        balanceLabel.style.top = topOffset;
        balanceLabel.style.right = rightOffset;

        balanceLabel.style.paddingLeft = 16;
        balanceLabel.style.paddingRight = 16;
        balanceLabel.style.paddingTop = 10;
        balanceLabel.style.paddingBottom = 10;

        balanceLabel.style.fontSize = fontSize;
        balanceLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        balanceLabel.style.color = Color.white;

        balanceLabel.style.backgroundColor =
            new Color(0.08f, 0.12f, 0.16f, 0.92f);

        balanceLabel.style.borderTopLeftRadius = 6;
        balanceLabel.style.borderTopRightRadius = 6;
        balanceLabel.style.borderBottomLeftRadius = 6;
        balanceLabel.style.borderBottomRightRadius = 6;

        balanceLabel.style.minWidth = 150;
        balanceLabel.style.unityTextAlign = TextAnchor.MiddleCenter;

        root.Add(balanceLabel);
    }

    private void HandleBalanceClicked(ClickEvent evt)
    {
        BalanceClicked?.Invoke();
    }

    private void HandleBalanceChanged(int newBalance)
    {
        UpdateBalanceLabel(newBalance);
    }

    private void UpdateBalanceLabel(int balance)
    {
        if (balanceLabel == null)
            return;

        balanceLabel.text = $"${balance:N0}";
    }

    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}
