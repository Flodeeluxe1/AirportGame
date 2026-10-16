
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

    [Header("Flight Schedule")]
    [SerializeField] private FlightManagementUIController flightManagementUIController;


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
    /// Removes the current airport layout.
    /// Does not reset the game clock, clear the active flight schedule,
    /// or delete any save files.
    /// </summary>
    public bool ResetAirportLayout()
    {
        if (!ValidateReferences())
            return false;

        buildController.CancelBuilding();

        PlacedBuilding[] placedBuildings =
            FindObjectsByType<PlacedBuilding>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

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

        Debug.Log(
            "AirportWorldResetController: Airport layout reset."
        );

        return true;
    }


    /// <summary>
    /// Starts a fresh airport.
    /// Clears the layout and active flight schedule, and resets the
    /// clock to the starting time configured in GameTimeManager.
    /// Existing airport save slots are left untouched.
    /// </summary>
    public bool StartNewGame()
    {
        if (!ValidateReferences())
            return false;

        if (!EnsureFlightManagementController())
            return false;

        // Do not clear the layout unless the schedule controller is available.
        if (!ResetAirportLayout())
            return false;

        flightManagementUIController.ClearActiveSchedule();
        gameTimeManager.ResetToStartTime();

        Debug.Log(
            "AirportWorldResetController: New game initialized. " +
            "Airport layout and active flight schedule cleared; " +
            "game clock reset. Existing saves were preserved."
        );

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
                "references are missing. Assign all references in the Inspector."
            );

            return false;
        }

        return true;
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
                "loading, or starting a new game."
            );

            return false;
        }

        return true;
    }


    /// <summary>
    /// Saves the airport layout, roads, taxiways, game time,
    /// simulation speed, placed-object transforms, and flight schedule.
    /// </summary>
    public bool TrySaveSlot(int slot, string saveName)
    {
        if (!ValidateReferences())
            return false;

        if (!EnsureFlightManagementController())
            return false;

        AirportSaveData data = new AirportSaveData
        {
            saveName = string.IsNullOrWhiteSpace(saveName)
                ? $"Save Slot {slot}"
                : saveName,

            gameTimeSeconds = gameTimeManager.GetGameTimeSeconds(),
            gameSpeedMultiplier = gameTimeManager.CurrentSpeedMultiplier,

            // Capture a snapshot of the active schedule at save time.
            flightSchedule =
                flightManagementUIController.GetScheduleForAirportSave()
        };

        PlacedBuilding[] buildings =
            FindObjectsByType<PlacedBuilding>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

        foreach (PlacedBuilding building in buildings)
        {
            if (building == null || building.Definition == null)
                continue;

            string definitionId = building.Definition.id;

            if (string.IsNullOrWhiteSpace(definitionId))
            {
                Debug.LogError(
                    $"Cannot save '{building.name}': its " +
                    "BuildableDefinition has no ID."
                );

                return false;
            }

            data.placedBuildings.Add(new PlacedBuildingSaveData
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
            });
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
                $"{slot}. {error}"
            );

            return false;
        }

        Debug.Log(
            $"AirportWorldResetController: Saved slot {slot}. " +
            $"Buildings: {data.placedBuildings.Count}, " +
            $"Road cells: {data.roadCells.Count}, " +
            $"Taxiway cells: {data.taxiwayCells.Count}, " +
            $"Scheduled flights: {data.flightSchedule.Count}."
        );

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
                $"Could not read save slot {slot}: {error}"
            );

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
            $"Scheduled flights: {data.flightSchedule.Count}. " +
            "The airport has not been changed."
        );

        return true;
    }


    /// <summary>
    /// Replaces the current airport layout and restores its buildings,
    /// foundations, roads, taxiways, game time, speed, and flight schedule.
    /// </summary>
    public bool TryRestoreBuildingsAndFoundations(int slot)
    {
        if (!ValidateReferences())
            return false;

        if (!ValidateBuildCatalog())
            return false;

        if (!EnsureFlightManagementController())
            return false;

        // Load and validate the save before clearing the current airport.
        if (!AirportSaveFileStore.TryLoad(
                slot,
                out AirportSaveData data,
                out string error))
        {
            Debug.LogError(
                $"Could not load slot {slot}: {error}. " +
                "The current airport has not been changed."
            );

            return false;
        }

        data.EnsureCollections();

        if (!ValidateSavedDefinitions(data, true))
            return false;

        // Check taxiway tile mappings before clearing the current airport.
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
                    "was found. The current airport has not been changed."
                );

                return false;
            }
        }

        // Replace the current layout rather than merging with it.
        if (!ResetAirportLayout())
            return false;

        int restoredBuildings = 0;
        int restoredFoundations = 0;
        int restoredEdges = 0;
        int restoredRoadCells = 0;
        int restoredTaxiwayCells = 0;
        int restoredCenterlineCells = 0;

        // ---------------------------------------------------------
        // 1. Restore placed objects, foundations, and edge ownership.
        // ---------------------------------------------------------

        foreach (PlacedBuildingSaveData saved in data.placedBuildings)
        {
            BuildableDefinition definition =
                FindDefinition(saved.definitionId);

            if (definition == null || definition.prefab == null)
            {
                Debug.LogError(
                    $"Cannot restore definition '{saved.definitionId}'."
                );
                return false;
            }

            Vector3 position = new Vector3(
                saved.positionX,
                saved.positionY,
                saved.positionZ
            );

            Quaternion rotation = new Quaternion(
                saved.rotationX,
                saved.rotationY,
                saved.rotationZ,
                saved.rotationW
            );

            GameObject placedObject = Instantiate(
                definition.prefab,
                position,
                rotation
            );

            placedObject.transform.localScale = new Vector3(
                saved.scaleX,
                saved.scaleY,
                saved.scaleZ
            );

            PlacedBuilding placedBuilding =
                placedObject.GetComponent<PlacedBuilding>();

            if (placedBuilding == null)
                placedBuilding = placedObject.AddComponent<PlacedBuilding>();

            Vector2Int origin = new Vector2Int(
                saved.originX,
                saved.originZ
            );

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
                    edgeDirection
                );
            }
            else
            {
                placedBuilding.Initialize(
                    definition,
                    origin,
                    Mathf.Max(1, saved.width),
                    Mathf.Max(1, saved.depth),
                    saved.rotation
                );
            }

            if (isFoundation)
            {
                DisableFoundationVisuals(placedObject);

                gridSurface.SetSurfaceArea(
                    origin,
                    Mathf.Max(1, saved.width),
                    Mathf.Max(1, saved.depth),
                    BuildSurface.Foundation,
                    placedObject
                );

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
                            placedObject
                        );
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
                    placedObject
                );
            }

            restoredBuildings++;
        }

        // ---------------------------------------------------------
        // 2. Restore roads and their visible tiles.
        // ---------------------------------------------------------

        foreach (RoadCellSaveData savedRoad in data.roadCells)
        {
            Vector2Int cell = new Vector2Int(
                savedRoad.x,
                savedRoad.z
            );

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
                    $"({savedRoad.directionX}, {savedRoad.directionZ})."
                );
                return false;
            }

            roadNetwork.AddRoad(cell, direction);
            roadTilemapController.SetRoadTile(cell, direction);

            restoredRoadCells++;
        }

        // ---------------------------------------------------------
        // 3. Restore taxiway definitions, network cells, tiles,
        //    and centerline membership.
        // ---------------------------------------------------------

        foreach (TaxiwayCellSaveData savedTaxiway in data.taxiwayCells)
        {
            Vector2Int cell = new Vector2Int(
                savedTaxiway.x,
                savedTaxiway.z
            );

            BuildableDefinition definition =
                FindDefinition(savedTaxiway.definitionId);

            if (definition == null)
            {
                Debug.LogError(
                    $"Cannot restore taxiway definition " +
                    $"'{savedTaxiway.definitionId}'."
                );
                return false;
            }

            var tile =
                taxiwayTilemapController.GetTileForDefinition(definition);

            if (tile == null)
            {
                Debug.LogError(
                    $"No taxiway tile mapping for '{savedTaxiway.definitionId}'."
                );
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

        // Rebuild foundation visuals from the restored logical surfaces.
        foundationTilemapController.RefreshFromGridSurface();

        // Restore the saved game time and simulation speed.
        gameTimeManager.SetGameTimeSeconds(data.gameTimeSeconds);
        gameTimeManager.SetSpeed(Mathf.RoundToInt(data.gameSpeedMultiplier));

        // Refresh the gate registry against the restored airport and then
        // replace the active schedule with this slot's saved schedule.
        bool scheduleRestored =
            flightManagementUIController.LoadScheduleForAirportSave(
                data.flightSchedule
            );

        if (!scheduleRestored)
        {
            Debug.LogError(
                $"AirportWorldResetController: Airport slot {slot} was " +
                "restored, but its flight schedule could not be loaded. " +
                "The active schedule may be empty."
            );
        }

        Debug.Log(
            $"AirportWorldResetController: Restore from slot {slot} finished. " +
            $"Objects: {restoredBuildings}, " +
            $"foundations: {restoredFoundations}, " +
            $"edge owners: {restoredEdges}, " +
            $"road cells: {restoredRoadCells}, " +
            $"taxiway cells: {restoredTaxiwayCells}, " +
            $"centerline cells: {restoredCenterlineCells}, " +
            $"scheduled flights saved: {data.flightSchedule.Count}. " +
            $"Game time: {gameTimeManager.FormattedTime}, " +
            $"speed: {gameTimeManager.CurrentSpeedMultiplier}x."
        );

        return scheduleRestored;
    }


    private bool ValidateBuildCatalog()
    {
        if (buildCatalog == null || buildCatalog.buildables == null)
        {
            Debug.LogError(
                "AirportWorldResetController: BuildCatalog is missing " +
                "or has no buildables assigned."
            );

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
                    $"'{saved.definitionId}'. The airport has not been changed."
                );

                return false;
            }

            if (definition.prefab == null)
            {
                Debug.LogError(
                    $"BuildableDefinition '{saved.definitionId}' has no prefab."
                );

                return false;
            }

            if (requireWorldTransforms && !saved.hasWorldTransform)
            {
                Debug.LogError(
                    $"Saved object '{saved.definitionId}' has no saved " +
                    "world transform. The airport has not been changed."
                );

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
                    "been changed."
                );

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
