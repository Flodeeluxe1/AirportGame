using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum RoomPurpose
{
    None,
    FlightSchool
}

public class RoomPurposeController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private GridManager gridManager;
    [SerializeField] private GridSurface gridSurface;
    [SerializeField] private EdgeOccupancy edgeOccupancy;
    [SerializeField] private RoomDetectionController roomDetection;
    [SerializeField] private BuildController buildController;
    [SerializeField] private RoomInfoPanelUIController infoPanel;

    [Header("Flight School Rules")]
    [SerializeField, Min(1)] private int minimumFoundationCells = 20;

    [Header("Input")]
    [SerializeField, Min(0.1f)] private float doubleClickSeconds = 0.35f;

    private readonly Dictionary<Vector2Int, RoomPurpose> assignments =
        new Dictionary<Vector2Int, RoomPurpose>();

    private Vector2Int previousClickCell;
    private float previousClickTime = -10f;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        if (Mouse.current == null ||
            !Mouse.current.leftButton.wasPressedThisFrame)
        {
            return;
        }

        Vector2 mousePosition = Mouse.current.position.ReadValue();

        // Do not interpret clicks on the information panel as world clicks.
        if (infoPanel != null &&
            infoPanel.IsPointerOverPanel(mousePosition))
        {
            return;
        }

        if (buildController != null && buildController.IsBuilding)
            return;

        if (!TryGetMouseCell(out Vector2Int cell))
            return;

        float now = Time.unscaledTime;

        bool isDoubleClick =
            cell == previousClickCell &&
            now - previousClickTime <= doubleClickSeconds;

        previousClickCell = cell;
        previousClickTime = now;

        if (!isDoubleClick)
            return;

        if (roomDetection == null)
            return;

        roomDetection.RefreshRooms();

        if (!roomDetection.TryGetRoomAt(cell, out var room))
        {
            Debug.Log(
                "Room information: double-click a floor cell inside an enclosed room.");
            return;
        }

        if (infoPanel == null)
        {
            Debug.LogWarning(
                "RoomInfoPanelController is not assigned or found.");
            return;
        }

        infoPanel.OpenForRoom(room);
    }

    private void ResolveReferences()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (gridSurface == null && gridManager != null)
            gridSurface = gridManager.GetComponent<GridSurface>();

        if (edgeOccupancy == null)
            edgeOccupancy = FindFirstObjectByType<EdgeOccupancy>();

        if (roomDetection == null)
            roomDetection = FindFirstObjectByType<RoomDetectionController>();

        if (buildController == null)
            buildController = FindFirstObjectByType<BuildController>();

        if (infoPanel == null)
            infoPanel = FindFirstObjectByType<RoomInfoPanelUIController>();
    }

    private bool TryGetMouseCell(out Vector2Int cell)
    {
        cell = Vector2Int.zero;

        if (mainCamera == null ||
            gridManager == null ||
            Mouse.current == null)
        {
            return false;
        }

        Ray ray = mainCamera.ScreenPointToRay(
            Mouse.current.position.ReadValue());

        Plane gridPlane = new Plane(
            Vector3.up,
            gridManager.transform.position);

        if (!gridPlane.Raycast(ray, out float distance))
            return false;

        cell = gridManager.WorldToGrid(ray.GetPoint(distance));
        return gridManager.IsInsideGrid(cell);
    }

    private Vector2Int GetAnchor(
        RoomDetectionController.DetectedRoom room)
    {
        Vector2Int anchor = room.cells[0];

        foreach (Vector2Int cell in room.cells)
        {
            if (cell.x < anchor.x ||
                (cell.x == anchor.x && cell.y < anchor.y))
            {
                anchor = cell;
            }
        }

        return anchor;
    }

    public RoomPurpose GetAssignedPurpose(
        RoomDetectionController.DetectedRoom room)
    {
        if (room == null ||
            room.cells == null ||
            room.cells.Count == 0)
        {
            return RoomPurpose.None;
        }

        Vector2Int anchor = GetAnchor(room);

        return assignments.TryGetValue(anchor, out RoomPurpose purpose)
            ? purpose
            : RoomPurpose.None;
    }

    public void AssignPurpose(
        RoomDetectionController.DetectedRoom room,
        RoomPurpose purpose)
    {
        if (room == null ||
            room.cells == null ||
            room.cells.Count == 0)
        {
            Debug.LogWarning("Cannot assign a purpose to an empty room.");
            return;
        }

        Vector2Int anchor = GetAnchor(room);

        if (purpose == RoomPurpose.None)
        {
            assignments.Remove(anchor);
            Debug.Log($"Room at {anchor} has no assigned purpose.");
            return;
        }

        // Assignment is allowed even if requirements are not yet met.
        // Validation controls operational status, not the ability to assign.
        assignments[anchor] = purpose;

        if (purpose == RoomPurpose.FlightSchool)
        {
            bool valid = ValidateFlightSchool(room, out string reason);

            Debug.Log(
                valid
                    ? $"Flight School assigned at {anchor}: VALID."
                    : $"Flight School assigned at {anchor}: INVALID — {reason}");
        }
        else
        {
            Debug.Log($"Room at {anchor} assigned to {purpose}.");
        }
    }

    /// <summary>
    /// Creates serializable copies of the current room-purpose assignments.
    /// The room's minimum cell is used as its stable anchor.
    /// </summary>
    public List<RoomPurposeAssignmentSaveData> GetAssignmentsForSave()
    {
        List<RoomPurposeAssignmentSaveData> savedAssignments =
            new List<RoomPurposeAssignmentSaveData>();

        foreach (KeyValuePair<Vector2Int, RoomPurpose> assignment in assignments)
        {
            if (assignment.Value == RoomPurpose.None)
                continue;

            savedAssignments.Add(new RoomPurposeAssignmentSaveData
            {
                anchorX = assignment.Key.x,
                anchorZ = assignment.Key.y,
                purpose = (int)assignment.Value
            });
        }

        return savedAssignments;
    }

    /// <summary>
    /// Clears all room-purpose assignments.
    /// </summary>
    public void ClearAssignments()
    {
        assignments.Clear();

        previousClickCell = Vector2Int.zero;
        previousClickTime = -10f;

        Debug.Log("RoomPurposeController: Room-purpose assignments cleared.");
    }

    /// <summary>
    /// Restores assignments after the airport foundations and boundaries
    /// have been recreated and room detection has been refreshed.
    /// Each saved anchor must belong to a currently detected enclosed room.
    /// </summary>
    public void RestoreAssignments(
        List<RoomPurposeAssignmentSaveData> savedAssignments)
    {
        assignments.Clear();
        ResolveReferences();

        if (roomDetection == null)
        {
            Debug.LogWarning(
                "RoomPurposeController: Cannot restore room assignments " +
                "because RoomDetectionController was not found.");
            return;
        }

        roomDetection.RefreshRooms();

        if (savedAssignments == null || savedAssignments.Count == 0)
        {
            Debug.Log(
                "RoomPurposeController: No saved room-purpose assignments to restore.");
            return;
        }

        int restoredCount = 0;

        foreach (RoomPurposeAssignmentSaveData saved in savedAssignments)
        {
            if (saved == null)
                continue;

            RoomPurpose purpose = (RoomPurpose)saved.purpose;

            if (purpose == RoomPurpose.None ||
                !System.Enum.IsDefined(typeof(RoomPurpose), purpose))
            {
                Debug.LogWarning(
                    $"RoomPurposeController: Skipped invalid saved room purpose " +
                    $"'{saved.purpose}' at ({saved.anchorX}, {saved.anchorZ}).");
                continue;
            }

            Vector2Int anchor = new Vector2Int(
                saved.anchorX,
                saved.anchorZ);

            RoomDetectionController.DetectedRoom matchedRoom = null;

            foreach (RoomDetectionController.DetectedRoom room in roomDetection.Rooms)
            {
                if (room == null ||
                    room.cells == null ||
                    room.cells.Count == 0)
                {
                    continue;
                }

                if (room.cells.Contains(anchor))
                {
                    matchedRoom = room;
                    break;
                }
            }

            if (matchedRoom == null)
            {
                Debug.LogWarning(
                    $"RoomPurposeController: Could not restore assignment " +
                    $"'{purpose}' at anchor {anchor}. No detected enclosed room " +
                    "contains that cell. The assignment was skipped.");
                continue;
            }

            Vector2Int currentAnchor = GetAnchor(matchedRoom);
            assignments[currentAnchor] = purpose;
            restoredCount++;

            if (purpose == RoomPurpose.FlightSchool)
            {
                bool valid = ValidateFlightSchool(
                    matchedRoom,
                    out string reason);

                Debug.Log(
                    valid
                        ? $"RoomPurposeController: Restored Flight School at " +
                          $"{currentAnchor}: VALID."
                        : $"RoomPurposeController: Restored Flight School at " +
                          $"{currentAnchor}: INVALID — {reason}");
            }
            else
            {
                Debug.Log(
                    $"RoomPurposeController: Restored '{purpose}' at {currentAnchor}.");
            }
        }

        Debug.Log(
            $"RoomPurposeController: Restored {restoredCount} of " +
            $"{savedAssignments.Count} saved room-purpose assignment(s).");
    }

    /// <summary>
    /// Returns all currently detected rooms assigned as Flight Schools.
    /// Validation is intentionally left to the caller so invalid assigned
    /// rooms remain visible and can become operational again after repairs.
    /// </summary>
    public List<RoomDetectionController.DetectedRoom> GetAssignedFlightSchoolRooms()
    {
        ResolveReferences();

        List<RoomDetectionController.DetectedRoom> result =
            new List<RoomDetectionController.DetectedRoom>();

        if (roomDetection == null)
            return result;

        roomDetection.RefreshRooms();

        foreach (RoomDetectionController.DetectedRoom room in roomDetection.Rooms)
        {
            if (room != null && IsFlightSchoolAssigned(room))
                result.Add(room);
        }

        return result;
    }

    public bool ValidateFlightSchool(
        RoomDetectionController.DetectedRoom room,
        out string reason)
    {
        reason = "";

        if (room == null || room.cells == null)
        {
            reason = "Room was not detected.";
            return false;
        }

        if (room.CellCount < minimumFoundationCells)
        {
            reason =
                $"Needs at least {minimumFoundationCells} foundation cells; found {room.CellCount}.";
            return false;
        }

        if (!HasDoor(room))
        {
            reason = "Needs at least one door on its boundary.";
            return false;
        }

        int desks = 0;
        int seats = 0;

        HashSet<Vector2Int> roomCells =
            new HashSet<Vector2Int>(room.cells);

        PlacedBuilding[] placedBuildings =
            FindObjectsByType<PlacedBuilding>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

        foreach (PlacedBuilding placed in placedBuildings)
        {
            if (placed == null || placed.Definition == null)
                continue;

            RoomFurnitureRole role =
                placed.Definition.roomFurnitureRole;

            if (role == RoomFurnitureRole.None)
                continue;

            if (!BuildingTouchesRoom(placed, roomCells))
                continue;

            if (role == RoomFurnitureRole.InstructorDesk)
                desks++;
            else if (role == RoomFurnitureRole.StudentSeat)
                seats++;
        }

        if (desks < 1)
        {
            reason = "Needs at least one instructor desk.";
            return false;
        }

        if (seats < 1)
        {
            reason = "Needs at least one student seat.";
            return false;
        }

        return true;
    }

    private bool BuildingTouchesRoom(
        PlacedBuilding building,
        HashSet<Vector2Int> roomCells)
    {
        if (building.IsEdgeBuilding)
            return false;

        for (int x = 0; x < Mathf.Max(1, building.Width); x++)
        {
            for (int z = 0; z < Mathf.Max(1, building.Depth); z++)
            {
                Vector2Int cell =
                    building.Origin + new Vector2Int(x, z);

                if (roomCells.Contains(cell))
                    return true;
            }
        }

        return false;
    }

    private bool HasDoor(
        RoomDetectionController.DetectedRoom room)
    {
        if (edgeOccupancy == null)
            return false;

        Vector2Int[] directions =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        foreach (Vector2Int cell in room.cells)
        {
            foreach (Vector2Int direction in directions)
            {
                Vector2Int neighbor = cell + direction;

                // Internal edges do not count as room entrances.
                if (room.cells.Contains(neighbor))
                    continue;

                GridEdge edge = GetSharedEdge(cell, direction);

                if (!edgeOccupancy.TryGetOccupant(
                        edge, out GameObject boundary))
                {
                    continue;
                }

                if (boundary == null)
                    continue;

                PlacedBuilding placed =
                    boundary.GetComponent<PlacedBuilding>();

                if (placed == null || placed.Definition == null)
                    continue;

                if (placed.Definition.category == BuildCategory.Door)
                    return true;
            }
        }

        return false;
    }

    private GridEdge GetSharedEdge(
        Vector2Int cell,
        Vector2Int direction)
    {
        if (direction == Vector2Int.down)
            return new GridEdge(cell, EdgeDirection.Horizontal);

        if (direction == Vector2Int.up)
        {
            return new GridEdge(
                cell + Vector2Int.up,
                EdgeDirection.Horizontal);
        }

        if (direction == Vector2Int.left)
            return new GridEdge(cell, EdgeDirection.Vertical);

        return new GridEdge(
            cell + Vector2Int.right,
            EdgeDirection.Vertical);
    }

    public bool IsFlightSchoolAssigned(
        RoomDetectionController.DetectedRoom room)
    {
        return GetAssignedPurpose(room) == RoomPurpose.FlightSchool;
    }

    public bool IsFlightSchoolValid(
        RoomDetectionController.DetectedRoom room)
    {
        return IsFlightSchoolAssigned(room) &&
               ValidateFlightSchool(room, out _);
    }
}