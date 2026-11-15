using UnityEngine;

/// <summary>
/// Coordinates two pilots socializing at a shared seating area.
/// Pilots walk to reserved seat positions, face one another and wait together.
/// They remain standing until sitting animations are added.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UtilityNeeds))]
[RequireComponent(typeof(PedestrianMovementController))]
[RequireComponent(typeof(PilotInteractionController))]
public class PilotSocialSeatingController : MonoBehaviour
{
    private enum SeatingState
    {
        Idle,
        Travelling,
        Socializing
    }

    [Header("Search")]
    [Min(0.1f)]
    [SerializeField] private float searchInterval = 3f;
    [Min(0.1f)]
    [SerializeField] private float searchRadius = 12f;
    [Range(0f, 100f)]
    [SerializeField] private float maximumSocialNeedValue = 80f;

    [Header("Socializing")]
    [Min(1)]
    [SerializeField] private int socializingDurationGameSeconds = 45;
    [Min(0f)]
    [SerializeField] private float socialSatisfactionAmount = 25f;
    [Min(0f)]
    [SerializeField] private float comfortSatisfactionAmount = 10f;
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
    private PilotSocialSeatingArea reservedArea;
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

    private void OnEnable()
    {
        if (movement == null)
            movement = GetComponent<PedestrianMovementController>();

        if (movement != null)
        {
            movement.Arrived += HandleMovementArrived;
            movement.PathFailed += HandlePathFailed;
        }

        ResolveGameTimeManager();
        nextSearchTime = Time.time + Random.Range(0f, searchInterval);
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
        CancelSocializing("Seating social interaction interrupted");
    }

