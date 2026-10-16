
using UnityEngine;

/// <summary>
/// Marks a placed grass parking building as a parking space
/// that can be reserved by a hobby aircraft.
/// </summary>
[DisallowMultipleComponent]
public class HobbyAircraftParkingSpot : MonoBehaviour
{
    [Header("Parking Position")]
    [Tooltip("Optional child Transform marking where the aircraft should park.")]
    [SerializeField] private Transform parkingPosition;

    [Header("Runtime Status")]
    [SerializeField] private GameObject reservedAircraft;

    private PlacedBuilding placedBuilding;

    public bool IsOccupied
    {
        get
        {
            // A destroyed aircraft must not leave the spot permanently occupied.
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

    public bool TryReserve(GameObject aircraft)
    {
        if (aircraft == null)
            return false;

        FindPlacedBuildingIfNeeded();

        // Only an actual placed building can be used as a parking spot.
        if (placedBuilding == null)
            return false;

        // Allow repeated reservation by the same aircraft.
        if (reservedAircraft == aircraft)
            return true;

        // Never overwrite another aircraft's reservation.
        if (IsOccupied)
            return false;

        reservedAircraft = aircraft;

        Debug.Log(
            $"Hobby parking spot reserved | Spot: {name} | " +
            $"Aircraft: {aircraft.name}",
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
            $"Hobby parking spot released | Spot: {name} | " +
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
                $"Hobby parking spot {name} was destroyed while " +
                $"reserved by {reservedAircraft.name}.",
                this
            );
        }
    }
}
