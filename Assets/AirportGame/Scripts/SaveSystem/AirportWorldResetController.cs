
using System.Collections.Generic;
using UnityEngine;

public class AirportWorldResetController : MonoBehaviour
{
    [Header("Building and Grid")]
    [SerializeField] private BuildController buildController;
    [SerializeField] private BuildCatalog buildCatalog;
    [SerializeField] private GridOccupancy gridOccupancy;
    [SerializeField] private EdgeOccupancy edgeOccupancy;
    [SerializeField] private GridSurface gridSurface;

    [Header("Roads")]
    [SerializeField] private RoadNetwork roadNetwork;
    [SerializeField] private RoadTilemapController roadTilemapController;

    [Header("Taxiways")]
    [SerializeField] private TaxiwayNetwork taxiwayNetwork;
    [SerializeField] private TaxiwayTilemapController taxiwayTilemapController;

    [Header("Foundation")]
    [SerializeField] private FoundationTilemapController foundationTilemapController;

    [Header("Game Time")]
    [SerializeField] private GameTimeManager gameTimeManager;

    [Header("Economy")]
    [SerializeField] private AirportEconomyManager economyManager;

    [Header("Flight Schedule")]
    [SerializeField] private FlightManagementUIController flightManagementUIController;

    [Header("Room Assignments")]
    [SerializeField] private RoomPurposeController roomPurposeController;
    [SerializeField] private RoomDetectionController roomDetectionController;

    [ContextMenu("TEST - Reset Airport Layout")]
    private void TestResetAirportLayout()
    {
        ResetAirportLayout();
    }

    [ContextMenu("TEST - Save Airport To Slot 1")]
    private void TestSaveSlot1()
    {
        TrySaveSlot(1, "Test Airport");
    }

    [ContextMenu("TEST - Validate Load From Slot 1")]
    private void TestValidateLoadSlot1()
    {
        TryValidateLoadSlot(1);
    }

    [ContextMenu("TEST - Restore Airport From Slot 1")]
    private void TestRestoreBuildingsAndFoundationsSlot1()
    {
        TryRestoreBuildingsAndFoundations(1);
    }

    /// <summary>
    /// Removes the current airport layout and clears room-purpose
    /// assignments. Does not reset the game clock, economy, active
    /// flight schedule, or delete any save files.
    /// </summary>
    public bool ResetAirportLayout()
    {
        if (!ValidateReferences())
            return false;

        ResolveRoomPurposeReferences();

        if (roomPurposeController != null)
            roomPurposeController.ClearAssignments();

        buildController.CancelBuilding();

        PlacedBuilding[] placedBuildings =
            FindObjectsByType<PlacedBuilding>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

        foreach (PlacedBuilding placedBuilding in placedBuildings)
        {
            if (placedBuilding != null)
                Destroy(placedBuilding.gameObject);
        }

        gridOccupancy.Clear();
        edgeOccupancy.Clear();
        gridSurface.ResetAllSurfaces();

        roadNetwork.Clear();
        roadTilemapController.ClearAllRoadTiles();

        taxiwayNetwork.Clear();
        taxiwayTilemapController.ClearAllTaxiwayTiles();

        foundationTilemapController.RefreshFromGridSurface();

        if (roomDetectionController != null)
            roomDetectionController.RefreshRooms();

        Debug.Log(
            "AirportWorldResetController: Airport layout reset.");

        return true;
    }

    /// <summary>
    /// Starts a fresh airport with an empty layout, cleared active
    /// flight schedule and room assignments, reset clock, and new-game
    /// starting balance. Existing airport save slots remain untouched.
    /// </summary>
    public bool StartNewGame()
    {
        if (!ValidateReferences())
            return false;

        if (!EnsureFlightManagementController())
            return false;

        if (!EnsureEconomyManager())
            return false;

        if (!ResetAirportLayout())
            return false;

        flightManagementUIController.ClearActiveSchedule();
        gameTimeManager.ResetToStartTime();
        economyManager.InitializeNewGame();

        Debug.Log(
            "AirportWorldResetController: New game initialized. " +
            "Airport layout, room assignments, and active flight schedule " +
            "cleared; game clock reset; starting balance granted. " +
            "Existing saves were preserved.");

        return true;
    }

