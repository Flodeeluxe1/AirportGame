
using UnityEngine;

[DisallowMultipleComponent]
public class AircraftFlightIdentity : MonoBehaviour
{
    public FlightDefinition Definition { get; private set; }

    public string FlightNumber =>
        Definition != null ? Definition.FlightNumber : string.Empty;

    public string PlannedGateStableId { get; private set; }

    public string ActualGateStableId { get; private set; }

    public int PlannedStartMinute { get; private set; }

    public long ScheduledDayIndex { get; private set; }

    public string OperationId =>
        $"{ScheduledDayIndex}:{FlightNumber}";

    public bool IsInitialized { get; private set; }

    public bool HasActualGate =>
        !string.IsNullOrEmpty(ActualGateStableId);

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
        PlannedGateStableId = plannedGateStableId;
        PlannedStartMinute = plannedStartMinute;
        ScheduledDayIndex = scheduledDayIndex;

        ActualGateStableId = string.Empty;
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

        ActualGateStableId = gateStableId;
    }

    public void ClearActualGate()
    {
        ActualGateStableId = string.Empty;
    }

    private string FormatTime(int minutes)
    {
        minutes = Mathf.Clamp(minutes, 0, 1440);

        if (minutes == 1440)
            return "24:00";

        return $"{minutes / 60:00}:{minutes % 60:00}";
    }
}