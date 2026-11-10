using System;
using System.Collections.Generic;
using UnityEngine;

public class PlacedBuilding : MonoBehaviour
{
    public static event Action<PlacedBuilding> BuildingChanged;

    private static readonly List<PlacedBuilding> activeBuildings =
        new List<PlacedBuilding>();

    public static IReadOnlyList<PlacedBuilding> ActiveBuildings =>
        activeBuildings;

    private bool isInitialized;
    [SerializeField] private string instanceId;
    private PlacedBuilding supportingSurface;
    private bool isSurfacePlaced;

    public string InstanceId
    {
        get
        {
            EnsureInstanceId();
            return instanceId;
        }
    }

    public bool IsSurfacePlaced => isSurfacePlaced;
    public PlacedBuilding SupportingSurface => supportingSurface;

    public BuildableDefinition Definition { get; private set; }

    public Vector2Int Origin { get; private set; }

    public int Width { get; private set; }

    public int Depth { get; private set; }

    public int Rotation { get; private set; }

    public int EdgeLength { get; private set; }

    public EdgeDirection EdgeDirection { get; private set; }

    public bool IsEdgeBuilding
    {
        get
        {
            return Definition != null &&
                   Definition.placementType ==
                   PlacementType.Edge;
        }
    }

    public void Initialize(
        BuildableDefinition definition,
        Vector2Int origin,
        int width,
        int depth,
        int rotation)
    {
        Definition = definition;
        isSurfacePlaced = false;
        supportingSurface = null;
        EnsureInstanceId();
        Origin = origin;
        Width = width;
        Depth = depth;
        Rotation = rotation;

        EdgeLength = 0;

        EdgeDirection =
            rotation == 90 ||
            rotation == 270
                ? EdgeDirection.Vertical
                : EdgeDirection.Horizontal;

        isInitialized = true;
        RegisterActiveBuilding();
        BuildingChanged?.Invoke(this);
    }

    public void InitializeEdge(
        BuildableDefinition definition,
        Vector2Int origin,
        int edgeLength,
        EdgeDirection direction)
    {
        Definition = definition;
        isSurfacePlaced = false;
        supportingSurface = null;
        EnsureInstanceId();

        Origin = origin;

        Width = 1;
        Depth = 1;

        EdgeLength =
            Mathf.Max(
                1,
                edgeLength
            );

        EdgeDirection =
            direction;

        Rotation =
            direction ==
            EdgeDirection.Horizontal
                ? 0
                : 90;

        isInitialized = true;
        RegisterActiveBuilding();
        BuildingChanged?.Invoke(this);
    }

    public void InitializeSurface(
        BuildableDefinition definition,
        int rotation,
        string restoredInstanceId = null)
    {
        Definition = definition;
        Origin = Vector2Int.zero;
        Width = 1;
        Depth = 1;
        Rotation = rotation;
        EdgeLength = 0;
        EdgeDirection = EdgeDirection.Horizontal;
        isSurfacePlaced = true;
        supportingSurface = null;

        if (!string.IsNullOrWhiteSpace(restoredInstanceId))
            instanceId = restoredInstanceId;

        EnsureInstanceId();
        isInitialized = true;
        RegisterActiveBuilding();
        BuildingChanged?.Invoke(this);
    }

    public void SetSupportingSurface(PlacedBuilding surface)
    {
        supportingSurface = surface;

        if (surface != null)
            transform.SetParent(surface.transform, true);
    }

    public void SetInstanceId(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            instanceId = value;

        EnsureInstanceId();
    }

    private void EnsureInstanceId()
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            instanceId = Guid.NewGuid().ToString("N");
    }

    private void RegisterActiveBuilding()
    {
        if (!activeBuildings.Contains(this))
            activeBuildings.Add(this);
    }

    private void UnregisterActiveBuilding()
    {
        activeBuildings.Remove(this);
    }

    private void OnEnable()
    {
        if (isInitialized)
        {
            RegisterActiveBuilding();
            BuildingChanged?.Invoke(this);
        }
    }

    private void OnDisable()
    {
        if (isInitialized)
        {
            UnregisterActiveBuilding();
            BuildingChanged?.Invoke(this);
        }
    }

    private void OnDestroy()
    {
        UnregisterActiveBuilding();

        if (isInitialized)
            BuildingChanged?.Invoke(this);
    }
}
