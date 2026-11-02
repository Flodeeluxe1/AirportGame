
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

    [Header("Debug")]
    [SerializeField] private bool showDebugMessages = true;

    private GameObject spawnedCargoTug;

    private readonly GameObject[] purchasedTrailers =
        new GameObject[4];

    private AirportEconomyManager economyManager;

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

        AirportCargoVehicleController controller =
            spawnedCargoTug.GetComponent<AirportCargoVehicleController>();

        if (controller == null)
        {
            Debug.LogError(
                "CargoTugLotSpawner: The cargo tug prefab must have an " +
                "AirportCargoVehicleController component on its root.",
                spawnedCargoTug
            );

            Destroy(spawnedCargoTug);
            return;
        }

        if (tugParking != null)
        {
            controller.SetHomeSpawnPoint(
                tugParking,
                Vector3.zero,
                spawnHeight
            );
        }
        else
        {
            controller.SetHomeSpawnPoint(
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
}
