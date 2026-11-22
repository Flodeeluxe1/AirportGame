using System;
using UnityEngine;

/// <summary>
/// Runtime cargo identity and lifecycle data.
/// This class is deliberately independent of a visual GameObject so cargo
/// can remain represented in the simulation while its box visual is released.
/// </summary>
[Serializable]
public sealed class CargoPackageData
{
    [SerializeField] private string packageId;
    [SerializeField] private CargoDirection direction;
    [SerializeField] private CargoPackageState state = CargoPackageState.Staged;
    [SerializeField] private string flightOperationId;
    [SerializeField] private CargoZone currentZone;

    public string PackageId => packageId;
    public CargoDirection Direction => direction;
    public CargoPackageState State => state;
    public string FlightOperationId => flightOperationId;
    public CargoZone CurrentZone => currentZone;

    public bool IsAvailableForCollection =>
        state == CargoPackageState.Staged && currentZone != null;

    public CargoPackageData() { }

    public CargoPackageData(
        string id,
        CargoDirection cargoDirection,
        CargoPackageState packageState,
        string assignedFlightOperationId,
        CargoZone startingZone)
    {
        Initialize(
            id,
            cargoDirection,
            packageState,
            assignedFlightOperationId,
            startingZone);
    }

    public void Initialize(
        string id,
        CargoDirection cargoDirection,
        CargoPackageState packageState,
        string assignedFlightOperationId,
        CargoZone startingZone)
    {
        packageId = id;
        direction = cargoDirection;
        state = packageState;
        flightOperationId = assignedFlightOperationId ?? string.Empty;
        currentZone = startingZone;
    }

    public bool IsForFlight(string operationId)
    {
        return direction == CargoDirection.Outgoing &&
               !string.IsNullOrWhiteSpace(operationId) &&
               flightOperationId == operationId;
    }

    public bool TryReserve()
    {
        if (state != CargoPackageState.Staged || currentZone == null)
            return false;

        state = CargoPackageState.Reserved;
        return true;
    }

    public bool SetInTransit()
    {
        if (state != CargoPackageState.Reserved)
            return false;

        state = CargoPackageState.InTransit;
        currentZone = null;
        return true;
    }

    public void SetDelivered()
    {
        state = CargoPackageState.Delivered;
        currentZone = null;
    }

    public bool ReturnToZone(CargoZone zone)
    {
        if (zone == null)
            return false;

        currentZone = zone;
        state = CargoPackageState.Staged;
        return true;
    }
}