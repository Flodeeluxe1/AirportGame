using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates, expires, accepts, and declines airline contract offers.
/// Flight obligation creation and timetable integration are handled in Phase 3.
/// </summary>
[DisallowMultipleComponent]
public class AirlineContractOfferManager : MonoBehaviour
{
    private const int EvaluationIntervalDays = 2;
    private const double SecondsPerDay = 86400.0;
    private const int MinimumOffersPerEvaluation = 1;
    private const int MaximumOffersPerEvaluation = 3;
    private const int OfferLifetimeDays = 3;

    [Header("Systems")]
    [SerializeField] private AirlineAirportSnapshotCollector snapshotCollector;
    [SerializeField] private GameTimeManager gameTimeManager;
    [SerializeField] private AirlineContractPerformanceTracker performanceTracker;

    [Header("Automatic Renewal")]
    [SerializeField, Range(0f, 100f)] private float minimumRenewalRelationship = 50f;
    [SerializeField, Range(0f, 1f)] private float minimumRenewalCompletionRate = 0.75f;
    [SerializeField, Range(0f, 0.1f)] private float paymentIncreasePerRenewal = 0.02f;
    [SerializeField, Range(0f, 0.5f)] private float maximumRenewalPaymentIncrease = 0.10f;

    [Header("Airlines")]
    [SerializeField] private List<AirlineDefinition> airlines =
        new List<AirlineDefinition>();

    [Header("Runtime State")]
    [SerializeField] private List<AirlineContractOffer> pendingOffers =
        new List<AirlineContractOffer>();
    [SerializeField] private List<ActiveAirlineContract> activeContracts =
        new List<ActiveAirlineContract>();

    private long lastEvaluatedPeriod = -1;
    private bool initializationComplete;
    private long dailyProcessingEventId;

    public event Action<AirlineContractOffer> OfferGenerated;
    public event Action<AirlineContractOffer> OfferExpired;
    public event Action<ActiveAirlineContract> ContractAccepted;
    public event Action<ActiveAirlineContract> ContractCompleted;
    public event Action<ActiveAirlineContract, ActiveAirlineContract> ContractRenewed;
    public event Action<ActiveAirlineContract> ContractRenewalPreferenceChanged;
    public event Action<string> OfferDeclined;
    public event Action ContractsRestored;

    public IReadOnlyList<AirlineContractOffer> PendingOffers => pendingOffers;
    public IReadOnlyList<ActiveAirlineContract> ActiveContracts => activeContracts;

    private void OnEnable()
    {
        ResolveReferences();

        SubscribeToGameTime();

        if (initializationComplete)
            ScheduleNextDailyProcessing();

        // Let the airport snapshot collector finish its initial scene refresh first.
        StartCoroutine(InitializeAfterAirportSystems());
    }

    private IEnumerator InitializeAfterAirportSystems()
    {
        yield return null;
        yield return null;
        ResolveReferences();
        initializationComplete = true;
        SubscribeToGameTime();
        EvaluateForCurrentDay();
        ScheduleNextDailyProcessing();
    }

    private void OnDisable()
    {
        if (gameTimeManager != null)
            gameTimeManager.GameTimeAdvanced -= HandleGameTimeAdvanced;
        CancelDailyProcessingEvent();
    }

    private void SubscribeToGameTime()
    {
        if (gameTimeManager == null)
            ResolveReferences();

        if (gameTimeManager == null)
            return;

        gameTimeManager.GameTimeAdvanced -= HandleGameTimeAdvanced;
        gameTimeManager.GameTimeAdvanced += HandleGameTimeAdvanced;
    }

    private void ResolveReferences()
    {
        if (snapshotCollector == null)
            snapshotCollector = FindAnyObjectByType<AirlineAirportSnapshotCollector>();

        if (gameTimeManager == null)
            gameTimeManager = FindAnyObjectByType<GameTimeManager>();

        if (performanceTracker == null)
            performanceTracker = FindAnyObjectByType<AirlineContractPerformanceTracker>();
    }

