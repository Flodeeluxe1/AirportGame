
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class AirportSaveData
{
    public const int CurrentSchemaVersion = 1;

    public int schemaVersion = CurrentSchemaVersion;
    public string saveName = "";
    public string savedAtUtc = "";

    public int gameTimeSeconds;
    public float gameSpeedMultiplier = 1f;

    public int balance;

    public List<PlacedBuildingSaveData> placedBuildings =
        new List<PlacedBuildingSaveData>();

    public List<RoadCellSaveData> roadCells =
        new List<RoadCellSaveData>();

    public List<TaxiwayCellSaveData> taxiwayCells =
        new List<TaxiwayCellSaveData>();

    // The flight schedule belongs to this airport save slot.
    public List<FlightScheduleSaveData> flightSchedule =
        new List<FlightScheduleSaveData>();

    public void EnsureCollections()
    {
        if (placedBuildings == null)
            placedBuildings = new List<PlacedBuildingSaveData>();

        if (roadCells == null)
            roadCells = new List<RoadCellSaveData>();

        if (taxiwayCells == null)
            taxiwayCells = new List<TaxiwayCellSaveData>();

        if (flightSchedule == null)
            flightSchedule = new List<FlightScheduleSaveData>();
    }
}

[Serializable]
public class PlacedBuildingSaveData
{
    public string definitionId = "";

    public int originX;
    public int originZ;
    public int width;
    public int depth;
    public int rotation;

    public bool isEdgeBuilding;
    public int edgeLength;
    public int edgeDirection;

    public bool hasWorldTransform;

    public float positionX;
    public float positionY;
    public float positionZ;

    public float rotationX;
    public float rotationY;
    public float rotationZ;
    public float rotationW = 1f;

    public float scaleX = 1f;
    public float scaleY = 1f;
    public float scaleZ = 1f;

    // Present only on foundations and boundary objects that use
    // FoundationBoundaryOwnership.
    public string boundaryOwnershipId = "";

    // On a boundary object, these are the IDs of its owning foundations.
    // On a foundation, this list is normally empty.
    public List<string> boundaryOwnerIds = new List<string>();
    public bool hasCustomFoundationFootprint;
    public List<Vector2Int> foundationCells = new List<Vector2Int>();

    
}

[Serializable]
public class RoadCellSaveData
{
    public int x;
    public int z;

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

    public string definitionId = "";
    public bool isCenterline;
}

[Serializable]
public class FlightScheduleSaveData
{
    public string flightNumber = "";
    public string gateStableId = "";
    public int startMinute;
}