    private bool ValidateReferences()
    {
        if (buildController == null ||
            gridOccupancy == null ||
            edgeOccupancy == null ||
            gridSurface == null ||
            roadNetwork == null ||
            roadTilemapController == null ||
            taxiwayNetwork == null ||
            taxiwayTilemapController == null ||
            foundationTilemapController == null ||
            gameTimeManager == null)
        {
            Debug.LogError(
                "AirportWorldResetController: One or more required " +
                "references are missing. Assign all references in the Inspector.");

            return false;
        }

        return true;
    }

    private void ResolveRoomPurposeReferences()
    {
        if (roomPurposeController == null)
        {
            roomPurposeController =
                FindFirstObjectByType<RoomPurposeController>();
        }

        if (roomDetectionController == null)
        {
            roomDetectionController =
                FindFirstObjectByType<RoomDetectionController>();
        }
    }

    private bool EnsureFlightManagementController()
    {
        if (flightManagementUIController == null)
        {
            flightManagementUIController =
                FindFirstObjectByType<FlightManagementUIController>();
        }

        if (flightManagementUIController == null)
        {
            Debug.LogError(
                "AirportWorldResetController: FlightManagementUIController " +
                "was not found. Assign it in the Inspector before saving, " +
                "loading, or starting a new game.");

            return false;
        }

        return true;
    }

    private bool EnsureEconomyManager()
    {
        if (economyManager == null)
        {
            economyManager =
                FindFirstObjectByType<AirportEconomyManager>();
        }

        if (economyManager == null)
        {
            Debug.LogError(
                "AirportWorldResetController: AirportEconomyManager " +
                "was not found. Assign it in the Inspector before " +
                "starting a new game, saving, or loading an airport.");

            return false;
        }

        return true;
    }

