using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene component for a placed bus-stop sign. Add child marker transforms
/// to the sign prefab for the roadside stopping point and passenger waiting
/// position; both markers remain on the sign's side of the road.
/// </summary>
[DisallowMultipleComponent]
public class PublicTransportStop : MonoBehaviour
{
    private static readonly List<PublicTransportStop> activeStops =
        new List<PublicTransportStop>();

    [Header("Stop Markers")]
    [SerializeField] private Transform roadsidePoint;
    [SerializeField] private Transform passengerWaitingPoint;

    [Header("Route")]
    [SerializeField] private int routeOrder;

    private PlacedBuilding placedBuilding;
    private bool isRegistered;

    public static event Action<PublicTransportStop> StopRegistered;
    public static event Action<PublicTransportStop> StopUnregistered;

    public static IReadOnlyList<PublicTransportStop> ActiveStops =>
        activeStops;

    public bool IsRegistered => isRegistered;
    public PlacedBuilding PlacedBuilding => placedBuilding;
    public int RouteOrder => routeOrder;
    public Transform RoadsidePoint => roadsidePoint;
    public Transform PassengerWaitingPoint => passengerWaitingPoint;

    public Vector3 RoadsidePosition =>
        roadsidePoint != null ? roadsidePoint.position : transform.position;

    public Vector3 WaitingPosition =>
        passengerWaitingPoint != null
            ? passengerWaitingPoint.position
            : transform.position;

    private void OnEnable()
    {
        PlacedBuilding.BuildingChanged += HandleBuildingChanged;
        TryRegister();
    }

    private void OnDisable()
    {
        PlacedBuilding.BuildingChanged -= HandleBuildingChanged;
        Unregister();
    }

    private void HandleBuildingChanged(PlacedBuilding building)
    {
        if (building == null || building.gameObject == gameObject)
            TryRegister();
    }

    private void TryRegister()
    {
        if (!isActiveAndEnabled)
            return;

        PlacedBuilding currentBuilding = GetComponent<PlacedBuilding>();
        if (currentBuilding == null)
            return;

        placedBuilding = currentBuilding;

        if (isRegistered)
            return;

        isRegistered = true;
        if (!activeStops.Contains(this))
            activeStops.Add(this);

        StopRegistered?.Invoke(this);
    }

    private void Unregister()
    {
        if (!isRegistered)
            return;

        isRegistered = false;
        activeStops.Remove(this);
        StopUnregistered?.Invoke(this);
        placedBuilding = null;
    }

    public bool TryGetAdjacentRoadCell(
        RoadNetwork roadNetwork,
        GridManager gridManager,
        out Vector2Int roadCell)
    {
        roadCell = Vector2Int.zero;

        if (roadNetwork == null || gridManager == null ||
            roadsidePoint == null)
            return false;

        roadCell = roadNetwork.GetRoadAnchor(
            gridManager.WorldToGrid(roadsidePoint.position));

        return roadNetwork.IsRoad(roadCell);
    }

    public void SetRouteOrder(int order)
    {
        routeOrder = Mathf.Max(0, order);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        routeOrder = Mathf.Max(0, routeOrder);
    }
#endif
}
