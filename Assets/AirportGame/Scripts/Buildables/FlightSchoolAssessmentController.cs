
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Serializable assessment results for one player-assigned Flight School room.
/// Parking figures represent the shared airport-wide training parking pool.
/// </summary>
[Serializable]
public class FlightSchoolRoomAssessment
{
    [Header("Room Identity")]
    public int anchorX;
    public int anchorZ;

    [Header("Room Measurements")]
    public int usableFoundationCells;
    public int instructorDeskCount;
    public int studentSeatCount;
    public int studentCapacity;

    [Header("Facility Quality")]
    [Range(0, 40)] public int areaQualityPoints;
    [Range(0, 40)] public int seatingQualityPoints;
    [Range(0, 20)] public int instructorDeskQualityPoints;
    [Range(0, 100)] public int facilityQualityScore;

    [Header("Operational Requirements")]
    public bool meetsMinimumOperationalRequirements;
    public string operationalRequirementMessage;

    [Header("Shared Airport Training Parking")]
    public int designatedTrainingParkingSpotCount;
    public int occupiedTrainingParkingSpotCount;
    public int availableTrainingParkingSpotCount;

    public Vector2Int RoomAnchor => new Vector2Int(anchorX, anchorZ);
}

/// <summary>
/// Calculates read-only Flight School assessments for assigned rooms.
/// This component does not spawn aircraft, reserve parking, pay rewards,
/// change room assignments, or control training lessons.
/// </summary>
[DisallowMultipleComponent]
public class FlightSchoolAssessmentController : MonoBehaviour
{
    private const int MinimumFoundationCells = 20;

    [Header("References")]
    [SerializeField]
    private RoomPurposeController roomPurposeController;

    [Header("Assessment Refresh")]
    [SerializeField, Min(0.1f)]
    private float refreshInterval = 0.5f;

    [Header("Runtime Assessment Results")]
    [SerializeField]
    private List<FlightSchoolRoomAssessment> assessments =
        new List<FlightSchoolRoomAssessment>();

    [SerializeField]
    private string debugStatus = "Waiting for Flight School rooms.";

    private float nextRefreshTime;

    /// <summary>
    /// Current assessment results, one per detected assigned Flight School.
    /// Other systems can read these results without modifying the list.
    /// </summary>
    public IReadOnlyList<FlightSchoolRoomAssessment> Assessments =>
        assessments;

    public string DebugStatus => debugStatus;

    private sealed class FurnitureCounts
    {
        public int instructorDesks;
        public int studentSeats;
    }

    private void Awake()
    {
        ResolveReferences();
        RefreshAssessments();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
            return;

        RefreshAssessments();
    }

    private void ResolveReferences()
    {
        if (roomPurposeController == null)
        {
            roomPurposeController =
                FindFirstObjectByType<RoomPurposeController>();
        }
    }

    [ContextMenu("Refresh Flight School Assessments")]
    public void RefreshAssessments()
    {
        nextRefreshTime =
            Time.unscaledTime + Mathf.Max(0.1f, refreshInterval);

        ResolveReferences();

        assessments.Clear();

        if (roomPurposeController == null)
        {
            debugStatus =
                "RoomPurposeController was not found in the scene.";
            return;
        }

        List<RoomDetectionController.DetectedRoom> rooms =
            roomPurposeController.GetAssignedFlightSchoolRooms();

        CountSharedTrainingParking(
            out int totalParking,
            out int occupiedParking,
            out int availableParking);

        Dictionary<Vector2Int, FurnitureCounts> furnitureByRoom =
            CountFurnitureByRoom(rooms);

        foreach (RoomDetectionController.DetectedRoom room in rooms)
        {
            if (room == null ||
                room.cells == null ||
                room.cells.Count == 0)
            {
                continue;
            }

            Vector2Int anchor = GetAnchor(room);

            if (!furnitureByRoom.TryGetValue(
                    anchor, out FurnitureCounts furniture))
            {
                furniture = new FurnitureCounts();
            }

            bool meetsRequirements =
                roomPurposeController.ValidateFlightSchool(
                    room, out string validationReason);

            int areaPoints =
                CalculateAreaQualityPoints(room.CellCount);

            int seatingPoints =
                CalculateSeatingQualityPoints(furniture.studentSeats);

            int deskPoints =
                CalculateInstructorDeskQualityPoints(
                    furniture.instructorDesks);

            FlightSchoolRoomAssessment assessment =
                new FlightSchoolRoomAssessment
                {
                    anchorX = anchor.x,
                    anchorZ = anchor.y,

                    usableFoundationCells = room.CellCount,
                    instructorDeskCount = furniture.instructorDesks,
                    studentSeatCount = furniture.studentSeats,
                    studentCapacity = furniture.studentSeats,

                    areaQualityPoints = areaPoints,
                    seatingQualityPoints = seatingPoints,
                    instructorDeskQualityPoints = deskPoints,
                    facilityQualityScore =
                        areaPoints + seatingPoints + deskPoints,

                    meetsMinimumOperationalRequirements =
                        meetsRequirements,

                    operationalRequirementMessage = meetsRequirements
                        ? "Meets all minimum room requirements."
                        : validationReason,

                    designatedTrainingParkingSpotCount =
                        totalParking,

                    occupiedTrainingParkingSpotCount =
                        occupiedParking,

                    availableTrainingParkingSpotCount =
                        availableParking
                };

            assessments.Add(assessment);
        }

        int operationalCount = 0;

        foreach (FlightSchoolRoomAssessment assessment in assessments)
        {
            if (assessment.meetsMinimumOperationalRequirements)
                operationalCount++;
        }

        debugStatus = assessments.Count == 0
            ? "No assigned Flight School rooms detected."
            : $"Assessed {assessments.Count} Flight School room(s); " +
              $"{operationalCount} meet minimum requirements.";
    }

