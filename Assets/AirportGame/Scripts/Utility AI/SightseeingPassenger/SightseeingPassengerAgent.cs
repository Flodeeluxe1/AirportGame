using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Individual sightseeing passenger. Uses the airport's normal pedestrian
/// pathfinding, needs and interaction queue, and returns to its own car.
/// </summary>
[DisallowMultipleComponent]
public class SightseeingPassengerAgent : MonoBehaviour
{
    public enum PassengerState
    {
        Initializing, QueuedForCheckIn, WalkingToCheckIn, CheckingIn,
        WaitingInTerminal, GatheringForBoarding, WalkingToAircraft, OnBoard, WalkingToCar, Completed
    }

    [Header("Runtime")]
    [SerializeField] private PassengerState state = PassengerState.Initializing;
    [SerializeField] private int ticketPricePaid;
    [SerializeField] private bool hasTicket;

    private SightseeingPassengerManager manager;
    private TrafficTestVehicle vehicle;
    private Transform waitingArea;
    private int waitingRadius;
    private PedestrianMovementController movement;
    private UtilityNeeds needs;
    private InteractionController interactionController;
    private PilotWanderingController pilotWanderingController;
    private PedestrianPathfinder pathfinder;
    private InteractionProvider checkInProvider;
    private InteractionDefinition checkInDefinition;
    private InteractionQueue checkInQueue;
    private Coroutine checkInRoutine;
    private Renderer[] renderers;
    private Collider[] colliders;
    private bool reservationHeld;
    private bool needsPaused;
    private float nextQueueRefresh;
    private Vector3 lastRequestedQueuePosition;
    private bool hasRequestedQueuePosition;
    private float nextWander;

    private sealed class FoundationCache
    {
        public readonly List<Vector2Int> cells = new List<Vector2Int>();
        public bool dirty = true;
    }

    private static readonly Dictionary<GridSurface, FoundationCache> FoundationCaches =
        new Dictionary<GridSurface, FoundationCache>();

    private GridManager gridManager;
    private GridSurface gridSurface;
    private FoundationCache foundationCache;
    private GridSurface subscribedFoundationSurface;
    private RoomPurposeController roomPurposeController;
    private PedestrianAccessProfile accessProfile;
    private float nextCarRetry;
    private int carRetries;

    public PassengerState State => state;
    public bool HasTicket => hasTicket;
    public bool IsOnBoard => state == PassengerState.OnBoard;
    public bool IsWalkingToAircraft => state == PassengerState.WalkingToAircraft;
    public bool IsGatheringForBoarding => state == PassengerState.GatheringForBoarding;
    public bool IsWalkingToCar => state == PassengerState.WalkingToCar;
    public bool IsWalkingToCheckIn => state == PassengerState.QueuedForCheckIn ||
        state == PassengerState.WalkingToCheckIn || state == PassengerState.CheckingIn;
    public PedestrianMovementController Movement => movement;

    public void SetDepartureUrgency(float urgency)
    {
        if (needs != null && state != PassengerState.OnBoard &&
            state != PassengerState.Completed)
            needs.SetDepartureUrgency(urgency);
    }

    public void Initialize(
        SightseeingPassengerManager owner,
        TrafficTestVehicle assignedVehicle,
        Transform waitingAreaAnchor,
        int waitingAreaRadius)
    {
        manager = owner;
        vehicle = assignedVehicle;
        waitingArea = waitingAreaAnchor;
        waitingRadius = Mathf.Max(1, waitingAreaRadius);

        needs = GetComponent<UtilityNeeds>();
        if (needs == null) needs = gameObject.AddComponent<UtilityNeeds>();
        needs.enabled = true;

        movement = GetComponent<PedestrianMovementController>();
        if (movement == null) movement = gameObject.AddComponent<PedestrianMovementController>();
        movement.enabled = true;

        PedestrianAccessProfile accessProfile = GetComponent<PedestrianAccessProfile>();
        if (accessProfile == null)
            accessProfile = gameObject.AddComponent<PedestrianAccessProfile>();
        accessProfile.enabled = true;

        interactionController = GetComponent<InteractionController>();
        if (interactionController == null)
            interactionController = gameObject.AddComponent<InteractionController>();
        interactionController.enabled = false;

        // Passenger roaming is constrained to the assigned public terminal
        // area below; do not run the hobby-pilot's airport-wide wanderer.
        pilotWanderingController = GetComponent<PilotWanderingController>();
        if (pilotWanderingController != null)
            pilotWanderingController.enabled = false;

        pathfinder = FindAnyObjectByType<PedestrianPathfinder>();
        ResolveFoundationReferences();
        UpdateFoundationSurfaceSubscription();
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);

