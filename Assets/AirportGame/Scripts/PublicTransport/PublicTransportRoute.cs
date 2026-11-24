using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cached ordered bus route. This component belongs on the same GameObject
/// as PublicTransportManager. Road paths are rebuilt only after stop changes
/// or a RoadNetwork topology-version change.
/// </summary>
[DisallowMultipleComponent]
public class PublicTransportRoute : MonoBehaviour
{
    private PublicTransportManager transportManager;
    private RoadNetwork cachedRoadNetwork;

    private readonly List<PublicTransportStop> orderedStops =
        new List<PublicTransportStop>();

    private readonly List<Vector2Int> routeRoadCells =
        new List<Vector2Int>();

    private bool routeDirty = true;
    private bool routeValid;
    private int cachedTopologyVersion = -1;
    private int rebuildVersion;

    public bool IsRouteValid => routeValid;
    public int RebuildVersion => rebuildVersion;
    public IReadOnlyList<PublicTransportStop> OrderedStops => orderedStops;
    public IReadOnlyList<Vector2Int> RouteRoadCells => routeRoadCells;

    private void Awake()
    {
        transportManager = GetComponent<PublicTransportManager>();
    }

    private void OnEnable()
    {
        if (transportManager == null)
            transportManager = GetComponent<PublicTransportManager>();

        if (transportManager != null)
            transportManager.StopsChanged += MarkRouteDirty;

        routeDirty = true;
        RebuildIfRequired();
    }

    private void OnDisable()
    {
        if (transportManager != null)
            transportManager.StopsChanged -= MarkRouteDirty;
    }

    private void Update()
    {
        RoadNetwork currentRoadNetwork =
            transportManager != null ? transportManager.RoadNetwork : null;

        if (currentRoadNetwork != cachedRoadNetwork ||
            (currentRoadNetwork != null &&
             currentRoadNetwork.TopologyVersion != cachedTopologyVersion))
        {
            routeDirty = true;
        }

        RebuildIfRequired();
    }

    private void MarkRouteDirty()
    {
        routeDirty = true;
    }

    private void RebuildIfRequired()
    {
        if (!routeDirty)
            return;

        routeDirty = false;
        rebuildVersion++;
        routeValid = false;
        orderedStops.Clear();
        routeRoadCells.Clear();

        if (transportManager == null)
            return;

        cachedRoadNetwork = transportManager.RoadNetwork;
        cachedTopologyVersion = cachedRoadNetwork != null
            ? cachedRoadNetwork.TopologyVersion
            : -1;

        GridManager gridManager = transportManager.GridManager;
        if (cachedRoadNetwork == null || gridManager == null)
            return;

        foreach (PublicTransportStop stop in transportManager.RegisteredStops)
        {
            if (stop != null && stop.IsRegistered)
                orderedStops.Add(stop);
        }

        orderedStops.Sort(CompareStops);

        if (orderedStops.Count == 0)
            return;

        List<Vector2Int> stopRoadCells = new List<Vector2Int>(orderedStops.Count);

        for (int i = 0; i < orderedStops.Count; i++)
        {
            if (!orderedStops[i].TryGetAdjacentRoadCell(
                    cachedRoadNetwork, gridManager, out Vector2Int roadCell))
            {
                routeRoadCells.Clear();
                return;
            }

            stopRoadCells.Add(roadCell);
        }

        if (orderedStops.Count == 1)
        {
            routeRoadCells.Add(stopRoadCells[0]);
            routeValid = true;
            return;
        }

        for (int i = 0; i < stopRoadCells.Count; i++)
        {
            int nextIndex = (i + 1) % stopRoadCells.Count;
            List<Vector2Int> segment = cachedRoadNetwork.FindPath(
                stopRoadCells[i], stopRoadCells[nextIndex]);

            if (segment == null || segment.Count == 0)
            {
                routeRoadCells.Clear();
                return;
            }

            int firstCell = routeRoadCells.Count > 0 ? 1 : 0;
            for (int cellIndex = firstCell; cellIndex < segment.Count; cellIndex++)
                routeRoadCells.Add(segment[cellIndex]);
        }

        routeValid = routeRoadCells.Count > 0;
    }

    private static int CompareStops(
        PublicTransportStop a,
        PublicTransportStop b)
    {
        int orderComparison = a.RouteOrder.CompareTo(b.RouteOrder);
        if (orderComparison != 0)
            return orderComparison;

        return a.GetInstanceID().CompareTo(b.GetInstanceID());
    }

    public bool TryGetStop(int index, out PublicTransportStop stop)
    {
        if (index >= 0 && index < orderedStops.Count)
        {
            stop = orderedStops[index];
            return stop != null;
        }

        stop = null;
        return false;
    }
}