    public bool TryGetAssessment(
        Vector2Int roomAnchor,
        out FlightSchoolRoomAssessment assessment)
    {
        foreach (FlightSchoolRoomAssessment item in assessments)
        {
            if (item != null && item.RoomAnchor == roomAnchor)
            {
                assessment = item;
                return true;
            }
        }

        assessment = null;
        return false;
    }

    private Dictionary<Vector2Int, FurnitureCounts> CountFurnitureByRoom(
        List<RoomDetectionController.DetectedRoom> rooms)
    {
        Dictionary<Vector2Int, FurnitureCounts> result =
            new Dictionary<Vector2Int, FurnitureCounts>();

        foreach (RoomDetectionController.DetectedRoom room in rooms)
        {
            if (room == null ||
                room.cells == null ||
                room.cells.Count == 0)
            {
                continue;
            }

            result[GetAnchor(room)] = new FurnitureCounts();
        }

        PlacedBuilding[] placedBuildings =
            FindObjectsByType<PlacedBuilding>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

        foreach (PlacedBuilding building in placedBuildings)
        {
            if (building == null ||
                building.Definition == null ||
                building.IsEdgeBuilding)
            {
                continue;
            }

            RoomFurnitureRole role =
                building.Definition.roomFurnitureRole;

            if (role != RoomFurnitureRole.InstructorDesk &&
                role != RoomFurnitureRole.StudentSeat)
            {
                continue;
            }

            // Attribute each placed furniture object to at most one room.
            // Normally its origin lies inside the room it belongs to.
            RoomDetectionController.DetectedRoom owner =
                FindOwningRoom(building, rooms);

            if (owner == null)
                continue;

            Vector2Int anchor = GetAnchor(owner);

            if (!result.TryGetValue(anchor, out FurnitureCounts counts))
                continue;

            if (role == RoomFurnitureRole.InstructorDesk)
                counts.instructorDesks++;
            else if (role == RoomFurnitureRole.StudentSeat)
                counts.studentSeats++;
        }

        return result;
    }

    private RoomDetectionController.DetectedRoom FindOwningRoom(
        PlacedBuilding building,
        List<RoomDetectionController.DetectedRoom> rooms)
    {
        // Prefer the room containing the building's origin cell.
        foreach (RoomDetectionController.DetectedRoom room in rooms)
        {
            if (room != null &&
                room.cells != null &&
                room.cells.Contains(building.Origin))
            {
                return room;
            }
        }

        // Fallback for unusual footprints whose origin is outside a room.
        // Return only one matching room so the object is never counted twice.
        foreach (RoomDetectionController.DetectedRoom room in rooms)
        {
            if (room == null ||
                room.cells == null ||
                room.cells.Count == 0)
            {
                continue;
            }

            HashSet<Vector2Int> roomCells =
                new HashSet<Vector2Int>(room.cells);

            if (BuildingTouchesRoom(building, roomCells))
                return room;
        }

        return null;
    }

    private bool BuildingTouchesRoom(
        PlacedBuilding building,
        HashSet<Vector2Int> roomCells)
    {
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

    private void CountSharedTrainingParking(
        out int total,
        out int occupied,
        out int available)
    {
        total = 0;
        occupied = 0;
        available = 0;

        HobbyAircraftParkingSpot[] spots =
            FindObjectsByType<HobbyAircraftParkingSpot>(
                FindObjectsSortMode.None);

        foreach (HobbyAircraftParkingSpot spot in spots)
        {
            if (spot == null ||
                !spot.IsPlacedBuilding ||
                !spot.ReservedForFlightSchool)
            {
                continue;
            }

            total++;

            if (spot.IsOccupied)
                occupied++;
            else
                available++;
        }
    }

    private int CalculateAreaQualityPoints(int foundationCells)
    {
        if (foundationCells < MinimumFoundationCells)
            return 0;

        if (foundationCells <= 29)
            return 10;

        if (foundationCells <= 39)
            return 20;

        if (foundationCells <= 59)
            return 30;

        return 40;
    }

    private int CalculateSeatingQualityPoints(int studentSeats)
    {
        if (studentSeats <= 0)
            return 0;

        if (studentSeats == 1)
            return 10;

        if (studentSeats <= 3)
            return 20;

        if (studentSeats <= 5)
            return 30;

        return 40;
    }

    private int CalculateInstructorDeskQualityPoints(int instructorDesks)
    {
        if (instructorDesks <= 0)
            return 0;

        if (instructorDesks == 1)
            return 10;

        return 20;
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
}