        movement.Arrived += HandleArrived;
        movement.PathFailed += HandlePathFailed;
        UpdateFoundationSurfaceSubscription();
        state = PassengerState.Initializing;
        nextWander = Time.time + Random.Range(3f, 8f);
    }

    public bool BeginCheckIn(InteractionProvider provider, InteractionDefinition definition, int price)
    {
        if (provider == null || definition == null || hasTicket ||
            state == PassengerState.OnBoard || state == PassengerState.Completed ||
            movement == null) return false;

        CancelCheckIn();
        checkInProvider = provider;
        checkInDefinition = definition;
        checkInQueue = provider.Queue;
        ticketPricePaid = Mathf.Max(0, price);

        if (checkInQueue != null && checkInQueue.QueueEnabled)
        {
            float travel = movement.EstimateTravelTime(provider);
            if (float.IsInfinity(travel) || float.IsNaN(travel) ||
                !checkInQueue.TryJoin(this, definition, travel))
            {
                ClearCheckInReferences();
                return false;
            }

            state = PassengerState.QueuedForCheckIn;
            nextQueueRefresh = 0f;
            UpdateQueue();
            return true;
        }

        if (!provider.TryReserve(this, definition))
        {
            ClearCheckInReferences();
            return false;
        }

        reservationHeld = true;
        state = PassengerState.WalkingToCheckIn;
        if (!movement.MoveTo(provider))
        {
            CancelCheckIn();
            state = PassengerState.Initializing;
            return false;
        }
        return true;
    }

    private void Update()
    {
        if (state == PassengerState.QueuedForCheckIn)
        {
            UpdateQueue();
            return;
        }

        if (state == PassengerState.WaitingInTerminal)
        {
            UpdateWaiting();
            return;
        }

        if (state == PassengerState.WalkingToCar && !movement.IsMoving &&
            Time.time >= nextCarRetry)
            TryReturnRoute();
    }

    private void UpdateQueue()
    {
        if (checkInProvider == null || !checkInProvider.InteractionsEnabled ||
            checkInQueue == null || !checkInQueue.QueueEnabled ||
            !checkInQueue.IsQueued(this))
        {
            CancelCheckIn();
            state = PassengerState.Initializing;
            return;
        }

        if (checkInQueue.IsActive(this))
        {
            hasRequestedQueuePosition = false;

            if (movement.IsAtInteractionApproach(checkInProvider))
            {
                BeginCheckInInteraction();
                return;
            }

            // A passenger may be promoted while walking to a waiting slot.
            // Replace that route immediately with the desk approach route.
            state = PassengerState.WalkingToCheckIn;
            if (!movement.MoveTo(checkInProvider)) HandleCheckInFailure();
            return;
        }

        if (Time.time < nextQueueRefresh) return;
        nextQueueRefresh = Time.time + 0.35f;
        Vector3 queuePosition = checkInQueue.GetQueuePosition(this);
        bool queueSlotChanged =
            !hasRequestedQueuePosition ||
            Vector3.Distance(lastRequestedQueuePosition, queuePosition) > 0.2f;

        // Compare against the requested slot, not movement.Destination.
        // The pathfinder may choose a nearby walkable cell, so those two
        // positions are not expected to be identical. Replanning every refresh
        // would continually interrupt the passenger before reaching the slot.
        if (queueSlotChanged)
        {
            PlacedBuilding deskBuilding =
                checkInProvider.GetComponentInParent<PlacedBuilding>();

            if (!movement.TryMoveToNearby(
                    queuePosition,
                    checkInProvider.InteractionPosition,
                    deskBuilding))
            {
                HandleCheckInFailure();
                return;
            }

            lastRequestedQueuePosition = queuePosition;
            hasRequestedQueuePosition = true;
        }
    }

    private void BeginCheckInInteraction()
    {
        if (state == PassengerState.CheckingIn ||
            checkInProvider == null || checkInDefinition == null) return;
        if (checkInQueue != null && checkInQueue.QueueEnabled &&
            !checkInQueue.IsActive(this)) return;

        state = PassengerState.CheckingIn;
        movement.AlignToAnimationPoint(checkInProvider);
        if (checkInRoutine != null) StopCoroutine(checkInRoutine);
        checkInRoutine = StartCoroutine(CompleteCheckIn());
    }

    private IEnumerator CompleteCheckIn()
    {
        float duration = checkInDefinition != null
            ? Mathf.Max(0f, checkInDefinition.DurationSeconds) : 0f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            if (checkInProvider == null || !checkInProvider.InteractionsEnabled)
            {
                HandleCheckInFailure();
                yield break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        InteractionProvider completedProvider = checkInProvider;
        if (checkInQueue != null && checkInQueue.QueueEnabled)
            checkInQueue.Complete(this);
        else if (reservationHeld && completedProvider != null)
            completedProvider.ReleaseReservation(this, checkInDefinition);

        // The animation anchor can occupy the desk's blocked footprint.
        // Return to the saved walkable InteractionPoint before terminal roaming.
        if (completedProvider != null)
            movement.AlignToInteractionPoint(completedProvider);

        reservationHeld = false;
        ClearCheckInReferences();
        checkInRoutine = null;
        hasTicket = true;
        state = PassengerState.WaitingInTerminal;
        if (manager != null) manager.HandlePassengerCheckedIn(this, ticketPricePaid);
        if (interactionController != null) interactionController.enabled = true;
        nextWander = Time.time + Random.Range(3f, 8f);
    }

    private void UpdateWaiting()
    {
        if (movement == null || movement.IsMoving ||
            interactionController == null || !interactionController.enabled ||
            interactionController.DebugActivity != "None" ||
            Time.time < nextWander || waitingArea == null || pathfinder == null)
            return;

        nextWander = Time.time + Random.Range(8f, 18f);
        if (gridManager == null || gridSurface == null)
        {
            ResolveFoundationReferences();
            if (gridManager == null || gridSurface == null) return;
        }

        UpdateFoundationSurfaceSubscription();
        RefreshFoundationCacheIfNeeded();
        if (foundationCache == null || foundationCache.cells.Count == 0) return;

        Vector2Int centre = gridManager.WorldToGrid(waitingArea.position);
        int candidateCount = foundationCache.cells.Count;
        int attempts = Mathf.Min(12, candidateCount);
        int startIndex = Random.Range(0, candidateCount);

        // Reuse a shared, change-invalidated Foundation cache and inspect only
        // a bounded number of candidates for each passenger wander.
        for (int i = 0; i < attempts; i++)
        {
            Vector2Int candidate = foundationCache.cells[(startIndex + i) % candidateCount];
            if (Mathf.Abs(candidate.x - centre.x) > waitingRadius ||
                Mathf.Abs(candidate.y - centre.y) > waitingRadius)
                continue;

            if (!pathfinder.IsWalkable(candidate) ||
                (roomPurposeController != null &&
                 !roomPurposeController.IsCellAccessibleToPedestrian(candidate, accessProfile)))
                continue;

            Vector3 destination = gridManager.GridToWorldCenter(candidate);
            destination.y = transform.position.y;
            if (movement.TryMoveTo(destination)) return;
        }
    }

    private void ResolveFoundationReferences()
    {
        if (gridManager == null)
            gridManager = FindAnyObjectByType<GridManager>();

        if (gridSurface == null)
            gridSurface = gridManager != null
                ? gridManager.GetComponent<GridSurface>()
                : FindAnyObjectByType<GridSurface>();

        if (roomPurposeController == null)
            roomPurposeController = FindAnyObjectByType<RoomPurposeController>();

        if (accessProfile == null)
            accessProfile = GetComponent<PedestrianAccessProfile>();
    }

    private void UpdateFoundationSurfaceSubscription()
    {
        if (subscribedFoundationSurface == gridSurface)
            return;

        if (subscribedFoundationSurface != null)
            subscribedFoundationSurface.FoundationSurfaceChanged -= HandleFoundationSurfaceChanged;

        subscribedFoundationSurface = gridSurface;
        foundationCache = null;

        if (subscribedFoundationSurface == null)
            return;

        subscribedFoundationSurface.FoundationSurfaceChanged += HandleFoundationSurfaceChanged;

        if (!FoundationCaches.TryGetValue(subscribedFoundationSurface, out foundationCache))
        {
            foundationCache = new FoundationCache();
            FoundationCaches.Add(subscribedFoundationSurface, foundationCache);
        }
    }

    private void HandleFoundationSurfaceChanged()
    {
        if (foundationCache != null)
            foundationCache.dirty = true;
    }

    private void RefreshFoundationCacheIfNeeded()
    {
        if (gridManager == null || gridSurface == null)
            return;

        if (foundationCache == null)
        {
            if (!FoundationCaches.TryGetValue(gridSurface, out foundationCache))
            {
                foundationCache = new FoundationCache();
                FoundationCaches.Add(gridSurface, foundationCache);
            }
        }

        if (!foundationCache.dirty)
            return;

        foundationCache.cells.Clear();
        for (int x = 0; x < gridManager.Width; x++)
        {
            for (int z = 0; z < gridManager.Height; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (gridSurface.GetSurface(cell) == BuildSurface.Foundation)
                    foundationCache.cells.Add(cell);
            }
        }

        foundationCache.dirty = false;
    }

    public bool BeginGatheringForBoarding(
        Vector3 gatheringPosition,
        SightseeingAircraftParkingSpot parkingSpot)
    {
        if (parkingSpot == null || !hasTicket ||
            state == PassengerState.OnBoard || state == PassengerState.Completed ||
            movement == null)
            return false;

        CancelCheckIn();
        if (interactionController != null) interactionController.enabled = false;
        movement.enabled = true;
        state = PassengerState.GatheringForBoarding;

        bool started = movement.TryMoveToNearby(
            gatheringPosition,
            parkingSpot.transform.position,
            parkingSpot.GetComponent<PlacedBuilding>());

        if (!started)
            state = PassengerState.WaitingInTerminal;
        return started;
    }

    public bool BeginBoarding(Transform boardingPoint, SightseeingAircraftParkingSpot parkingSpot)
    {
        if (boardingPoint == null || parkingSpot == null || !hasTicket ||
            state == PassengerState.OnBoard || state == PassengerState.Completed)
            return false;

        CancelCheckIn();
        if (interactionController != null) interactionController.enabled = false;
        movement.enabled = true;
        state = PassengerState.WalkingToAircraft;

        bool started = movement.TryMoveToNearby(
            boardingPoint.position,
            parkingSpot.transform.position,
            parkingSpot.GetComponent<PlacedBuilding>());
        if (!started) state = PassengerState.WaitingInTerminal;
        return started;
    }

    public void MarkOnBoard()
    {
        if (state != PassengerState.WalkingToAircraft) return;
        state = PassengerState.OnBoard;
        movement.enabled = false;
        if (interactionController != null) interactionController.enabled = false;

        if (needs != null)
        {
            needs.SetDepartureUrgency(0f);
            foreach (UtilityNeeds.NeedType need in System.Enum.GetValues(typeof(UtilityNeeds.NeedType)))
                needs.PauseNeedDecay(need);
            needsPaused = true;
        }
        SetVisible(false);
    }

    public void DisembarkToCar()
    {
        if (state != PassengerState.OnBoard) return;
        SetVisible(true);
        if (needs != null && needsPaused)
        {
            foreach (UtilityNeeds.NeedType need in System.Enum.GetValues(typeof(UtilityNeeds.NeedType)))
                needs.ResumeNeedDecay(need);
            needsPaused = false;
        }
        movement.enabled = true;
        BeginReturnToCar();
    }

    public void BeginReturnToCar()
    {
        if (state == PassengerState.Completed || vehicle == null) return;

        if (hasTicket && state != PassengerState.OnBoard)
        {
            if (manager != null)
                manager.HandlePassengerRefund(this, ticketPricePaid);
            hasTicket = false;
        }

        CancelCheckIn();
        if (interactionController != null) interactionController.enabled = false;
        movement.enabled = true;
        SetVisible(true);
        state = PassengerState.WalkingToCar;
        carRetries = 0;
        nextCarRetry = 0f;
        TryReturnRoute();
    }

    private void TryReturnRoute()
    {
        if (vehicle == null || movement == null)
        {
            CompleteWithoutCar();
            return;
        }
        if (!vehicle.IsParked)
        {
            nextCarRetry = Time.time + 1f;
            return;
        }

        ParkingSpace parking = vehicle.CurrentParkingSpace;
        PlacedBuilding building = parking != null ? parking.GetComponent<PlacedBuilding>() : null;
        Vector3 preferred = vehicle.ParkingPosition + vehicle.transform.right * 1.5f;

        if (movement.TryMoveToNearby(preferred, vehicle.ParkingPosition, building))
        {
            nextCarRetry = float.PositiveInfinity;
            return;
        }

        carRetries++;
        nextCarRetry = Time.time + 1f;
        if (carRetries >= 8) CompleteWithoutCar();
    }

    private void HandleArrived()
    {
        if (state == PassengerState.WalkingToCheckIn)
        {
            if (checkInQueue != null && checkInQueue.QueueEnabled &&
                !checkInQueue.IsActive(this))
            {
                state = PassengerState.QueuedForCheckIn;
                return;
            }
            BeginCheckInInteraction();
        }
        else if (state == PassengerState.GatheringForBoarding)
        {
            if (manager != null) manager.HandlePassengerReachedGatheringSpot(this);
        }
        else if (state == PassengerState.WalkingToAircraft)
        {
            if (manager != null) manager.HandlePassengerReachedAircraft(this);
        }
        else if (state == PassengerState.WalkingToCar)
        {
            state = PassengerState.Completed;
            if (vehicle != null) vehicle.RequestDeparture();
            Destroy(gameObject);
        }
    }

    private void HandlePathFailed()
    {
        if (state == PassengerState.WalkingToCar)
        {
            nextCarRetry = Time.time + 1f;
            return;
        }
        if (state == PassengerState.WaitingInTerminal) return;
        if (manager != null) manager.HandlePassengerRouteFailed(this);
    }

    private void HandleCheckInFailure()
    {
        CancelCheckIn();
        state = PassengerState.Initializing;
        if (manager != null) manager.HandlePassengerRouteFailed(this);
    }

    public void CancelCheckIn()
    {
        if (checkInRoutine != null)
        {
            StopCoroutine(checkInRoutine);
            checkInRoutine = null;
        }

        if (checkInQueue != null && checkInQueue.QueueEnabled)
            checkInQueue.Leave(this);
        else if (reservationHeld && checkInProvider != null && checkInDefinition != null)
            checkInProvider.ReleaseReservation(this, checkInDefinition);

        reservationHeld = false;
        ClearCheckInReferences();
    }

    private void ClearCheckInReferences()
    {
        checkInQueue = null;
        checkInProvider = null;
        checkInDefinition = null;
    }

    private void SetVisible(bool visible)
    {
        if (renderers != null)
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = visible;
        if (colliders != null)
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null) colliders[i].enabled = visible;
    }

    private void CompleteWithoutCar()
    {
        state = PassengerState.Completed;
        if (vehicle != null && vehicle.IsParked) vehicle.RequestDeparture();
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        CancelCheckIn();
        if (subscribedFoundationSurface != null)
            subscribedFoundationSurface.FoundationSurfaceChanged -= HandleFoundationSurfaceChanged;
        subscribedFoundationSurface = null;
        if (movement != null)
        {
            movement.Arrived -= HandleArrived;
            movement.PathFailed -= HandlePathFailed;
        }
    }
}
