using UnityEngine;

/// <summary>
/// Marks the grid position where a fuel-related building connects to
/// the pipeline network. Add this component to the building's Pipe_Connector
/// child object.
/// </summary>
[DisallowMultipleComponent]
public sealed class FuelConnector : MonoBehaviour
{
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

    private void ResolveGridManager()
    {
        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();
    }
}
