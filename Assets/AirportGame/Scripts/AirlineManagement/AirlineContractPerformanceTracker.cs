using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Records daily contract obligations when scheduled aircraft depart,
/// settles flight payments and penalties, and maintains airline relationship scores.
/// </summary>
[DisallowMultipleComponent]
public class AirlineContractPerformanceTracker : MonoBehaviour
{
    private const long SecondsPerDay = 86400L;

    [Header("References")]
    [SerializeField] private AirlineContractOfferManager contractManager;
    [SerializeField] private AirportEconomyManager economyManager;
    [SerializeField] private GameTimeManager gameTimeManager;

    [Header("Relationship Tuning")]
    [SerializeField] private float startingRelationship = 50f;
    [SerializeField] private float onTimeRelationshipChange = 2f;
    [SerializeField] private float lateRelationshipChange = -0.5f;
    [SerializeField] private float missedRelationshipChange = -5f;
    [SerializeField] private float cancelledRelationshipChange = -6f;
    [SerializeField] private float operationalFailureRelationshipChange = -3f;

    [Header("Runtime Relationships")]
    [SerializeField] private List<AirlineRelationshipRecord> relationships =
        new List<AirlineRelationshipRecord>();

    public event Action<ActiveAirlineContract> PerformanceUpdated;
    public event Action<AirlineDefinition, float> RelationshipUpdated;

    private void Awake()
    {
        ResolveReferences();
        if (relationships == null)
            relationships = new List<AirlineRelationshipRecord>();
    }

    private void OnEnable()
    {
        ResolveReferences();
        AircraftCargoPaxTakeoffController.ContractFlightDepartureCompleted += HandleFlightDepartureCompleted;
        if (gameTimeManager != null)
            gameTimeManager.GameTimeAdvanced += HandleGameTimeAdvanced;
        if (contractManager != null)
            contractManager.ContractCompleted += HandleContractCompleted;
    }

    private void Start()
    {
        ResolveReferences();
        if (gameTimeManager != null)
        {
            gameTimeManager.GameTimeAdvanced -= HandleGameTimeAdvanced;
            gameTimeManager.GameTimeAdvanced += HandleGameTimeAdvanced;
        }
        SubscribeToManager();
        FinalizeElapsedContractDays();
    }

    private void OnDisable()
    {
        AircraftCargoPaxTakeoffController.ContractFlightDepartureCompleted -= HandleFlightDepartureCompleted;
        if (gameTimeManager != null)
            gameTimeManager.GameTimeAdvanced -= HandleGameTimeAdvanced;
        if (contractManager != null)
            contractManager.ContractCompleted -= HandleContractCompleted;
    }

    private void ResolveReferences()
    {
        if (contractManager == null)
            contractManager = FindAnyObjectByType<AirlineContractOfferManager>();
        if (economyManager == null)
            economyManager = FindAnyObjectByType<AirportEconomyManager>();
        if (gameTimeManager == null)
            gameTimeManager = FindAnyObjectByType<GameTimeManager>();
    }

    private void SubscribeToManager()
    {
        if (contractManager == null)
            return;
        contractManager.ContractCompleted -= HandleContractCompleted;
        contractManager.ContractCompleted += HandleContractCompleted;
    }

    private void HandleGameTimeAdvanced(long previousAbsoluteSecond, long currentAbsoluteSecond)
    {
        FinalizeElapsedContractDays();
    }

    private void FinalizeElapsedContractDays()
    {
        if (contractManager == null || gameTimeManager == null)
            return;

        long targetDay = gameTimeManager.CurrentDay - 1;
        foreach (ActiveAirlineContract contract in contractManager.ActiveContracts)
        {
            if (contract == null || contract.status != AirlineContractStatus.Active)
                continue;
            FinalizeThroughDay(contract, targetDay);
        }
    }

    public void FinalizeContractForCompletion(ActiveAirlineContract contract)
    {
        if (contract == null)
            return;

        FinalizeThroughDay(contract, contract.endDay);
        AwardCompletionBonusIfEligible(contract);
        PerformanceUpdated?.Invoke(contract);
    }

    private void HandleContractCompleted(ActiveAirlineContract contract)
    {
        FinalizeContractForCompletion(contract);
    }

    private void FinalizeThroughDay(ActiveAirlineContract contract, long targetDay)
    {
        if (contract == null || contract.flightObligations == null)
            return;

        long firstDay = Math.Max(contract.startDay, contract.lastFinalizedDay + 1);
        for (long day = firstDay; day <= targetDay && day <= contract.endDay; day++)
        {
            foreach (AirlineContractFlightObligation obligation in contract.flightObligations)
            {
                if (obligation == null)
                    continue;
                if (FindRecord(contract, obligation.obligationId, day) != null)
                    continue;

                RecordOutcome(contract, obligation, day,
                    AirlineContractFlightResult.Missed, 0L);
            }

            contract.lastFinalizedDay = day;
        }
    }