    // GameTimeAdvanced is retained only to detect an explicit clock rewind.
    // Normal deadline processing is driven by the next midnight event.
    private void HandleGameTimeAdvanced(long previousAbsoluteSecond, long currentAbsoluteSecond)
    {
        if (initializationComplete && currentAbsoluteSecond < previousAbsoluteSecond)
            ScheduleNextDailyProcessing();
    }

    private void ScheduleNextDailyProcessing()
    {
        if (!initializationComplete || !isActiveAndEnabled ||
            gameTimeManager == null || gameTimeManager.EventScheduler == null)
            return;

        CancelDailyProcessingEvent();

        double now = GameTimeManager.SimulationTime;
        double nextDayBoundary =
            (System.Math.Floor(now / SecondsPerDay) + 1.0) * SecondsPerDay;

        dailyProcessingEventId = gameTimeManager.EventScheduler.ScheduleAt(
            nextDayBoundary,
            HandleDailyProcessing);
    }

    private void HandleDailyProcessing()
    {
        dailyProcessingEventId = 0;

        if (!initializationComplete || !isActiveAndEnabled)
            return;

        // These methods already process all elapsed days and guard against
        // duplicate finalization, even when simulation time jumps several days.
        CompleteExpiredContracts();
        ExpireOldOffers();
        EvaluateForCurrentDay();

        ScheduleNextDailyProcessing();
    }

    private void CancelDailyProcessingEvent()
    {
        if (dailyProcessingEventId != 0 &&
            gameTimeManager != null &&
            gameTimeManager.EventScheduler != null)
        {
            gameTimeManager.EventScheduler.Cancel(dailyProcessingEventId);
        }

        dailyProcessingEventId = 0;
    }

    private void EvaluateForCurrentDay()
    {
        if (gameTimeManager == null)
            ResolveReferences();

        if (gameTimeManager == null)
            return;

        long currentDay = gameTimeManager.CurrentDay;
        long evaluationPeriod = (currentDay - 1) / EvaluationIntervalDays;

        if (evaluationPeriod == lastEvaluatedPeriod)
            return;

        lastEvaluatedPeriod = evaluationPeriod;
        GenerateOffers(currentDay);
    }

    /// <summary>
    /// Rebuilds airport interest and checks for offers after a save has
    /// finished restoring its buildings, gates, runways, and equipment.
    /// This deliberately bypasses the normal two-day evaluation interval:
    /// the previous evaluation may have run against the empty startup airport.
    /// </summary>
    public void RefreshAfterAirportLoaded()
    {
        ResolveReferences();

        if (snapshotCollector == null || gameTimeManager == null)
            return;

        snapshotCollector.RefreshAllAirlines();
        lastEvaluatedPeriod =
            (gameTimeManager.CurrentDay - 1) / EvaluationIntervalDays;
        GenerateOffers(gameTimeManager.CurrentDay);
    }

    [ContextMenu("Generate Contract Offers Now")]
    public void GenerateOffersNow()
    {
        ResolveReferences();
        if (gameTimeManager == null)
            return;

        GenerateOffers(gameTimeManager.CurrentDay);
    }


