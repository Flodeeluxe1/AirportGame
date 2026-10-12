
using System;
using System.Collections.Generic;

[Serializable]
public class AirportSaveData
{
    public int schemaVersion = 1;

    public string saveName = "Airport Save";
    public string savedAtUtc = "";

    // Game time is stored as seconds since midnight.
    public int gameTimeSeconds;
    public int gameSpeedMultiplier = 1;

    // Main airport layout.
    public List<PlacedBuildingSaveData> placedBuildings =
        new List<PlacedBuildingSaveData>();

    // Road network data.
    public List<RoadCellSaveData> roadCells =
        new List<RoadCellSaveData>();

    // Taxiway network data.
    public List<TaxiwayCellSaveData> taxiwayCells =
        new List<TaxiwayCellSaveData>();

    public void EnsureCollections()
    {
        if (placedBuildings == null)
            placedBuildings = new List<PlacedBuildingSaveData>();

        if (roadCells == null)
            roadCells = new List<RoadCellSaveData>();

        if (taxiwayCells == null)
            taxiwayCells = new List<TaxiwayCellSaveData>();
    }
}

[Serializable]
public class PlacedBuildingSaveData
{
    // Stable identifier used to find the matching BuildableDefinition.
    public string definitionId = "";

    public int originX;
    public int originZ;

    public int width;
    public int depth;
    public int rotation;

    // Used by edge-placement objects.
    public bool isEdgeBuilding;
    public int edgeLength;
    public int edgeDirection;
}

[Serializable]
public class RoadCellSaveData
{
    public int x;
    public int z;

    // These fields let us restore directional road connections
    // and the placed road's orientation.
    public int directionX;
    public int directionZ;

    public bool connectNorth;
    public bool connectEast;
    public bool connectSouth;
    public bool connectWest;
}

[Serializable]
public class TaxiwayCellSaveData
{
    public int x;
    public int z;

    // Stable identifier used to find the matching buildable definition.
    public string definitionId = "";

    // Retained explicitly so the loader can restore or validate
    // the logical taxiway centerline.
    public bool isCenterline;
}