using UnityEngine;

/// <summary>Reusable signed change to one UtilityNeeds value.</summary>
[CreateAssetMenu(menuName = "Airport/Utility AI/Need Effect", fileName = "NewNeedEffect")]
public class NeedEffectDefinition : ScriptableObject
{
    [SerializeField] private UtilityNeeds.NeedType need;
    [SerializeField] private float amount = 25f;
    [Range(0f, 100f)]
    [SerializeField] private float maximumNeedValueToConsider = 80f;

    public UtilityNeeds.NeedType Need => need;
    public float Amount => amount;
    public float MaximumNeedValueToConsider => maximumNeedValueToConsider;

    private void OnValidate()
    {
        maximumNeedValueToConsider = Mathf.Clamp(maximumNeedValueToConsider, 0f, 100f);
    }
}
