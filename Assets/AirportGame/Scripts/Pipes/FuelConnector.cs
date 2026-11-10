using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks the grid position where a fuel-related building connects to
/// the pipeline network. Add this component to the building's Pipe_Connector
/// child object.
/// </summary>
[DisallowMultipleComponent]
public sealed class FuelConnector : MonoBehaviour
{
    private static readonly HashSet<FuelConnector> activeConnectors = new();

    public static IReadOnlyCollection<FuelConnector> ActiveConnectors =>
        activeConnectors;

    public static event Action<FuelConnector> ConnectorRegistered;
    public static event Action<FuelConnector> ConnectorUnregistered;

    [SerializeField] private GridManager gridManager;

    public Vector2Int GridCell
    {
        get
        {
            ResolveGridManager();
            return gridManager != null
                ? gridManager.WorldToGrid(transform.position)
                : Vector2Int.zero;
        }
    }

    public Transform BuildingRoot => transform.root;

    public AircraftFuelStation AircraftFuelStation =>
        GetComponentInParent<AircraftFuelStation>();

    public FuelStorageTank StorageTank =>
        GetComponentInParent<FuelStorageTank>();

    private void Awake()
    {
        ResolveGridManager();
    }

    private void OnEnable()
    {
        if (activeConnectors.Add(this))
            ConnectorRegistered?.Invoke(this);
    }

    private void OnDisable()
    {
        if (activeConnectors.Remove(this))
            ConnectorUnregistered?.Invoke(this);
    }

    private void ResolveGridManager()
    {
        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();
    }
}
