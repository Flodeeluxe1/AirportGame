using UnityEngine;

/// <summary>
/// Applies a configurable Hygiene penalty when the configured interaction
/// successfully completes at this toilet.
/// </summary>
[DisallowMultipleComponent]
public class ToiletHygieneEffect : MonoBehaviour
{
    [Header("Hygiene Penalty")]
    [SerializeField] private string interactionId = "use_toilet";
    [Min(0f)]
    [SerializeField] private float hygieneReduction = 20f;

    public void ApplyAfterInteraction(
        UtilityNeeds needs,
        InteractionDefinition completedInteraction)
    {
        if (needs == null || completedInteraction == null ||
            string.IsNullOrEmpty(interactionId) ||
            completedInteraction.InteractionId != interactionId ||
            hygieneReduction <= 0f)
            return;

        needs.ModifyNeed(UtilityNeeds.NeedType.Hygiene, -hygieneReduction);
    }

    private void OnValidate()
    {
        hygieneReduction = Mathf.Max(0f, hygieneReduction);
    }
}
