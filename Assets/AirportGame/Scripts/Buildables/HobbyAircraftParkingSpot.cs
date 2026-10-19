
using UnityEngine;

/// <summary>
/// Marks a placed grass parking building as a parking space
/// that can be reserved by hobby or flight-school aircraft.
/// </summary>
[DisallowMultipleComponent]
public class HobbyAircraftParkingSpot : MonoBehaviour
{
    [Header("Parking Position")]
    [Tooltip("Optional child Transform marking where the aircraft should park.")]
    [SerializeField] private Transform parkingPosition;

    [Header("Parking Designation")]
    [Tooltip(
        "When enabled, this spot is reserved for flight-school use. " +
        "This manual designation is never changed automatically."
    )]
    [SerializeField] private bool reservedForFlightSchool = false;

    [Header("Runtime Status")]
    [SerializeField] private GameObject reservedAircraft;

    private PlacedBuilding placedBuilding;

    public bool ReservedForFlightSchool =>
        reservedForFlightSchool;

    public bool IsOccupied
    {
        get
        {
            if (reservedAircraft == null)
                reservedAircraft = null;

            return reservedAircraft != null;
        }
    }

    public GameObject ReservedAircraft => reservedAircraft;

    public Vector3 ParkingWorldPosition =>
        parkingPosition != null
            ? parkingPosition.position
            : transform.position;

    public Quaternion ParkingWorldRotation =>
        parkingPosition != null
            ? parkingPosition.rotation
            : transform.rotation;

    public bool IsPlacedBuilding
    {
        get
        {
            FindPlacedBuildingIfNeeded();
            return placedBuilding != null;
        }
    }

    private void FindPlacedBuildingIfNeeded()
    {
        if (placedBuilding == null)
            placedBuilding = GetComponent<PlacedBuilding>();
    }

    /// <summary>
    /// Restores the manually chosen designation from a save file.
    /// Does not restore aircraft occupancy.
    /// </summary>
    public void RestoreFlightSchoolDesignation(bool isDesignated)
    {
        reservedForFlightSchool = isDesignated;
    }

    public bool TryReserve(
        GameObject aircraft,
        bool isFlightSchoolAircraft = false)
    {
        if (aircraft == null)
            return false;

        FindPlacedBuildingIfNeeded();

        if (placedBuilding == null)
            return false;

        if (reservedForFlightSchool && !isFlightSchoolAircraft)
            return false;

        if (reservedAircraft == aircraft)
            return true;

        if (IsOccupied)
            return false;

        reservedAircraft = aircraft;

        Debug.Log(
            $"Parking spot reserved | Spot: {name} | " +
            $"Aircraft: {aircraft.name} | " +
            $"Flight school: {reservedForFlightSchool}",
            this
        );

        return true;
    }

    public bool Release(GameObject aircraft)
    {
        if (aircraft == null || reservedAircraft != aircraft)
            return false;

        reservedAircraft = null;

        Debug.Log(
            $"Parking spot released | Spot: {name} | " +
            $"Aircraft: {aircraft.name}",
            this
        );

        return true;
    }

    private void OnDestroy()
    {
        if (reservedAircraft != null)
        {
            Debug.LogWarning(
                $"Parking spot {name} was destroyed while " +
                $"reserved by {reservedAircraft.name}.",
                this
            );
        }
    }
}
