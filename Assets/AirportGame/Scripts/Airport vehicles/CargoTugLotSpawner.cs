
using UnityEngine;

[DisallowMultipleComponent]
public class CargoTugLotSpawner : MonoBehaviour
{
    [Header("Prefabs")]
    [Tooltip("The cargo tug prefab, including GuidedCargoTrailerFollower.")]
    [SerializeField] private GameObject cargoTugPrefab;

    [Tooltip("The separate cargo trailer prefab.")]
    [SerializeField] private GameObject cargoTrailerPrefab;

    [Header("Spawn Offsets")]
    [Tooltip("Position offset from the CargoTugLot origin.")]
    [SerializeField] private Vector3 tugSpawnOffset = Vector3.zero;

    [Tooltip("Position offset from the CargoTugLot origin.")]
    [SerializeField] private Vector3 trailerSpawnOffset = Vector3.zero;

    [Header("Spawn Settings")]
    [Tooltip("World-space height at which both objects spawn.")]
    [SerializeField] private float spawnHeight = 0.23f;
    [SerializeField] private float trailerSpawnHeight = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool showDebugMessages = true;

    private GameObject spawnedCargoTug;
    private GameObject spawnedCargoTrailer;


    private void Start()
    {
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

        Vector3 tugPosition = transform.position + transform.rotation *
            tugSpawnOffset;

        tugPosition.y = spawnHeight;

        spawnedCargoTug = Instantiate(
            cargoTugPrefab,
            tugPosition,
            transform.rotation
        );

        Vector3 trailerPosition = transform.position + transform.rotation *
            trailerSpawnOffset;

        trailerPosition.y = trailerSpawnHeight;

        spawnedCargoTrailer = Instantiate(
            cargoTrailerPrefab,
            trailerPosition,
            transform.rotation
        );

        GuidedCargoTrailerFollower follower =
            spawnedCargoTug.GetComponent<GuidedCargoTrailerFollower>();

        if (follower == null)
        {
            Debug.LogError(
                "CargoTugLotSpawner: The cargo tug prefab must have a " +
                "GuidedCargoTrailerFollower component on its root GameObject.",
                spawnedCargoTug
            );

            Destroy(spawnedCargoTrailer);
            Destroy(spawnedCargoTug);
            return;
        }

        follower.SetTrailer(spawnedCargoTrailer.transform);

        AirportCargoVehicleController controller =
            spawnedCargoTug.GetComponent<AirportCargoVehicleController>();

        if (controller == null)
        {
            Debug.LogError(
                "CargoTugLotSpawner: The cargo tug prefab must have an " +
                "AirportCargoVehicleController component on its root.",
                spawnedCargoTug
            );

            Destroy(spawnedCargoTrailer);
            Destroy(spawnedCargoTug);
            return;
        }

        controller.SetHomeSpawnPoint(
            transform,
            tugSpawnOffset,
            spawnHeight
        );

        if (showDebugMessages)
        {
            Debug.Log(
                "CargoTugLotSpawner: Spawned cargo tug and trailer at " +
                gameObject.name + ". Home spawn point registered.",
                this
            );
        }
    }

}