    public AirlineManagementSaveData GetSaveData()
    {
        AirlineManagementSaveData data = new AirlineManagementSaveData
        {
            lastEvaluatedPeriod = lastEvaluatedPeriod
        };

        foreach (AirlineContractOffer offer in pendingOffers)
        {
            if (offer == null || offer.airline == null || offer.template == null)
                continue;

            data.pendingOffers.Add(new AirlineContractOfferSaveData
            {
                offerId = offer.offerId,
                airlineId = offer.airline.AirlineId,
                templateId = offer.template.TemplateId,
                airlineInterest = offer.airlineInterest,
                offeredOnDay = offer.offeredOnDay,
                expiresOnDay = offer.expiresOnDay,
                terms = offer.terms
            });
        }

        foreach (ActiveAirlineContract contract in activeContracts)
        {
            if (contract == null || contract.airline == null || contract.agreedTerms == null)
                continue;

            ActiveAirlineContractSaveData saved = new ActiveAirlineContractSaveData
            {
                contractId = contract.contractId,
                sourceOfferId = contract.sourceOfferId,
                airlineId = contract.airline.AirlineId,
                templateId = contract.sourceTemplate != null
                    ? contract.sourceTemplate.TemplateId : "",
                startDay = contract.startDay,
                endDay = contract.endDay,
                status = contract.status,
                agreedTerms = contract.agreedTerms,
                lastFinalizedDay = contract.lastFinalizedDay,
                onTimeFlights = contract.onTimeFlights,
                lateFlights = contract.lateFlights,
                missedFlights = contract.missedFlights,
                cancelledFlights = contract.cancelledFlights,
                operationalFailures = contract.operationalFailures,
                totalPaymentsReceived = contract.totalPaymentsReceived,
                totalPenaltiesPaid = contract.totalPenaltiesPaid,
                unpaidPenaltyAmount = contract.unpaidPenaltyAmount,
                completionBonusPaid = contract.completionBonusPaid,
                renewalCount = contract.renewalCount,
                renewalDeclined = contract.renewalDeclined,
                renewedByContractId = contract.renewedByContractId,
                renewalBasePaymentPerFlight = contract.renewalBasePaymentPerFlight,
                renewalBaseCompletionBonus = contract.renewalBaseCompletionBonus
            };

            if (contract.flightObligations != null)
                saved.flightObligations.AddRange(contract.flightObligations);
            if (contract.performanceRecords != null)
                saved.performanceRecords.AddRange(contract.performanceRecords);

            data.activeContracts.Add(saved);
        }

        return data;
    }

    public void ResetForNewGame()
    {
        pendingOffers.Clear();
        activeContracts.Clear();
        lastEvaluatedPeriod = -1;
        ContractsRestored?.Invoke();
    }

    public void RestoreFromSave(AirlineManagementSaveData data)
    {
        if (data == null)
            return;

        data.EnsureCollections();
        ResolveReferences();

        pendingOffers.Clear();
        activeContracts.Clear();
        lastEvaluatedPeriod = data.lastEvaluatedPeriod;

        IReadOnlyList<AirlineDefinition> configuredAirlines =
            airlines.Count > 0 ? airlines :
            snapshotCollector != null ? snapshotCollector.ConfiguredAirlines :
            new List<AirlineDefinition>();

        foreach (AirlineContractOfferSaveData saved in data.pendingOffers)
        {
            if (saved == null || saved.terms == null)
                continue;

            AirlineDefinition airline = FindConfiguredAirline(
                configuredAirlines, saved.airlineId);
            AirlineContractTemplate template = FindTemplate(
                airline, saved.templateId);

            if (airline == null || template == null)
            {
                Debug.LogWarning(
                    "AirlineContractOfferManager: Skipped a saved offer because " +
                    "its airline or contract template could not be resolved.", this);
                continue;
            }

            pendingOffers.Add(new AirlineContractOffer
            {
                offerId = saved.offerId,
                airline = airline,
                template = template,
                airlineInterest = saved.airlineInterest,
                offeredOnDay = saved.offeredOnDay,
                expiresOnDay = saved.expiresOnDay,
                terms = saved.terms
            });
        }

        foreach (ActiveAirlineContractSaveData saved in data.activeContracts)
        {
            if (saved == null || saved.agreedTerms == null)
                continue;

            AirlineDefinition airline = FindConfiguredAirline(
                configuredAirlines, saved.airlineId);
            if (airline == null)
            {
                Debug.LogWarning(
                    "AirlineContractOfferManager: Skipped saved contract '" +
                    saved.contractId + "' because its airline definition could not be resolved.", this);
                continue;
            }

            ActiveAirlineContract contract = new ActiveAirlineContract
            {
                contractId = saved.contractId,
                sourceOfferId = saved.sourceOfferId,
                airline = airline,
                sourceTemplate = FindTemplate(airline, saved.templateId),
                startDay = saved.startDay,
                endDay = saved.endDay,
                status = saved.status,
                agreedTerms = saved.agreedTerms,
                lastFinalizedDay = saved.lastFinalizedDay,
                onTimeFlights = saved.onTimeFlights,
                lateFlights = saved.lateFlights,
                missedFlights = saved.missedFlights,
                cancelledFlights = saved.cancelledFlights,
                operationalFailures = saved.operationalFailures,
                totalPaymentsReceived = saved.totalPaymentsReceived,
                totalPenaltiesPaid = saved.totalPenaltiesPaid,
                unpaidPenaltyAmount = saved.unpaidPenaltyAmount,
                completionBonusPaid = saved.completionBonusPaid,
                renewalCount = saved.renewalCount,
                renewalDeclined = saved.renewalDeclined,
                renewedByContractId = saved.renewedByContractId,
                renewalBasePaymentPerFlight = saved.renewalBasePaymentPerFlight,
                renewalBaseCompletionBonus = saved.renewalBaseCompletionBonus
            };

            contract.flightObligations.AddRange(saved.flightObligations);
            contract.performanceRecords.AddRange(saved.performanceRecords);
            activeContracts.Add(contract);
        }

        ContractsRestored?.Invoke();
    }

