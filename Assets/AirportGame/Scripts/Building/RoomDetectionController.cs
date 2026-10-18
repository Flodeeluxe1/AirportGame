
using System;
using System.Collections.Generic;
using UnityEngine;

public class RoomDetectionController : MonoBehaviour
{
    [Serializable]
    public class DetectedRoom
    {
        public List<Vector2Int> cells = new List<Vector2Int>();

        public int CellCount => cells.Count;

        public Vector2 Center
        {
            get
            {
                if (cells.Count == 0)
                    return Vector2.zero;

                Vector2 total = Vector2.zero;

                foreach (Vector2Int cell in cells)
                {
                    total += new Vector2(
                        cell.x + 0.5f,
                        cell.y + 0.5f
                    );
                }

                return total / cells.Count;
            }
        }
    }

    [Header("Grid References")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private GridSurface gridSurface;
    [SerializeField] private EdgeOccupancy edgeOccupancy;

    [Header("Detection")]
    [SerializeField, Min(0.1f)]
    private float refreshInterval = 0.5f;

    [SerializeField]
    private bool logRoomChanges = true;

    private readonly List<DetectedRoom> detectedRooms =
        new List<DetectedRoom>();

    private float nextRefreshTime;
    private string previousRoomSignature = "";

    public IReadOnlyList<DetectedRoom> Rooms => detectedRooms;

    public int RoomCount => detectedRooms.Count;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Start()
    {
        RefreshRooms();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime =
            Time.unscaledTime + refreshInterval;

        RefreshRooms();
    }

    private void ResolveReferences()
    {
        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (gridSurface == null && gridManager != null)
        {
            gridSurface =
                gridManager.GetComponent<GridSurface>();
        }

        if (edgeOccupancy == null)
        {
            edgeOccupancy =
                FindFirstObjectByType<EdgeOccupancy>();
        }
    }

    [ContextMenu("Refresh Rooms")]
    public void RefreshRooms()
    {
        ResolveReferences();

        detectedRooms.Clear();

        if (gridManager == null ||
            gridSurface == null ||
            edgeOccupancy == null)
        {
            Debug.LogWarning(
                "RoomDetectionController: Missing grid references."
            );

            return;
        }

        HashSet<Vector2Int> remainingFloor =
            new HashSet<Vector2Int>();

        for (int x = 0; x < gridManager.Width; x++)
        {
            for (int z = 0; z < gridManager.Height; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);

                if (gridSurface.GetSurface(cell) ==
                    BuildSurface.Foundation)
                {
                    remainingFloor.Add(cell);
                }
            }
        }

        while (remainingFloor.Count > 0)
        {
            Vector2Int start = default;

            foreach (Vector2Int cell in remainingFloor)
            {
                start = cell;
                break;
            }

            List<Vector2Int> component =
                CollectRoomComponent(start, remainingFloor);

            if (component.Count == 0)
                break;

            if (IsEnclosed(component))
            {
                DetectedRoom room = new DetectedRoom();
                room.cells.AddRange(component);
                detectedRooms.Add(room);
            }
        }

        detectedRooms.Sort(
            (a, b) => b.CellCount.CompareTo(a.CellCount)
        );

        string signature = BuildRoomSignature();

        if (logRoomChanges &&
            signature != previousRoomSignature)
        {
            Debug.Log(
                $"RoomDetectionController: Found " +
                $"{detectedRooms.Count} enclosed room(s). " +
                $"Floor cells: {CountDetectedCells()}."
            );
        }

        previousRoomSignature = signature;
    }

    private List<Vector2Int> CollectRoomComponent(
        Vector2Int start,
        HashSet<Vector2Int> remainingFloor)
    {
        List<Vector2Int> component =
            new List<Vector2Int>();

        Queue<Vector2Int> queue =
            new Queue<Vector2Int>();

        if (!remainingFloor.Remove(start))
            return component;

        queue.Enqueue(start);

        Vector2Int[] directions =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            component.Add(current);

            foreach (Vector2Int direction in directions)
            {
                Vector2Int neighbor = current + direction;

                if (!remainingFloor.Contains(neighbor))
                    continue;

                GridEdge sharedEdge =
                    GetSharedEdge(current, direction);

                // A wall or door separates rooms.
                if (IsValidBoundary(sharedEdge))
                    continue;

                remainingFloor.Remove(neighbor);
                queue.Enqueue(neighbor);
            }
        }

        return component;
    }

    private bool IsEnclosed(List<Vector2Int> component)
    {
        HashSet<Vector2Int> roomCells =
            new HashSet<Vector2Int>(component);

        Vector2Int[] directions =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        foreach (Vector2Int cell in component)
        {
            foreach (Vector2Int direction in directions)
            {
                Vector2Int neighbor = cell + direction;

                bool neighborIsSameRoom =
                    roomCells.Contains(neighbor) &&
                    !IsValidBoundary(
                        GetSharedEdge(cell, direction)
                    );

                if (neighborIsSameRoom)
                    continue;

                // Every exposed edge must have a wall or door.
                if (!IsValidBoundary(
                        GetSharedEdge(cell, direction)))
                {
                    return false;
                }
            }
        }

        return component.Count > 0;
    }

    private GridEdge GetSharedEdge(
        Vector2Int cell,
        Vector2Int direction)
    {
        if (direction == Vector2Int.down)
        {
            return new GridEdge(
                cell,
                EdgeDirection.Horizontal
            );
        }

        if (direction == Vector2Int.up)
        {
            return new GridEdge(
                cell + Vector2Int.up,
                EdgeDirection.Horizontal
            );
        }

        if (direction == Vector2Int.left)
        {
            return new GridEdge(
                cell,
                EdgeDirection.Vertical
            );
        }

        // Right edge
        return new GridEdge(
            cell + Vector2Int.right,
            EdgeDirection.Vertical
        );
    }

    private bool IsValidBoundary(GridEdge edge)
    {
        if (!edgeOccupancy.TryGetOccupant(
                edge,
                out GameObject boundaryObject))
        {
            return false;
        }

        if (boundaryObject == null)
            return false;

        PlacedBuilding placedBuilding =
            boundaryObject.GetComponent<PlacedBuilding>();

        if (placedBuilding == null ||
            placedBuilding.Definition == null)
        {
            return false;
        }

        BuildCategory category =
            placedBuilding.Definition.category;

        return category == BuildCategory.Wall ||
               category == BuildCategory.Door;
    }

    private int CountDetectedCells()
    {
        int total = 0;

        foreach (DetectedRoom room in detectedRooms)
            total += room.CellCount;

        return total;
    }

    private string BuildRoomSignature()
    {
        List<string> roomParts = new List<string>();

        foreach (DetectedRoom room in detectedRooms)
        {
            List<string> cellParts = new List<string>();

            foreach (Vector2Int cell in room.cells)
                cellParts.Add($"{cell.x},{cell.y}");

            cellParts.Sort(StringComparer.Ordinal);

            roomParts.Add(string.Join(";", cellParts));
        }

        roomParts.Sort(StringComparer.Ordinal);

        return string.Join("|", roomParts);
    }

    public bool TryGetRoomAt(
        Vector2Int cell,
        out DetectedRoom foundRoom)
    {
        foreach (DetectedRoom room in detectedRooms)
        {
            if (room.cells.Contains(cell))
            {
                foundRoom = room;
                return true;
            }
        }

        foundRoom = null;
        return false;
    }
}
