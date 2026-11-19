using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gives an otherwise-idle hobby pilot occasional short walks across Foundation
/// cells. Pilots who spawn outside Foundation first walk to a random reachable
/// Foundation cell. Need-based and social activities always take priority.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(UtilityNeeds))]
[RequireComponent(typeof(PedestrianMovementController))]
[RequireComponent(typeof(PilotInteractionController))]
public class PilotWanderingController : MonoBehaviour
{
    [Header("Wandering Area")]
    [Min(1)]
    [SerializeField] private int minimumRadiusCells = 3;
    [Min(1)]
    [SerializeField] private int maximumRadiusCells = 6;
    [Min(1)]
    [SerializeField] private int destinationSearchAttempts = 40;

    [Tooltip("Maximum cached Foundation cells to test for a reachable entry path per attempt.")]
    [Min(1)]
    [SerializeField] private int maximumFoundationPathAttempts = 12;

    [Header("Foundation Entry")]
    [SerializeField] private bool enterFoundationWhenOutside = true;

    [Header("Room Preferences")]
    [Tooltip("Relative chance multiplier for wandering destinations inside a Pilots Lounge.")]
    [Min(1f)]
    [SerializeField] private float loungeDestinationWeight = 2f;

    [Tooltip("Additional multiplier for lounge destinations while the pilot is already inside the lounge.")]
    [Min(1f)]
    [SerializeField] private float loungeStayWeight = 2f;

    [Header("Timing (In-Game Seconds)")]
    [Min(1)]
    [SerializeField] private int minimumIdleIntervalSeconds = 20;
    [Min(1)]
    [SerializeField] private int maximumIdleIntervalSeconds = 40;
    [Min(1)]
    [SerializeField] private int minimumPauseSeconds = 5;
    [Min(1)]
    [SerializeField] private int maximumPauseSeconds = 15;

    [Header("Social Priority")]
    [Range(0f, 100f)]
    [SerializeField] private float maximumSocialNeedValue = 80f;

    [Header("Runtime")]
    [SerializeField] private string currentStatus = "Idle";

    // Each GridSurface gets its own shared cache, so pilots cannot overwrite
    // one another's Foundation data when resolving different grid instances.
    private sealed class FoundationCache
    {
        public readonly List<Vector2Int> cells = new List<Vector2Int>();
        public bool dirty = true;
    }

    private static readonly Dictionary<GridSurface, FoundationCache> FoundationCaches =
        new Dictionary<GridSurface, FoundationCache>();

    private GridSurface subscribedFoundationSurface;
    private FoundationCache foundationCache;
    private Coroutine foundationEntryRoutine;

    private UtilityNeeds needs;
    private PedestrianMovementController movement;
    private PilotInteractionController interactionController;
    private GridManager gridManager;
    private GridSurface gridSurface;
    private PedestrianPathfinder pathfinder;
    private RoomPurposeController roomPurposeController;
    private GameTimeManager gameTimeManager;
    private bool subscribedToGameTime;
    private bool waitingAtDestination;
    private bool ownsCurrentMovement;
    private bool enteringFoundation;
    private Vector3 wanderingDestination;
    private long nextWanderGameSecond;
    private long waitUntilGameSecond;
    private float nextEligibilityCheckTime;

    public string CurrentStatus => currentStatus;

    private void Awake()
    {
        needs = GetComponent<UtilityNeeds>();
        movement = GetComponent<PedestrianMovementController>();
        interactionController = GetComponent<PilotInteractionController>();
        roomPurposeController = FindFirstObjectByType<RoomPurposeController>();
        ResolveWorldReferences();
        ResolveGameTimeManager();
    }

    private void OnEnable()
    {
        if (movement != null)
            movement.Arrived += HandleMovementArrived;

        ResolveGameTimeManager();
        UpdateFoundationSurfaceSubscription();
        nextWanderGameSecond = GetCurrentAbsoluteGameSecond() +
            Random.Range(minimumIdleIntervalSeconds, maximumIdleIntervalSeconds + 1);
    }

