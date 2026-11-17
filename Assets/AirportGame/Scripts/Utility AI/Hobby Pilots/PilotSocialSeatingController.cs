using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pairs two available ordinary seats advertised by InteractionProviders.
/// Each pilot reserves one seat and walks to that provider's InteractionPosition.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UtilityNeeds))]
[RequireComponent(typeof(PedestrianMovementController))]
[RequireComponent(typeof(PilotInteractionController))]
public class PilotSocialSeatingController : MonoBehaviour
{
    private static readonly List<PedestrianMovementController> NearbyMovements =
        new List<PedestrianMovementController>(16);
    private static readonly List<InteractionProvider> NearbyProviders =
        new List<InteractionProvider>(64);

    private enum SeatingState
    {
        Idle,
        Travelling,
        Socializing
    }

    private struct SeatOption
    {
        public InteractionProvider Provider;
        public InteractionDefinition Definition;
        public Vector3 Position;
    }

    [Header("Search")]
    [Min(0.1f)]
    [SerializeField] private float searchInterval = 3f;
    [Min(0.1f)]
    [SerializeField] private float searchRadius = 12f;
    [Min(0.1f)]
    [SerializeField] private float maximumDistanceBetweenSeats = 2.5f;
    [Range(0f, 100f)]
    [SerializeField] private float maximumSocialNeedValue = 80f;

    [Header("Socializing")]
    [Min(1)]
    [SerializeField] private int socializingDurationGameSeconds = 600;
    [Min(0f)]
    [SerializeField] private float socialSatisfactionAmount = 25f;
    [Min(0f)]
    [SerializeField] private float comfortSatisfactionAmount = 10f;
    [Min(0f)]
    [SerializeField] private float entertainmentSatisfactionAmount = 15f;
    [Min(0f)]
    [SerializeField] private float boredomSatisfactionAmount = 15f;
    [Min(1f)]
    [SerializeField] private float travelTimeout = 45f;
    [Min(1f)]
    [SerializeField] private float facingRotationSpeed = 540f;

    [Header("Runtime")]
    [SerializeField] private string currentStatus = "Idle";
    [SerializeField] private string currentPartnerName;

    private UtilityNeeds needs;
    private PedestrianMovementController movement;
    private PilotInteractionController interactionController;
    private GameTimeManager gameTimeManager;
    private bool subscribedToGameTime;
    private PilotSocialSeatingController partner;
    private InteractionProvider reservedSeatProvider;
    private InteractionDefinition reservedSeatDefinition;
    private InteractionProvider partnerSeatProvider;
    private InteractionDefinition partnerSeatDefinition;
    private SeatingState state;
    private bool isLeader;
    private bool hasArrived;
    private float nextSearchTime;
    private float travelElapsed;
    private long socializingStartGameSecond;

    public bool IsBusy => state != SeatingState.Idle;
    public string CurrentStatus => currentStatus;

    private void Awake()
    {
        needs = GetComponent<UtilityNeeds>();
        movement = GetComponent<PedestrianMovementController>();
        interactionController = GetComponent<PilotInteractionController>();
        ResolveGameTimeManager();
    }

    private bool preparingForDeparture;

    private void OnEnable()
    {
        preparingForDeparture = false;

        if (movement == null)
            movement = GetComponent<PedestrianMovementController>();

        if (movement != null)
        {
            movement.Arrived += HandleMovementArrived;
            movement.PathFailed += HandlePathFailed;
        }

        ResolveGameTimeManager();
        nextSearchTime = Time.time + UnityEngine.Random.Range(0f, searchInterval);
    }

    private void OnDisable()
    {
        if (movement != null)
        {
            movement.Arrived -= HandleMovementArrived;
            movement.PathFailed -= HandlePathFailed;
        }

        if (gameTimeManager != null && subscribedToGameTime)
            gameTimeManager.GameTimeAdvanced -= HandleGameTimeAdvanced;

        subscribedToGameTime = false;

        if (!preparingForDeparture)
            CancelSocializing("Seated conversation interrupted");
    }

