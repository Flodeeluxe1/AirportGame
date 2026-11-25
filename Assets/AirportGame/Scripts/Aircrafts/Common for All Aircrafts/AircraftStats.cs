using UnityEngine;

[DisallowMultipleComponent]
public class AircraftStats : MonoBehaviour
{
    [Header("Aircraft Operation")]
    [SerializeField]
    private FlightOperationType operationType = FlightOperationType.None;

    public FlightOperationType OperationType => operationType;

    [Header("Passenger Capacity")]
    [Min(0)]
    public int maxPassengers;

    [SerializeField, Min(0)]
    private int incomingPassengers;

    [SerializeField, Min(0)]
    private int outgoingPassengers;

    [Header("Taxi Speed (knots)")]
    [Min(0f)]
    public float maxTaxiSpeed = 15f;

    [SerializeField, Min(0f)]
    private float currentTaxiSpeed;

    [Header("Cargo Capacity (packages)")]
    [Min(0)]
    public int maxCargo = 20;

    [SerializeField, Min(0)]
    private int incomingCargo;

    [SerializeField, Min(0)]
    private int outgoingCargo;

    public int IncomingPassengers => incomingPassengers;
    public int OutgoingPassengers => outgoingPassengers;
    public float CurrentTaxiSpeed => currentTaxiSpeed;
    public int IncomingCargo => incomingCargo;
    public int OutgoingCargo => outgoingCargo;
    public int TotalCargo => incomingCargo + outgoingCargo;

    public void SetIncomingPassengers(int count)
    {
        incomingPassengers = Mathf.Clamp(count, 0, maxPassengers);
    }

    public void SetOutgoingPassengers(int count)
    {
        outgoingPassengers = Mathf.Clamp(count, 0, maxPassengers);
    }

    public void SetCurrentTaxiSpeed(float speedKnots)
    {
        currentTaxiSpeed = Mathf.Clamp(speedKnots, 0f, maxTaxiSpeed);
    }

    public void SetIncomingCargo(int count)
    {
        incomingCargo = Mathf.Max(0, count);
    }

    public void SetOutgoingCargo(int count)
    {
        outgoingCargo = Mathf.Max(0, count);
    }

    public bool HasCargoCapacityFor(int additionalPackages)
    {
        return additionalPackages >= 0 &&
               TotalCargo + additionalPackages <= maxCargo;
    }
}