    private static AirlineDefinition FindConfiguredAirline(
        IReadOnlyList<AirlineDefinition> configuredAirlines,
        string airlineId)
    {
        if (configuredAirlines == null || string.IsNullOrWhiteSpace(airlineId))
            return null;

        foreach (AirlineDefinition airline in configuredAirlines)
            if (airline != null && airline.AirlineId == airlineId)
                return airline;

        return null;
    }

    private static AirlineContractTemplate FindTemplate(
        AirlineDefinition airline,
        string templateId)
    {
        if (airline == null || airline.ContractTemplates == null ||
            string.IsNullOrWhiteSpace(templateId))
            return null;

        foreach (AirlineContractTemplate template in airline.ContractTemplates)
            if (template != null && template.TemplateId == templateId)
                return template;

        return null;
    }

    private void GenerateOffers(long currentDay)
    {
        if (snapshotCollector == null)
            ResolveReferences();

        if (snapshotCollector == null)
            return;

        List<OfferCandidate> candidates = BuildEligibleCandidates();
        if (candidates.Count == 0)
            return;

        Shuffle(candidates);
        int desiredOfferCount = UnityEngine.Random.Range(
            MinimumOffersPerEvaluation,
            MaximumOffersPerEvaluation + 1);
        int generatedCount = 0;

        foreach (OfferCandidate candidate in candidates)
        {
            if (generatedCount >= desiredOfferCount)
                break;

            // Interest affects both the chance of receiving an offer and its terms.
            float offerChance = Mathf.Lerp(0.15f, 0.9f, candidate.interest / 100f);
            if (UnityEngine.Random.value > offerChance)
                continue;

            AirlineContractOffer offer = new AirlineContractOffer
            {
                offerId = Guid.NewGuid().ToString("N"),
                airline = candidate.airline,
                template = candidate.template,
                airlineInterest = candidate.interest,
                offeredOnDay = currentDay,
                expiresOnDay = currentDay + OfferLifetimeDays,
                terms = AirlineContractTerms.FromTemplate(
                    candidate.template,
                    candidate.interest)
            };

            pendingOffers.Add(offer);
            generatedCount++;
            OfferGenerated?.Invoke(offer);
        }
    }

    private List<OfferCandidate> BuildEligibleCandidates()
    {
        List<OfferCandidate> candidates = new List<OfferCandidate>();

        IReadOnlyList<AirlineDefinition> configuredAirlines =
            airlines.Count > 0 ? airlines : snapshotCollector.ConfiguredAirlines;

        foreach (AirlineDefinition airline in configuredAirlines)
        {
            if (airline == null || HasActiveContract(airline))
                continue;

            AirlineInterestResult interestResult;
            if (!snapshotCollector.TryGetInterest(airline, out interestResult))
                interestResult = snapshotCollector.RefreshInterest(airline);

            if (airline.ContractTemplates == null)
                continue;

            foreach (AirlineContractTemplate template in airline.ContractTemplates)
            {
                if (template == null ||
                    interestResult.totalInterest < template.MinimumAirlineInterest ||
                    HasPendingOffer(airline, template) ||
                    !MeetsTemplateRequirements(airline, template))
                {
                    continue;
                }

                candidates.Add(new OfferCandidate
                {
                    airline = airline,
                    template = template,
                    interest = interestResult.totalInterest
                });
            }
        }

        return candidates;
    }