    /// <summary>
    /// Ends a seated conversation cleanly when the pilot's aircraft begins departure.
    /// The departure controller takes over movement immediately afterwards.
    /// </summary>
    public void PrepareForDeparture()
    {
        preparingForDeparture = true;
        CancelSocializing("Departure sequence started");
    }

    private void Update()
    {
        if (gameTimeManager == null)
            ResolveGameTimeManager();

        if (state != SeatingState.Idle &&
            (reservedSeatProvider == null || !reservedSeatProvider.InteractionsEnabled ||
             partnerSeatProvider == null || !partnerSeatProvider.InteractionsEnabled))
        {
            CancelSocializing("A reserved seat became unavailable");
            return;
        }

        if (state != SeatingState.Idle && partner == null)
        {
            ResetOwnState("Conversation partner unavailable");
            return;
        }

        if (state == SeatingState.Travelling)
        {
            travelElapsed += Time.deltaTime;
            if (travelElapsed >= travelTimeout)
                CancelSocializing("Could not reach reserved seats");

            return;
        }

        if (state == SeatingState.Socializing)
        {
            FacePartner();
            return;
        }

        if (Time.time < nextSearchTime)
            return;

        nextSearchTime = Time.time + searchInterval;
        TryFindSeatingPartner();
    }

    private bool CanInitiate()
    {
        return isActiveAndEnabled &&
               state == SeatingState.Idle &&
               partner == null &&
               needs != null &&
               movement != null &&
               !movement.IsMoving &&
               interactionController != null &&
               gameTimeManager != null &&
               interactionController.CanPauseForSocial &&
               !PilotGroupConversationController.ShouldPreferGroup(this) &&
               needs.GetValue(UtilityNeeds.NeedType.Social) <= maximumSocialNeedValue;
    }

    private bool CanJoin()
    {
        return isActiveAndEnabled &&
               state == SeatingState.Idle &&
               partner == null &&
               needs != null &&
               movement != null &&
               !movement.IsMoving &&
               interactionController != null &&
               gameTimeManager != null &&
               interactionController.CanPauseForSocial;
    }

    /// <summary>
    /// Returns true when these two pilots can both reach a pair of nearby,
    /// currently available seats. Used to give seated conversations priority
    /// over the standing-conversation fallback.
    /// </summary>
    public bool HasAvailableSeatPairWith(PilotSocialSeatingController other)
    {
        if (other == null || !CanInitiate() || !other.CanJoin())
            return false;

        if (Vector3.Distance(transform.position, other.transform.position) > searchRadius)
            return false;

        UtilityAISpatialRegistry<InteractionProvider>.GetNearby(
            transform.position, searchRadius, NearbyProviders);
        List<SeatOption> availableSeats = GetAvailableSeats(NearbyProviders);

        for (int i = 0; i < availableSeats.Count; i++)
        {
            SeatOption firstSeat = availableSeats[i];

            for (int j = i + 1; j < availableSeats.Count; j++)
            {
                SeatOption secondSeat = availableSeats[j];

                if (firstSeat.Provider == secondSeat.Provider ||
                    Vector3.Distance(firstSeat.Position, secondSeat.Position) >
                    maximumDistanceBetweenSeats)
                {
                    continue;
                }

                float ownFirst = movement.EstimateTravelTime(firstSeat.Position);
                float otherSecond = other.movement.EstimateTravelTime(secondSeat.Position);
                float ownSecond = movement.EstimateTravelTime(secondSeat.Position);
                float otherFirst = other.movement.EstimateTravelTime(firstSeat.Position);

                bool firstAssignmentReachable =
                    !float.IsInfinity(ownFirst) && !float.IsNaN(ownFirst) &&
                    !float.IsInfinity(otherSecond) && !float.IsNaN(otherSecond);

                bool secondAssignmentReachable =
                    !float.IsInfinity(ownSecond) && !float.IsNaN(ownSecond) &&
                    !float.IsInfinity(otherFirst) && !float.IsNaN(otherFirst);

                if (firstAssignmentReachable || secondAssignmentReachable)
                    return true;
            }
        }

        return false;
    }

