
using UnityEngine;

[DisallowMultipleComponent]
public class AircraftFlightIdentity : MonoBehaviour
{
    [Header("Flight Identity")]
    [SerializeField] private string flightNumber;

    [Header("Gate Identity")]
    [Tooltip("The intended gate from the flight schedule.")]
    [SerializeField] private string plannedGateStableId;

    [Tooltip("The gate actually assigned to this aircraft.")]
    [SerializeField] private string actualGateStableId;

    public FlightDefinition Definition { get; private set; }

    public string FlightNumber =>
        Definition != null ? Definition.FlightNumber : flightNumber;

    public string PlannedGateStableId => plannedGateStableId;

    public string ActualGateStableId => actualGateStableId;

    public int PlannedStartMinute { get; private set; }

    public long ScheduledDayIndex { get; private set; }

    public string OperationId =>
        $"{ScheduledDayIndex}:{FlightNumber}";

    public bool IsInitialized { get; private set; }

    public bool HasActualGate =>
        !string.IsNullOrEmpty(actualGateStableId);

    public void Initialize(
        FlightDefinition definition,
        string plannedGateStableId,
        int plannedStartMinute,
        long scheduledDayIndex)
    {
        if (definition == null)
        {
            Debug.LogError(
                $"{nameof(AircraftFlightIdentity)} on {name}: " +
                "Cannot initialize without a flight definition.",
                this
            );
            return;
        }

        Definition = definition;
        flightNumber = definition.FlightNumber;
        this.plannedGateStableId = plannedGateStableId;
        PlannedStartMinute = plannedStartMinute;
        ScheduledDayIndex = scheduledDayIndex;

        actualGateStableId = string.Empty;
        IsInitialized = true;

        Debug.Log(
            $"Aircraft initialized | Flight: {FlightNumber} | " +
            $"Planned gate: {PlannedGateStableId} | " +
            $"Scheduled time: {FormatTime(PlannedStartMinute)} | " +
            $"Operation: {OperationId}",
            this
        );
    }

    public void SetActualGate(string gateStableId)
    {
        if (string.IsNullOrWhiteSpace(gateStableId))
        {
            Debug.LogError(
                $"{FlightNumber}: Cannot assign an empty actual gate ID.",
                this
            );
            return;
        }

        actualGateStableId = gateStableId;
    }

    public void ClearActualGate()
    {
        actualGateStableId = string.Empty;
    }

    private string FormatTime(int minutes)
    {
        minutes = Mathf.Clamp(minutes, 0, 1440);

        if (minutes == 1440)
            return "24:00";

        return $"{minutes / 60:00}:{minutes % 60:00}";
    }
}
