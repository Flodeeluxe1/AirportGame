
using System.Collections.Generic;
using UnityEngine;

public class TrainingFlightController : MonoBehaviour
{
    private enum TrainingState
    {
        Inactive,
        WaitingForTakeoff,
        FlyingCircuit,
        Landing,
        TaxiingBackToSchool,
        Completed,
        Failed
    }

    [Header("Lesson")]
    [SerializeField, Min(1)]
    private int requiredCircuits = 5;

    [SerializeField, Min(1f)]
    private float circuitExtension = 150f;

    [SerializeField, Min(1f)]
    private float circuitWidth = 100f;

    [SerializeField, Min(1f)]
    private float circuitSpeed = 8f;

    [SerializeField, Min(0.1f)]
    private float waypointArrivalDistance = 2f;

    [Header("Runtime Status")]
    [SerializeField] private int completedCircuits;
    [SerializeField] private bool departureRequested;
    [SerializeField]
    private TrainingState currentState =
        TrainingState.Inactive;
    [SerializeField]
    private string debugStatus =
        "Waiting for Flight School Manager.";

    private AircraftFlightIdentity flightIdentity;
    private AircraftDepartureTaxiController departureController;
    private AircraftTakeoffController takeoffController;
    private AircraftLandingController landingController;
    private HobbyAircraftTaxiController taxiController;

    private readonly List<Vector3> circuitWaypoints = new();

    private int currentWaypointIndex;
    private bool landingCounted;

    public bool DepartureRequested => departureRequested;

    public int CompletedCircuits => completedCircuits;

    public int RequiredCircuits => requiredCircuits;

    public bool IsLessonComplete =>
        currentState == TrainingState.Completed;

    public bool HasFailed =>
        currentState == TrainingState.Failed;

    public string DebugStatus => debugStatus;

    private void Awake()
    {
        flightIdentity = GetComponent<AircraftFlightIdentity>();
        departureController =
            GetComponent<AircraftDepartureTaxiController>();
        takeoffController =
            GetComponent<AircraftTakeoffController>();
        landingController =
            GetComponent<AircraftLandingController>();
        taxiController =
            GetComponent<HobbyAircraftTaxiController>();
    }

    private void Update()
    {
        if (currentState == TrainingState.Inactive ||
            currentState == TrainingState.Completed ||
            currentState == TrainingState.Failed)
        {
            return;
        }

        if (!HasRequiredComponents())
        {
            FailTraining(
                "A required training, taxi, landing, or takeoff component is missing."
            );

            return;
        }

        switch (currentState)
        {
            case TrainingState.WaitingForTakeoff:
                UpdateWaitingForTakeoff();
                break;

            case TrainingState.FlyingCircuit:
                UpdateFlyingCircuit();
                break;

            case TrainingState.Landing:
                UpdateLanding();
                break;

            case TrainingState.TaxiingBackToSchool:
                UpdateTaxiingBackToSchool();
                break;
        }
    }

    public bool StartTrainingLesson()
    {
        if (currentState != TrainingState.Inactive)
        {
            Debug.LogWarning(
                $"{name}: This training lesson has already been started.",
                this
            );

            return false;
        }

        if (flightIdentity == null ||
            !flightIdentity.IsInitialized ||
            !flightIdentity.IsTrainingFlight)
        {
            Debug.LogWarning(
                $"{name}: Initialize this aircraft as a training flight " +
                "before starting its lesson.",
                this
            );

            return false;
        }

        if (!HasRequiredComponents())
        {
            FailTraining(
                "Cannot start the lesson because required components are missing."
            );

            return false;
        }

        completedCircuits = 0;
        landingCounted = false;

        RequestNextCircuitDeparture();

        currentState = TrainingState.WaitingForTakeoff;
        debugStatus =
            "Lesson started. Waiting for departure taxi and takeoff.";

        Debug.Log(
            $"{name}: Training lesson started. " +
            $"Required circuits: {requiredCircuits}.",
            this
        );

        return true;
    }

    private bool HasRequiredComponents()
    {
        return flightIdentity != null &&
               departureController != null &&
               takeoffController != null &&
               landingController != null &&
               taxiController != null;
    }

    private void RequestNextCircuitDeparture()
    {
        departureRequested = true;

        GetComponent<TrainingFlightController>()
            .departureRequested = true;

        // The departure taxi controller reads this request.
        debugStatus = "Departure requested. Taxiing toward runway.";
    }

    public void ClearDepartureRequest()
    {
        departureRequested = false;
    }

    private void UpdateWaitingForTakeoff()
    {
        if (!takeoffController.IsCompleted)
            return;

        if (!BuildCircuitWaypoints())
        {
            FailTraining(
                "Could not build the circuit because runway geometry is unavailable."
            );

            return;
        }

        currentWaypointIndex = 0;
        currentState = TrainingState.FlyingCircuit;

        debugStatus =
            $"Takeoff complete. Flying circuit " +
            $"{completedCircuits + 1} of {requiredCircuits}.";

        Debug.Log(
            $"{name}: Takeoff complete. Beginning training circuit " +
            $"{completedCircuits + 1}/{requiredCircuits}.",
            this
        );
    }

