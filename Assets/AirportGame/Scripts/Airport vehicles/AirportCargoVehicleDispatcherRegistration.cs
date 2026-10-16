using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AirportCargoVehicleController))]
public class AirportCargoVehicleDispatcherRegistration : MonoBehaviour
{

    public bool HasActiveAssignment => waitingForPickupArrival;
    private AirportCargoVehicleController vehicle;

    private string assignedOperationId;
    private bool waitingForPickupArrival;

    private void Awake()
    {
        vehicle = GetComponent<AirportCargoVehicleController>();
    }

    private void Start()
    {
        if (AirportBaggageDispatcher.Instance == null)
        {
            Debug.LogError(
                $"{name}: Cannot register cargo vehicle because " +
                "AirportBaggageDispatcher was not found.",
                this
            );

            return;
        }

        AirportBaggageDispatcher.Instance.RegisterVehicle(vehicle);
    }

    private void Update()
    {
        if (!waitingForPickupArrival || vehicle == null)
            return;

        if (vehicle.IsMoving)
            return;

        if (vehicle.MovementStatus == "Arrived")
        {
            waitingForPickupArrival = false;

            if (AirportBaggageDispatcher.Instance != null)
            {
                AirportBaggageDispatcher.Instance
                    .NotifyVehicleArrivedAtPickup(
                        vehicle,
                        assignedOperationId
                    );
            }

            return;
        }

        if (vehicle.MovementStatus.StartsWith("Failed:"))
        {
            waitingForPickupArrival = false;

            if (AirportBaggageDispatcher.Instance != null)
            {
                AirportBaggageDispatcher.Instance.NotifyAssignmentFailed(
                    vehicle,
                    assignedOperationId,
                    vehicle.MovementStatus
                );
            }
        }
    }


    public bool MoveToGate(
        AircraftGate actualGate,
        string operationId)
    {
        if (vehicle == null)
            vehicle = GetComponent<AirportCargoVehicleController>();

        if (vehicle == null || actualGate == null)
            return false;

        if (string.IsNullOrWhiteSpace(operationId))
            return false;

        bool interruptingReturn = vehicle.IsReturningHome;

        if (interruptingReturn)
            vehicle.CancelReturnHome();

        if (vehicle.IsMoving && !interruptingReturn)
        {
            Debug.LogWarning(
                $"{name}: Cannot start the gate assignment because " +
                "the vehicle is already moving on another route.",
                this
            );

            return false;
        }

        Transform stopPoint = FindStopPoint(actualGate.transform);

        if (stopPoint == null)
        {
            Debug.LogError(
                $"{name}: Gate '{actualGate.name}' has no child named " +
                "'CargoVehicleStopPoint'.",
                actualGate
            );

            return false;
        }

        assignedOperationId = operationId;
        waitingForPickupArrival = true;

        Debug.Log(
            $"{name}: Driving to CargoVehicleStopPoint at gate " +
            $"'{actualGate.StableId}' for request {operationId}.",
            this
        );

        bool routeStarted = vehicle.MoveTo(stopPoint.position);

        if (!routeStarted)
        {
            waitingForPickupArrival = false;
            assignedOperationId = string.Empty;
            return false;
        }

        return true;
    }


    private Transform FindStopPoint(Transform gateRoot)
    {
        Transform[] children =
            gateRoot.GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            if (child != gateRoot &&
                child.name == "CargoVehicleStopPoint")
            {
                return child;
            }
        }

        return null;
    }

    private void OnDestroy()
    {
        waitingForPickupArrival = false;

        if (vehicle == null)
            return;

        if (AirportBaggageDispatcher.Instance != null)
        {
            AirportBaggageDispatcher.Instance.UnregisterVehicle(vehicle);
        }
    }
}