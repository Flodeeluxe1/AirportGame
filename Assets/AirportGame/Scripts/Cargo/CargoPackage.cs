using UnityEngine;

public enum CargoDirection
{
    Incoming,
    Outgoing
}

public enum CargoPackageState
{
    Staged,
    Reserved,
    InTransit,
    Delivered
}

[DisallowMultipleComponent]
public class CargoPackage : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool logToConsole = false;
{
    [Header("Package Identity")]
    [SerializeField] private string packageId;

    [Header("Cargo Classification")]
    [SerializeField] private CargoDirection direction;
    [SerializeField]
    private CargoPackageState state =
        CargoPackageState.Staged;

    [Header("Flight Assignment")]
    [Tooltip(
        "Required for outgoing cargo. Must match the intended " +
        "aircraft's AircraftFlightIdentity.OperationId."
    )]
    [SerializeField] private string flightOperationId;

    [Header("Current Location")]
    [SerializeField] private CargoZone currentZone;

    public string PackageId => packageId;

    public CargoDirection Direction => direction;

    public CargoPackageState State => state;

    public string FlightOperationId => flightOperationId;

    public CargoZone CurrentZone => currentZone;

    public bool IsAvailableForCollection =>
        state == CargoPackageState.Staged &&
        currentZone != null;

    public void Initialize(
        string id,
        CargoDirection cargoDirection,
        string assignedFlightOperationId,
        CargoZone startingZone)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            LogError(
                $"{nameof(CargoPackage)} on {name}: " +
                "Package ID cannot be empty.",
                this
            );
            return;
        }

        if (cargoDirection == CargoDirection.Outgoing &&
            string.IsNullOrWhiteSpace(assignedFlightOperationId))
        {
            LogError(
                $"{nameof(CargoPackage)} on {name}: " +
                "Outgoing cargo requires a flight operation ID.",
                this
            );
            return;
        }

        packageId = id;
        direction = cargoDirection;
        flightOperationId = assignedFlightOperationId ?? string.Empty;

        currentZone = startingZone;
        state = CargoPackageState.Staged;
    }

    public bool IsForFlight(string operationId)
    {
        return direction == CargoDirection.Outgoing &&
               !string.IsNullOrWhiteSpace(operationId) &&
               flightOperationId == operationId;
    }

    public bool TryReserve()
    {
        if (state != CargoPackageState.Staged ||
            currentZone == null)
        {
            return false;
        }

        state = CargoPackageState.Reserved;
        return true;
    }

    public void SetInTransit()
    {
        if (state != CargoPackageState.Reserved)
        {
            LogWarning(
                $"{name}: Cannot move cargo into transit " +
                "unless it has been reserved.",
                this
            );
            return;
        }

        state = CargoPackageState.InTransit;
        currentZone = null;
    }

    public void SetDelivered()
    {
        state = CargoPackageState.Delivered;
        currentZone = null;
    }

    public void ReturnToZone(CargoZone zone)
    {
        if (zone == null)
        {
            LogError(
                $"{name}: Cannot return cargo to a missing zone.",
                this
            );
            return;
        }

        currentZone = zone;
        state = CargoPackageState.Staged;
    }

    private void Log(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.Log(message, context); else Debug.Log(message); }
    private void LogWarning(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogWarning(message, context); else Debug.LogWarning(message); }
    private void LogError(object message, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogError(message, context); else Debug.LogError(message); }
    private void LogException(System.Exception exception, UnityEngine.Object context = null) { if (!logToConsole) return; if (context != null) Debug.LogException(exception, context); else Debug.LogException(exception); }
}