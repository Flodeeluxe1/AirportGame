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
    private float nextRetryTime;

    public bool IsDeparting => isDeparting;
    public string CurrentStatus => currentStatus;

    private void Awake()
    {
        movement = GetComponent<PedestrianMovementController>();
    }

    private void OnEnable()
    {
        if (movement == null)
            movement = GetComponent<PedestrianMovementController>();

        if (movement != null)
            movement.Arrived += HandleMovementArrived;
    }

    private void OnDisable()
    {
        if (movement != null)
            movement.Arrived -= HandleMovementArrived;
    }

    private void Update()
    {
        if (!isDeparting || movement == null || movement.IsMoving)
            return;

        if (currentWaypoint < route.Count && Time.time >= nextRetryTime)
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
        nextRetryTime = Time.time;
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
            seating.enabled = false;

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

        int index = (startIndex + 1) % perimeter.Length;
        while (index != boardingIndex)
        {
            route.Add(perimeter[index]);
            index = (index + 1) % perimeter.Length;
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

        nextRetryTime = Time.time + retryInterval;

        if (!movement.MoveTo(route[currentWaypoint]))
        {
            currentStatus = $"Waiting for a path to walk-around point {currentWaypoint + 1}";
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

        currentWaypoint++;

        if (currentWaypoint >= route.Count)
        {
            CompleteBoarding();
            return;
        }

        nextRetryTime = Time.time;
        MoveToCurrentWaypoint();
    }

    private void CompleteBoarding()
    {
        if (!isDeparting)
            return;

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