
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class AirportSaveData
{
    public const int CurrentSchemaVersion = 9;

    public int schemaVersion = CurrentSchemaVersion;
    public string saveName = "";
    public string savedAtUtc = "";

    public int gameTimeSeconds;
    public long gameDay = 1;
    public float gameSpeedMultiplier = 1f;

    public int balance;

    public List<AirportFinancialTransaction> financialTransactions =
        new List<AirportFinancialTransaction>();

    public List<PlacedBuildingSaveData> placedBuildings =
        new List<PlacedBuildingSaveData>();

    public List<RoadCellSaveData> roadCells =
        new List<RoadCellSaveData>();

    public List<TaxiwayCellSaveData> taxiwayCells =
        new List<TaxiwayCellSaveData>();

    public List<TaxiwayAreaSaveData> taxiwayAreas =
    new List<TaxiwayAreaSaveData>();

    public List<Vector2Int> pipelineCells =
        new List<Vector2Int>();

    public List<PipelineNetworkSaveData> pipelineNetworks =
        new List<PipelineNetworkSaveData>();

    public List<FloorFinishCellSaveData> floorFinishCells =
        new List<FloorFinishCellSaveData>();

    public List<FlightScheduleSaveData> flightSchedule =
        new List<FlightScheduleSaveData>();

    public bool hasAirlineManagementData;
    public AirlineManagementSaveData airlineManagement =
        new AirlineManagementSaveData();

    public List<RoomPurposeAssignmentSaveData> roomPurposeAssignments =
        new List<RoomPurposeAssignmentSaveData>();

    public bool hasCustomAirportFees;
    public AirportManagementController.FeeSettings airportFeeSettings;

    public void EnsureCollections()
    {
        if (placedBuildings == null)
            placedBuildings = new List<PlacedBuildingSaveData>();

        if (financialTransactions == null)
            financialTransactions = new List<AirportFinancialTransaction>();

        foreach (PlacedBuildingSaveData building in placedBuildings)
        {
            if (building == null)
                continue;

            if (building.purchasedCargoTrailerSlots == null)
                building.purchasedCargoTrailerSlots = new List<int>();

            if (building.allowedDoorAccessGroupIds == null)
                building.allowedDoorAccessGroupIds = new List<string>();
        }

        if (roadCells == null)
            roadCells = new List<RoadCellSaveData>();

        if (taxiwayCells == null)
            taxiwayCells = new List<TaxiwayCellSaveData>();

        if (taxiwayAreas == null)
            taxiwayAreas = new List<TaxiwayAreaSaveData>();

        if (pipelineCells == null)
            pipelineCells = new List<Vector2Int>();

        if (pipelineNetworks == null)
            pipelineNetworks = new List<PipelineNetworkSaveData>();

        if (floorFinishCells == null)
            floorFinishCells = new List<FloorFinishCellSaveData>();

        if (flightSchedule == null)
            flightSchedule = new List<FlightScheduleSaveData>();

        if (airlineManagement == null)
            airlineManagement = new AirlineManagementSaveData();

        airlineManagement.EnsureCollections();

        if (roomPurposeAssignments == null)
            roomPurposeAssignments = new List<RoomPurposeAssignmentSaveData>();
    }
}

[Serializable]
public class PlacedBuildingSaveData
{
    public string definitionId = "";

    public bool isSurfacePlaced;
    public string instanceId = "";
    public string supportingSurfaceInstanceId = "";
    public string wallInnerFinishId = "";
    public string wallOuterFinishId = "";

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

    public string boundaryOwnershipId = "";
    public List<string> boundaryOwnerIds = new List<string>();

    public bool hasCustomFoundationFootprint;
    public List<Vector2Int> foundationCells = new List<Vector2Int>();

    // Flight School parking designation.
    // The has-field flag allows older saves to load without overwriting
    // the prefab's existing default designation.
    public bool hasFlightSchoolParkingDesignation;
    public bool reservedForFlightSchool;

