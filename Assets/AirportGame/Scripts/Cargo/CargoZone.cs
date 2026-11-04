using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class CargoZone : MonoBehaviour
{
    private static readonly HashSet<CargoZone> registeredZones =
        new HashSet<CargoZone>();

    public static IReadOnlyCollection<CargoZone> RegisteredZones =>
        registeredZones;

    public static event Action<CargoZone> AvailabilityChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        registeredZones.Clear();
        AvailabilityChanged = null;
    }

    [Header("Zone Points")]
    [SerializeField] private Transform vanStopPoint;
    [SerializeField] private Transform cargoAreaPoint;
    [SerializeField] private Transform airportVehicleStopPoint;

    [Header("Runtime Status")]
    [SerializeField] private int stagedPackageCount;

    private readonly HashSet<CargoPackage> packages =
        new HashSet<CargoPackage>();

    [Header("Flight Reservation")]
    [SerializeField] private bool hasFlightReservation;
    [SerializeField] private string reservedForFlightOperationId;

    public PlacedBuilding Building { get; private set; }

    public Transform VanStopPoint => vanStopPoint;

    public Transform CargoAreaPoint => cargoAreaPoint;

    public Transform AirportVehicleStopPoint =>
        airportVehicleStopPoint;

    public int StagedPackageCount => stagedPackageCount;

    private void OnEnable()
    {
        if (registeredZones.Add(this))
            NotifyAvailabilityChanged();
    }

    private void OnDisable()
    {
        if (registeredZones.Remove(this))
            NotifyAvailabilityChanged();
    }

    private void Awake()
    {
        Building = GetComponent<PlacedBuilding>();
        FindZonePoints();
        ValidateZone();
    }

    private void OnValidate()
    {
        FindZonePoints();
    }

    private void FindZonePoints()
    {
        if (vanStopPoint == null)
            vanStopPoint = FindChildPoint("VanStopPoint");

        if (cargoAreaPoint == null)
            cargoAreaPoint = FindChildPoint("CargoAreaPoint");

        if (airportVehicleStopPoint == null)
        {
            airportVehicleStopPoint =
                FindChildPoint("AirportVehicleStopPoint");
        }
    }

    private Transform FindChildPoint(string pointName)
    {
        Transform[] children =
            GetComponentsInChildren<Transform>(true);

        foreach (Transform child in children)
        {
            if (child != transform && child.name == pointName)
                return child;
        }

        return null;
    }

    private void NotifyAvailabilityChanged()
    {
        AvailabilityChanged?.Invoke(this);
    }

    public static List<CargoZone> GetZonesSortedByDistance(Vector3 worldPosition)
    {
        List<CargoZone> zones = new List<CargoZone>(registeredZones);

        zones.Sort((a, b) =>
        {
            if (a == null) return 1;
            if (b == null) return -1;

            Vector3 positionA = a.AirportVehicleStopPoint != null
                ? a.AirportVehicleStopPoint.position
                : a.transform.position;

            Vector3 positionB = b.AirportVehicleStopPoint != null
                ? b.AirportVehicleStopPoint.position
                : b.transform.position;

            return HorizontalDistanceSquared(worldPosition, positionA)
                .CompareTo(HorizontalDistanceSquared(worldPosition, positionB));
        });

        return zones;
    }

    private static float HorizontalDistanceSquared(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    private void ValidateZone()
    {
        if (vanStopPoint == null ||
            cargoAreaPoint == null ||
            airportVehicleStopPoint == null)
        {
            Debug.LogWarning(
                $"{name}: Cargo zone points are incomplete. " +
                "Required children: VanStopPoint, CargoAreaPoint, " +
                "and AirportVehicleStopPoint.",
                this
            );
            return;
        }

        Debug.Log(
            $"{name}: Cargo zone initialized successfully.",
            this
        );
    }

    public bool HasCargoFromOtherFlight(string operationId)
    {
        packages.RemoveWhere(package => package == null);

        string requestedOperationId = operationId ?? string.Empty;

        foreach (CargoPackage package in packages)
        {
            if (package.FlightOperationId != requestedOperationId)
                return true;
        }

        return false;
    }

    public bool IsAvailableForFlight(string operationId)
    {
        string requestedOperationId = operationId ?? string.Empty;

        if (hasFlightReservation &&
            reservedForFlightOperationId != requestedOperationId)
        {
            return false;
        }

        return !HasCargoFromOtherFlight(requestedOperationId);
    }

    public bool TryReserveForFlight(string operationId)
    {
        string requestedOperationId = operationId ?? string.Empty;

        if (!IsAvailableForFlight(requestedOperationId))
            return false;

        hasFlightReservation = true;
        reservedForFlightOperationId = requestedOperationId;
        NotifyAvailabilityChanged();
        return true;
    }

    public void ReleaseFlightReservation(string operationId)
    {
        string requestedOperationId = operationId ?? string.Empty;

        if (!hasFlightReservation ||
            reservedForFlightOperationId != requestedOperationId)
        {
            return;
        }

        hasFlightReservation = false;
        reservedForFlightOperationId = string.Empty;
        NotifyAvailabilityChanged();
    }

    public bool RegisterPackage(CargoPackage package)
    {
        if (package == null)
            return false;

        if (!IsAvailableForFlight(package.FlightOperationId))
        {
            Debug.LogWarning(
                $"{name}: Rejected package '{package.PackageId}' because " +
                "the cargo zone is reserved for or occupied by another flight.",
                this
            );

            return false;
        }

        if (!packages.Add(package))
            return true;

        RefreshPackageCount();
        NotifyAvailabilityChanged();
        return true;
    }

    public void UnregisterPackage(CargoPackage package)
    {
        if (package == null)
            return;

        if (!packages.Remove(package))
            return;

        RefreshPackageCount();
        NotifyAvailabilityChanged();
    }

    public int CountAvailablePackages(
        CargoDirection direction,
        string flightOperationId = null)
    {
        int count = 0;

        foreach (CargoPackage package in packages)
        {
            if (package == null ||
                !package.IsAvailableForCollection ||
                package.Direction != direction)
            {
                continue;
            }

            if (direction == CargoDirection.Outgoing &&
                !package.IsForFlight(flightOperationId))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    public bool HasAvailablePackages(
        CargoDirection direction,
        string flightOperationId = null)
    {
        return CountAvailablePackages(direction, flightOperationId) > 0;
    }

    public bool TryGetFirstAvailablePackage(
        CargoDirection direction,
        out CargoPackage firstPackage,
        string flightOperationId = null)
    {
        foreach (CargoPackage package in packages)
        {
            if (package == null ||
                !package.IsAvailableForCollection ||
                package.Direction != direction)
            {
                continue;
            }

            if (direction == CargoDirection.Outgoing &&
                !package.IsForFlight(flightOperationId))
            {
                continue;
            }

            firstPackage = package;
            return true;
        }

        firstPackage = null;
        return false;
    }

    public List<CargoPackage> GetAvailablePackages(
        CargoDirection direction,
        string flightOperationId = null)
    {
        List<CargoPackage> result =
            new List<CargoPackage>();

        foreach (CargoPackage package in packages)
        {
            if (package == null ||
                !package.IsAvailableForCollection ||
                package.Direction != direction)
            {
                continue;
            }

            if (direction == CargoDirection.Outgoing &&
                !package.IsForFlight(flightOperationId))
            {
                continue;
            }

            result.Add(package);
        }

        return result;
    }

    private void RefreshPackageCount()
    {
        packages.RemoveWhere(package => package == null);

        int count = 0;

        foreach (CargoPackage package in packages)
        {
            if (package.State == CargoPackageState.Staged)
                count++;
        }

        stagedPackageCount = count;
    }
}