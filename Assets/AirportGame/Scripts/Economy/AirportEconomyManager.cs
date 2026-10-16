using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class AirportFinancialTransaction
{
    public string description = "";
    public float amount;
    public long gameDay = 1;
    public int gameTimeSeconds;
    public string recordedAtUtc = "";
}

public class AirportEconomyManager : MonoBehaviour
{
    private const int MaximumTransactionHistory = 500;

    [Header("New Game")]
    [Min(0f)]
    [SerializeField] private float startingBalance = 50000f;

    [Header("Runtime")]
    [SerializeField] private float currentBalance = 50000f;
    [SerializeField] private List<AirportFinancialTransaction> transactions =
        new List<AirportFinancialTransaction>();

    [Header("References")]
    [SerializeField] private GameTimeManager gameTimeManager;

    public float StartingBalance => Mathf.Max(0f, startingBalance);
    public float Balance => currentBalance;
    public IReadOnlyList<AirportFinancialTransaction> Transactions => transactions;

    private void Awake()
    {
        if (gameTimeManager == null)
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();

        if (transactions == null)
            transactions = new List<AirportFinancialTransaction>();
    }

    /// <summary>
    /// Initializes finances for a genuinely new airport.
    /// This must not be called when loading an existing save.
    /// </summary>
    public void ResetForNewGame()
    {
        currentBalance = StartingBalance;

        if (transactions == null)
            transactions = new List<AirportFinancialTransaction>();
        else
            transactions.Clear();

        Debug.Log(
            $"AirportEconomyManager: New airport finances initialized. " +
            $"Starting balance: {FormatMoney(currentBalance)}.",
            this
        );
    }

    /// <summary>
    /// Records an income or expense. Negative amounts are expenses.
    /// Expenses are rejected if the airport cannot afford them.
    /// </summary>
    public bool TryApplyTransaction(
        string description,
        float amount,
        out string error)
    {
        error = string.Empty;

        if (float.IsNaN(amount) || float.IsInfinity(amount))
        {
            error = "Transaction amount must be a finite number.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            error = "Transaction description cannot be empty.";
            return false;
        }

        if (amount < 0f && currentBalance + amount < 0f)
        {
            error =
                $"Insufficient funds for '{description}'. " +
                $"Available: {FormatMoney(currentBalance)}; " +
                $"required: {FormatMoney(-amount)}.";
            return false;
        }

        currentBalance += amount;

        if (transactions == null)
            transactions = new List<AirportFinancialTransaction>();

        AirportFinancialTransaction transaction =
            new AirportFinancialTransaction
            {
                description = description.Trim(),
                amount = amount,
                gameDay = gameTimeManager != null
                    ? gameTimeManager.CurrentDay
                    : 1,
                gameTimeSeconds = gameTimeManager != null
                    ? gameTimeManager.GetGameTimeSeconds()
                    : 0,
                recordedAtUtc = DateTime.UtcNow.ToString("O")
            };

        transactions.Add(transaction);

        if (transactions.Count > MaximumTransactionHistory)
            transactions.RemoveAt(0);

        Debug.Log(
            $"AirportEconomyManager: {description} " +
            $"{(amount >= 0f ? "+" : "-")}{FormatMoney(Mathf.Abs(amount))}. " +
            $"Balance: {FormatMoney(currentBalance)}.",
            this
        );

        return true;
    }

    /// <summary>
    /// Restores the balance and transaction history belonging to a save slot.
    /// </summary>
    public void RestoreFromSave(
        float savedBalance,
        List<AirportFinancialTransaction> savedTransactions)
    {
        currentBalance = Mathf.Max(0f, savedBalance);

        transactions = savedTransactions != null
            ? new List<AirportFinancialTransaction>(savedTransactions)
            : new List<AirportFinancialTransaction>();

        if (transactions.Count > MaximumTransactionHistory)
        {
            transactions.RemoveRange(
                0,
                transactions.Count - MaximumTransactionHistory
            );
        }

        Debug.Log(
            $"AirportEconomyManager: Restored finances. " +
            $"Balance: {FormatMoney(currentBalance)}; " +
            $"transactions: {transactions.Count}.",
            this
        );
    }

    public List<AirportFinancialTransaction> GetTransactionsForSave()
    {
        return transactions != null
            ? new List<AirportFinancialTransaction>(transactions)
            : new List<AirportFinancialTransaction>();
    }

    private static string FormatMoney(float amount)
    {
        return "$" + amount.ToString("N2");
    }
}
