using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Opens the airport's financial reports when the existing balance HUD is clicked.
/// Attach this component beside AirportEconomyHUD on the main AirportUI object.
/// </summary>
public class AirportFinancialReportsUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private AirportEconomyHUD economyHUD;
    [SerializeField] private AirportEconomyManager economyManager;
    [SerializeField] private GameTimeManager gameTimeManager;

    private static readonly Color WindowColor = new Color32(27, 32, 39, 250);
    private static readonly Color CardColor = new Color32(39, 47, 57, 255);
    private static readonly Color MutedTextColor = new Color32(174, 186, 199, 255);
    private static readonly Color IncomeColor = new Color32(100, 210, 140, 255);
    private static readonly Color ExpenseColor = new Color32(235, 125, 105, 255);

    private VisualElement root;
    private VisualElement window;
    private ScrollView reportScroll;
    private Label currentBalanceLabel;
    private Label lifetimeIncomeLabel;
    private Label lifetimeExpensesLabel;
    private Label lifetimeNetLabel;
    private Label dailyIncomeLabel;
    private Label dailyExpensesLabel;
    private Label dailyNetLabel;
    private Label monthIncomeLabel;
    private Label monthExpensesLabel;
    private Label monthNetLabel;
    private VisualElement incomeBreakdown;
    private VisualElement expenseBreakdown;
    private VisualElement recentTransactions;

    private bool windowOpen;
    private float nextRefreshTime;

    private void OnEnable()
    {
        ResolveReferences();

        if (uiDocument == null)
        {
            Debug.LogError(
                "AirportFinancialReportsUI: UIDocument is missing. Assign the main AirportUI document.",
                this);
            enabled = false;
            return;
        }

        root = uiDocument.rootVisualElement;
        if (root == null)
        {
            Debug.LogError("AirportFinancialReportsUI: UIDocument has no root element.", this);
            enabled = false;
            return;
        }

        if (economyHUD != null)
            economyHUD.BalanceClicked += OpenWindow;
        else
            Debug.LogWarning(
                "AirportFinancialReportsUI: AirportEconomyHUD was not found. " +
                "The reports window cannot be opened from the balance until it is assigned.",
                this);

        CreateWindow();
        window.style.display = DisplayStyle.None;
    }

    private void OnDisable()
    {
        if (economyHUD != null)
            economyHUD.BalanceClicked -= OpenWindow;

        if (root != null && window != null)
            root.Remove(window);

        root = null;
        window = null;
        windowOpen = false;
    }

    private void Update()
    {
        if (!windowOpen || Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime = Time.unscaledTime + 0.5f;
        ResolveDataReferences();
        RefreshWindow();
    }

    private void ResolveReferences()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null)
            uiDocument = FindAnyObjectByType<UIDocument>();

        if (economyHUD == null)
            economyHUD = GetComponent<AirportEconomyHUD>();
        if (economyHUD == null)
            economyHUD = FindAnyObjectByType<AirportEconomyHUD>();

        ResolveDataReferences();
    }

    private void ResolveDataReferences()
    {
        if (economyManager == null)
            economyManager = FindAnyObjectByType<AirportEconomyManager>();
        if (gameTimeManager == null)
            gameTimeManager = FindAnyObjectByType<GameTimeManager>();
    }

    private void CreateWindow()
    {
        window = new VisualElement();
        window.name = "airport-financial-reports-window";
        window.style.position = Position.Absolute;
        window.style.left = new Length(12, LengthUnit.Percent);
        window.style.right = new Length(12, LengthUnit.Percent);
        window.style.top = 54;
        window.style.bottom = 58;
        window.style.minWidth = 460;
        window.style.flexDirection = FlexDirection.Column;
        window.style.paddingLeft = 16;
        window.style.paddingRight = 16;
        window.style.paddingTop = 14;
        window.style.paddingBottom = 12;
        window.style.backgroundColor = WindowColor;
        window.style.borderTopLeftRadius = 8;
        window.style.borderTopRightRadius = 8;
        window.style.borderBottomLeftRadius = 8;
        window.style.borderBottomRightRadius = 8;
        root.Add(window);

        CreateHeader();

        reportScroll = new ScrollView(ScrollViewMode.Vertical);
        reportScroll.name = "airport-financial-reports-scroll";
        reportScroll.style.flexGrow = 1;
        reportScroll.style.minHeight = 0;
        reportScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        reportScroll.contentContainer.style.flexDirection = FlexDirection.Column;
        window.Add(reportScroll);

        CreateCurrentBalance();
        CreatePeriodSummaries();
        CreateCategoryBreakdown();
        CreateRecentTransactions();
    }

    private void CreateHeader()
    {
        VisualElement header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.justifyContent = Justify.SpaceBetween;
        header.style.marginBottom = 10;

        Label title = new Label("FINANCIAL REPORTS");
        title.style.fontSize = 22;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = Color.white;

        Button closeButton = new Button(CloseWindow) { text = "X" };
        closeButton.name = "airport-financial-reports-close-button";
        closeButton.style.width = 38;
        closeButton.style.height = 34;
        closeButton.style.minWidth = 38;
        closeButton.style.fontSize = 16;
        closeButton.style.unityFontStyleAndWeight = FontStyle.Bold;

        header.Add(title);
        header.Add(closeButton);
        window.Add(header);

        Label subtitle = new Label("AIRPORT INCOME, EXPENSES AND TRANSACTION HISTORY");
        subtitle.style.fontSize = 10;
        subtitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        subtitle.style.color = MutedTextColor;
        subtitle.style.marginBottom = 8;
        window.Add(subtitle);
    }

    private void CreateCurrentBalance()
    {
        VisualElement card = CreateCard();
        card.style.marginBottom = 8;

        Label heading = CreateSmallHeading("CURRENT BALANCE");
        currentBalanceLabel = new Label("$0");
        currentBalanceLabel.style.fontSize = 30;
        currentBalanceLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        currentBalanceLabel.style.color = Color.white;
        currentBalanceLabel.style.marginTop = 2;

        card.Add(heading);
        card.Add(currentBalanceLabel);
        reportScroll.Add(card);
    }

    private void CreatePeriodSummaries()
    {
        reportScroll.Add(CreateSectionTitle("ALL-TIME SUMMARY"));
        VisualElement lifetimeCard = CreateCard();
        lifetimeIncomeLabel = AddMetric(lifetimeCard, "TOTAL INCOME", IncomeColor);
        lifetimeExpensesLabel = AddMetric(lifetimeCard, "TOTAL EXPENSES", ExpenseColor);
        lifetimeNetLabel = AddMetric(lifetimeCard, "NET PROFIT", Color.white);
        reportScroll.Add(lifetimeCard);

        reportScroll.Add(CreateSectionTitle("DAILY SUMMARY"));
        VisualElement dailyCard = CreateCard();
        dailyIncomeLabel = AddMetric(dailyCard, "INCOME", IncomeColor);
        dailyExpensesLabel = AddMetric(dailyCard, "EXPENSES", ExpenseColor);
        dailyNetLabel = AddMetric(dailyCard, "NET PROFIT", Color.white);
        reportScroll.Add(dailyCard);

        reportScroll.Add(CreateSectionTitle("ROLLING 30-DAY SUMMARY"));
        VisualElement monthCard = CreateCard();
        monthIncomeLabel = AddMetric(monthCard, "INCOME", IncomeColor);
        monthExpensesLabel = AddMetric(monthCard, "EXPENSES", ExpenseColor);
        monthNetLabel = AddMetric(monthCard, "NET PROFIT", Color.white);
        reportScroll.Add(monthCard);
    }

    private void CreateCategoryBreakdown()
    {
        reportScroll.Add(CreateSectionTitle("LAST 30 DAYS BY CATEGORY"));

        VisualElement card = CreateCard();
        Label incomeTitle = CreateSmallHeading("INCOME");
        incomeTitle.style.color = IncomeColor;
        incomeBreakdown = new VisualElement();
        incomeBreakdown.style.flexDirection = FlexDirection.Column;

        Label expenseTitle = CreateSmallHeading("EXPENSES");
        expenseTitle.style.color = ExpenseColor;
        expenseTitle.style.marginTop = 10;
        expenseBreakdown = new VisualElement();
        expenseBreakdown.style.flexDirection = FlexDirection.Column;

        card.Add(incomeTitle);
        card.Add(incomeBreakdown);
        card.Add(expenseTitle);
        card.Add(expenseBreakdown);
        reportScroll.Add(card);
    }

    private void CreateRecentTransactions()
    {
        reportScroll.Add(CreateSectionTitle("RECENT TRANSACTIONS"));

        VisualElement card = CreateCard();
        recentTransactions = new VisualElement();
        recentTransactions.style.flexDirection = FlexDirection.Column;
        card.Add(recentTransactions);
        reportScroll.Add(card);
    }

    private Label AddMetric(VisualElement parent, string title, Color valueColor)
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.marginBottom = 5;

        Label name = new Label(title);
        name.style.fontSize = 12;
        name.style.unityFontStyleAndWeight = FontStyle.Bold;
        name.style.color = MutedTextColor;

        Label value = new Label("$0");
        value.style.fontSize = 15;
        value.style.unityFontStyleAndWeight = FontStyle.Bold;
        value.style.color = valueColor;

        row.Add(name);
        row.Add(value);
        parent.Add(row);
        return value;
    }

    private void RefreshWindow()
    {
        if (economyManager == null)
        {
            currentBalanceLabel.text = "Economy manager unavailable";
            lifetimeIncomeLabel.text = "--";
            lifetimeExpensesLabel.text = "--";
            lifetimeNetLabel.text = "--";
            dailyIncomeLabel.text = "--";
            dailyExpensesLabel.text = "--";
            dailyNetLabel.text = "--";
            monthIncomeLabel.text = "--";
            monthExpensesLabel.text = "--";
            monthNetLabel.text = "--";
            incomeBreakdown.Clear();
            expenseBreakdown.Clear();
            recentTransactions.Clear();
            return;
        }

        currentBalanceLabel.text = FormatMoney(economyManager.Balance);

        long now = GetCurrentAbsoluteGameSecond();
        long currentDayIndex = gameTimeManager != null
            ? gameTimeManager.CurrentDay - 1L
            : now / 86400L;
        long rollingStart = Math.Max(0L, now - 30L * 86400L);

        long lifetimeIncome = 0L;
        long lifetimeExpenses = 0L;
        long dailyIncome = 0L;
        long dailyExpenses = 0L;
        long periodIncome = 0L;
        long periodExpenses = 0L;

        Dictionary<AirportFinancialCategory, long> incomeByCategory =
            new Dictionary<AirportFinancialCategory, long>();
        Dictionary<AirportFinancialCategory, long> expensesByCategory =
            new Dictionary<AirportFinancialCategory, long>();

        IReadOnlyList<AirportFinancialTransaction> transactions =
            economyManager.Transactions;

        for (int i = 0; i < transactions.Count; i++)
        {
            AirportFinancialTransaction transaction = transactions[i];
            if (transaction == null || transaction.amount <= 0)
                continue;

            if (transaction.isIncome)
                lifetimeIncome += transaction.amount;
            else
                lifetimeExpenses += transaction.amount;

            long transactionDay = transaction.absoluteGameSecond / 86400L;
            if (transactionDay == currentDayIndex)
            {
                if (transaction.isIncome)
                    dailyIncome += transaction.amount;
                else
                    dailyExpenses += transaction.amount;
            }

            if (transaction.absoluteGameSecond < rollingStart ||
                transaction.absoluteGameSecond > now)
                continue;

            if (transaction.isIncome)
            {
                periodIncome += transaction.amount;
                AddCategoryTotal(incomeByCategory, transaction);
            }
            else
            {
                periodExpenses += transaction.amount;
                AddCategoryTotal(expensesByCategory, transaction);
            }
        }

        lifetimeIncomeLabel.text = FormatMoney(lifetimeIncome);
        lifetimeExpensesLabel.text = FormatMoney(lifetimeExpenses);
        lifetimeNetLabel.text = FormatSignedMoney(lifetimeIncome - lifetimeExpenses);

        dailyIncomeLabel.text = FormatMoney(dailyIncome);
        dailyExpensesLabel.text = FormatMoney(dailyExpenses);
        dailyNetLabel.text = FormatSignedMoney(dailyIncome - dailyExpenses);

        monthIncomeLabel.text = FormatMoney(periodIncome);
        monthExpensesLabel.text = FormatMoney(periodExpenses);
        monthNetLabel.text = FormatSignedMoney(periodIncome - periodExpenses);

        PopulateCategoryList(incomeBreakdown, incomeByCategory, true);
        PopulateCategoryList(expenseBreakdown, expensesByCategory, false);
        PopulateRecentTransactions(transactions);
    }

    private void AddCategoryTotal(
        Dictionary<AirportFinancialCategory, long> totals,
        AirportFinancialTransaction transaction)
    {
        if (!totals.ContainsKey(transaction.category))
            totals[transaction.category] = 0L;

        totals[transaction.category] += transaction.amount;
    }

    private void PopulateCategoryList(
        VisualElement parent,
        Dictionary<AirportFinancialCategory, long> totals,
        bool isIncome)
    {
        parent.Clear();

        if (totals.Count == 0)
        {
            Label empty = new Label("No transactions recorded.");
            empty.style.fontSize = 11;
            empty.style.color = MutedTextColor;
            parent.Add(empty);
            return;
        }

        foreach (KeyValuePair<AirportFinancialCategory, long> entry in totals)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;
            row.style.marginTop = 3;

            Label category = new Label(GetCategoryName(entry.Key));
            category.style.fontSize = 12;
            category.style.color = Color.white;

            Label amount = new Label(FormatMoney(entry.Value));
            amount.style.fontSize = 12;
            amount.style.unityFontStyleAndWeight = FontStyle.Bold;
            amount.style.color = isIncome ? IncomeColor : ExpenseColor;

            row.Add(category);
            row.Add(amount);
            parent.Add(row);
        }
    }

    private void PopulateRecentTransactions(
        IReadOnlyList<AirportFinancialTransaction> transactions)
    {
        recentTransactions.Clear();

        List<AirportFinancialTransaction> sorted =
            new List<AirportFinancialTransaction>();

        for (int i = 0; i < transactions.Count; i++)
        {
            if (transactions[i] != null)
                sorted.Add(transactions[i]);
        }

        sorted.Sort((a, b) =>
            b.absoluteGameSecond.CompareTo(a.absoluteGameSecond));

        int count = Mathf.Min(25, sorted.Count);
        if (count == 0)
        {
            Label empty = new Label("No financial transactions recorded yet.");
            empty.style.fontSize = 11;
            empty.style.color = MutedTextColor;
            recentTransactions.Add(empty);
            return;
        }

        for (int i = 0; i < count; i++)
            recentTransactions.Add(CreateTransactionRow(sorted[i]));
    }

    private VisualElement CreateTransactionRow(AirportFinancialTransaction transaction)
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.paddingTop = 6;
        row.style.paddingBottom = 6;
        row.style.borderBottomWidth = 1;
        row.style.borderBottomColor = (Color)new Color32(65, 75, 86, 180);

        VisualElement details = new VisualElement();
        details.style.flexDirection = FlexDirection.Column;
        details.style.flexGrow = 1;
        details.style.minWidth = 0;

        Label description = new Label(
            string.IsNullOrWhiteSpace(transaction.description)
                ? GetCategoryName(transaction.category)
                : transaction.description);
        description.style.fontSize = 12;
        description.style.color = Color.white;
        description.style.whiteSpace = WhiteSpace.Normal;

        Label metadata = new Label(
            GetCategoryName(transaction.category) + "  ·  " +
            FormatGameDate(transaction.absoluteGameSecond));
        metadata.style.fontSize = 10;
        metadata.style.color = MutedTextColor;
        metadata.style.marginTop = 2;

        Label amount = new Label(
            (transaction.isIncome ? "+" : "-") + FormatMoney(transaction.amount));
        amount.style.fontSize = 13;
        amount.style.unityFontStyleAndWeight = FontStyle.Bold;
        amount.style.color = transaction.isIncome ? IncomeColor : ExpenseColor;
        amount.style.marginLeft = 12;

        details.Add(description);
        details.Add(metadata);
        row.Add(details);
        row.Add(amount);
        return row;
    }

    private VisualElement CreateCard()
    {
        VisualElement card = new VisualElement();
        card.style.flexDirection = FlexDirection.Column;
        card.style.paddingLeft = 11;
        card.style.paddingRight = 11;
        card.style.paddingTop = 9;
        card.style.paddingBottom = 9;
        card.style.marginBottom = 4;
        card.style.backgroundColor = CardColor;
        card.style.borderTopLeftRadius = 6;
        card.style.borderTopRightRadius = 6;
        card.style.borderBottomLeftRadius = 6;
        card.style.borderBottomRightRadius = 6;
        return card;
    }

    private Label CreateSmallHeading(string text)
    {
        Label label = new Label(text);
        label.style.fontSize = 10;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.color = MutedTextColor;
        return label;
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

    private void OpenWindow()
    {
        if (window == null)
            return;

        windowOpen = true;
        window.style.display = DisplayStyle.Flex;
        nextRefreshTime = 0f;
        RefreshWindow();
    }

    private void CloseWindow()
    {
        windowOpen = false;
        if (window != null)
            window.style.display = DisplayStyle.None;
    }

    private long GetCurrentAbsoluteGameSecond()
    {
        if (gameTimeManager == null)
            return 0L;

        return (gameTimeManager.CurrentDay - 1L) * 86400L +
               gameTimeManager.GetGameTimeSeconds();
    }

    private string FormatGameDate(long absoluteGameSecond)
    {
        long safeSeconds = Math.Max(0L, absoluteGameSecond);
        long day = safeSeconds / 86400L + 1L;
        long secondsInDay = safeSeconds % 86400L;
        int hour = (int)(secondsInDay / 3600L);
        int minute = (int)((secondsInDay % 3600L) / 60L);
        return $"Day {day}  {hour:00}:{minute:00}";
    }

    private string GetCategoryName(AirportFinancialCategory category)
    {
        switch (category)
        {
            case AirportFinancialCategory.Construction: return "Construction";
            case AirportFinancialCategory.FuelDelivery: return "Fuel deliveries";
            case AirportFinancialCategory.FuelRefund: return "Fuel refunds";
            case AirportFinancialCategory.FuelSales: return "Aircraft fuel sales";
            case AirportFinancialCategory.WasteCollection: return "Waste collection";
            case AirportFinancialCategory.VehiclePurchase: return "Vehicle purchases";
            case AirportFinancialCategory.LandingFees: return "Landing fees";
            case AirportFinancialCategory.ParkingFees: return "Parking fees";
            case AirportFinancialCategory.AircraftCleaning: return "Aircraft cleaning";
            case AirportFinancialCategory.PassengerRevenue: return "Passenger revenue";
            case AirportFinancialCategory.SightseeingFlights: return "Sightseeing flights";
            case AirportFinancialCategory.FlightSchool: return "Flight school";
            default: return "Other";
        }
    }

    private string FormatMoney(long amount)
    {
        return "$" + amount.ToString("N0");
    }

    private string FormatSignedMoney(long amount)
    {
        if (amount > 0)
            return "+" + FormatMoney(amount);
        if (amount < 0)
            return "-" + FormatMoney(Math.Abs(amount));
        return FormatMoney(0);
    }
}
