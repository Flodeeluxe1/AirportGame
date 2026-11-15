using UnityEngine;

/// <summary>
/// Gives an otherwise-idle hobby pilot occasional short walks across Foundation
/// cells. Need-based and social activities always take priority.
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

    private UtilityNeeds needs;
    private PedestrianMovementController movement;
    private PilotInteractionController interactionController;
    private GridManager gridManager;
    private GridSurface gridSurface;
    private PedestrianPathfinder pathfinder;
    private GameTimeManager gameTimeManager;
    private bool subscribedToGameTime;
    private bool waitingAtDestination;
    private long nextWanderGameSecond;
    private long waitUntilGameSecond;
    private float nextEligibilityCheckTime;

    public string CurrentStatus => currentStatus;

    private void Awake()
    {
        needs = GetComponent<UtilityNeeds>();
        movement = GetComponent<PedestrianMovementController>();
        interactionController = GetComponent<PilotInteractionController>();
        ResolveWorldReferences();
        ResolveGameTimeManager();
    }

    private void OnEnable()
    {
        if (movement != null)
            movement.Arrived += HandleMovementArrived;

        ResolveGameTimeManager();
        nextWanderGameSecond = GetCurrentAbsoluteGameSecond() +
            Random.Range(minimumIdleIntervalSeconds, maximumIdleIntervalSeconds + 1);
    }

    private void OnDisable()
    {
        if (movement != null)
        {
            movement.Arrived -= HandleMovementArrived;
            if (movement.IsMoving)
                movement.Stop();
        }

        if (gameTimeManager != null && subscribedToGameTime)
            gameTimeManager.GameTimeAdvanced -= HandleGameTimeAdvanced;

        subscribedToGameTime = false;
        waitingAtDestination = false;
        currentStatus = "Disabled";
    }

    private void Update()
    {
        if (gameTimeManager == null)
            ResolveGameTimeManager();

        if (movement == null || interactionController == null || needs == null)
            return;

        if (!interactionController.CanWander)
        {
            if (movement.IsMoving)
                movement.Stop();

            waitingAtDestination = false;
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

        if (waitingAtDestination)
        {
            if (currentGameSecond < waitUntilGameSecond)
                return;

            waitingAtDestination = false;
            currentStatus = "Idle";
            nextWanderGameSecond = currentGameSecond +
                Random.Range(minimumIdleIntervalSeconds, maximumIdleIntervalSeconds + 1);
        }

        if (movement.IsMoving || currentGameSecond < nextWanderGameSecond)
            return;

        TryStartWander();
    }

    private void TryStartWander()
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

        for (int attempt = 0; attempt < destinationSearchAttempts; attempt++)
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

            float travelTime = movement.EstimateTravelTime(destination);
            if (float.IsInfinity(travelTime) || float.IsNaN(travelTime))
                continue;

            if (!movement.MoveTo(destination))
                continue;

            waitingAtDestination = false;
            currentStatus = "Wandering";
            return;
        }

        currentStatus = "No reachable Foundation destination";
        nextWanderGameSecond = GetCurrentAbsoluteGameSecond() + 30;
    }

    private void HandleMovementArrived()
    {
        if (currentStatus != "Wandering")
            return;

        waitingAtDestination = true;
        int pause = Random.Range(minimumPauseSeconds, maximumPauseSeconds + 1);
        waitUntilGameSecond = GetCurrentAbsoluteGameSecond() + pause;
        currentStatus = "Pausing after wandering";
    }

    private void StopWandering(string reason)
    {
        if (movement != null && movement.IsMoving)
            movement.Stop();

        waitingAtDestination = false;
        currentStatus = reason;
        nextWanderGameSecond = GetCurrentAbsoluteGameSecond() +
            Random.Range(minimumIdleIntervalSeconds, maximumIdleIntervalSeconds + 1);
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
        minimumIdleIntervalSeconds = Mathf.Max(1, minimumIdleIntervalSeconds);
        maximumIdleIntervalSeconds = Mathf.Max(minimumIdleIntervalSeconds, maximumIdleIntervalSeconds);
        minimumPauseSeconds = Mathf.Max(1, minimumPauseSeconds);
        maximumPauseSeconds = Mathf.Max(minimumPauseSeconds, maximumPauseSeconds);
        maximumSocialNeedValue = Mathf.Clamp(maximumSocialNeedValue, 0f, 100f);
    }
}