    /// <summary>
    /// Saves the airport layout, roads, taxiways, game time,
    /// simulation speed, economy balance, object transforms,
    /// boundary ownership, flight schedule, and room-purpose assignments.
    /// </summary>
    public bool TrySaveSlot(int slot, string saveName)
    {
        if (!ValidateReferences())
            return false;

        if (!EnsureFlightManagementController())
            return false;

        if (!EnsureEconomyManager())
            return false;

        ResolveRoomPurposeReferences();

        AirportSaveData data = new AirportSaveData
        {
            saveName = string.IsNullOrWhiteSpace(saveName)
                ? $"Save Slot {slot}"
                : saveName,

            gameTimeSeconds = gameTimeManager.GetGameTimeSeconds(),
            gameSpeedMultiplier = gameTimeManager.CurrentSpeedMultiplier,
            balance = economyManager.Balance,

            flightSchedule =
                flightManagementUIController.GetScheduleForAirportSave(),

            roomPurposeAssignments =
                roomPurposeController != null
                    ? roomPurposeController.GetAssignmentsForSave()
                    : new List<RoomPurposeAssignmentSaveData>()
        };

        PlacedBuilding[] buildings =
            FindObjectsByType<PlacedBuilding>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

        foreach (PlacedBuilding building in buildings)
        {
            if (building == null || building.Definition == null)
                continue;

            string definitionId = building.Definition.id;

            if (string.IsNullOrWhiteSpace(definitionId))
            {
                Debug.LogError(
                    $"Cannot save '{building.name}': its " +
                    "BuildableDefinition has no ID.");

                return false;
            }

            PlacedBuildingSaveData savedBuilding = new PlacedBuildingSaveData
            {
                definitionId = definitionId,

                originX = building.Origin.x,
                originZ = building.Origin.y,
                width = building.Width,
                depth = building.Depth,
                rotation = building.Rotation,

                isEdgeBuilding = building.IsEdgeBuilding,
                edgeLength = building.EdgeLength,
                edgeDirection = (int)building.EdgeDirection,

                hasWorldTransform = true,

                positionX = building.transform.position.x,
                positionY = building.transform.position.y,
                positionZ = building.transform.position.z,

                rotationX = building.transform.rotation.x,
                rotationY = building.transform.rotation.y,
                rotationZ = building.transform.rotation.z,
                rotationW = building.transform.rotation.w,

                scaleX = building.transform.localScale.x,
                scaleY = building.transform.localScale.y,
                scaleZ = building.transform.localScale.z
            };


            HobbyAircraftParkingSpot parkingSpot =
                building.GetComponent<HobbyAircraftParkingSpot>();

            if (parkingSpot != null)
            {
                savedBuilding.hasFlightSchoolParkingDesignation = true;
                savedBuilding.reservedForFlightSchool =
                    parkingSpot.ReservedForFlightSchool;
            }

            FoundationBoundaryOwnership boundaryOwnership =
                building.GetComponent<FoundationBoundaryOwnership>();

            if (boundaryOwnership != null)
            {
                boundaryOwnership.Initialize(edgeOccupancy);

                savedBuilding.boundaryOwnershipId =
                    boundaryOwnership.OwnershipId;

                savedBuilding.boundaryOwnerIds =
                    new List<string>(boundaryOwnership.SavedOwnerIds);
            }

            if (building.Definition.placementType == PlacementType.Area)
            {
                savedBuilding.hasCustomFoundationFootprint = true;

                for (int x = 0; x < building.Width; x++)
                {
                    for (int z = 0; z < building.Depth; z++)
                    {
                        Vector2Int cell =
                            building.Origin + new Vector2Int(x, z);

                        if (gridSurface.IsSurfaceOwner(
                                cell,
                                building.gameObject))
                        {
                            savedBuilding.foundationCells.Add(cell);
                        }
                    }
                }
            }

            data.placedBuildings.Add(savedBuilding);
        }

        data.roadCells = roadNetwork.GetSaveData();
        data.taxiwayCells = taxiwayNetwork.GetSaveData();

        data.EnsureCollections();

        if (!AirportSaveFileStore.TrySave(
                slot,
                data,
                out string error))
        {
            Debug.LogError(
                $"AirportWorldResetController: Save failed for slot " +
                $"{slot}. {error}");

            return false;
        }

        Debug.Log(
            $"AirportWorldResetController: Saved slot {slot}. " +
            $"Buildings: {data.placedBuildings.Count}, " +
            $"Road cells: {data.roadCells.Count}, " +
            $"Taxiway cells: {data.taxiwayCells.Count}, " +
            $"Scheduled flights: {data.flightSchedule.Count}, " +
            $"Room assignments: {data.roomPurposeAssignments.Count}, " +
            $"Balance: ${data.balance:N0}.");

        return true;
    }

    /// <summary>
    /// Validates a save slot without changing the current airport.
    /// </summary>
    public bool TryValidateLoadSlot(int slot)
    {
        if (!ValidateReferences())
            return false;

        if (!ValidateBuildCatalog())
            return false;

        if (!AirportSaveFileStore.TryLoad(
                slot,
                out AirportSaveData data,
                out string error))
        {
            Debug.LogError(
                $"Could not read save slot {slot}: {error}");

            return false;
        }

        data.EnsureCollections();

        if (!ValidateSavedDefinitions(data, false))
            return false;

        Debug.Log(
            $"Save slot {slot} passed validation. " +
            $"Buildings: {data.placedBuildings.Count}, " +
            $"Road cells: {data.roadCells.Count}, " +
            $"Taxiway cells: {data.taxiwayCells.Count}, " +
            $"Scheduled flights: {data.flightSchedule.Count}, " +
            $"Room assignments: {data.roomPurposeAssignments.Count}, " +
            $"Balance: ${data.balance:N0}. " +
            "The airport has not been changed.");

        return true;
    }