    private void HandleFlightDepartureCompleted(AircraftFlightIdentity identity)
    {
        if (identity == null || identity.Definition == null ||
            string.IsNullOrWhiteSpace(identity.Definition.ContractId) ||
            string.IsNullOrWhiteSpace(identity.Definition.ObligationId))
            return;

        ResolveReferences();
        if (contractManager == null || gameTimeManager == null)
            return;

        ActiveAirlineContract contract = FindContract(identity.Definition.ContractId);
        if (contract == null || contract.status != AirlineContractStatus.Active)
            return;

        long scheduledDay = identity.ScheduledDayIndex + 1;
        if (scheduledDay < contract.startDay || scheduledDay > contract.endDay ||
            scheduledDay <= contract.lastFinalizedDay)
            return;

        AirlineContractFlightObligation obligation =
            contract.flightObligations.Find(item =>
                item != null && item.obligationId == identity.Definition.ObligationId);
        if (obligation == null || FindRecord(contract, obligation.obligationId, scheduledDay) != null)
            return;

        long actualAbsoluteSecond =
            (gameTimeManager.CurrentDay - 1L) * SecondsPerDay +
            gameTimeManager.GetGameTimeSeconds();
        long expectedDepartureSecond =
            identity.ScheduledDayIndex * SecondsPerDay +
            identity.PlannedStartMinute * 60L +
            Math.Max(0, contract.agreedTerms.turnaroundMinutes) * 60L;

        AirlineContractFlightResult result =
            actualAbsoluteSecond <= expectedDepartureSecond
                ? AirlineContractFlightResult.OnTime
                : AirlineContractFlightResult.Late;

        RecordOutcome(contract, obligation, scheduledDay, result, actualAbsoluteSecond);
    }

    /// <summary>Can be called by cancellation or operational-failure systems.</summary>
    public bool RecordCancelledFlight(string contractId, string obligationId, long scheduledDay)
    {
        return RecordExternalOutcome(contractId, obligationId, scheduledDay,
            AirlineContractFlightResult.Cancelled);
    }

    /// <summary>Can be called when an accepted obligation cannot be operated due to an airport-side failure.</summary>
    public bool RecordOperationalFailure(string contractId, string obligationId, long scheduledDay)
    {
        return RecordExternalOutcome(contractId, obligationId, scheduledDay,
            AirlineContractFlightResult.OperationalFailure);
    }

    private bool RecordExternalOutcome(
        string contractId, string obligationId, long scheduledDay,
        AirlineContractFlightResult result)
    {
        ActiveAirlineContract contract = FindContract(contractId);
        if (contract == null || contract.status != AirlineContractStatus.Active ||
            scheduledDay <= contract.lastFinalizedDay)
            return false;

        AirlineContractFlightObligation obligation =
            contract.flightObligations.Find(item =>
                item != null && item.obligationId == obligationId);
        if (obligation == null || FindRecord(contract, obligationId, scheduledDay) != null)
            return false;

        RecordOutcome(contract, obligation, scheduledDay, result, 0L);
        return true;
    }

    private void RecordOutcome(
        ActiveAirlineContract contract,
        AirlineContractFlightObligation obligation,
        long scheduledDay,
        AirlineContractFlightResult result,
        long completedAbsoluteGameSecond)
    {
        if (contract.performanceRecords == null)
            contract.performanceRecords = new List<AirlineContractFlightPerformanceRecord>();

        AirlineContractFlightPerformanceRecord record =
            new AirlineContractFlightPerformanceRecord
            {
                obligationId = obligation.obligationId,
                flightNumber = obligation.flightNumber,
                scheduledDay = scheduledDay,
                result = result,
                completedAbsoluteGameSecond = completedAbsoluteGameSecond
            };

        bool completed = result == AirlineContractFlightResult.OnTime ||
                         result == AirlineContractFlightResult.Late;

        if (completed)
        {
            int payment = Mathf.Max(0, contract.agreedTerms.paymentPerCompletedFlight);
            if (payment > 0 && economyManager != null &&
                economyManager.AddRevenue(payment,
                    "Airline contract flight " + obligation.flightNumber,
                    AirportFinancialCategory.AirlineContracts))
            {
                record.paymentReceived = payment;
                contract.totalPaymentsReceived += payment;
            }

            if (result == AirlineContractFlightResult.OnTime)
            {
                contract.onTimeFlights++;
                AdjustRelationship(contract.airline, onTimeRelationshipChange);
            }
            else
            {
                contract.lateFlights++;
                AdjustRelationship(contract.airline, lateRelationshipChange);
            }
        }
        else
        {
            int penalty = Mathf.Max(0, contract.agreedTerms.missedFlightPenalty);
            record.penaltyCharged = penalty;

            if (penalty > 0 && economyManager != null &&
                economyManager.TrySpend(penalty,
                    "Airline contract " + result + " flight " + obligation.flightNumber,
                    AirportFinancialCategory.AirlineContracts))
            {
                record.penaltyPaid = penalty;
                contract.totalPenaltiesPaid += penalty;
            }
            else if (penalty > 0)
            {
                contract.unpaidPenaltyAmount += penalty;
            }

            switch (result)
            {
                case AirlineContractFlightResult.Cancelled:
                    contract.cancelledFlights++;
                    AdjustRelationship(contract.airline, cancelledRelationshipChange);
                    break;
                case AirlineContractFlightResult.OperationalFailure:
                    contract.operationalFailures++;
                    AdjustRelationship(contract.airline, operationalFailureRelationshipChange);
                    break;
                default:
                    contract.missedFlights++;
                    AdjustRelationship(contract.airline, missedRelationshipChange);
                    break;
            }
        }

        contract.performanceRecords.Add(record);
        PerformanceUpdated?.Invoke(contract);
    }

