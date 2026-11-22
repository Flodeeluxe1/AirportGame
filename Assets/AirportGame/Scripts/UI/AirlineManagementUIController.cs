using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

/// <summary>
/// Player-facing Airline Management window. Uses the shared airport UIDocument
/// and the existing AirlineContractOfferManager / snapshot collector systems.
/// </summary>
[DisallowMultipleComponent]
public class AirlineManagementUIController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private AirlineContractOfferManager contractManager;
    [SerializeField] private AirlineAirportSnapshotCollector snapshotCollector;
    [SerializeField] private GameTimeManager gameTimeManager;

    private static readonly Color WindowColor = new Color32(27, 32, 39, 250);
    private static readonly Color CardColor = new Color32(39, 47, 57, 255);
    private static readonly Color MutedTextColor = new Color32(174, 186, 199, 255);
    private static readonly Color AccentColor = new Color32(43, 91, 128, 255);
    private static readonly Color GreenColor = new Color32(65, 165, 112, 255);
    private static readonly Color AmberColor = new Color32(205, 151, 65, 255);

    private VisualElement root;
    private VisualElement window;
    private VisualElement content;
    private Label summaryOffers;
    private Label summaryActive;
    private Label summaryDailyValue;
    private Button openButton;
    private Button offersTab;
    private Button activeTab;
    private Button airlinesTab;
    private bool windowOpen;
    private int selectedTab;

    private void Awake()
    {
        ResolveReferences();

        if (uiDocument == null)
        {
            Debug.LogError("AirlineManagementUIController: shared UIDocument was not found.", this);
            enabled = false;
            return;
        }

        root = uiDocument.rootVisualElement;
        CreateOpenButton();
        CreateWindow();
        window.style.display = DisplayStyle.None;
    }

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        if (root == null)
            return;
        if (openButton != null)
            root.Remove(openButton);
        if (window != null)
            root.Remove(window);
    }

    private void Update()
    {
        if (windowOpen && Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame)
            CloseWindow();
    }

    private void ResolveReferences()
    {
        if (uiDocument == null)
        {
            FinanceManagementUIController financeUI =
                FindAnyObjectByType<FinanceManagementUIController>();
            if (financeUI != null)
                uiDocument = financeUI.GetComponent<UIDocument>();
        }
        if (uiDocument == null)
            uiDocument = FindAnyObjectByType<UIDocument>();
        if (contractManager == null)
            contractManager = FindAnyObjectByType<AirlineContractOfferManager>();
        if (snapshotCollector == null)
            snapshotCollector = FindAnyObjectByType<AirlineAirportSnapshotCollector>();
        if (gameTimeManager == null)
            gameTimeManager = FindAnyObjectByType<GameTimeManager>();
    }

    private void Subscribe()
    {
        if (contractManager != null)
        {
            contractManager.OfferGenerated -= HandleOfferChanged;
            contractManager.OfferExpired -= HandleOfferExpired;
            contractManager.ContractAccepted -= HandleContractChanged;
            contractManager.ContractCompleted -= HandleContractChanged;
            contractManager.OfferGenerated += HandleOfferChanged;
            contractManager.OfferExpired += HandleOfferExpired;
            contractManager.ContractAccepted += HandleContractChanged;
            contractManager.ContractCompleted += HandleContractChanged;
        }

        if (snapshotCollector != null)
        {
            snapshotCollector.InterestUpdated -= HandleInterestUpdated;
            snapshotCollector.InterestUpdated += HandleInterestUpdated;
        }
    }

    private void Unsubscribe()
    {
        if (contractManager != null)
        {
            contractManager.OfferGenerated -= HandleOfferChanged;
            contractManager.OfferExpired -= HandleOfferExpired;
            contractManager.ContractAccepted -= HandleContractChanged;
            contractManager.ContractCompleted -= HandleContractChanged;
        }
        if (snapshotCollector != null)
            snapshotCollector.InterestUpdated -= HandleInterestUpdated;
    }

    private void HandleOfferChanged(AirlineContractOffer offer) => RefreshIfOpen();
    private void HandleOfferExpired(AirlineContractOffer offer) => RefreshIfOpen();
    private void HandleContractChanged(ActiveAirlineContract contract) => RefreshIfOpen();
    private void HandleInterestUpdated(AirlineDefinition airline, AirlineInterestResult result) => RefreshIfOpen();

    private void RefreshIfOpen()
    {
        if (windowOpen)
            RefreshWindow();
    }

    private void CreateOpenButton()
    {
        openButton = new Button(OpenWindow) { text = "Airline Management" };
        openButton.name = "airline-management-open-button";
        openButton.style.position = Position.Absolute;
        // Finance is 135 px wide at right = 550. Leave a 12 px gap.
        openButton.style.right = 697;
        openButton.style.bottom = 14;
        openButton.style.height = 42;
        openButton.style.minWidth = 170;
        openButton.style.paddingLeft = 14;
        openButton.style.paddingRight = 14;
        openButton.style.backgroundColor = AccentColor;
        openButton.style.color = Color.white;
        SetRadius(openButton, 6);
        root.Add(openButton);
    }

    private void CreateWindow()
    {
        window = new VisualElement();
        window.name = "airline-management-window";
        window.style.position = Position.Absolute;
        window.style.left = new Length(14, LengthUnit.Percent);
        window.style.right = new Length(14, LengthUnit.Percent);
        window.style.top = 48;
        window.style.bottom = 68;
        window.style.flexDirection = FlexDirection.Column;
        window.style.paddingLeft = 14;
        window.style.paddingRight = 14;
        window.style.paddingTop = 12;
        window.style.paddingBottom = 12;
        window.style.backgroundColor = WindowColor;
        SetRadius(window, 8);
        root.Add(window);

        VisualElement header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.justifyContent = Justify.SpaceBetween;

        VisualElement headingGroup = new VisualElement();
        headingGroup.style.flexDirection = FlexDirection.Column;
        Label title = new Label("AIRLINE MANAGEMENT");
        title.style.fontSize = 22;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = Color.white;
        Label subtitle = new Label("AIRLINE PARTNERSHIPS AND CONTRACTS");
        subtitle.style.fontSize = 10;
        subtitle.style.color = MutedTextColor;
        subtitle.style.marginTop = 2;
        headingGroup.Add(title);
        headingGroup.Add(subtitle);

        Button close = new Button(CloseWindow) { text = "Close  X" };
        close.style.minWidth = 80;
        header.Add(headingGroup);
        header.Add(close);
        window.Add(header);

        VisualElement summary = new VisualElement();
        summary.style.flexDirection = FlexDirection.Row;
        summary.style.marginTop = 10;
        summary.style.marginBottom = 10;
        summary.Add(CreateSummaryCard("AVAILABLE OFFERS", out summaryOffers));
        summary.Add(CreateSummaryCard("ACTIVE CONTRACTS", out summaryActive));
        summary.Add(CreateSummaryCard("ACTIVE DAILY VALUE", out summaryDailyValue));
        window.Add(summary);

        VisualElement tabs = new VisualElement();
        tabs.style.flexDirection = FlexDirection.Row;
        tabs.style.marginBottom = 8;
        offersTab = CreateTab("Offers", 0);
        activeTab = CreateTab("Active Contracts", 1);
        airlinesTab = CreateTab("Airlines", 2);
        tabs.Add(offersTab);
        tabs.Add(activeTab);
        tabs.Add(airlinesTab);
        window.Add(tabs);

        ScrollView scroll = new ScrollView(ScrollViewMode.Vertical);
        scroll.name = "airline-management-content-scroll";
        scroll.style.flexGrow = 1;
        scroll.style.minHeight = 0;
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        content = scroll.contentContainer;
        content.style.flexDirection = FlexDirection.Column;
        window.Add(scroll);
    }

    private VisualElement CreateSummaryCard(string heading, out Label value)
    {
        VisualElement card = CreateCard();
        card.style.flexGrow = 1;
        card.style.marginRight = 6;
        Label title = new Label(heading);
        title.style.fontSize = 9;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = MutedTextColor;
        value = new Label("0");
        value.style.fontSize = 19;
        value.style.unityFontStyleAndWeight = FontStyle.Bold;
        value.style.color = Color.white;
        card.Add(title);
        card.Add(value);
        return card;
    }

    private Button CreateTab(string label, int tab)
    {
        Button button = new Button(() =>
        {
            selectedTab = tab;
            RefreshWindow();
        }) { text = label };
        button.style.height = 34;
        button.style.minWidth = 125;
        button.style.marginRight = 6;
        return button;
    }

    private void OpenWindow()
    {
        windowOpen = true;
        window.style.display = DisplayStyle.Flex;
        RefreshWindow();
    }

    private void CloseWindow()
    {
        windowOpen = false;
        window.style.display = DisplayStyle.None;
    }

    private void RefreshWindow()
    {
        ResolveReferences();
        int offerCount = contractManager != null ? contractManager.PendingOffers.Count : 0;
        int activeCount = 0;
        int dailyValue = 0;

        if (contractManager != null)
        {
            foreach (ActiveAirlineContract contract in contractManager.ActiveContracts)
            {
                if (contract == null || contract.status != AirlineContractStatus.Active)
                    continue;
                activeCount++;
                if (contract.agreedTerms != null)
                    dailyValue += GetFlightsPerDay(contract.agreedTerms) *
                                  contract.agreedTerms.paymentPerCompletedFlight;
            }
        }

        summaryOffers.text = offerCount.ToString();
        summaryActive.text = activeCount.ToString();
        summaryDailyValue.text = dailyValue.ToString("N0") + " €";

        StyleTab(offersTab, selectedTab == 0);
        StyleTab(activeTab, selectedTab == 1);
        StyleTab(airlinesTab, selectedTab == 2);
        content.Clear();

        if (selectedTab == 0)
            BuildOffersTab();
        else if (selectedTab == 1)
            BuildActiveContractsTab();
        else
            BuildAirlinesTab();
    }

    private void BuildOffersTab()
    {
        AddSectionHeading("AVAILABLE CONTRACT OFFERS",
            "Review the terms before accepting an airline partnership.");

        if (contractManager == null)
        {
            AddEmptyState("Airline contract manager is not available in this scene.");
            return;
        }

        if (contractManager.PendingOffers.Count == 0)
        {
            AddEmptyState("There are no available offers right now. New offers are evaluated periodically.");
            return;
        }

        foreach (AirlineContractOffer offer in contractManager.PendingOffers)
        {
            if (offer == null)
                continue;
            content.Add(BuildOfferCard(offer));
        }
    }

    private VisualElement BuildOfferCard(AirlineContractOffer offer)
    {
        VisualElement card = CreateCard();
        VisualElement top = new VisualElement();
        top.style.flexDirection = FlexDirection.Row;
        top.style.alignItems = Align.Center;

        AddAirlineLogo(top, offer.airline);
        VisualElement identity = new VisualElement();
        identity.style.flexGrow = 1;
        Label name = new Label(offer.airline != null ? offer.airline.DisplayName : "Unknown airline");
        name.style.fontSize = 15;
        name.style.unityFontStyleAndWeight = FontStyle.Bold;
        name.style.color = Color.white;
        Label template = new Label(offer.template != null ? offer.template.DisplayName : "Contract offer");
        template.style.fontSize = 10;
        template.style.color = MutedTextColor;
        identity.Add(name);
        identity.Add(template);
        top.Add(identity);
        Label status = new Label("AVAILABLE");
        status.style.fontSize = 9;
        status.style.unityFontStyleAndWeight = FontStyle.Bold;
        status.style.color = GreenColor;
        top.Add(status);
        card.Add(top);

        AirlineContractTerms terms = offer.terms;
        if (terms == null)
        {
            AddDescription(card, "This offer has no valid contract terms.");
            return card;
        }

        VisualElement metrics = new VisualElement();
        metrics.style.flexDirection = FlexDirection.Row;
        metrics.style.flexWrap = Wrap.Wrap;
        metrics.style.marginTop = 10;
        AddMetric(metrics, "DURATION", terms.durationDays + " days");
        AddMetric(metrics, "PAYMENT", terms.paymentPerCompletedFlight.ToString("N0") + " € / flight");
        AddMetric(metrics, "DAILY FLIGHTS", GetFlightsPerDay(terms).ToString());
        AddMetric(metrics, "TURNAROUND", terms.turnaroundMinutes + " min");
        card.Add(metrics);

        VisualElement requirements = CreateInset();
        Label reqHeading = new Label("DAILY FLIGHT REQUIREMENTS");
        StyleSmallHeading(reqHeading);
        requirements.Add(reqHeading);
        AddWindowRequirements(requirements, terms);
        card.Add(requirements);

        VisualElement detail = CreateInset();
        Label detailHeading = new Label("AIRPORT REQUIREMENTS");
        StyleSmallHeading(detailHeading);
        detail.Add(detailHeading);
        AddDetailLine(detail, "Minimum gate", terms.minimumGateSize.ToString());
        AddDetailLine(detail, "Runway length", terms.minimumRunwayLengthMeters.ToString("0") + " m");
        if (offer.airline == null || offer.airline.OperationType != AirlineOperationType.Passenger)
        {
            AddDetailLine(detail, "Cargo storage", terms.minimumCargoStorageCapacity.ToString("0.#") + " packages");
            AddDetailLine(detail, "Handling throughput", terms.minimumCargoHandlingThroughputPerHour.ToString("0.#") + " packages/hour");
        }
        AddDetailLine(detail, "Missed-flight reduction", (terms.missedFlightPaymentReduction * 100f).ToString("0") + "%");
        AddDetailLine(detail, "Completion bonus", terms.completionBonus.ToString("N0") + " €");
        card.Add(detail);

        Label interest = new Label("AIRLINE INTEREST  " + offer.airlineInterest.ToString("0") + " / 100");
        interest.style.fontSize = 10;
        interest.style.color = MutedTextColor;
        interest.style.marginTop = 7;
        card.Add(interest);

        long day = gameTimeManager != null ? gameTimeManager.CurrentDay : 1;
        Label expiry = new Label("Expires in " + Math.Max(0, offer.expiresOnDay - day) + " in-game days");
        expiry.style.fontSize = 10;
        expiry.style.color = AmberColor;
        expiry.style.marginTop = 3;
        card.Add(expiry);

        VisualElement actions = new VisualElement();
        actions.style.flexDirection = FlexDirection.Row;
        actions.style.justifyContent = Justify.FlexEnd;
        actions.style.marginTop = 9;
        Button decline = new Button(() =>
        {
            if (contractManager != null && contractManager.DeclineOffer(offer.offerId))
                RefreshWindow();
        }) { text = "Decline" };
        decline.style.minWidth = 100;
        decline.style.height = 36;
        Button accept = new Button(() =>
        {
            if (contractManager != null && contractManager.AcceptOffer(offer.offerId))
                RefreshWindow();
        }) { text = "Accept Contract" };
        accept.style.minWidth = 140;
        accept.style.height = 36;
        accept.style.marginLeft = 8;
        accept.style.backgroundColor = GreenColor;
        accept.style.color = Color.white;
        actions.Add(decline);
        actions.Add(accept);
        card.Add(actions);
        return card;
    }

    private void BuildActiveContractsTab()
    {
        AddSectionHeading("ACTIVE CONTRACTS",
            "Monitor accepted agreements and their recurring daily obligations.");

        if (contractManager == null)
        {
            AddEmptyState("Airline contract manager is not available in this scene.");
            return;
        }

        int shown = 0;
        foreach (ActiveAirlineContract contract in contractManager.ActiveContracts)
        {
            if (contract == null || contract.status != AirlineContractStatus.Active)
                continue;
            content.Add(BuildActiveContractCard(contract));
            shown++;
        }

        if (shown == 0)
            AddEmptyState("You have no active airline contracts. Accepted offers will appear here.");
    }

    private VisualElement BuildActiveContractCard(ActiveAirlineContract contract)
    {
        VisualElement card = CreateCard();
        VisualElement top = new VisualElement();
        top.style.flexDirection = FlexDirection.Row;
        top.style.alignItems = Align.Center;
        AddAirlineLogo(top, contract.airline);

        VisualElement identity = new VisualElement();
        identity.style.flexGrow = 1;
        Label name = new Label(contract.airline != null ? contract.airline.DisplayName : "Unknown airline");
        name.style.fontSize = 15;
        name.style.unityFontStyleAndWeight = FontStyle.Bold;
        name.style.color = Color.white;
        Label subtitle = new Label(contract.sourceTemplate != null ? contract.sourceTemplate.DisplayName : "Active contract");
        subtitle.style.fontSize = 10;
        subtitle.style.color = MutedTextColor;
        identity.Add(name);
        identity.Add(subtitle);
        top.Add(identity);
        Label active = new Label("ACTIVE");
        active.style.fontSize = 9;
        active.style.unityFontStyleAndWeight = FontStyle.Bold;
        active.style.color = GreenColor;
        top.Add(active);
        card.Add(top);

        long day = gameTimeManager != null ? gameTimeManager.CurrentDay : contract.startDay;
        int duration = contract.agreedTerms != null ? contract.agreedTerms.durationDays : (int)(contract.endDay - contract.startDay + 1);
        int currentDay = Mathf.Clamp((int)(day - contract.startDay + 1), 1, Math.Max(1, duration));
        AddDescription(card, "Contract progress  ·  Day " + currentDay + " / " + duration);

        VisualElement progressTrack = new VisualElement();
        progressTrack.style.height = 7;
        progressTrack.style.backgroundColor = new Color32(58, 67, 78, 255);
        SetRadius(progressTrack, 4);
        VisualElement progress = new VisualElement();
        progress.style.height = 7;
        progress.style.width = new Length((float)currentDay / Math.Max(1, duration) * 100f, LengthUnit.Percent);
        progress.style.backgroundColor = GreenColor;
        SetRadius(progress, 4);
        progressTrack.Add(progress);
        card.Add(progressTrack);

        if (contract.agreedTerms != null)
        {
            VisualElement metrics = new VisualElement();
            metrics.style.flexDirection = FlexDirection.Row;
            metrics.style.flexWrap = Wrap.Wrap;
            AddMetric(metrics, "DAILY FLIGHTS", GetFlightsPerDay(contract.agreedTerms).ToString());
            AddMetric(metrics, "PAYMENT / FLIGHT", contract.agreedTerms.paymentPerCompletedFlight.ToString("N0") + " €");
            AddMetric(metrics, "TURNAROUND", contract.agreedTerms.turnaroundMinutes + " min");
            card.Add(metrics);

            VisualElement requirements = CreateInset();
            Label reqHeading = new Label("RECURRING DAILY OBLIGATIONS");
            StyleSmallHeading(reqHeading);
            requirements.Add(reqHeading);
            AddWindowRequirements(requirements, contract.agreedTerms);
            card.Add(requirements);
        }

        AddDescription(card, "Schedule each recurring obligation once in Flight Management. The timetable repeats it on each active contract day.");
        return card;
    }

    private void BuildAirlinesTab()
    {
        AddSectionHeading("AIRLINES",
            "Airport attractiveness is shown separately from satisfaction with completed operations.");

        if (snapshotCollector == null)
        {
            AddEmptyState("Airline airport data is not available in this scene.");
            return;
        }

        IReadOnlyList<AirlineDefinition> airlines = snapshotCollector.ConfiguredAirlines;
        if (airlines == null || airlines.Count == 0)
        {
            AddEmptyState("No airline definitions are configured yet.");
            return;
        }

        foreach (AirlineDefinition airline in airlines)
        {
            if (airline == null)
                continue;

            AirlineInterestResult result;
            if (!snapshotCollector.TryGetInterest(airline, out result))
                result = snapshotCollector.RefreshInterest(airline);

            VisualElement card = CreateCard();
            VisualElement top = new VisualElement();
            top.style.flexDirection = FlexDirection.Row;
            top.style.alignItems = Align.Center;
            AddAirlineLogo(top, airline);

            VisualElement identity = new VisualElement();
            identity.style.flexGrow = 1;
            Label name = new Label(airline.DisplayName);
            name.style.fontSize = 15;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.color = Color.white;
            Label type = new Label(airline.OperationType.ToString());
            type.style.fontSize = 10;
            type.style.color = MutedTextColor;
            identity.Add(name);
            identity.Add(type);
            top.Add(identity);

            Label score = new Label(result.totalInterest.ToString("0") + " / 100");
            score.style.fontSize = 17;
            score.style.unityFontStyleAndWeight = FontStyle.Bold;
            score.style.color = GetScoreColor(result.totalInterest);
            top.Add(score);
            card.Add(top);

            AddInterestFactor(card, "Airport fees", result.airportFees);
            AddInterestFactor(card, "Suitable gates", result.suitableGates);
            AddInterestFactor(card, "Runway suitability", result.runwaySuitability);
            if (airline.OperationType != AirlineOperationType.Passenger)
                AddInterestFactor(card, "Cargo handling capacity", result.cargoHandlingCapacity);
            AddInterestFactor(card, "Night operations", result.nightOperations);
            AddInterestFactor(card, "Airport reputation", result.airportReputation);
            AddInterestFactor(card, "Previous contract performance", result.previousContractPerformance);
            AddDescription(card, "Relationship satisfaction is tracked separately and will be added with contract performance.");
            content.Add(card);
        }
    }

    private void AddInterestFactor(VisualElement parent, string label, float score)
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginTop = 5;
        Label title = new Label(label);
        title.style.fontSize = 10;
        title.style.color = MutedTextColor;
        title.style.flexGrow = 1;
        Label value = new Label(score.ToString("0") + "%");
        value.style.fontSize = 10;
        value.style.color = Color.white;
        row.Add(title);
        row.Add(value);
        parent.Add(row);

        VisualElement track = new VisualElement();
        track.style.height = 4;
        track.style.marginTop = 2;
        track.style.backgroundColor = new Color32(58, 67, 78, 255);
        SetRadius(track, 2);
        VisualElement fill = new VisualElement();
        fill.style.height = 4;
        fill.style.width = new Length(Mathf.Clamp(score, 0f, 100f), LengthUnit.Percent);
        fill.style.backgroundColor = GetScoreColor(score);
        SetRadius(fill, 2);
        track.Add(fill);
        parent.Add(track);
    }

    private void AddWindowRequirements(VisualElement parent, AirlineContractTerms terms)
    {
        if (terms.flightWindows == null || terms.flightWindows.Count == 0)
        {
            AddDetailLine(parent, "Flight windows", "No daily flights specified");
            return;
        }

        foreach (AirlineContractFlightWindowRequirement requirement in terms.flightWindows)
        {
            if (requirement == null || requirement.flightsPerDay <= 0)
                continue;
            AddDetailLine(parent, requirement.window.ToString(),
                requirement.flightsPerDay + (requirement.flightsPerDay == 1 ? " flight / day" : " flights / day"));
        }
    }

    private static int GetFlightsPerDay(AirlineContractTerms terms)
    {
        int total = 0;
        if (terms == null || terms.flightWindows == null)
            return total;
        foreach (AirlineContractFlightWindowRequirement requirement in terms.flightWindows)
            if (requirement != null)
                total += Mathf.Max(0, requirement.flightsPerDay);
        return total;
    }

    private void AddSectionHeading(string heading, string description)
    {
        Label title = new Label(heading);
        title.style.fontSize = 13;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = new Color32(215, 222, 231, 255);
        title.style.marginTop = 3;
        title.style.marginBottom = 2;
        content.Add(title);
        AddDescription(content, description);
    }

    private void AddEmptyState(string message)
    {
        VisualElement card = CreateCard();
        Label label = new Label(message);
        label.style.fontSize = 11;
        label.style.color = MutedTextColor;
        label.style.whiteSpace = WhiteSpace.Normal;
        card.Add(label);
        content.Add(card);
    }

    private void AddAirlineLogo(VisualElement parent, AirlineDefinition airline)
    {
        if (airline == null || airline.Logo == null)
            return;
        Image logo = new Image { sprite = airline.Logo, scaleMode = ScaleMode.ScaleToFit };
        logo.style.width = 42;
        logo.style.height = 42;
        logo.style.marginRight = 10;
        parent.Add(logo);
    }

    private void AddMetric(VisualElement parent, string heading, string value)
    {
        VisualElement metric = CreateInset();
        metric.style.minWidth = 130;
        metric.style.flexGrow = 1;
        metric.style.marginRight = 6;
        metric.style.marginBottom = 5;
        Label title = new Label(heading);
        title.style.fontSize = 9;
        title.style.color = MutedTextColor;
        Label contentLabel = new Label(value);
        contentLabel.style.fontSize = 12;
        contentLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        contentLabel.style.color = Color.white;
        metric.Add(title);
        metric.Add(contentLabel);
        parent.Add(metric);
    }

    private void AddDetailLine(VisualElement parent, string label, string value)
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.marginTop = 2;
        Label left = new Label(label);
        left.style.fontSize = 10;
        left.style.color = MutedTextColor;
        Label right = new Label(value);
        right.style.fontSize = 10;
        right.style.color = Color.white;
        row.Add(left);
        row.Add(right);
        parent.Add(row);
    }

    private void AddDescription(VisualElement parent, string text)
    {
        Label label = new Label(text);
        label.style.fontSize = 10;
        label.style.color = MutedTextColor;
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.marginTop = 5;
        label.style.marginBottom = 4;
        parent.Add(label);
    }

    private VisualElement CreateCard()
    {
        VisualElement card = new VisualElement();
        card.style.flexDirection = FlexDirection.Column;
        card.style.paddingLeft = 10;
        card.style.paddingRight = 10;
        card.style.paddingTop = 9;
        card.style.paddingBottom = 9;
        card.style.marginBottom = 7;
        card.style.backgroundColor = CardColor;
        SetRadius(card, 6);
        return card;
    }

    private VisualElement CreateInset()
    {
        VisualElement inset = new VisualElement();
        inset.style.paddingLeft = 8;
        inset.style.paddingRight = 8;
        inset.style.paddingTop = 6;
        inset.style.paddingBottom = 6;
        inset.style.marginTop = 6;
        inset.style.backgroundColor = new Color32(31, 38, 47, 255);
        SetRadius(inset, 4);
        return inset;
    }

    private static void StyleSmallHeading(Label label)
    {
        label.style.fontSize = 9;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.color = new Color32(215, 222, 231, 255);
        label.style.marginBottom = 4;
    }

    private static void StyleTab(Button button, bool selected)
    {
        button.style.backgroundColor = selected ? AccentColor : CardColor;
        button.style.color = Color.white;
        button.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
    }

    private static void SetRadius(VisualElement element, float radius)
    {
        element.style.borderTopLeftRadius = radius;
        element.style.borderTopRightRadius = radius;
        element.style.borderBottomLeftRadius = radius;
        element.style.borderBottomRightRadius = radius;
    }

    private static Color GetScoreColor(float score)
    {
        if (score >= 70f)
            return GreenColor;
        if (score >= 40f)
            return AmberColor;
        return new Color32(205, 91, 82, 255);
    }
}