    private void TryFindSeatingPartner()
    {
        if (!CanInitiate())
            return;

        UtilityAISpatialRegistry<PedestrianMovementController>.GetNearby(
            transform.position, searchRadius, NearbyMovements);
        UtilityAISpatialRegistry<InteractionProvider>.GetNearby(
            transform.position, searchRadius, NearbyProviders);

        PilotSocialSeatingController selectedPartner = null;
        SeatOption selectedOwnSeat = default;
        SeatOption selectedPartnerSeat = default;
        float bestScore = float.PositiveInfinity;

        List<SeatOption> availableSeats = GetAvailableSeats(NearbyProviders);
        if (availableSeats.Count < 2)
            return;

        for (int pilotIndex = 0; pilotIndex < NearbyMovements.Count; pilotIndex++)
        {
            PilotSocialSeatingController candidate =
                NearbyMovements[pilotIndex].GetComponent<PilotSocialSeatingController>();
            if (candidate == null || candidate == this || !candidate.CanJoin())
                continue;

            float pilotDistance = Vector3.Distance(transform.position, candidate.transform.position);
            if (pilotDistance > searchRadius)
                continue;

            for (int i = 0; i < availableSeats.Count; i++)
            {
                SeatOption firstSeat = availableSeats[i];

                for (int j = i + 1; j < availableSeats.Count; j++)
                {
                    SeatOption secondSeat = availableSeats[j];

                    float seatDistance = Vector3.Distance(firstSeat.Position, secondSeat.Position);
                    if (seatDistance > maximumDistanceBetweenSeats)
                        continue;

                    float ownFirst = movement.EstimateTravelTime(firstSeat.Position);
                    float partnerSecond = candidate.movement.EstimateTravelTime(secondSeat.Position);
                    float ownSecond = movement.EstimateTravelTime(secondSeat.Position);
                    float partnerFirst = candidate.movement.EstimateTravelTime(firstSeat.Position);

                    float firstAssignment = ownFirst + partnerSecond;
                    float secondAssignment = ownSecond + partnerFirst;
                    bool swap = secondAssignment < firstAssignment;
                    float totalTravel = Mathf.Min(firstAssignment, secondAssignment);

                    if (float.IsInfinity(totalTravel) || float.IsNaN(totalTravel) ||
                        totalTravel >= bestScore)
                        continue;

                    bestScore = totalTravel;
                    selectedPartner = candidate;
                    selectedOwnSeat = swap ? secondSeat : firstSeat;
                    selectedPartnerSeat = swap ? firstSeat : secondSeat;
                }
            }
        }

        if (selectedPartner != null)
        {
            BeginSocializing(
                selectedPartner,
                selectedOwnSeat,
                selectedPartnerSeat);
        }
    }

    private static List<SeatOption> GetAvailableSeats(IReadOnlyList<InteractionProvider> providers)
    {
        List<SeatOption> seats = new List<SeatOption>(providers.Count);

        for (int providerIndex = 0; providerIndex < providers.Count; providerIndex++)
        {
            InteractionProvider provider = providers[providerIndex];
            if (provider == null || !provider.InteractionsEnabled)
                continue;

            IReadOnlyList<InteractionDefinition> definitions = provider.Interactions;
            for (int i = 0; i < definitions.Count; i++)
            {
                InteractionDefinition definition = definitions[i];
                if (!IsSeatInteraction(definition) || !provider.CanOffer(definition))
                    continue;

                seats.Add(new SeatOption
                {
                    Provider = provider,
                    Definition = definition,
                    Position = provider.InteractionPosition
                });
                break;
            }
        }

        return seats;
    }

