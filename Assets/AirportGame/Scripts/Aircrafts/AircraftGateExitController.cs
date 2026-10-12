
using System;
using UnityEngine;

public class AircraftGateExitController : MonoBehaviour
{
    public enum GateExitState
    {
        WaitingForTurnaround,
        MovingForwardToTaxiway,
        TurningAtGate,
        ReadyForTaxi
    }

    [Header("References")]
    [SerializeField]
    private AircraftTurnaroundController turnaroundController;

    [SerializeField]
    private AircraftFlightIdentity flightIdentity;

    [SerializeField]
    private AircraftGateRegistry gateRegistry;

    [SerializeField]
    private GridManager gridManager;

    [SerializeField]
    private TaxiwayNetwork taxiwayNetwork;

    [SerializeField]
    private GameTimeManager gameTimeManager;

    [Header("Forward Exit")]
    [Tooltip("Maximum sideways offset from the aircraft's forward line, in grid-cell widths.")]
    [Range(0.05f, 0.5f)]
    [SerializeField]
    private float maximumLateralOffsetInCells = 0.4f;

    [Tooltip("Speed for moving straight out of the gate, in world units per second.")]
    [Min(0.1f)]
    [SerializeField]
    private float forwardExitSpeed = 2f;

    [Min(0.01f)]
    [SerializeField]
    private float arrivalTolerance = 0.1f;

    [Header("Fallback Turn")]
    [Tooltip("Rotation speed for the 180-degree turn at the gate.")]
    [Min(1f)]
    [SerializeField]
    private float turnSpeedDegreesPerSecond = 10f;

    [Header("Runtime Status")]
    [SerializeField]
    private GateExitState currentState =
        GateExitState.WaitingForTurnaround;

    [SerializeField]
    private bool usedForwardExit;

    [SerializeField]
    private Vector2Int forwardExitCell;

    [SerializeField]
    private bool hasForwardExitCell;

    private Vector3 forwardExitTarget;
    private Quaternion targetRotation;
    private bool configurationWarningLogged;

    public GateExitState CurrentState => currentState;

    public bool IsExitingGate =>
        currentState == GateExitState.MovingForwardToTaxiway ||
        currentState == GateExitState.TurningAtGate;

    public bool ReadyForTaxi =>
        currentState == GateExitState.ReadyForTaxi;

    public bool UsedForwardExit => usedForwardExit;

    public bool HasForwardExitCell => hasForwardExitCell;

    public Vector2Int ForwardExitCell => forwardExitCell;

    public event Action<AircraftGateExitController> ReadyForTaxiReached;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        if (currentState == GateExitState.WaitingForTurnaround)
        {
            if (turnaroundController != null &&
                turnaroundController.ReadyToDepart)
            {
                BeginGateExit();
            }

            return;
        }

        if (currentState == GateExitState.ReadyForTaxi)
            return;

        float deltaTime = GetSimulationDeltaTime();

        if (deltaTime <= 0f)
            return;

