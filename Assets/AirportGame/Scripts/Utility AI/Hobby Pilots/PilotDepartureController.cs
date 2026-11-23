using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Takes control of a hobby pilot when its aircraft is ready to depart.
/// The pilot returns to the aircraft, walks a complete circuit around it,
/// then walks to the boarding position and despawns through the callback.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PedestrianMovementController))]
public class PilotDepartureController : MonoBehaviour
{
    [Header("Walk-Around")]
    [Min(0.1f)]
    [SerializeField] private float aircraftClearance = 1.25f;
    [Min(0.1f)]
    [SerializeField] private float retryInterval = 2f;

    [Header("Runtime")]
    [SerializeField] private bool isDeparting;
    [SerializeField] private string currentStatus = "Available";
    [SerializeField] private int currentWaypoint;
    [SerializeField] private int totalWaypoints;

    private readonly List<Vector3> route = new List<Vector3>();
    private PedestrianMovementController movement;
    private Transform aircraft;
    private Action boardedCallback;
    private double nextRetryTime;
    private GameTimeManager gameTimeManager;
    private long retryEventId;

    public bool IsDeparting => isDeparting;
    public string CurrentStatus => currentStatus;

    private void Awake()
    {
        movement = GetComponent<PedestrianMovementController>();
        gameTimeManager = GameTimeManager.Instance;
    }

    private void OnEnable()
    {
        if (movement == null)
            movement = GetComponent<PedestrianMovementController>();
        if (gameTimeManager == null)
            gameTimeManager = GameTimeManager.Instance;

        if (movement != null)
            movement.Arrived += HandleMovementArrived;

        if (isDeparting && currentWaypoint < route.Count &&
            movement != null && !movement.IsMoving)
            ScheduleRetry();
    }

    private void OnDisable()
    {
        if (movement != null)
            movement.Arrived -= HandleMovementArrived;

        CancelRetryEvent();
    }

    private void Update()
    {
        if (!isDeparting || movement == null || movement.IsMoving ||
            currentWaypoint >= route.Count)
            return;

        if (gameTimeManager == null)
            gameTimeManager = GameTimeManager.Instance;

        // Keep a polling fallback only if the central scheduler is unavailable.
        if ((gameTimeManager == null || gameTimeManager.EventScheduler == null) &&
            GameTimeManager.SimulationTime >= nextRetryTime)
            MoveToCurrentWaypoint();
    }

    public bool BeginDeparture(
        Transform aircraftTransform,
        Vector3 boardingLocalOffset,
        Action onBoarded)
    {
        if (isDeparting || aircraftTransform == null || movement == null)
            return false;

        aircraft = aircraftTransform;
        boardedCallback = onBoarded;
        isDeparting = true;
        currentStatus = "Returning to aircraft";

        // End any current interaction, queue, conversation or idle wandering.
        // Their OnDisable handlers release reservations and stop their routes.
        DisablePilotActivityControllers();
        movement.Stop();

        if (!BuildWalkAroundRoute(boardingLocalOffset))
        {
            currentStatus = "Could not create aircraft walk-around route";
            return false;
        }

        currentWaypoint = 0;
        totalWaypoints = route.Count;
        nextRetryTime = GameTimeManager.SimulationTime;
        MoveToCurrentWaypoint();
        return true;
    }

    private void DisablePilotActivityControllers()
    {
        PilotGroupConversationController group =
            GetComponent<PilotGroupConversationController>();
        if (group != null)
            group.enabled = false;

        PilotSocialSeatingController seating =
            GetComponent<PilotSocialSeatingController>();
        if (seating != null)
        {
            seating.PrepareForDeparture();
            seating.enabled = false;
        }

        PilotSocialInteractionController social =
            GetComponent<PilotSocialInteractionController>();
        if (social != null)
            social.enabled = false;

        PilotInteractionController interactions =
            GetComponent<PilotInteractionController>();
        if (interactions != null)
            interactions.enabled = false;

        PilotWanderingController wandering =
            GetComponent<PilotWanderingController>();
        if (wandering != null)
            wandering.enabled = false;
    }

