using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reusable data describing an interaction that an AI agent can perform.
/// Interaction behaviour is implemented by the agent/action system, not by this asset.
/// </summary>
public class InteractionDefinition : ScriptableObject
{
    [Serializable]
    public class NeedEffect
    {
        public UtilityNeeds.NeedType need;
        [Min(-100f)]
        [Tooltip("Signed change: positive values improve the need; negative values reduce it.")]
        public float satisfactionAmount = 25f;
        [Range(0f, 100f)]
        [Tooltip("The interaction is only considered while this need is below the threshold.")]
        public float maximumNeedValueToConsider = 80f;
    }

    [Serializable]
    public class NeedConflict
    {
        public UtilityNeeds.NeedType need;
        [Range(0f, 1f)]
        [Tooltip("How strongly this need reduces the interaction's utility when it becomes critical. 0 = no penalty, 1 = up to 100% penalty.")]
        public float penaltyAtMaximumUrgency = 1f;
    }

    [Header("Identity")]
    [SerializeField] private string interactionId;
    [SerializeField] private string displayName = "New Interaction";
    [TextArea]
    [SerializeField] private string description;

    [Header("Usage")]
    [Min(0f)]
    [SerializeField] private float durationSeconds = 5f;
    [Min(1)]
    [SerializeField] private int maxConcurrentUsers = 1;
    [Min(0f)]
    [SerializeField] private float interactionRange = 1.5f;

    [Header("Need Effects")]
    [SerializeField] private List<NeedEffect> needEffects = new List<NeedEffect>();

    [Header("Need Conflicts")]
    [SerializeField] private List<NeedConflict> needConflicts = new List<NeedConflict>();

    public string InteractionId => interactionId;
    public string DisplayName => displayName;
    public string Description => description;
    public float DurationSeconds => durationSeconds;
    public int MaxConcurrentUsers => Mathf.Max(1, maxConcurrentUsers);
    public float InteractionRange => Mathf.Max(0f, interactionRange);
    public IReadOnlyList<NeedEffect> NeedEffects => needEffects;
    public IReadOnlyList<NeedConflict> NeedConflicts => needConflicts;

    private void OnValidate()
    {
        maxConcurrentUsers = Mathf.Max(1, maxConcurrentUsers);
        durationSeconds = Mathf.Max(0f, durationSeconds);
        interactionRange = Mathf.Max(0f, interactionRange);

        if (needEffects == null)
            needEffects = new List<NeedEffect>();

        foreach (NeedEffect effect in needEffects)
        {
            if (effect != null)
            {
                effect.satisfactionAmount = Mathf.Clamp(effect.satisfactionAmount, -100f, 100f);
                effect.maximumNeedValueToConsider =
                    Mathf.Clamp(effect.maximumNeedValueToConsider, 0f, 100f);
            }
        }

        if (needConflicts == null)
            needConflicts = new List<NeedConflict>();

        foreach (NeedConflict conflict in needConflicts)
        {
            if (conflict != null)
                conflict.penaltyAtMaximumUrgency =
                    Mathf.Clamp01(conflict.penaltyAtMaximumUrgency);
        }
    }
}