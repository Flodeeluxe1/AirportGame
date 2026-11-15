using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reusable data describing an interaction that an AI agent can perform.
/// Interaction behaviour is implemented by the agent/action system, not by this asset.
/// </summary>
[CreateAssetMenu(
    fileName = "InteractionDefinition",
    menuName = "Airport/Utility AI/Interaction Definition")]
public class InteractionDefinition : ScriptableObject
{
    [Serializable]
    public class NeedEffect
    {
        public UtilityNeeds.NeedType need;
        [Min(0f)] public float satisfactionAmount = 25f;
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

    public string InteractionId => interactionId;
    public string DisplayName => displayName;
    public string Description => description;
    public float DurationSeconds => durationSeconds;
    public int MaxConcurrentUsers => Mathf.Max(1, maxConcurrentUsers);
    public float InteractionRange => Mathf.Max(0f, interactionRange);
    public IReadOnlyList<NeedEffect> NeedEffects => needEffects;

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
                effect.satisfactionAmount = Mathf.Max(0f, effect.satisfactionAmount);
        }
    }
}