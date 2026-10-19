
using UnityEngine;

/// <summary>
/// Identifies a dedicated parking spot for sightseeing aircraft.
/// The prefab's root Transform is the parking position.
/// This component does not change the existing hobby parking system.
/// </summary>
[DisallowMultipleComponent]
public class SightseeingAircraftParkingSpot : MonoBehaviour
{
    [Header("Parking Position")]
    [Tooltip("The aircraft parks at this prefab's root position and rotation.")]
    [SerializeField] private bool usePrefabRoot = true;

    [Header("Runtime Status")]
    [SerializeField] private GameObject reservedAircraft;

    private PlacedBuilding placedBuilding;

    public Vector3 ParkingWorldPosition => transform.position;
    public Quaternion ParkingWorldRotation => transform.rotation;

    public GameObject ReservedAircraft => reservedAircraft;

    public bool IsOccupied => reservedAircraft != null;

    public bool IsPlacedBuilding
    {
        get
        {
            FindPlacedBuilding();
            return placedBuilding != null;
        }
    }

    private void Awake()
    {
        FindPlacedBuilding();
    }

    private void FindPlacedBuilding()
    {
        if (placedBuilding == null)
            placedBuilding = GetComponent<PlacedBuilding>();
    }

    /// <summary>
    /// Reserves this spot for one sightseeing aircraft.
    /// </summary>
    public bool TryReserve(GameObject aircraft)
    {
        if (aircraft == null)
            return false;

        FindPlacedBuilding();

        if (placedBuilding == null)
        {
            Debug.LogWarning(
                $"Sightseeing parking spot '{name}' cannot be reserved " +
                "because it has no PlacedBuilding component.",
                this
            );

            return false;
        }

        if (reservedAircraft == aircraft)
            return true;

        if (IsOccupied)
            return false;

        reservedAircraft = aircraft;

        Debug.Log(
            $"SIGHTSEEING PARKING RESERVED | " +
            $"Spot: {name} | Aircraft: {aircraft.name}",
            this
        );

        return true;
    }

    /// <summary>
    /// Releases the spot only when the requesting aircraft owns it.
    /// </summary>
    public bool Release(GameObject aircraft)
    {
        if (aircraft == null || reservedAircraft != aircraft)
            return false;

        reservedAircraft = null;

        Debug.Log(
            $"SIGHTSEEING PARKING RELEASED | " +
            $"Spot: {name} | Aircraft: {aircraft.name}",
            this
        );

        return true;
    }

    private void OnDestroy()
    {
        if (reservedAircraft != null)
        {
            Debug.LogWarning(
                $"Sightseeing parking spot '{name}' was destroyed " +
                $"while reserved by '{reservedAircraft.name}'.",
                this
            );
        }
    }
}
