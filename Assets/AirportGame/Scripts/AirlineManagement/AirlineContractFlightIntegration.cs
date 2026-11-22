using System.Collections;
using UnityEngine;

/// <summary>
/// Turns accepted contract flight obligations into entries in the existing
/// flight catalog. Flight Management UI and ScheduledAircraftSpawner remain
/// the only timetable and aircraft-spawning systems.
/// </summary>
[DisallowMultipleComponent]
public class AirlineContractFlightIntegration : MonoBehaviour
{
    [SerializeField] private AirlineContractOfferManager contractManager;
    [SerializeField] private TestFlightCatalog flightCatalog;
    [SerializeField] private FlightManagementUIController flightManagementUI;

    private void OnEnable()
    {
        ResolveReferences();

        if (contractManager != null)
        {
            contractManager.ContractAccepted += HandleContractAccepted;
            contractManager.ContractCompleted += HandleContractCompleted;
            contractManager.ContractsRestored += HandleContractsRestored;
        }

        RegisterExistingContracts();
    }

    private void Start()
    {
        ResolveReferences();
        RegisterExistingContracts();
        StartCoroutine(RefreshFlightManagementAfterInitialization());
    }

    private void OnDisable()
    {
        if (contractManager != null)
        {
            contractManager.ContractAccepted -= HandleContractAccepted;
            contractManager.ContractCompleted -= HandleContractCompleted;
            contractManager.ContractsRestored -= HandleContractsRestored;
        }
    }

    private IEnumerator RefreshFlightManagementAfterInitialization()
    {
        yield return null;
        ResolveReferences();
        RegisterExistingContracts();

        if (flightManagementUI != null)
            flightManagementUI.RefreshAvailableFlights();
    }

    private void ResolveReferences()
    {
        if (contractManager == null)
            contractManager = GetComponent<AirlineContractOfferManager>();

        if (contractManager == null)
            contractManager = FindAnyObjectByType<AirlineContractOfferManager>();

        if (flightCatalog == null)
            flightCatalog = FindAnyObjectByType<TestFlightCatalog>();

        if (flightManagementUI == null)
            flightManagementUI = FindAnyObjectByType<FlightManagementUIController>();
    }

    private void RegisterExistingContracts()
    {
        if (contractManager == null)
            return;

        foreach (ActiveAirlineContract contract in contractManager.ActiveContracts)
            RegisterContractFlights(contract);
    }

    public void ResetContractFlightsForNewGame()
    {
        ResolveReferences();

        if (contractManager == null || flightCatalog == null)
            return;

        flightCatalog.RemoveAllContractFlights();

        if (flightManagementUI != null)
            flightManagementUI.RefreshAvailableFlights();
    }

    private void HandleContractsRestored()
    {
        if (flightCatalog == null)
            ResolveReferences();

        if (flightCatalog != null)
            flightCatalog.RemoveAllContractFlights();

        RegisterExistingContracts();

        if (flightManagementUI == null)
            ResolveReferences();

        if (flightManagementUI != null)
            flightManagementUI.RefreshAvailableFlights();
    }

    private void HandleContractAccepted(ActiveAirlineContract contract)
    {
        RegisterContractFlights(contract);

        if (flightManagementUI == null)
            ResolveReferences();

        if (flightManagementUI != null)
            flightManagementUI.RefreshAvailableFlights();
    }

    private void HandleContractCompleted(ActiveAirlineContract contract)
    {
        if (contract == null)
            return;

        if (flightCatalog == null || flightManagementUI == null)
            ResolveReferences();

        if (!string.IsNullOrWhiteSpace(contract.renewedByContractId))
        {
            if (flightManagementUI != null)
                flightManagementUI.TransferContractFlights(
                    contract.contractId, contract.renewedByContractId);
        }
        else if (flightManagementUI != null)
        {
            flightManagementUI.RemoveContractFlights(contract.contractId);
        }

        if (flightCatalog != null)
            flightCatalog.RemoveContractFlights(contract.contractId);

        if (flightManagementUI != null)
            flightManagementUI.RefreshAvailableFlights();
    }

    private void RegisterContractFlights(ActiveAirlineContract contract)
    {
        if (contract == null ||
            contract.status != AirlineContractStatus.Active ||
            contract.airline == null ||
            contract.agreedTerms == null ||
            contract.flightObligations == null ||
            flightCatalog == null)
        {
            return;
        }

        FlightOperationType operationType;
        switch (contract.airline.OperationType)
        {
            case AirlineOperationType.Passenger:
                operationType = FlightOperationType.PassengerOnly;
                break;

            case AirlineOperationType.CargoAndPassengers:
                Debug.LogWarning(
                    "Contract " + contract.contractId +
                    " requires cargo + passenger aircraft, which the current " +
                    "ScheduledAircraftSpawner does not support. Its flights were not added.");
                return;

            default:
                operationType = FlightOperationType.CargoOnly;
                break;
        }

        foreach (AirlineContractFlightObligation obligation in contract.flightObligations)
        {
            if (obligation == null ||
                string.IsNullOrWhiteSpace(obligation.flightNumber) ||
                flightCatalog.ContainsFlight(obligation.flightNumber))
            {
                continue;
            }

            FlightTimeWindow window = ConvertWindow(obligation.window);
            Color stripColor = GetStripColor(window);

            FlightDefinition definition = new FlightDefinition(
                obligation.flightNumber,
                contract.airline.DisplayName,
                contract.agreedTerms.turnaroundMinutes,
                contract.agreedTerms.minimumGateSize,
                window,
                stripColor,
                operationType,
                contract.contractId,
                obligation.obligationId);

            flightCatalog.AddContractFlight(definition);
        }
    }

    private static FlightTimeWindow ConvertWindow(AirlineOperatingWindow window)
    {
        switch (window)
        {
            case AirlineOperatingWindow.Morning:
                return FlightTimeWindow.Morning;
            case AirlineOperatingWindow.Afternoon:
                return FlightTimeWindow.Afternoon;
            case AirlineOperatingWindow.Evening:
                return FlightTimeWindow.Evening;
            default:
                return FlightTimeWindow.Night;
        }
    }

    private static Color GetStripColor(FlightTimeWindow window)
    {
        switch (window)
        {
            case FlightTimeWindow.Morning:
                return new Color32(155, 210, 145, 255);
            case FlightTimeWindow.Afternoon:
                return new Color32(245, 190, 105, 255);
            case FlightTimeWindow.Evening:
                return new Color32(190, 165, 225, 255);
            default:
                return new Color32(140, 195, 235, 255);
        }
    }
}
