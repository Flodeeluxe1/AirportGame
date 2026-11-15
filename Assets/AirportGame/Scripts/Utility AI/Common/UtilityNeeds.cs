using System;
using UnityEngine;

/// <summary>
/// Reusable, visit-scoped needs for Utility AI agents.
/// Values range from 0 (critical) to 100 (fully satisfied).
/// Decay is driven by in-game time rather than frame time.
/// </summary>
[DisallowMultipleComponent]
public class UtilityNeeds : MonoBehaviour
{
    public enum NeedType
    {
        Thirst,
        Hunger,
        Bladder,
        Energy,
        Social,
        Entertainment,
        Comfort,
        Curiosity
    }

    [Serializable]
    private class NeedDecayRates
    {
        [Min(0f)] public float thirst = 0.25f;
        [Min(0f)] public float hunger = 0.15f;
        [Min(0f)] public float bladder = 0.2f;
        [Min(0f)] public float energy = 0.08f;
        [Min(0f)] public float social = 0.06f;
        [Min(0f)] public float entertainment = 0.08f;
        [Min(0f)] public float comfort = 0.05f;
        [Min(0f)] public float curiosity = 0.05f;
    }

    [Header("Initial State")]
    [SerializeField, Range(0f, 100f)] private float initialNeedValue = 100f;

    [Header("Decay Per In-Game Minute")]
    [SerializeField] private NeedDecayRates decayRates = new NeedDecayRates();

    [Header("Current Needs")]
    [SerializeField, Range(0f, 100f)] private float thirst = 100f;
    [SerializeField, Range(0f, 100f)] private float hunger = 100f;
    [SerializeField, Range(0f, 100f)] private float bladder = 100f;
    [SerializeField, Range(0f, 100f)] private float energy = 100f;
    [SerializeField, Range(0f, 100f)] private float social = 100f;
    [SerializeField, Range(0f, 100f)] private float entertainment = 100f;
    [SerializeField, Range(0f, 100f)] private float comfort = 100f;
    [SerializeField, Range(0f, 100f)] private float curiosity = 100f;

    private GameTimeManager gameTimeManager;
    private long lastGameSecond;
    private bool subscribedToGameTime;

    public event Action<NeedType, float> NeedChanged;
    public event Action NeedsChanged;

    public float GetValue(NeedType need)
    {
        return need switch
        {
            NeedType.Thirst => thirst,
            NeedType.Hunger => hunger,
            NeedType.Bladder => bladder,
            NeedType.Energy => energy,
            NeedType.Social => social,
            NeedType.Entertainment => entertainment,
            NeedType.Comfort => comfort,
            NeedType.Curiosity => curiosity,
            _ => 100f
        };
    }

    /// <summary>
    /// Returns urgency from 0 (no urgency) to 100 (critical).
    /// </summary>
    public float GetUrgency(NeedType need)
    {
        return 100f - GetValue(need);
    }

    /// <summary>
    /// Adds satisfaction to a need. Positive amounts improve the need;
    /// negative amounts reduce it.
    /// </summary>
    public void ModifyNeed(NeedType need, float amount)
    {
        SetValue(need, GetValue(need) + amount);
    }

    public void SatisfyNeed(NeedType need, float amount)
    {
        ModifyNeed(need, Mathf.Abs(amount));
    }

    /// <summary>
    /// Resets all needs for a new airport visit.
    /// This state is intentionally not persisted between visits.
    /// </summary>
    public void ResetForNewVisit()
    {
        SetAllNeeds(initialNeedValue);
        RefreshGameTimeBaseline();

        foreach (NeedType need in Enum.GetValues(typeof(NeedType)))
            NeedChanged?.Invoke(need, GetValue(need));

        NeedsChanged?.Invoke();
    }

    private void OnEnable()
    {
        ResolveGameTimeManager();
    }

    private void Start()
    {
        ResolveGameTimeManager();
    }

    private void OnDisable()
    {
        UnsubscribeFromGameTime();
    }

    private void ResolveGameTimeManager()
    {
        if (gameTimeManager == null)
            gameTimeManager = FindFirstObjectByType<GameTimeManager>();

        if (gameTimeManager == null || subscribedToGameTime)
            return;

        gameTimeManager.GameTimeAdvanced += HandleGameTimeAdvanced;
        subscribedToGameTime = true;
        RefreshGameTimeBaseline();
    }

