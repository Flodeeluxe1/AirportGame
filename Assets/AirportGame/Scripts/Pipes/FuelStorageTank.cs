using UnityEngine;

/// <summary>
/// Adds storage capacity to the shared fuel network when this tank's
/// FuelConnector is connected to that network.
/// </summary>
[DisallowMultipleComponent]
public sealed class FuelStorageTank : MonoBehaviour
{
    [Header("Storage")]
    [Min(1f)]
    [SerializeField] private float capacityLitres = 20000f;

    [Min(0f)]
    [SerializeField] private float initialFuelLitres = 0f;

    public float CapacityLitres => Mathf.Max(1f, capacityLitres);
    public float InitialFuelLitres =>
        Mathf.Clamp(initialFuelLitres, 0f, CapacityLitres);

#if UNITY_EDITOR
    private void OnValidate()
    {
        capacityLitres = Mathf.Max(1f, capacityLitres);
        initialFuelLitres = Mathf.Clamp(initialFuelLitres, 0f, capacityLitres);
    }
#endif
}
