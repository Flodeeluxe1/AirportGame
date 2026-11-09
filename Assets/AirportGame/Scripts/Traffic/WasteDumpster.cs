using UnityEngine;

/// <summary>
/// Stores waste for one physical dumpster.
/// Waste values are generic gameplay units; they are not tied to a visual fill level.
/// </summary>
[DisallowMultipleComponent]
public class WasteDumpster : MonoBehaviour
{
    [Header("Waste Capacity")]
    [Min(0.01f)]
    [SerializeField] private float maximumCapacity = 100f;
    [Min(0f)]
    [SerializeField] private float currentWaste;

    public float MaximumCapacity => Mathf.Max(0.01f, maximumCapacity);
    public float CurrentWaste => Mathf.Clamp(currentWaste, 0f, MaximumCapacity);
    public float RemainingCapacity => Mathf.Max(0f, MaximumCapacity - CurrentWaste);
    public bool IsFull => RemainingCapacity <= 0.001f;
    public bool HasWaste => CurrentWaste > 0.001f;

    /// <summary>Adds waste and returns the amount accepted by this dumpster.</summary>
    public float AddWaste(float amount)
    {
        if (amount <= 0f)
            return 0f;

        float accepted = Mathf.Min(amount, RemainingCapacity);
        currentWaste = Mathf.Clamp(CurrentWaste + accepted, 0f, MaximumCapacity);
        return accepted;
    }

    /// <summary>Removes up to the requested amount and returns the amount collected.</summary>
    public float CollectWaste(float requestedAmount)
    {
        if (requestedAmount <= 0f || !HasWaste)
            return 0f;

        float collected = Mathf.Min(requestedAmount, CurrentWaste);
        currentWaste = Mathf.Max(0f, CurrentWaste - collected);
        return collected;
    }

    private void OnValidate()
    {
        maximumCapacity = Mathf.Max(0.01f, maximumCapacity);
        currentWaste = Mathf.Clamp(currentWaste, 0f, maximumCapacity);
    }
}
