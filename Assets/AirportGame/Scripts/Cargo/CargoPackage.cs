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

    // Retained as serialized backing fields for compatibility with existing
    // prefabs and scene objects. CargoPackageData is the runtime data owner.
    [Header("Package Identity")]
    [SerializeField] private string packageId;

    [Header("Cargo Classification")]
    [SerializeField] private CargoDirection direction;
    [SerializeField] private CargoPackageState state =
        CargoPackageState.Staged;

    [Header("Flight Assignment")]
    [Tooltip(
        "Required for outgoing cargo. Must match the intended " +
        "aircraft's AircraftFlightIdentity.OperationId."
    )]
    [SerializeField] private string flightOperationId;

    [Header("Current Location")]
    [SerializeField] private CargoZone currentZone;

    private CargoPackageData data;

    private void OnDestroy()
    {
        if (data != null)
            data.DetachVisual(this);
    }

    public CargoPackageData Data
    {
        get
        {
            EnsureData();
            return data;
        }
    }

    public string PackageId => Data.PackageId;
    public CargoDirection Direction => Data.Direction;
    public CargoPackageState State => Data.State;
    public string FlightOperationId => Data.FlightOperationId;
    public CargoZone CurrentZone => Data.CurrentZone;
    public bool IsAvailableForCollection => Data.IsAvailableForCollection;

    private void Awake()
    {
        EnsureData();
    }

    private void EnsureData()
    {
        if (data != null)
            return;

        data = new CargoPackageData(
            packageId,
            direction,
            state,
            flightOperationId,
            currentZone);
    }

    /// <summary>
    /// Rebinds a visual component to an existing cargo record.
    /// The record remains valid independently of this GameObject.
    /// </summary>
    public void BindData(CargoPackageData packageData)
    {
        if (packageData == null)
        {
            LogError(
                $"{nameof(CargoPackage)} on {name}: Cannot bind missing cargo data.",
                this);
            return;
        }

        if (data != null)
            data.DetachVisual(this);

        data = packageData;
        data.AttachVisual(this);
        SyncSerializedFields();
    }

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

        EnsureData();
        data.Initialize(
            id,
            cargoDirection,
            CargoPackageState.Staged,
            assignedFlightOperationId,
            startingZone);
        data.AttachVisual(this);
        SyncSerializedFields();
    }

    public void SetVisualPrefab(GameObject prefab)
    {
        Data.SetVisualPrefab(prefab);
    }

    public bool IsForFlight(string operationId)
    {
        return Data.IsForFlight(operationId);
    }

    public bool TryReserve()
    {
        bool reserved = Data.TryReserve();
        SyncSerializedFields();
        return reserved;
    }

    public void SetInTransit()
    {
        if (!Data.SetInTransit())
        {
            LogWarning(
                $"{name}: Cannot move cargo into transit " +
                "unless it has been reserved.",
                this);
            return;
        }

        SyncSerializedFields();
    }

    public void SetDelivered()
    {
        Data.SetDelivered();
        SyncSerializedFields();
    }

    public void ReturnToZone(CargoZone zone)
    {
        if (!Data.ReturnToZone(zone))
        {
            LogError(
                $"{name}: Cannot return cargo to a missing zone.",
                this);
            return;
        }

        SyncSerializedFields();
    }

    private void SyncSerializedFields()
    {
        if (data == null)
            return;

        packageId = data.PackageId;
        direction = data.Direction;
        state = data.State;
        flightOperationId = data.FlightOperationId;
        currentZone = data.CurrentZone;
    }

    private void Log(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.Log(message, context);
        else Debug.Log(message);
    }

    private void LogWarning(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.LogWarning(message, context);
        else Debug.LogWarning(message);
    }

    private void LogError(object message, UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.LogError(message, context);
        else Debug.LogError(message);
    }

    private void LogException(
        System.Exception exception,
        UnityEngine.Object context = null)
    {
        if (!logToConsole) return;
        if (context != null) Debug.LogException(exception, context);
        else Debug.LogException(exception);
    }
}