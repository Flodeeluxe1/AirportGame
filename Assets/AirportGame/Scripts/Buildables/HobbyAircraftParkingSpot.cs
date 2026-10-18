
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

    /// <summary>
    /// Whether this spot is manually designated for flight-school use.
    /// The player controls this setting.
    /// </summary>
    public bool ReservedForFlightSchool =>
        reservedForFlightSchool;

    /// <summary>
    /// Whether an aircraft currently occupies or has reserved this spot.
    /// A destroyed aircraft does not leave the spot permanently occupied.
    /// </summary>
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
    /// Reserves this spot for an aircraft.
    /// Flight-school aircraft must explicitly identify themselves
    /// when reserving a school-designated spot.
    /// The manual designation is never modified here.
    /// </summary>
    public bool TryReserve(
        GameObject aircraft,
        bool isFlightSchoolAircraft = false)
    {
        if (aircraft == null)
            return false;

        FindPlacedBuildingIfNeeded();

        // Only an actual placed building can be used as a parking spot.
        if (placedBuilding == null)
            return false;

        // Hobby and other ordinary aircraft cannot use school-designated spots.
        if (reservedForFlightSchool && !isFlightSchoolAircraft)
            return false;

        // Allow repeated reservation by the same aircraft.
        if (reservedAircraft == aircraft)
            return true;

        // Never overwrite another aircraft's reservation.
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

    /// <summary>
    /// Releases the aircraft's occupancy reservation.
    /// Does not change the manual flight-school designation.
    /// </summary>
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
