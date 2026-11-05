using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AirportCargoVehicleController))]
public class AirportCargoVehicleDispatcherRegistration : MonoBehaviour
{

    public bool HasActiveAssignment => waitingForPickupArrival;
    private AirportCargoVehicleController vehicle;

    private string assignedOperationId;
    private bool waitingForPickupArrival;
    private bool registeredWithDispatcher;

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

        SyncDispatcherAvailability();
    }

    private void Update()
    {
        SyncDispatcherAvailability();

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


    private void SyncDispatcherAvailability()
    {
        if (vehicle == null)
            return;

        AirportBaggageDispatcher dispatcher =
            AirportBaggageDispatcher.Instance;

        if (dispatcher == null)
            return;

        bool hasTrailerCapacity = vehicle.MaximumCargoCapacity > 0;

        if (hasTrailerCapacity && !registeredWithDispatcher)
        {
            dispatcher.RegisterVehicle(vehicle);
            registeredWithDispatcher = true;
        }
        else if (!hasTrailerCapacity && registeredWithDispatcher)
        {
            dispatcher.UnregisterVehicle(vehicle);
            registeredWithDispatcher = false;
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

        bool routeStarted = vehicle.MoveTo(
            stopPoint.position,
            actualGate.gameObject);

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

        if (registeredWithDispatcher &&
            AirportBaggageDispatcher.Instance != null)
        {
            AirportBaggageDispatcher.Instance.UnregisterVehicle(vehicle);
            registeredWithDispatcher = false;
        }
    }
}