    private void AwardCompletionBonusIfEligible(ActiveAirlineContract contract)
    {
        if (contract == null || contract.completionBonusPaid ||
            contract.agreedTerms == null || contract.agreedTerms.completionBonus <= 0)
            return;

        if (contract.missedFlights > 0 || contract.cancelledFlights > 0 ||
            contract.operationalFailures > 0)
            return;

        if (economyManager != null &&
            economyManager.AddRevenue(contract.agreedTerms.completionBonus,
                "Airline contract completion bonus",
                AirportFinancialCategory.AirlineContracts))
        {
            contract.totalPaymentsReceived += contract.agreedTerms.completionBonus;
            contract.completionBonusPaid = true;
        }
    }

    private AirlineContractFlightPerformanceRecord FindRecord(
        ActiveAirlineContract contract, string obligationId, long day)
    {
        if (contract.performanceRecords == null)
            return null;
        return contract.performanceRecords.Find(record =>
            record != null && record.obligationId == obligationId &&
            record.scheduledDay == day);
    }

    private ActiveAirlineContract FindContract(string contractId)
    {
        if (contractManager == null || string.IsNullOrWhiteSpace(contractId))
            return null;
        foreach (ActiveAirlineContract contract in contractManager.ActiveContracts)
            if (contract != null && contract.contractId == contractId)
                return contract;
        return null;
    }

    private void AdjustRelationship(AirlineDefinition airline, float change)
    {
        if (airline == null || string.IsNullOrWhiteSpace(airline.AirlineId))
            return;

        AirlineRelationshipRecord record = relationships.Find(item =>
            item != null && item.airlineId == airline.AirlineId);
        if (record == null)
        {
            record = new AirlineRelationshipRecord
            {
                airlineId = airline.AirlineId,
                airlineName = airline.DisplayName,
                relationship = startingRelationship
            };
            relationships.Add(record);
        }

        record.airlineName = airline.DisplayName;
        record.relationship = Mathf.Clamp(record.relationship + change, 0f, 100f);
        RelationshipUpdated?.Invoke(airline, record.relationship);
    }

    public float GetRelationshipScore(AirlineDefinition airline)
    {
        if (airline == null || string.IsNullOrWhiteSpace(airline.AirlineId))
            return startingRelationship;

        AirlineRelationshipRecord record = relationships.Find(item =>
            item != null && item.airlineId == airline.AirlineId);
        return record != null ? record.relationship : startingRelationship;
    }


    public void ResetForNewGame()
    {
        if (relationships == null)
            relationships = new List<AirlineRelationshipRecord>();
        else
            relationships.Clear();
    }

    public List<AirlineRelationshipRecord> GetRelationshipsForSave()
    {
        List<AirlineRelationshipRecord> saved =
            new List<AirlineRelationshipRecord>();

        if (relationships == null)
            return saved;

        foreach (AirlineRelationshipRecord record in relationships)
        {
            if (record == null)
                continue;

            saved.Add(new AirlineRelationshipRecord
            {
                airlineId = record.airlineId,
                airlineName = record.airlineName,
                relationship = record.relationship
            });
        }

        return saved;
    }

    public void RestoreRelationshipsFromSave(
        List<AirlineRelationshipRecord> savedRelationships)
    {
        relationships = new List<AirlineRelationshipRecord>();

        if (savedRelationships == null)
            return;

        foreach (AirlineRelationshipRecord record in savedRelationships)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.airlineId))
                continue;

            relationships.Add(new AirlineRelationshipRecord
            {
                airlineId = record.airlineId,
                airlineName = record.airlineName,
                relationship = Mathf.Clamp(record.relationship, 0f, 100f)
            });
        }
    }

    private void LogWarning(string message)
    {
        Debug.LogWarning("AirlineContractPerformanceTracker: " + message, this);
    }
}