    private void Update()
    {
        if (gameTimeManager == null)
            ResolveGameTimeManager();

        if (state != SeatingState.Idle &&
            (reservedArea == null || !reservedArea.isActiveAndEnabled))
        {
            CancelSocializing("Shared seating area became unavailable");
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
                CancelSocializing("Could not reach seating positions");

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

    private void TryFindSeatingPartner()
    {
        if (!CanInitiate())
            return;

        PilotSocialSeatingController[] pilots =
            FindObjectsByType<PilotSocialSeatingController>(FindObjectsSortMode.None);

        PilotSocialSeatingController selectedPartner = null;
        PilotSocialSeatingArea selectedArea = null;
        Transform selectedOwnSeat = null;
        Transform selectedPartnerSeat = null;
        float closestDistance = searchRadius;

        PilotSocialSeatingArea[] areas =
            FindObjectsByType<PilotSocialSeatingArea>(FindObjectsSortMode.None);

        foreach (PilotSocialSeatingController candidate in pilots)
        {
            if (candidate == null || candidate == this || !candidate.CanJoin())
                continue;

            float distance = Vector3.Distance(transform.position, candidate.transform.position);
            if (distance >= closestDistance)
                continue;

            foreach (PilotSocialSeatingArea area in areas)
            {
                if (area == null || !area.IsAvailable)
                    continue;

                // The two pilots must both be able to reach their assigned seats.
                // The actual reservation is made again immediately before pairing.
                Transform[] seats = GetSeatPoints(area);
                if (seats == null || seats.Length < 2 || seats[0] == null || seats[1] == null)
                    continue;

                float ownTravel = movement.EstimateTravelTime(seats[0].position);
                float partnerTravel = candidate.movement.EstimateTravelTime(seats[1].position);

                if (float.IsInfinity(ownTravel) || float.IsNaN(ownTravel) ||
                    float.IsInfinity(partnerTravel) || float.IsNaN(partnerTravel))
                {
                    // Try the opposite seat assignment before rejecting this area.
                    ownTravel = movement.EstimateTravelTime(seats[1].position);
                    partnerTravel = candidate.movement.EstimateTravelTime(seats[0].position);
                    if (float.IsInfinity(ownTravel) || float.IsNaN(ownTravel) ||
                        float.IsInfinity(partnerTravel) || float.IsNaN(partnerTravel))
                        continue;

                    Transform swap = seats[0];
                    seats[0] = seats[1];
                    seats[1] = swap;
                }

                selectedPartner = candidate;
                selectedArea = area;
                selectedOwnSeat = seats[0];
                selectedPartnerSeat = seats[1];
                closestDistance = distance;
                break;
            }
        }

        if (selectedPartner != null && selectedArea != null)
            BeginSocializing(selectedPartner, selectedArea, selectedOwnSeat, selectedPartnerSeat);
    }

    // Returns a copy so seat-order swapping never changes the provider's serialized array.
    private static Transform[] GetSeatPoints(PilotSocialSeatingArea area)
    {
        return area.GetSeatPointSnapshot();
    }

    private void BeginSocializing(
        PilotSocialSeatingController other,
        PilotSocialSeatingArea area,
        Transform ownSeat,
        Transform otherSeat)
    {
        if (other == null || area == null || ownSeat == null || otherSeat == null ||
            !CanInitiate() || !other.CanJoin())
            return;

        if (!area.TryReservePair(this, other, out Transform reservedOwnSeat, out Transform reservedOtherSeat))
            return;

        // The area reserves seats in its configured order. Reconcile that order
        // with the path estimates selected during discovery.
        if (reservedOwnSeat != ownSeat || reservedOtherSeat != otherSeat)
        {
            if (reservedOwnSeat != otherSeat || reservedOtherSeat != ownSeat)
            {
                area.ReleasePair(this, other);
                return;
            }

            Transform swap = ownSeat;
            ownSeat = otherSeat;
            otherSeat = swap;
        }

        partner = other;
        other.partner = this;
        reservedArea = area;
        other.reservedArea = area;
        isLeader = true;
        other.isLeader = false;
        state = SeatingState.Travelling;
        other.state = SeatingState.Travelling;
        hasArrived = false;
        other.hasArrived = false;
        travelElapsed = 0f;
        other.travelElapsed = 0f;
        socializingStartGameSecond = GetCurrentAbsoluteGameSecond();
        other.socializingStartGameSecond = socializingStartGameSecond;
        currentPartnerName = other.gameObject.name;
        other.currentPartnerName = gameObject.name;
        currentStatus = "Walking to shared seating";
        other.currentStatus = "Walking to shared seating";

        if (!movement.MoveTo(ownSeat.position))
        {
            CancelSocializing("Could not reach shared seating");
            return;
        }

        if (partner != other || other.partner != this)
            return;

        if (!other.movement.MoveTo(otherSeat.position))
            CancelSocializing("Could not reach shared seating");
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
            CancelSocializing("Path to seating position failed");
    }

    private void TryBeginSocializing()
    {
        if (partner == null || state != SeatingState.Travelling ||
            !hasArrived || !partner.hasArrived)
            return;

        state = SeatingState.Socializing;
        partner.state = SeatingState.Socializing;
        socializingStartGameSecond = GetCurrentAbsoluteGameSecond();
        partner.socializingStartGameSecond = socializingStartGameSecond;
        currentStatus = "Socializing at seat";
        partner.currentStatus = "Socializing at seat";
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
        PilotSocialSeatingArea area = reservedArea;

        if (other == null)
        {
            ResetOwnState("Seating partner unavailable");
            return;
        }

        needs.SatisfyNeed(UtilityNeeds.NeedType.Social, socialSatisfactionAmount);
        needs.SatisfyNeed(UtilityNeeds.NeedType.Comfort, comfortSatisfactionAmount);
        other.needs.SatisfyNeed(UtilityNeeds.NeedType.Social, other.socialSatisfactionAmount);
        other.needs.SatisfyNeed(UtilityNeeds.NeedType.Comfort, other.comfortSatisfactionAmount);

        if (area != null)
            area.ReleasePair(this, other);

        partner = null;
        other.partner = null;
        reservedArea = null;
        other.reservedArea = null;
        ResetOwnState("Seating socializing complete");
        other.ResetOwnState("Seating socializing complete");
    }

    private void CancelSocializing(string reason)
    {
        PilotSocialSeatingController other = partner;
        PilotSocialSeatingArea area = reservedArea;
        partner = null;

        if (area != null && other != null)
            area.ReleasePair(this, other);

        ResetOwnState(reason);

        if (other != null && other.partner == this)
        {
            other.partner = null;
            other.reservedArea = null;
            other.ResetOwnState(reason);
        }
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
        reservedArea = null;
        nextSearchTime = Time.time + searchInterval;
    }

    private void OnValidate()
    {
        searchInterval = Mathf.Max(0.1f, searchInterval);
        searchRadius = Mathf.Max(0.1f, searchRadius);
        maximumSocialNeedValue = Mathf.Clamp(maximumSocialNeedValue, 0f, 100f);
        socializingDurationGameSeconds = Mathf.Max(1, socializingDurationGameSeconds);
        socialSatisfactionAmount = Mathf.Max(0f, socialSatisfactionAmount);
        comfortSatisfactionAmount = Mathf.Max(0f, comfortSatisfactionAmount);
        travelTimeout = Mathf.Max(1f, travelTimeout);
        facingRotationSpeed = Mathf.Max(1f, facingRotationSpeed);
    }
}