    private bool MeetsTemplateRequirements(
        AirlineDefinition airline,
        AirlineContractTemplate template)
    {
        AirlineAirportSnapshot snapshot = snapshotCollector.BuildSnapshot(airline);
        if (snapshot == null)
            return false;

        if (snapshot.suitableGateCount <= 0 ||
            (int)airline.PreferredGateSize < (int)template.MinimumGateSize ||
            snapshot.longestRunwayLengthMeters < template.MinimumRunwayLengthMeters)
        {
            return false;
        }

        if (airline.OperationType != AirlineOperationType.Passenger &&
            (snapshot.cargoStorageCapacity < template.MinimumCargoStorageCapacity ||
             snapshot.cargoHandlingThroughputPerHour <
             template.MinimumCargoHandlingThroughputPerHour))
        {
            return false;
        }

        return template.TotalFlightsPerDay > 0;
    }

    public bool AcceptOffer(string offerId)
    {
        if (string.IsNullOrWhiteSpace(offerId) || gameTimeManager == null)
            return false;

        AirlineContractOffer offer = pendingOffers.Find(
            item => item != null && item.offerId == offerId);

        if (offer == null)
            return false;

        if (gameTimeManager.CurrentDay >= offer.expiresOnDay ||
            offer.airline == null ||
            HasActiveContract(offer.airline))
        {
            RemoveOfferAsExpired(offer);
            return false;
        }

        ActiveAirlineContract contract = new ActiveAirlineContract
        {
            contractId = Guid.NewGuid().ToString("N"),
            sourceOfferId = offer.offerId,
            airline = offer.airline,
            sourceTemplate = offer.template,
            startDay = gameTimeManager.CurrentDay,
            lastFinalizedDay = gameTimeManager.CurrentDay - 1,
            endDay = gameTimeManager.CurrentDay + offer.terms.durationDays - 1,
            status = AirlineContractStatus.Active,
            agreedTerms = offer.terms,
            renewalBasePaymentPerFlight = offer.terms.paymentPerCompletedFlight,
            renewalBaseCompletionBonus = offer.terms.completionBonus
        };

        contract.flightObligations = BuildFlightObligations(
            contract.contractId,
            offer.terms);

        pendingOffers.Remove(offer);
        activeContracts.Add(contract);
        ContractAccepted?.Invoke(contract);
        return true;
    }

    private static List<AirlineContractFlightObligation> BuildFlightObligations(
        string contractId,
        AirlineContractTerms terms)
    {
        List<AirlineContractFlightObligation> obligations =
            new List<AirlineContractFlightObligation>();

        if (terms == null || terms.flightWindows == null)
            return obligations;

        int slotIndex = 0;
        string contractCode = contractId.Length > 8
            ? contractId.Substring(0, 8).ToUpperInvariant()
            : contractId.ToUpperInvariant();

        foreach (AirlineContractFlightWindowRequirement requirement in terms.flightWindows)
        {
            if (requirement == null)
                continue;

            for (int i = 0; i < requirement.flightsPerDay; i++)
            {
                slotIndex++;
                obligations.Add(new AirlineContractFlightObligation
                {
                    obligationId = Guid.NewGuid().ToString("N"),
                    flightNumber = $"CT{contractCode}{slotIndex:00}",
                    window = requirement.window,
                    slotIndex = slotIndex
                });
            }
        }

        return obligations;
    }

    public bool IsContractActiveOnDay(string contractId, long day)
    {
        if (string.IsNullOrWhiteSpace(contractId))
            return false;

        ActiveAirlineContract contract = activeContracts.Find(item =>
            item != null &&
            item.contractId == contractId &&
            item.status == AirlineContractStatus.Active);

        return contract != null &&
               day >= contract.startDay &&
               day <= contract.endDay;
    }