    // Door access configuration. The flag preserves prefab defaults for
    // older saves and distinguishes an intentionally unrestricted door.
    public bool hasSavedDoorAccessSettings;
    public List<string> allowedDoorAccessGroupIds = new List<string>();

    // Purchased cargo trailer parking slots for CargoTugLotSpawner.
    public List<int> purchasedCargoTrailerSlots = new List<int>();

    // Fuel station stock. The flag keeps older save files compatible.
    public bool hasSavedFuelStock;
    public float fuelStationStockLitres;
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
    public string contractId = "";
    public string obligationId = "";
}

[Serializable]
public class RoomPurposeAssignmentSaveData
{
    public int anchorX;
    public int anchorZ;
    public int purpose;
}

[Serializable]
public class TaxiwayAreaSaveData
{
    public int originX;
    public int originZ;
    public int width;
    public int depth;
}


[Serializable]
public class PipelineNetworkSaveData
{
    public List<Vector2Int> pipeCells = new List<Vector2Int>();
    public List<Vector2Int> connectorCells = new List<Vector2Int>();
    public float currentFuelLitres;
}


[Serializable]
public class AirlineManagementSaveData
{
    public long lastEvaluatedPeriod = -1;
    public List<AirlineContractOfferSaveData> pendingOffers =
        new List<AirlineContractOfferSaveData>();
    public List<ActiveAirlineContractSaveData> activeContracts =
        new List<ActiveAirlineContractSaveData>();
    public List<AirlineRelationshipRecord> relationships =
        new List<AirlineRelationshipRecord>();

    public void EnsureCollections()
    {
        if (pendingOffers == null)
            pendingOffers = new List<AirlineContractOfferSaveData>();
        if (activeContracts == null)
            activeContracts = new List<ActiveAirlineContractSaveData>();
        if (relationships == null)
            relationships = new List<AirlineRelationshipRecord>();

        foreach (ActiveAirlineContractSaveData contract in activeContracts)
        {
            if (contract == null)
                continue;
            if (contract.flightObligations == null)
                contract.flightObligations = new List<AirlineContractFlightObligation>();
            if (contract.performanceRecords == null)
                contract.performanceRecords = new List<AirlineContractFlightPerformanceRecord>();
            if (contract.agreedTerms != null && contract.agreedTerms.flightWindows == null)
                contract.agreedTerms.flightWindows =
                    new List<AirlineContractFlightWindowRequirement>();
        }

        foreach (AirlineContractOfferSaveData offer in pendingOffers)
        {
            if (offer != null && offer.terms != null && offer.terms.flightWindows == null)
                offer.terms.flightWindows =
                    new List<AirlineContractFlightWindowRequirement>();
        }
    }
}

[Serializable]
public class AirlineContractOfferSaveData
{
    public string offerId = "";
    public string airlineId = "";
    public string templateId = "";
    public float airlineInterest;
    public long offeredOnDay;
    public long expiresOnDay;
    public AirlineContractTerms terms;
}

[Serializable]
public class ActiveAirlineContractSaveData
{
    public string contractId = "";
    public string sourceOfferId = "";
    public string airlineId = "";
    public string templateId = "";
    public long startDay;
    public long endDay;
    public AirlineContractStatus status;
    public AirlineContractTerms agreedTerms;
    public List<AirlineContractFlightObligation> flightObligations =
        new List<AirlineContractFlightObligation>();
    public long lastFinalizedDay;
    public List<AirlineContractFlightPerformanceRecord> performanceRecords =
        new List<AirlineContractFlightPerformanceRecord>();
    public int onTimeFlights;
    public int lateFlights;
    public int missedFlights;
    public int cancelledFlights;
    public int operationalFailures;
    public int totalPaymentsReceived;
    public int totalPenaltiesPaid;
    public int unpaidPenaltyAmount;
    public bool completionBonusPaid;
    public int renewalCount;
    public bool renewalDeclined;
    public int renewalBasePaymentPerFlight;
    public int renewalBaseCompletionBonus;
}
