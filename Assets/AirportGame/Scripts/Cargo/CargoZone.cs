using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class CargoZone : MonoBehaviour
{
    [Header("Zone Points")]
    [SerializeField] private Transform vanStopPoint;
    [SerializeField] private Transform cargoAreaPoint;
    [SerializeField] private Transform airportVehicleStopPoint;

    [Header("Runtime Status")]
    [SerializeField] private int stagedPackageCount;

    private readonly HashSet<CargoPackage> packages =
        new HashSet<CargoPackage>();

    public PlacedBuilding Building { get; private set; }

    public Transform VanStopPoint => vanStopPoint;

    public Transform CargoAreaPoint => cargoAreaPoint;

    public Transform AirportVehicleStopPoint =>
        airportVehicleStopPoint;

    public int StagedPackageCount => stagedPackageCount;

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

    public bool RegisterPackage(CargoPackage package)
    {
        if (package == null)
            return false;

        if (!packages.Add(package))
            return true;

        RefreshPackageCount();
        return true;
    }

    public void UnregisterPackage(CargoPackage package)
    {
        if (package == null)
            return;

        packages.Remove(package);
        RefreshPackageCount();
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