    private void UnsubscribeFromGameTime()
    {
        if (gameTimeManager != null && subscribedToGameTime)
            gameTimeManager.GameTimeAdvanced -= HandleGameTimeAdvanced;

        subscribedToGameTime = false;
    }

    private void RefreshGameTimeBaseline()
    {
        if (gameTimeManager == null)
            return;

        lastGameSecond =
            (gameTimeManager.CurrentDay - 1) * 86400L +
            gameTimeManager.GetGameTimeSeconds();
    }

    private void HandleGameTimeAdvanced(long previousSecond, long currentSecond)
    {
        // Ignore clock rewinds and establish the new baseline.
        if (currentSecond <= previousSecond)
        {
            lastGameSecond = currentSecond;
            return;
        }

        long elapsedSeconds = currentSecond - lastGameSecond;
        lastGameSecond = currentSecond;

        if (elapsedSeconds <= 0)
            return;

        float elapsedGameMinutes = elapsedSeconds / 60f;
        bool changed = false;

        changed |= ApplyDecay(NeedType.Thirst, decayRates.thirst, elapsedGameMinutes);
        changed |= ApplyDecay(NeedType.Hunger, decayRates.hunger, elapsedGameMinutes);
        changed |= ApplyDecay(NeedType.Bladder, decayRates.bladder, elapsedGameMinutes);
        changed |= ApplyDecay(NeedType.Energy, decayRates.energy, elapsedGameMinutes);
        changed |= ApplyDecay(NeedType.Social, decayRates.social, elapsedGameMinutes);
        changed |= ApplyDecay(NeedType.Entertainment, decayRates.entertainment, elapsedGameMinutes);
        changed |= ApplyDecay(NeedType.Comfort, decayRates.comfort, elapsedGameMinutes);
        changed |= ApplyDecay(NeedType.Curiosity, decayRates.curiosity, elapsedGameMinutes);

        if (changed)
            NeedsChanged?.Invoke();
    }

    private bool ApplyDecay(NeedType need, float ratePerMinute, float elapsedGameMinutes)
    {
        if (ratePerMinute <= 0f || elapsedGameMinutes <= 0f)
            return false;

        float previousValue = GetValue(need);
        float nextValue = Mathf.Max(0f, previousValue - ratePerMinute * elapsedGameMinutes);

        if (Mathf.Approximately(previousValue, nextValue))
            return false;

        SetValue(need, nextValue, false);
        NeedChanged?.Invoke(need, nextValue);
        return true;
    }

    private void SetAllNeeds(float value)
    {
        float clamped = Mathf.Clamp(value, 0f, 100f);
        thirst = clamped;
        hunger = clamped;
        bladder = clamped;
        energy = clamped;
        social = clamped;
        entertainment = clamped;
        comfort = clamped;
        curiosity = clamped;
    }

    private void SetValue(NeedType need, float value, bool notify = true)
    {
        float clamped = Mathf.Clamp(value, 0f, 100f);
        float previousValue = GetValue(need);

        if (Mathf.Approximately(previousValue, clamped))
            return;

        switch (need)
        {
            case NeedType.Thirst: thirst = clamped; break;
            case NeedType.Hunger: hunger = clamped; break;
            case NeedType.Bladder: bladder = clamped; break;
            case NeedType.Energy: energy = clamped; break;
            case NeedType.Social: social = clamped; break;
            case NeedType.Entertainment: entertainment = clamped; break;
            case NeedType.Comfort: comfort = clamped; break;
            case NeedType.Curiosity: curiosity = clamped; break;
        }

        if (notify)
        {
            NeedChanged?.Invoke(need, clamped);
            NeedsChanged?.Invoke();
        }
    }

    private void OnValidate()
    {
        initialNeedValue = Mathf.Clamp(initialNeedValue, 0f, 100f);

        thirst = Mathf.Clamp(thirst, 0f, 100f);
        hunger = Mathf.Clamp(hunger, 0f, 100f);
        bladder = Mathf.Clamp(bladder, 0f, 100f);
        energy = Mathf.Clamp(energy, 0f, 100f);
        social = Mathf.Clamp(social, 0f, 100f);
        entertainment = Mathf.Clamp(entertainment, 0f, 100f);
        comfort = Mathf.Clamp(comfort, 0f, 100f);
        curiosity = Mathf.Clamp(curiosity, 0f, 100f);
    }
}
