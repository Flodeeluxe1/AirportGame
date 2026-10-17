
using UnityEngine;

public class FlightSchoolManager : MonoBehaviour
{
    [Header("School Building")]
    [Tooltip("Must match the id on the school's BuildableDefinition.")]
    [SerializeField] private string schoolBuildableId = "airport.flight.school";

    [Header("Training Aircraft")]
    [SerializeField] private GameObject trainingAircraftPrefab;

    [Header("Lesson Reward")]
    [SerializeField, Min(1)] private int lessonReward = 300;

    [Header("Runtime Status")]
    [SerializeField] private GameObject currentTrainingAircraft;
    [SerializeField] private bool rewardPaid;
    [SerializeField]
    private string debugStatus =
        "Waiting for the school building to be placed.";

    private PlacedBuilding placedBuilding;
    private AirportEconomyManager economyManager;
    private bool isPlacedSchool;

    private void Update()
    {
        if (!isPlacedSchool)
        {
            if (!TryInitializePlacedSchool())
                return;
        }

        if (currentTrainingAircraft != null)
        {
            CheckLessonCompletion();
            return;
        }

        if (rewardPaid)
            return;

        TrySpawnTrainingAircraft();
    }

    private bool TryInitializePlacedSchool()
    {
        if (string.IsNullOrWhiteSpace(schoolBuildableId))
        {
            debugStatus = "Set the school Buildable Definition ID.";
            return false;
        }

        placedBuilding = GetComponent<PlacedBuilding>();

        if (placedBuilding == null ||
            placedBuilding.Definition == null)
        {
            debugStatus =
                "Waiting for the school to be placed and initialized.";
            return false;
        }

        if (placedBuilding.Definition.id != schoolBuildableId)
        {
            debugStatus =
                $"Buildable ID mismatch. Expected '{schoolBuildableId}'.";
            return false;
        }

        isPlacedSchool = true;

        economyManager =
            FindFirstObjectByType<AirportEconomyManager>();

        debugStatus =
            "School placed. Waiting for a designated free parking spot.";

        Debug.Log(
            $"{name}: Flight school initialized.",
            this
        );

        return true;
    }

    private void TrySpawnTrainingAircraft()
    {
        if (trainingAircraftPrefab == null)
        {
            debugStatus =
                "Assign the training aircraft prefab in the Inspector.";
            return;
        }

        HobbyAircraftParkingSpot[] parkingSpots =
            FindObjectsByType<HobbyAircraftParkingSpot>(
                FindObjectsSortMode.None
            );

        foreach (HobbyAircraftParkingSpot spot in parkingSpots)
        {
            if (spot == null ||
                !spot.IsPlacedBuilding ||
                !spot.ReservedForFlightSchool ||
                spot.IsOccupied)
            {
                continue;
            }

            GameObject aircraft = Instantiate(
                trainingAircraftPrefab,
                spot.ParkingWorldPosition,
                spot.ParkingWorldRotation
            );

            if (aircraft == null)
                continue;

            if (!spot.TryReserve(aircraft, true))
            {
                Destroy(aircraft);
                continue;
            }

            AircraftFlightIdentity identity =
                aircraft.GetComponent<AircraftFlightIdentity>();

            TrainingFlightController trainingController =
                aircraft.GetComponent<TrainingFlightController>();

            if (identity == null || trainingController == null)
            {
                Debug.LogError(
                    $"{name}: The training aircraft prefab needs " +
                    "AircraftFlightIdentity and TrainingFlightController.",
                    aircraft
                );

                spot.Release(aircraft);
                Destroy(aircraft);
                return;
            }

            string trainingNumber =
                $"TRAIN-{System.Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant()}";

            identity.InitializeTrainingFlight(
                trainingNumber,
                0,
                0
            );

            currentTrainingAircraft = aircraft;
            rewardPaid = false;

            if (!trainingController.StartTrainingLesson())
            {
                Debug.LogError(
                    $"{name}: The training lesson could not start.",
                    this
                );

                spot.Release(aircraft);
                currentTrainingAircraft = null;
                Destroy(aircraft);
                return;
            }

            debugStatus =
                $"Training aircraft active at parking spot '{spot.name}'.";

            Debug.Log(
                $"{name}: Training aircraft spawned at {spot.name}.",
                this
            );

            return;
        }

        debugStatus =
            "Waiting for an unoccupied parking spot designated for flight school.";
    }

    private void CheckLessonCompletion()
    {
        TrainingFlightController trainingController =
            currentTrainingAircraft
                .GetComponent<TrainingFlightController>();

        if (trainingController == null)
        {
            debugStatus =
                "Training controller missing from the active aircraft.";
            return;
        }

        if (trainingController.HasFailed)
        {
            debugStatus =
                "Training lesson stopped. Check the aircraft's Console errors.";
            return;
        }

        if (!trainingController.IsLessonComplete || rewardPaid)
            return;

        if (economyManager == null)
        {
            economyManager =
                FindFirstObjectByType<AirportEconomyManager>();
        }

        if (economyManager == null)
        {
            debugStatus =
                "Lesson complete, but AirportEconomyManager was not found.";
            return;
        }

        if (!economyManager.AddRevenue(
                lessonReward,
                "Flight school lesson completed"))
        {
            debugStatus =
                "Lesson complete, but reward payment failed. Will retry.";
            return;
        }

        rewardPaid = true;

        debugStatus =
            $"Lesson complete. Paid ${lessonReward}. Aircraft remains parked.";

        Debug.Log(
            $"{name}: Flight school paid ${lessonReward}. " +
            "The training aircraft remains parked.",
            this
        );
    }

    private void OnDestroy()
    {
        // Removing the school removes its aircraft, even if it is airborne.
        // The parking spot's manual flight-school designation is not changed.
        if (currentTrainingAircraft != null)
        {
            Destroy(currentTrainingAircraft);
            currentTrainingAircraft = null;
        }
    }
}
