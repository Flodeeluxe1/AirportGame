
using UnityEngine;

[DisallowMultipleComponent]
public class CargoTugLotSpawner : MonoBehaviour
{
    [Header("Prefabs")]
    [Tooltip("The cargo tug prefab, including GuidedCargoTrailerFollower.")]
    [SerializeField] private GameObject cargoTugPrefab;

    [Tooltip("The separate cargo trailer prefab.")]
    [SerializeField] private GameObject cargoTrailerPrefab;

    [Header("Spawn Points")]
    [Tooltip("Child transform used as the tug's spawn and parking position.")]
    [SerializeField] private Transform tugParking;

    [Tooltip("Parking transforms for additional trailers. P1 to P4 are found automatically if empty.")]
    [SerializeField]
    private Transform[] trailerParkingPositions =
        new Transform[4];

    [Header("Spawn Offsets")]
    [Tooltip("Fallback tug position offset if TugParking is not assigned.")]
    [SerializeField] private Vector3 tugSpawnOffset = Vector3.zero;

    [Header("Spawn Settings")]
    [Tooltip("World-space height at which the tug spawns.")]
    [SerializeField] private float spawnHeight = 0.23f;

    [Tooltip("World-space height at which purchased trailers spawn.")]
    [SerializeField] private float trailerSpawnHeight = 0.5f;

    [Header("Trailer Shop")]
    [SerializeField, Min(1)] private int trailerPrice = 1000;

    [Header("Trailer Attachment")]
    [Tooltip("Maximum distance from the home position at which the tug is considered parked.")]
    [SerializeField, Min(0.05f)] private float homeArrivalTolerance = 0.25f;

    [Header("Debug")]
    [SerializeField] private bool showDebugMessages = true;

    private GameObject spawnedCargoTug;

    private AirportCargoVehicleController tugController;
    private GuidedCargoTrailerFollower trailerFollower;

    private readonly GameObject[] purchasedTrailers =
        new GameObject[4];

    private AirportEconomyManager economyManager;

    private bool trailersAttached;
    private bool returnJourneyObserved;

    public int TrailerPrice => trailerPrice;

    private void Start()
    {
        ResolveSpawnPoints();

        if (cargoTugPrefab == null)
        {
            Debug.LogError(
                "CargoTugLotSpawner: Cargo tug prefab is not assigned.",
                this
            );
            return;
        }

        if (cargoTrailerPrefab == null)
        {
            Debug.LogError(
                "CargoTugLotSpawner: Cargo trailer prefab is not assigned.",
                this
            );
            return;
        }

        Vector3 tugPosition;
        Quaternion tugRotation;

        if (tugParking != null)
        {
            tugPosition = tugParking.position;
            tugRotation = tugParking.rotation;
        }
        else
        {
            tugPosition = transform.position +
                transform.rotation * tugSpawnOffset;

            tugRotation = transform.rotation;
        }

        tugPosition.y = spawnHeight;

        spawnedCargoTug = Instantiate(
            cargoTugPrefab,
            tugPosition,
            tugRotation
        );

        tugController =
            spawnedCargoTug.GetComponent<AirportCargoVehicleController>();

        if (tugController == null)
        {
            Debug.LogError(
                "CargoTugLotSpawner: The cargo tug prefab must have an " +
                "AirportCargoVehicleController component on its root.",
                spawnedCargoTug
            );

            Destroy(spawnedCargoTug);
            return;
        }

        trailerFollower =
            spawnedCargoTug.GetComponent<GuidedCargoTrailerFollower>();

        if (tugParking != null)
        {
            tugController.SetHomeSpawnPoint(
                tugParking,
                Vector3.zero,
                spawnHeight
            );
        }
        else
        {
            tugController.SetHomeSpawnPoint(
                transform,
                tugSpawnOffset,
                spawnHeight
            );
        }

        economyManager =
            FindFirstObjectByType<AirportEconomyManager>();

        if (showDebugMessages)
        {
            Debug.Log(
                "CargoTugLotSpawner: Spawned cargo tug without a trailer at " +
                gameObject.name + ".",
                this
            );
        }
    }

    private void Update()
    {
        if (tugController == null)
            return;

        if (tugController.IsReturningHome)
            returnJourneyObserved = true;

        if (tugController.IsMoving)
        {
            if (!trailersAttached)
                AttachPurchasedTrailers();

            return;
        }

        if (returnJourneyObserved &&
            !tugController.IsReturningHome &&
            tugController.MovementStatus == "Arrived" &&
            IsTugAtHome())
        {
            DetachTrailersToParking();

            returnJourneyObserved = false;
        }
    }

