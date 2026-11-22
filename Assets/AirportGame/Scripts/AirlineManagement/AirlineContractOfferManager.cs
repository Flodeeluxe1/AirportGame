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
    private const int MinimumOffersPerEvaluation = 1;
    private const int MaximumOffersPerEvaluation = 3;
    private const int OfferLifetimeDays = 3;

    [Header("Systems")]
    [SerializeField] private AirlineAirportSnapshotCollector snapshotCollector;
    [SerializeField] private GameTimeManager gameTimeManager;

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

    public event Action<AirlineContractOffer> OfferGenerated;
    public event Action<AirlineContractOffer> OfferExpired;
    public event Action<ActiveAirlineContract> ContractAccepted;
    public event Action<string> OfferDeclined;

    public IReadOnlyList<AirlineContractOffer> PendingOffers => pendingOffers;
    public IReadOnlyList<ActiveAirlineContract> ActiveContracts => activeContracts;

    private void OnEnable()
    {
        ResolveReferences();

        if (gameTimeManager != null)
            gameTimeManager.GameTimeAdvanced += HandleGameTimeAdvanced;

        // Let the airport snapshot collector finish its initial scene refresh first.
        StartCoroutine(InitializeAfterAirportSystems());
    }

    private IEnumerator InitializeAfterAirportSystems()
    {
        yield return null;
        yield return null;
        ResolveReferences();
        initializationComplete = true;
        EvaluateForCurrentDay();
    }

    private void OnDisable()
    {
        if (gameTimeManager != null)
            gameTimeManager.GameTimeAdvanced -= HandleGameTimeAdvanced;
    }

    private void ResolveReferences()
    {
        if (snapshotCollector == null)
            snapshotCollector = FindAnyObjectByType<AirlineAirportSnapshotCollector>();

        if (gameTimeManager == null)
            gameTimeManager = FindAnyObjectByType<GameTimeManager>();
    }

    private void HandleGameTimeAdvanced(long previousAbsoluteSecond, long currentAbsoluteSecond)
    {
        if (!initializationComplete)
            return;

        if (gameTimeManager == null)
            ResolveReferences();

        if (gameTimeManager == null)
            return;

        ExpireOldOffers();
        EvaluateForCurrentDay();
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

    [ContextMenu("Generate Contract Offers Now")]
    public void GenerateOffersNow()
    {
        ResolveReferences();
        if (gameTimeManager == null)
            return;

        GenerateOffers(gameTimeManager.CurrentDay);
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
            endDay = gameTimeManager.CurrentDay + offer.terms.durationDays - 1,
            status = AirlineContractStatus.Active,
            agreedTerms = offer.terms
        };

        pendingOffers.Remove(offer);
        activeContracts.Add(contract);
        ContractAccepted?.Invoke(contract);
        return true;
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
