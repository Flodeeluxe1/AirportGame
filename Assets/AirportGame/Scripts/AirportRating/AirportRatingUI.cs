using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Displays the current airport rating in the main HUD and provides
/// an independently toggled rating window.
/// Attach to the AirportUI GameObject and use its main UIDocument.
/// </summary>
public class AirportRatingUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private AirportRatingManager ratingManager;

    [Header("HUD Appearance")]
    [SerializeField, Min(12)] private int fontSize = 24;
    [SerializeField, Min(0)] private float topOffset = 20f;
    [SerializeField, Min(0)] private float rightOffset = 420f;
    [SerializeField, Min(80)] private float buttonWidth = 170f;

    private static readonly Color WindowColor =
        new Color32(27, 32, 39, 250);
    private static readonly Color CardColor =
        new Color32(39, 47, 57, 255);
    private static readonly Color MutedTextColor =
        new Color32(174, 186, 199, 255);
    private static readonly Color AccentColor =
        new Color32(91, 170, 218, 255);

    private VisualElement root;
    private Button ratingButton;
    private VisualElement window;
    private Label buttonRatingLabel;
    private Label overallRatingLabel;
    private Label visitorSatisfactionLabel;
    private Label environmentLabel;
    private Label completedVisitorsLabel;
    private Label activeVisitorsLabel;
    private Label visitorHoursLabel;
    private VisualElement satisfactionBar;
    private VisualElement environmentBar;

    private bool windowOpen;
    private float nextRefreshTime;

    private void OnEnable()
    {
        ResolveReferences();

        if (uiDocument == null)
        {
            Debug.LogError(
                "AirportRatingUI: UIDocument is missing. Assign the main AirportUI UIDocument.",
                this);
            enabled = false;
            return;
        }

        root = uiDocument.rootVisualElement;

        if (root == null)
        {
            Debug.LogError(
                "AirportRatingUI: The UIDocument has no root element.",
                this);
            enabled = false;
            return;
        }

        CreateRatingButton();
        CreateWindow();
        window.style.display = DisplayStyle.None;
        RefreshUI();
    }

    private void OnDisable()
    {
        if (root != null)
        {
            if (ratingButton != null)
                root.Remove(ratingButton);

            if (window != null)
                root.Remove(window);
        }

        ratingButton = null;
        window = null;
        root = null;
        windowOpen = false;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime = Time.unscaledTime + 0.25f;
        ResolveRatingManager();
        RefreshUI();
    }

    private void ResolveReferences()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null)
            uiDocument = FindFirstObjectByType<UIDocument>();

        ResolveRatingManager();
    }

    private void ResolveRatingManager()
    {
        if (ratingManager == null)
            ratingManager = AirportRatingManager.Instance;

        if (ratingManager == null)
            ratingManager = FindFirstObjectByType<AirportRatingManager>();
    }

    private void CreateRatingButton()
    {
        ratingButton = new Button(OpenWindow);
        ratingButton.name = "airport-rating-open-button";
        ratingButton.style.position = Position.Absolute;
        ratingButton.style.top = topOffset;
        ratingButton.style.right = rightOffset;
        ratingButton.style.width = buttonWidth;
        ratingButton.style.paddingLeft = 16;
        ratingButton.style.paddingRight = 16;
        ratingButton.style.paddingTop = 10;
        ratingButton.style.paddingBottom = 10;
        ratingButton.style.flexDirection = FlexDirection.Column;
        ratingButton.style.alignItems = Align.Center;
        ratingButton.style.justifyContent = Justify.Center;
        ratingButton.style.backgroundColor =
            new Color(0.08f, 0.12f, 0.16f, 0.92f);
        ratingButton.style.color = Color.white;
        ratingButton.style.borderTopLeftRadius = 6;
        ratingButton.style.borderTopRightRadius = 6;
        ratingButton.style.borderBottomLeftRadius = 6;
        ratingButton.style.borderBottomRightRadius = 6;

        buttonRatingLabel = new Label("-- / 100");
        buttonRatingLabel.style.fontSize = fontSize;
        buttonRatingLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        buttonRatingLabel.style.color = Color.white;

        ratingButton.Add(buttonRatingLabel);
        root.Add(ratingButton);
    }

    private void CreateWindow()
    {
        window = new VisualElement();
        window.name = "airport-rating-window";
        window.style.position = Position.Absolute;
        window.style.left = new Length(25, LengthUnit.Percent);
        window.style.right = new Length(25, LengthUnit.Percent);
        window.style.top = 70;
        window.style.bottom = 80;
        window.style.minWidth = 420;
        window.style.flexDirection = FlexDirection.Column;
        window.style.paddingLeft = 16;
        window.style.paddingRight = 16;
        window.style.paddingTop = 14;
        window.style.paddingBottom = 14;
        window.style.backgroundColor = WindowColor;
        window.style.borderTopLeftRadius = 8;
        window.style.borderTopRightRadius = 8;
        window.style.borderBottomLeftRadius = 8;
        window.style.borderBottomRightRadius = 8;

        // Add directly to the document root so this is independent
        // of the other management windows.
        root.Add(window);

        CreateHeader();
        CreateOverallRating();
        CreateRatingBreakdown();
        CreateVisitorStatistics();
    }

    private void CreateHeader()
    {
        VisualElement header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.justifyContent = Justify.SpaceBetween;
        header.style.marginBottom = 12;

        Label title = new Label("AIRPORT RATING");
        title.style.fontSize = 22;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = Color.white;

        Button closeButton = new Button(CloseWindow)
        {
            text = "X"
        };
        closeButton.name = "airport-rating-close-button";
        closeButton.style.width = 38;
        closeButton.style.height = 34;
        closeButton.style.minWidth = 38;
        closeButton.style.fontSize = 16;
        closeButton.style.unityFontStyleAndWeight = FontStyle.Bold;

        header.Add(title);
        header.Add(closeButton);
        window.Add(header);
    }

    private void CreateOverallRating()
    {
        VisualElement card = CreateCard();
        card.style.alignItems = Align.Center;
        card.style.paddingTop = 18;
        card.style.paddingBottom = 18;
        card.style.marginBottom = 12;

        Label heading = new Label("OVERALL AIRPORT RATING");
        heading.style.fontSize = 11;
        heading.style.unityFontStyleAndWeight = FontStyle.Bold;
        heading.style.color = MutedTextColor;

        overallRatingLabel = new Label("-- / 100");
        overallRatingLabel.style.fontSize = 42;
        overallRatingLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        overallRatingLabel.style.color = Color.white;
        overallRatingLabel.style.marginTop = 4;

        card.Add(heading);
        card.Add(overallRatingLabel);
        window.Add(card);
    }

    private void CreateRatingBreakdown()
    {
        window.Add(CreateSectionTitle("RATING BREAKDOWN"));

        VisualElement satisfactionCard = CreateCard();
        visitorSatisfactionLabel = AddRatingRow(
            satisfactionCard,
            "Visitor Satisfaction",
            out satisfactionBar);
        window.Add(satisfactionCard);

        VisualElement environmentCard = CreateCard();
        environmentLabel = AddRatingRow(
            environmentCard,
            "Environment",
            out environmentBar);
        window.Add(environmentCard);
    }

    private void CreateVisitorStatistics()
    {
        window.Add(CreateSectionTitle("VISITOR STATISTICS"));

        VisualElement statistics = CreateCard();
        statistics.style.flexDirection = FlexDirection.Row;
        statistics.style.flexWrap = Wrap.Wrap;
        statistics.style.justifyContent = Justify.SpaceBetween;

        completedVisitorsLabel = AddStatistic(statistics, "COMPLETED VISITORS");
        activeVisitorsLabel = AddStatistic(statistics, "ACTIVE VISITORS");
        visitorHoursLabel = AddStatistic(statistics, "RECORDED VISITOR HOURS");

        window.Add(statistics);
    }

    private Label AddRatingRow(
        VisualElement parent,
        string title,
        out VisualElement progressBar)
    {
        VisualElement content = new VisualElement();
        content.style.flexDirection = FlexDirection.Column;
        content.style.flexGrow = 1;

        VisualElement heading = new VisualElement();
        heading.style.flexDirection = FlexDirection.Row;
        heading.style.justifyContent = Justify.SpaceBetween;
        heading.style.alignItems = Align.Center;

        Label nameLabel = new Label(title);
        nameLabel.style.fontSize = 13;
        nameLabel.style.color = Color.white;

        Label valueLabel = new Label("--");
        valueLabel.style.fontSize = 15;
        valueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        valueLabel.style.color = Color.white;

        heading.Add(nameLabel);
        heading.Add(valueLabel);

        VisualElement track = new VisualElement();
        track.style.height = 8;
        track.style.marginTop = 7;
        track.style.backgroundColor = (Color)new Color32(65, 75, 86, 255);
        track.style.borderTopLeftRadius = 4;
        track.style.borderTopRightRadius = 4;
        track.style.borderBottomLeftRadius = 4;
        track.style.borderBottomRightRadius = 4;

        progressBar = new VisualElement();
        progressBar.style.height = 8;
        progressBar.style.width = Length.Percent(50);
        progressBar.style.backgroundColor = AccentColor;
        progressBar.style.borderTopLeftRadius = 4;
        progressBar.style.borderTopRightRadius = 4;
        progressBar.style.borderBottomLeftRadius = 4;
        progressBar.style.borderBottomRightRadius = 4;
        track.Add(progressBar);

        content.Add(heading);
        content.Add(track);
        parent.Add(content);

        parent.style.marginBottom = 7;
        return valueLabel;
    }

    private Label AddStatistic(VisualElement parent, string title)
    {
        VisualElement item = new VisualElement();
        item.style.minWidth = 135;
        item.style.flexGrow = 1;
        item.style.marginRight = 10;
        item.style.marginBottom = 8;

        Label heading = new Label(title);
        heading.style.fontSize = 9;
        heading.style.unityFontStyleAndWeight = FontStyle.Bold;
        heading.style.color = MutedTextColor;

        Label value = new Label("--");
        value.style.fontSize = 19;
        value.style.unityFontStyleAndWeight = FontStyle.Bold;
        value.style.color = Color.white;
        value.style.marginTop = 3;

        item.Add(heading);
        item.Add(value);
        parent.Add(item);
        return value;
    }

    private Label CreateSectionTitle(string text)
    {
        Label label = new Label(text);
        label.style.fontSize = 11;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.color = MutedTextColor;
        label.style.marginTop = 8;
        label.style.marginBottom = 6;
        return label;
    }

    private VisualElement CreateCard()
    {
        VisualElement card = new VisualElement();
        card.style.flexDirection = FlexDirection.Column;
        card.style.paddingLeft = 11;
        card.style.paddingRight = 11;
        card.style.paddingTop = 9;
        card.style.paddingBottom = 9;
        card.style.backgroundColor = CardColor;
        card.style.borderTopLeftRadius = 6;
        card.style.borderTopRightRadius = 6;
        card.style.borderBottomLeftRadius = 6;
        card.style.borderBottomRightRadius = 6;
        return card;
    }

    private void OpenWindow()
    {
        if (window == null)
            return;

        windowOpen = true;
        window.style.display = DisplayStyle.Flex;
        RefreshUI();
    }

    private void CloseWindow()
    {
        windowOpen = false;

        if (window != null)
            window.style.display = DisplayStyle.None;
    }

    private void RefreshUI()
    {
        if (ratingManager == null)
        {
            if (buttonRatingLabel != null)
                buttonRatingLabel.text = "-- / 100";

            if (overallRatingLabel != null)
                overallRatingLabel.text = "-- / 100";

            if (visitorSatisfactionLabel != null)
                visitorSatisfactionLabel.text = "--";

            if (environmentLabel != null)
                environmentLabel.text = "--";

            if (completedVisitorsLabel != null)
                completedVisitorsLabel.text = "--";

            if (activeVisitorsLabel != null)
                activeVisitorsLabel.text = "--";

            if (visitorHoursLabel != null)
                visitorHoursLabel.text = "--";

            return;
        }

        float rating = ratingManager.AirportRating;
        if (buttonRatingLabel != null)
            buttonRatingLabel.text = $"{Mathf.RoundToInt(rating)} / 100";

        if (!windowOpen)
            return;

        overallRatingLabel.text = $"{Mathf.RoundToInt(rating)} / 100";

        float satisfaction = ratingManager.VisitorSatisfaction;
        visitorSatisfactionLabel.text = $"{Mathf.RoundToInt(satisfaction)}";
        satisfactionBar.style.width = Length.Percent(satisfaction);

        float environment = ratingManager.EnvironmentExperience;
        environmentLabel.text = $"{Mathf.RoundToInt(environment)}";
        environmentBar.style.width = Length.Percent(environment);

        completedVisitorsLabel.text =
            ratingManager.CompletedVisitors.ToString("N0");
        activeVisitorsLabel.text =
            ratingManager.ActiveVisitorCount.ToString("N0");
        visitorHoursLabel.text =
            $"{ratingManager.RecordedVisitorHours:N1} h";
    }
}
