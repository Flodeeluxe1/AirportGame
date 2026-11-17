using System;
using System.Collections.Generic;
using UnityEngine;

public enum AirportFinancialCategory
{
    Other,
    Construction,
    FuelDelivery,
    FuelRefund,
    WasteCollection,
    VehiclePurchase,
    LandingFees,
    ParkingFees,
    AircraftCleaning,
    PassengerRevenue,
    FlightSchool
}

[Serializable]
public class AirportFinancialTransaction
{
    public int amount;
    public bool isIncome;
    public AirportFinancialCategory category;
    public string description;
    public long absoluteGameSecond;
}

/// <summary>
/// Holds the airport's current money balance and records every successful
/// income and expense for the financial reports.
/// </summary>
public class AirportEconomyManager : MonoBehaviour
{
    private const long SecondsPerDay = 86400L;

    [Header("New Game")]
    [SerializeField, Min(0)]
    private int startingBalance = 50000;

    [Header("Current Economy")]
    [SerializeField, Min(0)]
    private int balance;

    [Header("Financial History")]
    [SerializeField]
    private List<AirportFinancialTransaction> transactions =
        new List<AirportFinancialTransaction>();

    private GameTimeManager gameTimeManager;

    public event Action<int> OnBalanceChanged;

    public int Balance => balance;
    public int StartingBalance => startingBalance;
    public IReadOnlyList<AirportFinancialTransaction> Transactions => transactions;

    private void Awake()
    {
        gameTimeManager = FindFirstObjectByType<GameTimeManager>();
        if (transactions == null)
            transactions = new List<AirportFinancialTransaction>();
    }

    /// <summary>
    /// Called only when a genuinely new airport is started.
    /// The starting grant establishes the balance but is not operating income.
    /// </summary>
    public void InitializeNewGame()
    {
        transactions.Clear();
        SetBalance(startingBalance);

        Debug.Log(
            $"AirportEconomyManager: New airport funded with {FormatMoney(balance)}.");
    }

    public bool TrySpend(int amount, string reason)
    {
        return TrySpend(amount, reason, AirportFinancialCategory.Other);
    }

    public bool TrySpend(
        int amount,
        string reason,
        AirportFinancialCategory category)
    {
        if (amount <= 0)
        {
            Debug.LogWarning("AirportEconomyManager: Spending amount must be positive.");
            return false;
        }

        if (balance < amount)
        {
            Debug.Log(
                $"AirportEconomyManager: Cannot afford {FormatMoney(amount)} for '{reason}'. " +
                $"Current balance: {FormatMoney(balance)}.");
            return false;
        }

        SetBalance(balance - amount);
        RecordTransaction(amount, false, category, reason);
        return true;
    }

    public bool AddRevenue(int amount, string reason)
    {
        return AddRevenue(amount, reason, AirportFinancialCategory.Other);
    }

    public bool AddRevenue(
        int amount,
        string reason,
        AirportFinancialCategory category)
    {
        if (amount <= 0)
        {
            Debug.LogWarning("AirportEconomyManager: Revenue amount must be positive.");
            return false;
        }

        long newBalance = (long)balance + amount;
        if (newBalance > int.MaxValue)
        {
            Debug.LogWarning(
                "AirportEconomyManager: Revenue would exceed the maximum supported balance.");
            return false;
        }

        SetBalance((int)newBalance);
        RecordTransaction(amount, true, category, reason);

        Debug.Log(
            $"AirportEconomyManager: Earned {FormatMoney(amount)} from '{reason}'. " +
            $"New balance: {FormatMoney(balance)}.");
        return true;
    }

    public List<AirportFinancialTransaction> GetTransactionsForSave()
    {
        return new List<AirportFinancialTransaction>(transactions);
    }

    public void RestoreTransactionsFromSave(
        List<AirportFinancialTransaction> savedTransactions)
    {
        transactions.Clear();

        if (savedTransactions == null)
            return;

        foreach (AirportFinancialTransaction transaction in savedTransactions)
        {
            if (transaction == null || transaction.amount <= 0)
                continue;

            transactions.Add(new AirportFinancialTransaction
            {
                amount = transaction.amount,
                isIncome = transaction.isIncome,
                category = transaction.category,
                description = transaction.description ?? string.Empty,
                absoluteGameSecond = Math.Max(0L, transaction.absoluteGameSecond)
            });
        }
    }

    /// <summary>
    /// Restores the balance belonging to a loaded airport save.
    /// This does not create a transaction or apply the new-game starting balance.
    /// </summary>
    public void RestoreBalanceFromSave(int savedBalance)
    {
        if (savedBalance < 0)
        {
            Debug.LogWarning(
                "AirportEconomyManager: Saved balance was negative. Restoring with a balance of $0.");
            savedBalance = 0;
        }

        SetBalance(savedBalance);
        Debug.Log(
            $"AirportEconomyManager: Restored airport balance: {FormatMoney(balance)}.");
    }

    private void RecordTransaction(
        int amount,
        bool isIncome,
        AirportFinancialCategory category,
        string reason)
    {
        if (transactions == null)
            transactions = new List<AirportFinancialTransaction>();

        if (gameTimeManager == null)
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();

        long absoluteGameSecond = 0L;
        if (gameTimeManager != null)
        {
            absoluteGameSecond =
                (gameTimeManager.CurrentDay - 1L) * SecondsPerDay +
                gameTimeManager.GetGameTimeSeconds();
        }

        transactions.Add(new AirportFinancialTransaction
        {
            amount = amount,
            isIncome = isIncome,
            category = category,
            description = string.IsNullOrWhiteSpace(reason) ? "Unspecified transaction" : reason,
            absoluteGameSecond = absoluteGameSecond
        });
    }

    private void SetBalance(int newBalance)
    {
        newBalance = Mathf.Max(0, newBalance);
        if (balance == newBalance)
            return;

        balance = newBalance;
        OnBalanceChanged?.Invoke(balance);
    }

    private string FormatMoney(int amount)
    {
        return "$" + amount.ToString("N0");
    }
}