    private static bool IsSeatInteraction(InteractionDefinition definition)
    {
        if (definition == null || string.IsNullOrEmpty(definition.InteractionId))
            return false;

        string id = definition.InteractionId;
        return id.IndexOf("sit", StringComparison.OrdinalIgnoreCase) >= 0 ||
               id.IndexOf("seat", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void BeginSocializing(
        PilotSocialSeatingController other,
        SeatOption ownSeat,
        SeatOption otherSeat)
    {
        if (other == null || ownSeat.Provider == null || otherSeat.Provider == null ||
            ownSeat.Provider == otherSeat.Provider ||
            !CanInitiate() || !other.CanJoin())
            return;

        if (!ownSeat.Provider.TryReserve(this, ownSeat.Definition))
            return;

        if (!otherSeat.Provider.TryReserve(other, otherSeat.Definition))
        {
            ownSeat.Provider.ReleaseReservation(this, ownSeat.Definition);
            return;
        }

        partner = other;
        other.partner = this;
        reservedSeatProvider = ownSeat.Provider;
        reservedSeatDefinition = ownSeat.Definition;
        other.reservedSeatProvider = otherSeat.Provider;
        other.reservedSeatDefinition = otherSeat.Definition;
        partnerSeatProvider = otherSeat.Provider;
        partnerSeatDefinition = otherSeat.Definition;
        other.partnerSeatProvider = ownSeat.Provider;
        other.partnerSeatDefinition = ownSeat.Definition;
        isLeader = true;
        other.isLeader = false;
        state = SeatingState.Travelling;
        other.state = SeatingState.Travelling;
        hasArrived = false;
        other.hasArrived = false;
        travelElapsed = 0f;
        other.travelElapsed = 0f;
        currentPartnerName = other.gameObject.name;
        other.currentPartnerName = gameObject.name;
        currentStatus = "Walking to available seats";
        other.currentStatus = "Walking to available seats";

        if (!movement.MoveTo(ownSeat.Position))
        {
            CancelSocializing("Could not reach reserved seat");
            return;
        }

        if (partner != other || other.partner != this)
            return;

        if (!other.movement.MoveTo(otherSeat.Position))
            CancelSocializing("Could not reach reserved seat");
    }

    private void HandleMovementArrived()
    {
        if (state != SeatingState.Travelling)
            return;

        hasArrived = true;
        TryBeginSocializing();
    }

    private void HandlePathFailed()
    {
        if (state == SeatingState.Travelling)
            CancelSocializing("Path to reserved seat failed");
    }

    private void TryBeginSocializing()
    {
        if (partner == null || state != SeatingState.Travelling ||
            !hasArrived || !partner.hasArrived)
            return;

        // The movement targets are reachable approach points. Once both pilots
        // arrive, place each pilot at the seat's animation position.
        if (reservedSeatProvider != null)
        {
            movement.AlignToAnimationPoint(reservedSeatProvider);
        }

        if (partner.reservedSeatProvider != null)
        {
            partner.movement.AlignToAnimationPoint(partner.reservedSeatProvider);
        }

        state = SeatingState.Socializing;
        partner.state = SeatingState.Socializing;
        socializingStartGameSecond = GetCurrentAbsoluteGameSecond();
        partner.socializingStartGameSecond = socializingStartGameSecond;
        currentStatus = "Socializing while seated";
        partner.currentStatus = "Socializing while seated";
        movement.Stop();
        partner.movement.Stop();
        FacePartner();
        partner.FacePartner();
    }

    private void ResolveGameTimeManager()
    {
        if (gameTimeManager == null)
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();

        if (gameTimeManager == null || subscribedToGameTime)
            return;

        gameTimeManager.GameTimeAdvanced += HandleGameTimeAdvanced;
        subscribedToGameTime = true;
    }

    private void HandleGameTimeAdvanced(long previousSecond, long currentSecond)
    {
        if (state != SeatingState.Socializing || !isLeader)
            return;

        if (currentSecond - socializingStartGameSecond >= socializingDurationGameSeconds)
            CompleteSocializing();
    }

    private long GetCurrentAbsoluteGameSecond()
    {
        if (gameTimeManager == null)
            return 0;

        return (gameTimeManager.CurrentDay - 1) * 86400L +
               gameTimeManager.GetGameTimeSeconds();
    }

    private void FacePartner()
    {
        if (partner == null)
            return;

        Vector3 direction = partner.transform.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Quaternion targetRotation =
            Quaternion.LookRotation(direction.normalized, Vector3.up);

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            facingRotationSpeed * Time.deltaTime);
    }

    private void CompleteSocializing()
    {
        PilotSocialSeatingController other = partner;
        if (other == null)
        {
            ResetOwnState("Seating partner unavailable");
            return;
        }

        needs.SatisfyNeed(UtilityNeeds.NeedType.Social, socialSatisfactionAmount);
        needs.SatisfyNeed(UtilityNeeds.NeedType.Comfort, comfortSatisfactionAmount);
        needs.SatisfyNeed(UtilityNeeds.NeedType.Entertainment, entertainmentSatisfactionAmount);
        needs.SatisfyNeed(UtilityNeeds.NeedType.Boredom, boredomSatisfactionAmount);
        other.needs.SatisfyNeed(UtilityNeeds.NeedType.Social, other.socialSatisfactionAmount);
        other.needs.SatisfyNeed(UtilityNeeds.NeedType.Comfort, other.comfortSatisfactionAmount);
        other.needs.SatisfyNeed(UtilityNeeds.NeedType.Entertainment,
            other.entertainmentSatisfactionAmount);
        other.needs.SatisfyNeed(UtilityNeeds.NeedType.Boredom,
            other.boredomSatisfactionAmount);

        // Return both pilots to reachable approach points before freeing the seats.
        // Their AnimationPoints may be inside blocked building footprints.
        if (reservedSeatProvider != null && movement != null)
            movement.AlignToInteractionPoint(reservedSeatProvider);

        if (other.reservedSeatProvider != null && other.movement != null)
            other.movement.AlignToInteractionPoint(other.reservedSeatProvider);

        ReleaseSeatReservations();
        other.ReleaseSeatReservations();

        partner = null;
        other.partner = null;
        ResetOwnState("Seated conversation complete");
        other.ResetOwnState("Seated conversation complete");
    }

    private void ReleaseSeatReservations()
    {
        if (reservedSeatProvider != null && reservedSeatDefinition != null)
            reservedSeatProvider.ReleaseReservation(this, reservedSeatDefinition);

        reservedSeatProvider = null;
        reservedSeatDefinition = null;
        partnerSeatProvider = null;
        partnerSeatDefinition = null;
    }

    private void CancelSocializing(string reason)
    {
        PilotSocialSeatingController other = partner;
        partner = null;

        ReleaseSeatReservations();

        if (other != null && other.partner == this)
        {
            other.partner = null;
            other.ReleaseSeatReservations();
            other.ResetOwnState(reason);
        }

        ResetOwnState(reason);
    }

    private void ResetOwnState(string status)
    {
        if (movement != null && movement.IsMoving)
            movement.Stop();

        state = SeatingState.Idle;
        isLeader = false;
        hasArrived = false;
        travelElapsed = 0f;
        socializingStartGameSecond = 0;
        currentPartnerName = string.Empty;
        currentStatus = status;
        nextSearchTime = Time.time + searchInterval;
    }

    private void OnValidate()
    {
        searchInterval = Mathf.Max(0.1f, searchInterval);
        searchRadius = Mathf.Max(0.1f, searchRadius);
        maximumDistanceBetweenSeats = Mathf.Max(0.1f, maximumDistanceBetweenSeats);
        maximumSocialNeedValue = Mathf.Clamp(maximumSocialNeedValue, 0f, 100f);
        socializingDurationGameSeconds = Mathf.Max(1, socializingDurationGameSeconds);
        socialSatisfactionAmount = Mathf.Max(0f, socialSatisfactionAmount);
        comfortSatisfactionAmount = Mathf.Max(0f, comfortSatisfactionAmount);
        entertainmentSatisfactionAmount = Mathf.Max(0f, entertainmentSatisfactionAmount);
        boredomSatisfactionAmount = Mathf.Max(0f, boredomSatisfactionAmount);
        travelTimeout = Mathf.Max(1f, travelTimeout);
        facingRotationSpeed = Mathf.Max(1f, facingRotationSpeed);
    }
}