        switch (currentState)
        {
            case GateExitState.MovingForwardToTaxiway:
                UpdateForwardExit(deltaTime);
                break;

            case GateExitState.TurningAtGate:
                UpdateGateTurn(deltaTime);
                break;
        }
    }

    private float GetSimulationDeltaTime()
    {
        if (gameTimeManager == null)
            return Time.unscaledDeltaTime;

        float speed = gameTimeManager.CurrentSpeedMultiplier;

        if (speed <= 0f)
            return 0f;

        return Time.unscaledDeltaTime * speed;
    }

    private void ResolveReferences()
    {
        if (turnaroundController == null)
        {
            turnaroundController =
                GetComponent<AircraftTurnaroundController>();
        }

        if (flightIdentity == null)
        {
            flightIdentity =
                GetComponent<AircraftFlightIdentity>();
        }

        if (gateRegistry == null)
        {
            gateRegistry =
                FindFirstObjectByType<AircraftGateRegistry>();
        }

        if (gridManager == null)
        {
            gridManager =
                FindFirstObjectByType<GridManager>();
        }

        if (taxiwayNetwork == null)
        {
            taxiwayNetwork =
                FindFirstObjectByType<TaxiwayNetwork>();
        }

        if (gameTimeManager == null)
        {
            gameTimeManager =
                FindFirstObjectByType<GameTimeManager>();
        }
    }

    /// <summary>
    /// Starts the forward exit if a valid connection exists.
    /// Otherwise, rotates the aircraft 180 degrees in place.
    /// </summary>
    public bool BeginGateExit()
    {
        if (currentState != GateExitState.WaitingForTurnaround)
            return false;

        ResolveReferences();

        if (turnaroundController == null ||
            !turnaroundController.ReadyToDepart)
        {
            return false;
        }

        if (gridManager == null ||
            taxiwayNetwork == null ||
            gateRegistry == null ||
            flightIdentity == null)
        {
            LogConfigurationWarning(
                "GridManager, TaxiwayNetwork, AircraftGateRegistry, " +
                "or AircraftFlightIdentity is missing."
            );

            return false;
        }

        if (gridManager.CellSize <= 0f)
        {
            LogConfigurationWarning(
                "GridManager has an invalid CellSize."
            );

            return false;
        }

        bool searchValid;

        bool foundForwardExit = TryFindForwardTaxiwayExit(
            out searchValid,
            out Vector2Int candidateExitCell,
            out Vector3 candidateTarget
        );

        if (!searchValid)
            return false;

        configurationWarningLogged = false;
        hasForwardExitCell = false;

        if (foundForwardExit)
        {
            forwardExitCell = candidateExitCell;
            hasForwardExitCell = true;
            forwardExitTarget = candidateTarget;
            usedForwardExit = true;

            currentState = GateExitState.MovingForwardToTaxiway;

            Debug.Log(
                $"{name}: Forward gate exit selected at grid cell " +
                $"{forwardExitCell}. Moving forward without turning.",
                this
            );

            return true;
        }

        // No usable forward taxiway connection.
        // Turn 180 degrees slowly at the parking position.
        usedForwardExit = false;

        targetRotation = Quaternion.Euler(
            0f,
            transform.eulerAngles.y + 180f,
            0f
        );

        currentState = GateExitState.TurningAtGate;

        Debug.Log(
            $"{name}: No usable forward taxiway connection. " +
            "Turning 180 degrees at the gate.",
            this
        );

        return true;
    }



    private bool TryFindForwardTaxiwayExit(
        out bool searchValid,
        out Vector2Int selectedCell,
        out Vector3 selectedTarget)
    {
        searchValid = false;
        selectedCell = default;
        selectedTarget = transform.position;

        if (!TryGetAssignedGate(
                out AircraftGate gate,
                out PlacedBuilding placedBuilding))
        {
            LogConfigurationWarning(
                "Could not resolve the assigned gate or its PlacedBuilding footprint."
            );

            return false;
        }

        int width = placedBuilding.Width;
        int depth = placedBuilding.Depth;

        if (width < 1 || depth < 1)
        {
            LogConfigurationWarning(
                "The assigned gate has an invalid footprint."
            );

            return false;
        }

        Vector3 forward = transform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.001f)
        {
            LogConfigurationWarning(
                "The aircraft has no valid forward direction."
            );

            return false;
        }

        forward.Normalize();

        Vector2Int direction = GetGridDirection(forward);

        float cellSize = gridManager.CellSize;
        float maximumLateralOffset =
            cellSize * maximumLateralOffsetInCells;

        Vector3 aircraftPosition = transform.position;

        float bestLateralDistance = float.MaxValue;
        float bestForwardDistance = float.MaxValue;

        // Search the entire front edge of the gate footprint.
        int edgeCellCount = direction.x != 0 ? depth : width;

        for (int i = 0; i < edgeCellCount; i++)
        {
            Vector2Int edgeCell;

            if (direction.x > 0)
            {
                edgeCell = new Vector2Int(
                    placedBuilding.Origin.x + width - 1,
                    placedBuilding.Origin.y + i
                );
            }
            else if (direction.x < 0)
            {
                edgeCell = new Vector2Int(
                    placedBuilding.Origin.x,
                    placedBuilding.Origin.y + i
                );
            }
            else if (direction.y > 0)
            {
                edgeCell = new Vector2Int(
                    placedBuilding.Origin.x + i,
                    placedBuilding.Origin.y + depth - 1
                );
            }
            else
            {
                edgeCell = new Vector2Int(
                    placedBuilding.Origin.x + i,
                    placedBuilding.Origin.y
                );
            }

            // Check the cell immediately beyond the gate's front edge.
            Vector2Int candidateCell = edgeCell + direction;

            // The complete taxiway footprint is registered in TaxiwayNetwork.
            // The candidate does not have to be a centerline cell.
            if (!taxiwayNetwork.IsTaxiway(candidateCell))
                continue;

            Vector3 candidateWorld =
                gridManager.GridToWorldCenter(candidateCell);

            candidateWorld.y = aircraftPosition.y;

            Vector3 offset = candidateWorld - aircraftPosition;
            offset.y = 0f;

            float forwardDistance = Vector3.Dot(offset, forward);

            if (forwardDistance <= 0f)
                continue;

            Vector3 lateralOffset =
                offset - forward * forwardDistance;

            float lateralDistance = lateralOffset.magnitude;

            // Only accept a taxiway that the aircraft can reach by moving
            // straight forward, without turning at the gate.
            if (lateralDistance > maximumLateralOffset)
                continue;

            bool isBetterCandidate =
                lateralDistance < bestLateralDistance - 0.001f ||
                (
                    Mathf.Abs(lateralDistance - bestLateralDistance) <= 0.001f &&
                    forwardDistance < bestForwardDistance
                );

            if (!isBetterCandidate)
                continue;

            bestLateralDistance = lateralDistance;
            bestForwardDistance = forwardDistance;
            selectedCell = candidateCell;

            // Keep the aircraft's heading unchanged during gate exit.
            selectedTarget =
                aircraftPosition + forward * forwardDistance;
        }

        searchValid = true;

        return bestLateralDistance < float.MaxValue;
    }

    private bool TryGetAssignedGate(
        out AircraftGate gate,
        out PlacedBuilding placedBuilding)
    {
        gate = null;
        placedBuilding = null;

        if (gateRegistry == null || flightIdentity == null)
            return false;

        gateRegistry.RefreshGates();

        string gateStableId =
            flightIdentity.HasActualGate
                ? flightIdentity.ActualGateStableId
                : flightIdentity.PlannedGateStableId;

        if (string.IsNullOrEmpty(gateStableId))
            return false;

        if (!gateRegistry.TryGetGate(gateStableId, out gate))
            return false;

        placedBuilding = gate.GetComponent<PlacedBuilding>();

        if (placedBuilding == null)
        {
            placedBuilding =
                gate.GetComponentInParent<PlacedBuilding>();
        }

        if (placedBuilding == null)
        {
            placedBuilding =
                gate.GetComponentInChildren<PlacedBuilding>(true);
        }

        return placedBuilding != null;
    }

    private bool HasCenterlineContinuation(
        Vector2Int candidateCell,
        Vector2Int forwardDirection)
    {
        foreach (Vector2Int connectedCell in
                 taxiwayNetwork.GetConnectedTaxiwayCells(candidateCell))
        {
            // Ignore a connection back toward the gate.
            if (connectedCell == candidateCell - forwardDirection)
                continue;

            if (taxiwayNetwork.IsCenterlineCell(connectedCell))
                return true;
        }

        return false;
    }

    private Vector2Int GetGridDirection(Vector3 forward)
    {
        // Established grid convention:
        // Vector2Int.x represents world X.
        // Vector2Int.y represents logical world Z.

        if (Mathf.Abs(forward.x) > Mathf.Abs(forward.z))
        {
            return new Vector2Int(
                forward.x >= 0f ? 1 : -1,
                0
            );
        }

        return new Vector2Int(
            0,
            forward.z >= 0f ? 1 : -1
        );
    }

    private void UpdateForwardExit(float deltaTime)
    {
        transform.position = Vector3.MoveTowards(
            transform.position,
            forwardExitTarget,
            forwardExitSpeed * deltaTime
        );

        if (Vector3.Distance(
                transform.position,
                forwardExitTarget) <= arrivalTolerance)
        {
            CompleteGateExit();
        }
    }

    private void UpdateGateTurn(float deltaTime)
    {
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            turnSpeedDegreesPerSecond * deltaTime
        );

        if (Quaternion.Angle(
                transform.rotation,
                targetRotation) <= 0.5f)
        {
            transform.rotation = targetRotation;
            CompleteGateExit();
        }
    }

    public void SetReadyForTaxiForTesting()
    {
        usedForwardExit = false;
        hasForwardExitCell = false;

        currentState = GateExitState.ReadyForTaxi;

        Debug.Log(
            $"{name}: Test aircraft initialized in ReadyForTaxi state.",
            this
        );

        ReadyForTaxiReached?.Invoke(this);
    }

    private void CompleteGateExit()
    {
        currentState = GateExitState.ReadyForTaxi;

        Debug.Log(
            $"{name}: Gate exit complete. Ready for taxi. " +
            $"Forward exit used: {usedForwardExit}.",
            this
        );

        ReadyForTaxiReached?.Invoke(this);
    }

    private void LogConfigurationWarning(string message)
    {
        if (configurationWarningLogged)
            return;

        configurationWarningLogged = true;

        Debug.LogWarning(
            $"{name}: AircraftGateExitController: {message}",
            this
        );
    }

    [ContextMenu("Debug/Begin Gate Exit")]
    private void DebugBeginGateExit()
    {
        BeginGateExit();
    }
}