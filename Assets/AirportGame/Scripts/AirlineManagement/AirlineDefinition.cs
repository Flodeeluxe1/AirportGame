using System.Collections.Generic;
using UnityEngine;

public enum AirlineOperationType
{
    Cargo,
    Passenger,
    CargoAndPassengers
}

[System.Serializable]
public class AirlineInterestWeights
{
    [Min(0f)] public float airportFees = 20f;
    [Min(0f)] public float suitableGates = 15f;
    [Min(0f)] public float runwaySuitability = 15f;
    [Min(0f)] public float cargoHandlingCapacity = 15f;
    [Min(0f)] public float nightOperations = 10f;
    [Min(0f)] public float airportReputation = 10f;
    [Min(0f)] public float previousContractPerformance = 15f;

    public float Total =>
        airportFees +
        suitableGates +
        runwaySuitability +
        cargoHandlingCapacity +
        nightOperations +
        airportReputation +
        previousContractPerformance;
}

[CreateAssetMenu(
    fileName = "AirlineDefinition",
    menuName = "Airport/Airline Management/Airline Definition"
)]
public class AirlineDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string airlineId;
    [SerializeField] private string displayName;
    [SerializeField] private Sprite logo;

    [Header("Operations")]
    [SerializeField] private AirlineOperationType operationType =
        AirlineOperationType.Cargo;

    [Header("Airline Interest Weights")]
    [SerializeField] private AirlineInterestWeights interestWeights =
        new AirlineInterestWeights();

    [Header("Airport Fee Preferences")]
    [Min(0)]
    [SerializeField] private int idealLandingAndParkingFee = 250;

    [Min(0)]
    [SerializeField] private int maximumAcceptableLandingAndParkingFee = 750;

    [Header("Airport Infrastructure Preferences")]
    [SerializeField] private AircraftGateSize preferredGateSize =
        AircraftGateSize.Grass;

    [Min(1)]
    [SerializeField] private int desiredSuitableGateCount = 1;

    [Min(0f)]
    [SerializeField] private float minimumRunwayLengthMeters = 100f;

    [Header("Passenger Airport Size Preferences")]
    [Min(0)]
    [SerializeField] private int desiredFoundationCellCount = 100;

    [Header("Cargo Capacity Preferences")]
    [Min(0f)]
    [SerializeField] private float desiredCargoStorageCapacity = 100f;

    [Min(0f)]
    [SerializeField] private float desiredCargoHandlingThroughputPerHour = 10f;

    [Header("Contract Templates")]
    [SerializeField] private List<AirlineContractTemplate> contractTemplates =
        new List<AirlineContractTemplate>();

    [Header("Other Preferences")]
    [Range(0f, 100f)]
    [SerializeField] private float minimumPreferredAirportReputation = 50f;

    [Range(0f, 2f)]
    [SerializeField] private float priceSensitivity = 1f;

    public string AirlineId => airlineId;
    public string DisplayName => displayName;
    public Sprite Logo => logo;
    public AirlineOperationType OperationType => operationType;
    public AirlineInterestWeights InterestWeights => interestWeights;
    public int IdealLandingAndParkingFee => idealLandingAndParkingFee;
    public int MaximumAcceptableLandingAndParkingFee =>
        maximumAcceptableLandingAndParkingFee;
    public AircraftGateSize PreferredGateSize => preferredGateSize;
    public int DesiredSuitableGateCount => desiredSuitableGateCount;
    public float MinimumRunwayLengthMeters => minimumRunwayLengthMeters;
    public int DesiredFoundationCellCount => desiredFoundationCellCount;
    public float DesiredCargoStorageCapacity => desiredCargoStorageCapacity;
    public float DesiredCargoHandlingThroughputPerHour =>
        desiredCargoHandlingThroughputPerHour;
    public float MinimumPreferredAirportReputation =>
        minimumPreferredAirportReputation;
    public float PriceSensitivity => priceSensitivity;
    public IReadOnlyList<AirlineContractTemplate> ContractTemplates => contractTemplates;

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(airlineId))
            airlineId = name;

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = name;

        idealLandingAndParkingFee = Mathf.Max(0, idealLandingAndParkingFee);
        maximumAcceptableLandingAndParkingFee = Mathf.Max(
            idealLandingAndParkingFee,
            maximumAcceptableLandingAndParkingFee);

        desiredSuitableGateCount = Mathf.Max(1, desiredSuitableGateCount);
        minimumRunwayLengthMeters = Mathf.Max(0f, minimumRunwayLengthMeters);
        desiredFoundationCellCount = Mathf.Max(0, desiredFoundationCellCount);
        desiredCargoStorageCapacity = Mathf.Max(0f, desiredCargoStorageCapacity);
        desiredCargoHandlingThroughputPerHour = Mathf.Max(
            0f,
            desiredCargoHandlingThroughputPerHour);

        if (interestWeights == null)
            interestWeights = new AirlineInterestWeights();

        if (contractTemplates == null)
            contractTemplates = new List<AirlineContractTemplate>();
    }
}