    private bool BuildWalkAroundRoute(Vector3 boardingLocalOffset)
    {
        route.Clear();

        BoxCollider aircraftCollider =
            aircraft.GetComponentInChildren<BoxCollider>(true);

        Bounds bounds = aircraftCollider != null
            ? aircraftCollider.bounds
            : new Bounds(aircraft.position, new Vector3(2f, 1f, 4f));

        float halfWidth = bounds.extents.x + aircraftClearance;
        float halfDepth = bounds.extents.z + aircraftClearance;
        float y = transform.position.y;

        Vector3 center = bounds.center;
        center.y = y;

        // Eight perimeter points keep each leg short and make the walk-around
        // visibly follow the aircraft's outline rather than cutting across it.
        Vector3[] perimeter =
        {
            center + new Vector3(0f, 0f, halfDepth),
            center + new Vector3(halfWidth, 0f, halfDepth),
            center + new Vector3(halfWidth, 0f, 0f),
            center + new Vector3(halfWidth, 0f, -halfDepth),
            center + new Vector3(0f, 0f, -halfDepth),
            center + new Vector3(-halfWidth, 0f, -halfDepth),
            center + new Vector3(-halfWidth, 0f, 0f),
            center + new Vector3(-halfWidth, 0f, halfDepth)
        };

        Vector3 boardingPosition = aircraft.TransformPoint(boardingLocalOffset);
        boardingPosition.y = y;

        int startIndex = FindNearestPerimeterPoint(perimeter, transform.position);
        int boardingIndex = FindNearestPerimeterPoint(perimeter, boardingPosition);

        // Approach the nearest side, complete one full clockwise circuit,
        // then continue to the side nearest the boarding position.
        for (int i = 0; i <= perimeter.Length; i++)
            route.Add(perimeter[(startIndex + i) % perimeter.Length]);

        if (boardingIndex != startIndex)
        {
            int index = (startIndex + 1) % perimeter.Length;

            while (true)
            {
                route.Add(perimeter[index]);

                if (index == boardingIndex)
                    break;

                index = (index + 1) % perimeter.Length;
            }
        }

        route.Add(boardingPosition);
        return route.Count > 0;
    }

    private int FindNearestPerimeterPoint(Vector3[] points, Vector3 position)
    {
        int bestIndex = 0;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < points.Length; i++)
        {
            float distance = Vector3.Distance(position, points[i]);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = i;
        }

        return bestIndex;
    }

    private void MoveToCurrentWaypoint()
    {
        if (currentWaypoint >= route.Count)
        {
            CompleteBoarding();
            return;
        }

        CancelRetryEvent();
        nextRetryTime = GameTimeManager.SimulationTime + retryInterval;

        if (!movement.MoveTo(route[currentWaypoint]))
        {
            currentStatus = $"Waiting for a path to walk-around point {currentWaypoint + 1}";
            ScheduleRetry();
            return;
        }

        currentStatus = currentWaypoint == route.Count - 1
            ? "Walking to aircraft boarding position"
            : $"Walk-around {currentWaypoint + 1}/{route.Count - 1}";
    }

    private void HandleMovementArrived()
    {
        if (!isDeparting)
            return;

        CancelRetryEvent();
        currentWaypoint++;

        if (currentWaypoint >= route.Count)
        {
            CompleteBoarding();
            return;
        }

        nextRetryTime = GameTimeManager.SimulationTime;
        MoveToCurrentWaypoint();
    }

    private void ScheduleRetry()
    {
        if (!isActiveAndEnabled || !isDeparting ||
            currentWaypoint >= route.Count || movement == null ||
            movement.IsMoving || retryEventId != 0)
            return;

        if (gameTimeManager == null)
            gameTimeManager = GameTimeManager.Instance;

        if (gameTimeManager == null || gameTimeManager.EventScheduler == null)
            return;

        retryEventId = gameTimeManager.EventScheduler.ScheduleAfter(
            Mathf.Max(0.1f, retryInterval),
            HandleScheduledRetry);
    }

    private void HandleScheduledRetry()
    {
        retryEventId = 0;

        if (!isActiveAndEnabled || !isDeparting ||
            movement == null || movement.IsMoving)
            return;

        MoveToCurrentWaypoint();
    }

    private void CancelRetryEvent()
    {
        if (retryEventId != 0 &&
            gameTimeManager != null && gameTimeManager.EventScheduler != null)
            gameTimeManager.EventScheduler.Cancel(retryEventId);

        retryEventId = 0;
    }

    private void CompleteBoarding()
    {
        if (!isDeparting)
            return;

        CancelRetryEvent();
        isDeparting = false;
        currentStatus = "Boarded";
        movement.Stop();

        Action callback = boardedCallback;
        boardedCallback = null;
        callback?.Invoke();
    }

    private void OnValidate()
    {
        aircraftClearance = Mathf.Max(0.1f, aircraftClearance);
        retryInterval = Mathf.Max(0.1f, retryInterval);
    }
}