    private void OnDisable()
    {
        if (foundationEntryRoutine != null)
        {
            StopCoroutine(foundationEntryRoutine);
            foundationEntryRoutine = null;
        }

        if (subscribedFoundationSurface != null)
            subscribedFoundationSurface.FoundationSurfaceChanged -= HandleFoundationSurfaceChanged;
        subscribedFoundationSurface = null;

        if (movement != null)
        {
            movement.Arrived -= HandleMovementArrived;
            StopOwnWanderMovement();
        }

        if (gameTimeManager != null && subscribedToGameTime)
            gameTimeManager.GameTimeAdvanced -= HandleGameTimeAdvanced;

        subscribedToGameTime = false;
        waitingAtDestination = false;
        enteringFoundation = false;
        currentStatus = "Disabled";
    }

    private void Update()
    {
        long performanceTimingStart = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
        if (gameTimeManager == null)
            ResolveGameTimeManager();

        if (movement == null || interactionController == null || needs == null)
            return;

        if (!interactionController.CanWander)
        {
            StopOwnWanderMovement();
            waitingAtDestination = false;
            enteringFoundation = false;
            currentStatus = "Another activity has priority";
            return;
        }

        if (needs.GetValue(UtilityNeeds.NeedType.Social) <= maximumSocialNeedValue)
        {
            StopWandering("Social need has priority");
            return;
        }

        if (Time.time < nextEligibilityCheckTime)
            return;

        nextEligibilityCheckTime = Time.time + 1f;

        if (interactionController.HasSuitableAvailableInteraction())
        {
            StopWandering("A need can be satisfied");
            return;
        }

        long currentGameSecond = GetCurrentAbsoluteGameSecond();

        if (enteringFoundation)
            return;

        if (waitingAtDestination)
        {
            if (currentGameSecond < waitUntilGameSecond)
                return;

            waitingAtDestination = false;
            currentStatus = "Idle";
            nextWanderGameSecond = currentGameSecond +
                Random.Range(minimumIdleIntervalSeconds, maximumIdleIntervalSeconds + 1);
        }

        if (enterFoundationWhenOutside && !IsOnFoundation())
        {
            if (movement.IsMoving)
                return;

            TryEnterFoundation();
            return;
        }

        if (movement.IsMoving || currentGameSecond < nextWanderGameSecond)
            return;

        TryStartWander();
    
        }
        finally
        {
            PerformanceMonitoringManager.RecordTrackedMethod("PilotWanderingController.Update", performanceTimingStart);
        }
    }

    private bool IsOnFoundation()
    {
        long performanceTimingStart = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
        if (gridManager == null || gridSurface == null)
            ResolveWorldReferences();

        if (gridManager == null || gridSurface == null)
            return false;

        Vector2Int currentCell = gridManager.WorldToGrid(transform.position);
        return gridManager.IsInsideGrid(currentCell) &&
               gridSurface.GetSurface(currentCell) == BuildSurface.Foundation;
    
        }
        finally
        {
            PerformanceMonitoringManager.RecordTrackedMethod("PilotWanderingController.IsOnFoundation", performanceTimingStart);
        }
    }

    private void TryEnterFoundation()
    {
        if (foundationEntryRoutine != null)
            return;

        enteringFoundation = true;
        foundationEntryRoutine = StartCoroutine(TryEnterFoundationRoutine());
    }

    private IEnumerator TryEnterFoundationRoutine()
    {
        long performanceTimingStart = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
        if (gridManager == null || gridSurface == null || pathfinder == null)
            ResolveWorldReferences();

        if (gridManager == null || gridSurface == null || pathfinder == null)
        {
            currentStatus = "Foundation entry unavailable: grid references missing";
            nextWanderGameSecond = GetCurrentAbsoluteGameSecond() + 30;
            enteringFoundation = false;
            foundationEntryRoutine = null;
            yield break;
        }

        UpdateFoundationSurfaceSubscription();
        RefreshFoundationCacheIfNeeded();

        if (foundationCache == null || foundationCache.cells.Count == 0)
        {
            currentStatus = "No Foundation cells available";
            nextWanderGameSecond = GetCurrentAbsoluteGameSecond() + 30;
            enteringFoundation = false;
            foundationEntryRoutine = null;
            yield break;
        }

        int candidateCount = foundationCache.cells.Count;
        int attempts = Mathf.Min(maximumFoundationPathAttempts, candidateCount);
        int startIndex = Random.Range(0, candidateCount);

        // Spread path searches across frames instead of allowing a newly
        // spawned pilot to run many expensive A* searches in one Update.
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            int candidateIndex = (startIndex + attempt) % candidateCount;
            Vector2Int candidateCell = foundationCache.cells[candidateIndex];

            if (pathfinder.IsWalkable(candidateCell))
            {
                Vector3 destination = gridManager.GridToWorldCenter(candidateCell);
                destination.y = transform.position.y;

                if (movement.MoveTo(destination))
                {
                    wanderingDestination = destination;
                    ownsCurrentMovement = true;
                    enteringFoundation = false;
                    waitingAtDestination = false;
                    currentStatus = "Entering foundation";
                    foundationEntryRoutine = null;
                    yield break;
                }
            }

            yield return null;
        }

        currentStatus = "No reachable Foundation destination";
        nextWanderGameSecond = GetCurrentAbsoluteGameSecond() + 30;
        enteringFoundation = false;
        foundationEntryRoutine = null;
    
        }
        finally
        {
            PerformanceMonitoringManager.RecordTrackedMethod("PilotWanderingController.TryEnterFoundationRoutine", performanceTimingStart);
        }
    }
    private void TryStartWander()
    {
        long performanceTimingStart = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
        if (gridManager == null || gridSurface == null || pathfinder == null)
            ResolveWorldReferences();

        if (gridManager == null || gridSurface == null || pathfinder == null)
        {
            currentStatus = "Wandering unavailable: grid references missing";
            nextWanderGameSecond = GetCurrentAbsoluteGameSecond() + 30;
            return;
        }

        Vector2Int origin = gridManager.WorldToGrid(transform.position);

        if (roomPurposeController == null)
            roomPurposeController = FindFirstObjectByType<RoomPurposeController>();

        bool currentlyInsideLounge = roomPurposeController != null &&
            roomPurposeController.IsPositionInPilotsLounge(transform.position);

        float loungeWeight = Mathf.Max(1f, loungeDestinationWeight);
        float stayWeight = currentlyInsideLounge
            ? Mathf.Max(1f, loungeStayWeight)
            : 1f;
        float maximumCandidateWeight = loungeWeight * stayWeight;

        for (int attempt = 0; attempt < destinationSearchAttempts; attempt++)
        {
            long candidateTimingStart = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
            int radius = Random.Range(minimumRadiusCells, maximumRadiusCells + 1);
            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector2Int offset = new Vector2Int(
                Mathf.RoundToInt(Mathf.Cos(angle) * radius),
                Mathf.RoundToInt(Mathf.Sin(angle) * radius));

            if (offset == Vector2Int.zero)
                continue;

            Vector2Int candidateCell = origin + offset;

            if (!gridManager.IsInsideGrid(candidateCell) ||
                gridSurface.GetSurface(candidateCell) != BuildSurface.Foundation ||
                !pathfinder.IsWalkable(candidateCell))
                continue;

            Vector3 destination = gridManager.GridToWorldCenter(candidateCell);
            destination.y = transform.position.y;

            bool candidateInsideLounge = roomPurposeController != null &&
                roomPurposeController.IsPositionInPilotsLounge(destination);

            float candidateWeight = candidateInsideLounge
                ? loungeWeight * stayWeight
                : 1f;

            // Weighted rejection sampling biases the random destinations without
            // changing the pathfinder or adding extra path searches.
            if (Random.value > candidateWeight / maximumCandidateWeight)
                continue;

            // MoveTo performs the authoritative path search. Avoid calculating
            // the same route once for estimation and again for movement.
            if (!movement.MoveTo(destination))
                continue;

            wanderingDestination = destination;
            ownsCurrentMovement = true;
            waitingAtDestination = false;
            currentStatus = "Wandering";
            return;
        
            }
            finally
            {
                PerformanceMonitoringManager.RecordTrackedMethod(
                    "PilotWanderingController.WanderCandidateCheck", candidateTimingStart);
            }
        }

        currentStatus = "No reachable Foundation destination";
        nextWanderGameSecond = GetCurrentAbsoluteGameSecond() + 30;
    
        }
        finally
        {
            PerformanceMonitoringManager.RecordTrackedMethod("PilotWanderingController.TryStartWander", performanceTimingStart);
        }
    }

    private void HandleMovementArrived()
    {
        if (!ownsCurrentMovement ||
            (currentStatus != "Wandering" && currentStatus != "Entering foundation"))
            return;

        bool wasEnteringFoundation = enteringFoundation;
        ownsCurrentMovement = false;
        enteringFoundation = false;
        waitingAtDestination = true;

        int pause = Random.Range(minimumPauseSeconds, maximumPauseSeconds + 1);
        waitUntilGameSecond = GetCurrentAbsoluteGameSecond() + pause;
        currentStatus = wasEnteringFoundation
            ? "Pausing after entering foundation"
            : "Pausing after wandering";
    }

    private void StopWandering(string reason)
    {
        StopOwnWanderMovement();
        enteringFoundation = false;
        waitingAtDestination = false;
        currentStatus = reason;
        nextWanderGameSecond = GetCurrentAbsoluteGameSecond() +
            Random.Range(minimumIdleIntervalSeconds, maximumIdleIntervalSeconds + 1);
    }

    private void StopOwnWanderMovement()
    {
        if (!ownsCurrentMovement)
            return;

        // Another controller may have replaced the route since wandering began.
        // Never stop a route that no longer belongs to this controller.
        if (movement != null && movement.IsMoving &&
            Vector3.Distance(movement.Destination, wanderingDestination) <= 0.1f)
        {
            movement.Stop();
        }

        ownsCurrentMovement = false;
    }

    private void ResolveWorldReferences()
    {
        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (gridManager != null && gridSurface == null)
            gridSurface = gridManager.GetComponent<GridSurface>();

        if (gridSurface == null)
            gridSurface = FindFirstObjectByType<GridSurface>();

        if (pathfinder == null)
            pathfinder = FindFirstObjectByType<PedestrianPathfinder>();

        UpdateFoundationSurfaceSubscription();
    }

    private void UpdateFoundationSurfaceSubscription()
    {
        if (subscribedFoundationSurface == gridSurface)
            return;

        if (subscribedFoundationSurface != null)
            subscribedFoundationSurface.FoundationSurfaceChanged -= HandleFoundationSurfaceChanged;

        subscribedFoundationSurface = gridSurface;
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
        // The clock can be rewound while testing; reset the next wander deadline.
        if (currentSecond < previousSecond)
        {
            nextWanderGameSecond = currentSecond +
                Random.Range(minimumIdleIntervalSeconds, maximumIdleIntervalSeconds + 1);
        }
    }

    private long GetCurrentAbsoluteGameSecond()
    {
        if (gameTimeManager == null)
            return 0;

        return (gameTimeManager.CurrentDay - 1) * 86400L +
               gameTimeManager.GetGameTimeSeconds();
    }

    private void OnValidate()
    {
        minimumRadiusCells = Mathf.Max(1, minimumRadiusCells);
        maximumRadiusCells = Mathf.Max(minimumRadiusCells, maximumRadiusCells);
        destinationSearchAttempts = Mathf.Max(1, destinationSearchAttempts);
        maximumFoundationPathAttempts = Mathf.Max(1, maximumFoundationPathAttempts);
        loungeDestinationWeight = Mathf.Max(1f, loungeDestinationWeight);
        loungeStayWeight = Mathf.Max(1f, loungeStayWeight);
        minimumIdleIntervalSeconds = Mathf.Max(1, minimumIdleIntervalSeconds);
        maximumIdleIntervalSeconds = Mathf.Max(minimumIdleIntervalSeconds, maximumIdleIntervalSeconds);
        minimumPauseSeconds = Mathf.Max(1, minimumPauseSeconds);
        maximumPauseSeconds = Mathf.Max(minimumPauseSeconds, maximumPauseSeconds);
        maximumSocialNeedValue = Mathf.Clamp(maximumSocialNeedValue, 0f, 100f);
    }
}