    private bool BuildCircuitWaypoints()
    {
        circuitWaypoints.Clear();

        AircraftRunway runway =
            departureController.SelectedRunway;

        if (runway == null)
            return false;

        GridManager gridManager =
            FindFirstObjectByType<GridManager>();

        if (gridManager == null ||
            !runway.TryGetGeometry(
                gridManager,
                out Vector3 runwayCenter,
                out Vector3 runwayAxis,
                out float runwayLength,
                out float runwaySurfaceY))
        {
            return false;
        }

        Vector3 direction =
            departureController.TakeoffDirection;

        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
        {
            runwayAxis.y = 0f;

            if (runwayAxis.sqrMagnitude < 0.0001f)
                return false;

            direction = runwayAxis;
        }

        direction.Normalize();

        Vector3 crosswindDirection =
            Vector3.Cross(Vector3.up, direction).normalized;

        float downwindDistance =
            runwayLength * 0.5f + circuitExtension;

        // A simple rectangular training circuit:
        // upwind, crosswind, downwind, then base.
        Vector3 upwindPoint =
            runwayCenter + direction * downwindDistance;

        Vector3 crosswindPoint =
            upwindPoint + crosswindDirection * circuitWidth;

        Vector3 downwindPoint =
            runwayCenter -
            direction * downwindDistance +
            crosswindDirection * circuitWidth;

        Vector3 basePoint =
            runwayCenter -
            direction * downwindDistance;

        float flightAltitude = runwaySurfaceY + 20f;

        upwindPoint.y = flightAltitude;
        crosswindPoint.y = flightAltitude;
        downwindPoint.y = flightAltitude;
        basePoint.y = flightAltitude;

        circuitWaypoints.Add(upwindPoint);
        circuitWaypoints.Add(crosswindPoint);
        circuitWaypoints.Add(downwindPoint);
        circuitWaypoints.Add(basePoint);

        return true;
    }

    private void UpdateFlyingCircuit()
    {
        if (currentWaypointIndex >= circuitWaypoints.Count)
        {
            BeginLanding();
            return;
        }

        Vector3 target =
            circuitWaypoints[currentWaypointIndex];

        Vector3 toTarget =
            target - transform.position;

        if (toTarget.magnitude <= waypointArrivalDistance)
        {
            currentWaypointIndex++;

            if (currentWaypointIndex >= circuitWaypoints.Count)
                BeginLanding();

            return;
        }

        Vector3 direction = toTarget.normalized;

        if (direction.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(direction, Vector3.up);

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                30f * Time.deltaTime
            );
        }

        transform.position = Vector3.MoveTowards(
            transform.position,
            target,
            circuitSpeed * Time.deltaTime
        );
    }

    private void BeginLanding()
    {
        if (!landingController.BeginTrainingLanding())
        {
            FailTraining(
                "The landing controller could not begin the training landing."
            );

            return;
        }

        landingCounted = false;
        currentState = TrainingState.Landing;

        debugStatus =
            $"Circuit {completedCircuits + 1}: approaching runway to land.";

        Debug.Log(
            $"{name}: Circuit pattern complete. Beginning landing.",
            this
        );
    }

    private void UpdateLanding()
    {
        if (!landingController.IsLandingComplete)
            return;

        if (!landingCounted)
        {
            completedCircuits++;
            landingCounted = true;

            Debug.Log(
                $"{name}: Training landing complete. " +
                $"Circuits completed: {completedCircuits}/{requiredCircuits}.",
                this
            );
        }

        currentState = TrainingState.TaxiingBackToSchool;
        debugStatus =
            "Landing complete. Taxiing back to the school parking spot.";
    }

    private void UpdateTaxiingBackToSchool()
    {
        if (!taxiController.HasArrivedAtParking)
            return;

        if (completedCircuits >= requiredCircuits)
        {
            currentState = TrainingState.Completed;
            departureRequested = false;

            debugStatus =
                $"Lesson complete: {completedCircuits} circuits. " +
                "Ready for Flight School Manager payout.";

            Debug.Log(
                $"{name}: TRAINING LESSON COMPLETE | " +
                $"Circuits: {completedCircuits}/{requiredCircuits}. " +
                "The Flight School Manager can now pay the lesson reward.",
                this
            );

            return;
        }

        // Prepare both controllers for another departure.
        ClearDepartureRequest();

        if (!takeoffController.ResetForTrainingCircuit())
        {
            FailTraining(
                "Could not reset the takeoff controller for the next circuit."
            );

            return;
        }

        if (!departureController.ResetForTrainingCircuit())
        {
            FailTraining(
                "Could not reset the departure controller for the next circuit."
            );

            return;
        }

        RequestNextCircuitDeparture();

        currentState = TrainingState.WaitingForTakeoff;
        debugStatus =
            $"Returned to school parking. Preparing circuit " +
            $"{completedCircuits + 1}/{requiredCircuits}.";

        Debug.Log(
            $"{name}: Ready for training circuit " +
            $"{completedCircuits + 1}/{requiredCircuits}.",
            this
        );
    }

    private void FailTraining(string reason)
    {
        currentState = TrainingState.Failed;
        departureRequested = false;
        debugStatus = reason;

        Debug.LogError(
            $"{name}: Training lesson stopped. {reason}",
            this
        );
    }
}