    /// <summary>
    /// Replaces the current airport layout and restores its buildings,
    /// foundations, boundary ownership, roads, taxiways, room assignments,
    /// time, economy, and flight schedule.
    /// </summary>
    public bool TryRestoreBuildingsAndFoundations(int slot)
    {
        if (!ValidateReferences())
            return false;

        if (!ValidateBuildCatalog())
            return false;

        if (!EnsureFlightManagementController())
            return false;

        if (!EnsureEconomyManager())
            return false;

        if (!AirportSaveFileStore.TryLoad(
                slot,
                out AirportSaveData data,
                out string error))
        {
            Debug.LogError(
                $"Could not load slot {slot}: {error}. " +
                "The current airport has not been changed.");

            return false;
        }

        data.EnsureCollections();

        if (!ValidateSavedDefinitions(data, true))
            return false;

        foreach (TaxiwayCellSaveData savedTaxiway in data.taxiwayCells)
        {
            BuildableDefinition definition =
                FindDefinition(savedTaxiway.definitionId);

            if (definition == null ||
                taxiwayTilemapController.GetTileForDefinition(definition) == null)
            {
                Debug.LogError(
                    $"Cannot restore taxiway definition " +
                    $"'{savedTaxiway.definitionId}': no taxiway tile mapping " +
                    "was found. The current airport has not been changed.");

                return false;
            }
        }

        if (!ResetAirportLayout())
            return false;

        int restoredBuildings = 0;
        int restoredFoundations = 0;
        int restoredEdges = 0;
        int restoredRoadCells = 0;
        int restoredTaxiwayCells = 0;
        int restoredCenterlineCells = 0;

        List<FoundationBoundaryOwnership> restoredOwnershipComponents =
            new List<FoundationBoundaryOwnership>();

        // 1. Restore placed objects and logical grid/edge occupancy.
        foreach (PlacedBuildingSaveData saved in data.placedBuildings)
        {
            BuildableDefinition definition =
                FindDefinition(saved.definitionId);

            if (definition == null || definition.prefab == null)
            {
                Debug.LogError(
                    $"Cannot restore definition '{saved.definitionId}'.");

                return false;
            }

            Vector3 position = new Vector3(
                saved.positionX,
                saved.positionY,
                saved.positionZ);

            Quaternion rotation = new Quaternion(
                saved.rotationX,
                saved.rotationY,
                saved.rotationZ,
                saved.rotationW);

            GameObject placedObject = Instantiate(
                definition.prefab,
                position,
                rotation);

            placedObject.transform.localScale = new Vector3(
                saved.scaleX,
                saved.scaleY,
                saved.scaleZ);

            PlacedBuilding placedBuilding =
                placedObject.GetComponent<PlacedBuilding>();

            if (placedBuilding == null)
                placedBuilding = placedObject.AddComponent<PlacedBuilding>();

            Vector2Int origin = new Vector2Int(
                saved.originX,
                saved.originZ);

            bool isFoundation =
                definition.placementType == PlacementType.Area;

            bool isEdgeObject =
                definition.placementType == PlacementType.Edge;

            EdgeDirection edgeDirection =
                (EdgeDirection)saved.edgeDirection;

            if (isEdgeObject)
            {
                placedBuilding.InitializeEdge(
                    definition,
                    origin,
                    Mathf.Max(1, saved.edgeLength),
                    edgeDirection);
            }
            else
            {
                placedBuilding.Initialize(
                    definition,
                    origin,
                    Mathf.Max(1, saved.width),
                    Mathf.Max(1, saved.depth),
                    saved.rotation);
            }

            if (saved.hasFlightSchoolParkingDesignation)
            {
                HobbyAircraftParkingSpot parkingSpot =
                    placedObject.GetComponent<HobbyAircraftParkingSpot>();

                if (parkingSpot != null)
                {
                    parkingSpot.RestoreFlightSchoolDesignation(
                        saved.reservedForFlightSchool);
                }
            }

            // Recreate ownership data when the saved object had it.
            // Empty fields on older saves remain compatible.
            if (!string.IsNullOrWhiteSpace(saved.boundaryOwnershipId) ||
                (saved.boundaryOwnerIds != null &&
                 saved.boundaryOwnerIds.Count > 0))
            {
                FoundationBoundaryOwnership ownership =
                    placedObject.GetComponent<FoundationBoundaryOwnership>();

                if (ownership == null)
                {
                    ownership =
                        placedObject.AddComponent<FoundationBoundaryOwnership>();
                }

                ownership.Initialize(edgeOccupancy);
                ownership.RestoreOwnershipData(
                    saved.boundaryOwnershipId,
                    saved.boundaryOwnerIds);

                restoredOwnershipComponents.Add(ownership);
            }

            if (isFoundation)
            {
                DisableFoundationVisuals(placedObject);

                if (saved.hasCustomFoundationFootprint)
                {
                    // New saves store the actual remaining cells.
                    foreach (Vector2Int cell in saved.foundationCells)
                    {
                        gridSurface.SetSurfaceArea(
                            cell,
                            1,
                            1,
                            BuildSurface.Foundation,
                            placedObject);
                    }
                }
                else
                {
                    // Backward compatibility for older saves.
                    gridSurface.SetSurfaceArea(
                        origin,
                        Mathf.Max(1, saved.width),
                        Mathf.Max(1, saved.depth),
                        BuildSurface.Foundation,
                        placedObject);
                }

                restoredFoundations++;
            }
            else if (isEdgeObject)
            {
                // Wall-mounted fixtures reference an edge but do not own it.
                if (!definition.isWallMounted)
                {
                    int edgeLength = Mathf.Max(1, saved.edgeLength);

                    for (int i = 0; i < edgeLength; i++)
                    {
                        Vector2Int edgeCell = origin;

                        if (edgeDirection == EdgeDirection.Horizontal)
                            edgeCell.x += i;
                        else
                            edgeCell.y += i;

                        edgeOccupancy.Occupy(
                            new GridEdge(edgeCell, edgeDirection),
                            placedObject);
                    }

                    restoredEdges++;
                }
            }
            else
            {
                gridOccupancy.Occupy(
                    origin,
                    Mathf.Max(1, saved.width),
                    Mathf.Max(1, saved.depth),
                    placedObject);
            }

            restoredBuildings++;
        }

        // Resolve relationships after every saved object is instantiated.
        FoundationBoundaryOwnership.ResolveSavedOwnership(
            restoredOwnershipComponents);

        // 2. Restore roads and their visible tiles.
        foreach (RoadCellSaveData savedRoad in data.roadCells)
        {
            Vector2Int cell = new Vector2Int(
                savedRoad.x,
                savedRoad.z);

            RoadDirection direction;

            if (savedRoad.directionX == 0 &&
                savedRoad.directionZ == 1)
            {
                direction = RoadDirection.North;
            }
            else if (savedRoad.directionX == 1 &&
                     savedRoad.directionZ == 0)
            {
                direction = RoadDirection.East;
            }
            else if (savedRoad.directionX == 0 &&
                     savedRoad.directionZ == -1)
            {
                direction = RoadDirection.South;
            }
            else if (savedRoad.directionX == -1 &&
                     savedRoad.directionZ == 0)
            {
                direction = RoadDirection.West;
            }
            else
            {
                Debug.LogError(
                    $"Invalid saved road direction at cell {cell}: " +
                    $"({savedRoad.directionX}, {savedRoad.directionZ}).");

                return false;
            }

            roadNetwork.AddRoad(cell, direction);
            roadTilemapController.SetRoadTile(cell, direction);

            restoredRoadCells++;
        }

        // 3. Restore taxiway definitions, network cells, tiles,
        // and centerline membership.
        foreach (TaxiwayCellSaveData savedTaxiway in data.taxiwayCells)
        {
            Vector2Int cell = new Vector2Int(
                savedTaxiway.x,
                savedTaxiway.z);

            BuildableDefinition definition =
                FindDefinition(savedTaxiway.definitionId);

            if (definition == null)
            {
                Debug.LogError(
                    $"Cannot restore taxiway definition " +
                    $"'{savedTaxiway.definitionId}'.");

                return false;
            }

            var tile =
                taxiwayTilemapController.GetTileForDefinition(definition);

            if (tile == null)
            {
                Debug.LogError(
                    $"No taxiway tile mapping for '{savedTaxiway.definitionId}'.");

                return false;
            }

            taxiwayNetwork.AddTaxiwayCell(cell, definition);
            taxiwayTilemapController.SetTaxiwayTile(cell, tile);

            if (savedTaxiway.isCenterline)
            {
                taxiwayNetwork.AddTaxiwayCenterlineCell(cell);
                restoredCenterlineCells++;
            }

            restoredTaxiwayCells++;
        }

        foundationTilemapController.RefreshFromGridSurface();

        // 4. Refresh room detection and restore room-purpose assignments
        // only after the saved foundations and boundaries are back in place.
        ResolveRoomPurposeReferences();

        if (roomDetectionController != null)
            roomDetectionController.RefreshRooms();

        if (roomPurposeController != null)
        {
            roomPurposeController.RestoreAssignments(
                data.roomPurposeAssignments);
        }
        else
        {
            Debug.LogWarning(
                "AirportWorldResetController: RoomPurposeController was " +
                "not found. The airport loaded, but room-purpose assignments " +
                "could not be restored.");
        }

        // 5. Restore saved game time, speed, and balance.
        gameTimeManager.SetGameTimeSeconds(data.gameTimeSeconds);
        gameTimeManager.SetSpeed(
            Mathf.RoundToInt(data.gameSpeedMultiplier));

        economyManager.RestoreBalanceFromSave(data.balance);

        // Refresh the gate registry against the restored airport and then
        // replace the active schedule with this slot's saved schedule.
        bool scheduleRestored =
            flightManagementUIController.LoadScheduleForAirportSave(
                data.flightSchedule);

        if (!scheduleRestored)
        {
            Debug.LogError(
                $"AirportWorldResetController: Airport slot {slot} was " +
                "restored, but its flight schedule could not be loaded. " +
                "The active schedule may be empty.");
        }

        Debug.Log(
            $"AirportWorldResetController: Restore from slot {slot} finished. " +
            $"Objects: {restoredBuildings}, " +
            $"foundations: {restoredFoundations}, " +
            $"edge owners: {restoredEdges}, " +
            $"road cells: {restoredRoadCells}, " +
            $"taxiway cells: {restoredTaxiwayCells}, " +
            $"centerline cells: {restoredCenterlineCells}, " +
            $"room assignments saved: {data.roomPurposeAssignments.Count}, " +
            $"scheduled flights saved: {data.flightSchedule.Count}. " +
            $"Balance: ${economyManager.Balance:N0}. " +
            $"Game time: {gameTimeManager.FormattedTime}, " +
            $"speed: {gameTimeManager.CurrentSpeedMultiplier}x.");

        return scheduleRestored;
    }