    private void ResolveSpawnPoints()
    {
        if (tugParking == null)
            tugParking = transform.Find("TugParking");

        if (trailerParkingPositions == null ||
            trailerParkingPositions.Length != 4)
        {
            trailerParkingPositions = new Transform[4];
        }

        for (int i = 0; i < trailerParkingPositions.Length; i++)
        {
            if (trailerParkingPositions[i] != null)
                continue;

            trailerParkingPositions[i] =
                transform.Find("P" + (i + 1));
        }
    }

    public bool IsTrailerPurchased(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= purchasedTrailers.Length)
            return false;

        return purchasedTrailers[slotIndex] != null;
    }

    public bool TryPurchaseTrailer(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= purchasedTrailers.Length)
        {
            Debug.LogWarning(
                "CargoTugLotSpawner: Invalid trailer parking slot.",
                this
            );
            return false;
        }

        if (IsTrailerPurchased(slotIndex))
            return false;

        if (cargoTrailerPrefab == null)
        {
            Debug.LogError(
                "CargoTugLotSpawner: Cargo trailer prefab is missing.",
                this
            );
            return false;
        }

        if (trailerParkingPositions == null ||
            trailerParkingPositions[slotIndex] == null)
        {
            Debug.LogError(
                "CargoTugLotSpawner: Parking position P" +
                (slotIndex + 1) + " was not found.",
                this
            );
            return false;
        }

        if (economyManager == null)
        {
            economyManager =
                FindFirstObjectByType<AirportEconomyManager>();
        }

        if (economyManager == null)
        {
            Debug.LogError(
                "CargoTugLotSpawner: AirportEconomyManager was not found.",
                this
            );
            return false;
        }

        if (!economyManager.TrySpend(
                trailerPrice,
                "Cargo trailer purchase"))
        {
            return false;
        }

        Transform parkingPosition =
            trailerParkingPositions[slotIndex];

        Vector3 spawnPosition = parkingPosition.position;
        spawnPosition.y = trailerSpawnHeight;

        GameObject trailer = Instantiate(
            cargoTrailerPrefab,
            spawnPosition,
            parkingPosition.rotation
        );

        trailer.name = "CargoTrailer_P" + (slotIndex + 1);

        purchasedTrailers[slotIndex] = trailer;

        // If the tug is already moving, add the newly purchased
        // trailer to the existing trailer chain immediately.
        if (tugController != null && tugController.IsMoving)
            AttachPurchasedTrailers();

        if (showDebugMessages)
        {
            Debug.Log(
                "CargoTugLotSpawner: Purchased trailer for P" +
                (slotIndex + 1) + ".",
                this
            );
        }

        return true;
    }

    private void AttachPurchasedTrailers()
    {
        if (trailerFollower == null)
        {
            trailerFollower =
                spawnedCargoTug.GetComponent<GuidedCargoTrailerFollower>();
        }

        if (trailerFollower == null)
        {
            Debug.LogError(
                "CargoTugLotSpawner: The cargo tug prefab needs a " +
                "GuidedCargoTrailerFollower component to tow purchased trailers.",
                spawnedCargoTug
            );
            return;
        }

        Transform[] trailerTransforms = new Transform[4];
        int trailerCount = 0;

        for (int i = 0; i < purchasedTrailers.Length; i++)
        {
            if (purchasedTrailers[i] == null)
                continue;

            trailerTransforms[i] = purchasedTrailers[i].transform;
            trailerCount++;
        }

        if (trailerCount == 0)
            return;

        trailerFollower.SetTrailers(trailerTransforms);
        trailersAttached = true;

        if (showDebugMessages)
        {
            Debug.Log(
                "CargoTugLotSpawner: Attached " +
                trailerCount + " purchased trailer(s) to the tug.",
                this
            );
        }
    }

    private void DetachTrailersToParking()
    {
        if (!trailersAttached)
            return;

        if (trailerFollower != null)
            trailerFollower.ClearTrailers();

        for (int i = 0; i < purchasedTrailers.Length; i++)
        {
            GameObject trailer = purchasedTrailers[i];

            if (trailer == null ||
                trailerParkingPositions[i] == null)
            {
                continue;
            }

            Transform parkingPosition =
                trailerParkingPositions[i];

            Vector3 position = parkingPosition.position;
            position.y = trailerSpawnHeight;

            trailer.transform.SetPositionAndRotation(
                position,
                parkingPosition.rotation
            );
        }

        trailersAttached = false;

        if (showDebugMessages)
        {
            Debug.Log(
                "CargoTugLotSpawner: Tug parked. Purchased trailers " +
                "detached and returned to their parking positions.",
                this
            );
        }
    }

    private bool IsTugAtHome()
    {
        Vector3 homePosition;

        if (tugParking != null)
        {
            homePosition = tugParking.position;
        }
        else
        {
            homePosition = transform.position +
                transform.rotation * tugSpawnOffset;
        }

        homePosition.y = spawnHeight;

        return Vector3.Distance(
            spawnedCargoTug.transform.position,
            homePosition
        ) <= homeArrivalTolerance;
    }
}