    public bool DeclineOffer(string offerId)
    {
        if (string.IsNullOrWhiteSpace(offerId))
            return false;

        AirlineContractOffer offer = pendingOffers.Find(
            item => item != null && item.offerId == offerId);

        if (offer == null)
            return false;

        pendingOffers.Remove(offer);
        OfferDeclined?.Invoke(offerId);
        return true;
    }


    public bool DeclineAutomaticRenewal(string contractId)
    {
        if (string.IsNullOrWhiteSpace(contractId))
            return false;

        ActiveAirlineContract contract = activeContracts.Find(item =>
            item != null && item.contractId == contractId &&
            item.status == AirlineContractStatus.Active);

        if (contract == null || contract.renewalDeclined)
            return false;

        contract.renewalDeclined = true;
        ContractRenewalPreferenceChanged?.Invoke(contract);
        return true;
    }

    private void CompleteExpiredContracts()
    {
        if (gameTimeManager == null)
            return;

        // Only process contracts that existed when this update began. This
        // prevents large time jumps from immediately renewing a new agreement
        // several times in one pass.
        int contractsAtStart = activeContracts.Count;
        for (int i = 0; i < contractsAtStart; i++)
        {
            ActiveAirlineContract contract = activeContracts[i];
            if (contract == null ||
                contract.status != AirlineContractStatus.Active ||
                gameTimeManager.CurrentDay <= contract.endDay)
            {
                continue;
            }

            contract.status = AirlineContractStatus.Completed;

            ResolveReferences();
            if (performanceTracker != null)
                performanceTracker.FinalizeContractForCompletion(contract);

            ActiveAirlineContract renewedContract =
                TryAutomaticallyRenew(contract);

            if (renewedContract != null)
                contract.renewedByContractId = renewedContract.contractId;

            ContractCompleted?.Invoke(contract);

            if (renewedContract != null)
            {
                activeContracts.Add(renewedContract);
                ContractAccepted?.Invoke(renewedContract);
                ContractRenewed?.Invoke(contract, renewedContract);
            }
        }
    }

    private ActiveAirlineContract TryAutomaticallyRenew(
        ActiveAirlineContract previousContract)
    {
        ResolveReferences();
        if (previousContract == null || previousContract.renewalDeclined ||
            previousContract.airline == null || previousContract.sourceTemplate == null ||
            previousContract.agreedTerms == null ||
            performanceTracker == null || snapshotCollector == null)
            return null;

        if (HasActiveContract(previousContract.airline))
            return null;

        float relationship = performanceTracker.GetRelationshipScore(previousContract.airline);
        if (relationship < minimumRenewalRelationship)
            return null;

        int expectedFlights = previousContract.flightObligations != null
            ? previousContract.flightObligations.Count *
              Mathf.Max(1, previousContract.agreedTerms.durationDays)
            : 0;
        int completedFlights = previousContract.onTimeFlights + previousContract.lateFlights;
        float completionRate = expectedFlights > 0
            ? (float)completedFlights / expectedFlights
            : 0f;

        if (completionRate < minimumRenewalCompletionRate ||
            previousContract.unpaidPenaltyAmount > 0)
            return null;

        AirlineInterestResult interest;
        if (!snapshotCollector.TryGetInterest(previousContract.airline, out interest))
            interest = snapshotCollector.RefreshInterest(previousContract.airline);

        if (interest.totalInterest < previousContract.sourceTemplate.MinimumAirlineInterest ||
            !MeetsTemplateRequirements(previousContract.airline, previousContract.sourceTemplate))
            return null;

        AirlineContractTerms renewedTerms = CopyTerms(previousContract.agreedTerms);
        int nextRenewalCount = previousContract.renewalCount + 1;
        int basePayment = previousContract.renewalBasePaymentPerFlight > 0
            ? previousContract.renewalBasePaymentPerFlight
            : previousContract.agreedTerms.paymentPerCompletedFlight;
        int baseBonus = previousContract.renewalBaseCompletionBonus > 0
            ? previousContract.renewalBaseCompletionBonus
            : previousContract.agreedTerms.completionBonus;
        float paymentMultiplier = 1f + Mathf.Min(
            nextRenewalCount * paymentIncreasePerRenewal,
            maximumRenewalPaymentIncrease);

        renewedTerms.paymentPerCompletedFlight =
            Mathf.RoundToInt(basePayment * paymentMultiplier);
        renewedTerms.completionBonus =
            Mathf.RoundToInt(baseBonus * paymentMultiplier);

        ActiveAirlineContract renewedContract = new ActiveAirlineContract
        {
            contractId = Guid.NewGuid().ToString("N"),
            sourceOfferId = "renewal:" + previousContract.contractId,
            airline = previousContract.airline,
            sourceTemplate = previousContract.sourceTemplate,
            startDay = previousContract.endDay + 1,
            lastFinalizedDay = previousContract.endDay,
            endDay = previousContract.endDay + Mathf.Max(1, renewedTerms.durationDays),
            status = AirlineContractStatus.Active,
            agreedTerms = renewedTerms,
            renewalCount = nextRenewalCount,
            renewalBasePaymentPerFlight = basePayment,
            renewalBaseCompletionBonus = baseBonus
        };

        // Keep the same flight numbers and obligation IDs so an existing
        // timetable can continue seamlessly under the renewed contract ID.
        if (previousContract.flightObligations != null)
        {
            foreach (AirlineContractFlightObligation obligation in previousContract.flightObligations)
            {
                if (obligation == null)
                    continue;

                renewedContract.flightObligations.Add(
                    new AirlineContractFlightObligation
                    {
                        obligationId = obligation.obligationId,
                        flightNumber = obligation.flightNumber,
                        window = obligation.window,
                        slotIndex = obligation.slotIndex
                    });
            }
        }

        return renewedContract;
    }