    private bool ValidateBuildCatalog()
    {
        if (buildCatalog == null || buildCatalog.buildables == null)
        {
            Debug.LogError(
                "AirportWorldResetController: BuildCatalog is missing " +
                "or has no buildables assigned.");

            return false;
        }

        return true;
    }

    private bool ValidateSavedDefinitions(
        AirportSaveData data,
        bool requireWorldTransforms)
    {
        foreach (PlacedBuildingSaveData saved in data.placedBuildings)
        {
            BuildableDefinition definition =
                FindDefinition(saved.definitionId);

            if (definition == null)
            {
                Debug.LogError(
                    $"Save references missing building definition ID " +
                    $"'{saved.definitionId}'. The airport has not been changed.");

                return false;
            }

            if (definition.prefab == null)
            {
                Debug.LogError(
                    $"BuildableDefinition '{saved.definitionId}' has no prefab.");

                return false;
            }

            if (requireWorldTransforms && !saved.hasWorldTransform)
            {
                Debug.LogError(
                    $"Saved object '{saved.definitionId}' has no saved " +
                    "world transform. The airport has not been changed.");

                return false;
            }
        }

        foreach (TaxiwayCellSaveData savedTaxiway in data.taxiwayCells)
        {
            if (FindDefinition(savedTaxiway.definitionId) == null)
            {
                Debug.LogError(
                    $"Save references missing taxiway definition ID " +
                    $"'{savedTaxiway.definitionId}'. The airport has not " +
                    "been changed.");

                return false;
            }
        }

        return true;
    }

    private BuildableDefinition FindDefinition(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId) ||
            buildCatalog == null ||
            buildCatalog.buildables == null)
        {
            return null;
        }

        foreach (BuildableDefinition definition in buildCatalog.buildables)
        {
            if (definition != null && definition.id == definitionId)
                return definition;
        }

        return null;
    }

    private bool HasDefinition(string definitionId)
    {
        return FindDefinition(definitionId) != null;
    }

    private void DisableFoundationVisuals(GameObject foundationObject)
    {
        Renderer[] renderers =
            foundationObject.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in renderers)
            renderer.enabled = false;
    }
}
