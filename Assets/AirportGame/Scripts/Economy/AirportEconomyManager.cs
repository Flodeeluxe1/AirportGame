
using System;
using UnityEngine;

/// <summary>
/// Holds the airport's current money balance and provides
/// the central API for spending money and earning revenue.
///
/// This component does not save or load files itself.
/// AirportWorldResetController handles that through AirportSaveData.
/// </summary>
public class AirportEconomyManager : MonoBehaviour
{
    [Header("New Game")]
    [SerializeField, Min(0)]
    private int startingBalance = 50000;

    [Header("Current Economy")]
    [SerializeField, Min(0)]
    private int balance;

    /// <summary>
    /// Raised whenever the balance changes.
    /// A future economy UI can subscribe to this event.
    /// </summary>
    public event Action<int> OnBalanceChanged;

    public int Balance => balance;

    public int StartingBalance => startingBalance;

    /// <summary>
    /// Called only when a genuinely new airport is started.
    /// Never call this when loading a save.
    /// </summary>
    public void InitializeNewGame()
    {
        SetBalance(startingBalance);

        Debug.Log(
            $"AirportEconomyManager: New airport funded with " +
            $"{FormatMoney(balance)}."
        );
    }

    /// <summary>
    /// Attempts to spend money.
    /// Returns false if the amount is invalid or funds are insufficient.
    /// </summary>
    public bool TrySpend(int amount, string reason)
    {
        if (amount <= 0)
        {
            Debug.LogWarning(
                "AirportEconomyManager: Spending amount must be positive."
            );

            return false;
        }

        if (balance < amount)
        {
            Debug.Log(
                $"AirportEconomyManager: Cannot afford " +
                $"{FormatMoney(amount)} for '{reason}'. " +
                $"Current balance: {FormatMoney(balance)}."
            );

            return false;
        }

        SetBalance(balance - amount);

        /*
        Debug.Log(
            $"AirportEconomyManager: Spent {FormatMoney(amount)} " +
            $"for '{reason}'. Remaining balance: {FormatMoney(balance)}."
        );
        */

        return true;
    }

    /// <summary>
    /// Adds revenue to the airport's balance.
    /// Returns false if the amount is invalid or the balance would overflow.
    /// </summary>
    public bool AddRevenue(int amount, string reason)
    {
        if (amount <= 0)
        {
            Debug.LogWarning(
                "AirportEconomyManager: Revenue amount must be positive."
            );

            return false;
        }

        long newBalance = (long)balance + amount;

        if (newBalance > int.MaxValue)
        {
            Debug.LogWarning(
                "AirportEconomyManager: Revenue would exceed the " +
                "maximum supported balance."
            );

            return false;
        }

        SetBalance((int)newBalance);

        Debug.Log(
            $"AirportEconomyManager: Earned {FormatMoney(amount)} " +
            $"from '{reason}'. New balance: {FormatMoney(balance)}."
        );

        return true;
    }

    /// <summary>
    /// Restores the balance belonging to a loaded airport save.
    /// This deliberately does not apply the new-game starting balance.
    /// </summary>
    public void RestoreBalanceFromSave(int savedBalance)
    {
        if (savedBalance < 0)
        {
            Debug.LogWarning(
                "AirportEconomyManager: Saved balance was negative. " +
                "Restoring with a balance of $0."
            );

            savedBalance = 0;
        }

        SetBalance(savedBalance);

        Debug.Log(
            $"AirportEconomyManager: Restored airport balance: " +
            $"{FormatMoney(balance)}."
        );
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