    private static AirlineContractTerms CopyTerms(AirlineContractTerms source)
    {
        if (source == null)
            return null;

        AirlineContractTerms copy = new AirlineContractTerms
        {
            durationDays = source.durationDays,
            turnaroundMinutes = source.turnaroundMinutes,
            minimumGateSize = source.minimumGateSize,
            minimumRunwayLengthMeters = source.minimumRunwayLengthMeters,
            minimumCargoStorageCapacity = source.minimumCargoStorageCapacity,
            minimumCargoHandlingThroughputPerHour =
                source.minimumCargoHandlingThroughputPerHour,
            paymentPerCompletedFlight = source.paymentPerCompletedFlight,
            missedFlightPenalty = source.missedFlightPenalty,
            completionBonus = source.completionBonus
        };

        if (source.flightWindows != null)
        {
            foreach (AirlineContractFlightWindowRequirement requirement in source.flightWindows)
            {
                if (requirement == null)
                    continue;

                copy.flightWindows.Add(new AirlineContractFlightWindowRequirement
                {
                    window = requirement.window,
                    flightsPerDay = requirement.flightsPerDay
                });
            }
        }

        return copy;
    }

    private void ExpireOldOffers()
    {
        if (gameTimeManager == null)
            return;

        for (int i = pendingOffers.Count - 1; i >= 0; i--)
        {
            AirlineContractOffer offer = pendingOffers[i];
            if (offer == null)
                pendingOffers.RemoveAt(i);
            else if (gameTimeManager.CurrentDay >= offer.expiresOnDay)
                RemoveOfferAsExpired(offer);
        }
    }

    private void RemoveOfferAsExpired(AirlineContractOffer offer)
    {
        if (offer == null)
            return;

        pendingOffers.Remove(offer);
        OfferExpired?.Invoke(offer);
    }

    private bool HasActiveContract(AirlineDefinition airline)
    {
        return activeContracts.Exists(contract =>
            contract != null &&
            contract.airline == airline &&
            contract.status == AirlineContractStatus.Active);
    }

    private bool HasPendingOffer(
        AirlineDefinition airline,
        AirlineContractTemplate template)
    {
        return pendingOffers.Exists(offer =>
            offer != null &&
            offer.airline == airline &&
            offer.template == template);
    }

    private static void Shuffle<T>(IList<T> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            T temporary = items[i];
            items[i] = items[j];
            items[j] = temporary;
        }
    }

    private struct OfferCandidate
    {
        public AirlineDefinition airline;
        public AirlineContractTemplate template;
        public float interest;
    }
}
