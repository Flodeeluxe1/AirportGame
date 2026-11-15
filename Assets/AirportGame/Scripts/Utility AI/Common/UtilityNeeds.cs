using System;
using UnityEngine;

/// <summary>
/// Reusable, visit-scoped needs for Utility AI agents.
/// Values range from 0 (critical) to 100 (fully satisfied).
/// Decay is driven by in-game time rather than frame time.
/// Call ResetForNewVisit when an agent begins a fresh airport visit.
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

    [Serializable]
    private class NeedInitialRanges
    {
        [Range(0f, 100f)] public float thirstMin = 70f;
        [Range(0f, 100f)] public float thirstMax = 100f;
        [Range(0f, 100f)] public float hungerMin = 70f;
        [Range(0f, 100f)] public float hungerMax = 100f;
        [Range(0f, 100f)] public float bladderMin = 70f;
        [Range(0f, 100f)] public float bladderMax = 100f;
        [Range(0f, 100f)] public float energyMin = 70f;
        [Range(0f, 100f)] public float energyMax = 100f;
        [Range(0f, 100f)] public float socialMin = 70f;
        [Range(0f, 100f)] public float socialMax = 100f;
        [Range(0f, 100f)] public float entertainmentMin = 70f;
        [Range(0f, 100f)] public float entertainmentMax = 100f;
        [Range(0f, 100f)] public float comfortMin = 70f;
        [Range(0f, 100f)] public float comfortMax = 100f;
        [Range(0f, 100f)] public float curiosityMin = 70f;
        [Range(0f, 100f)] public float curiosityMax = 100f;

        public float GetRandomValue(NeedType need)
        {
            float minimum;
            float maximum;

            switch (need)
            {
                case NeedType.Thirst: minimum = thirstMin; maximum = thirstMax; break;
                case NeedType.Hunger: minimum = hungerMin; maximum = hungerMax; break;
                case NeedType.Bladder: minimum = bladderMin; maximum = bladderMax; break;
                case NeedType.Energy: minimum = energyMin; maximum = energyMax; break;
                case NeedType.Social: minimum = socialMin; maximum = socialMax; break;
                case NeedType.Entertainment: minimum = entertainmentMin; maximum = entertainmentMax; break;
                case NeedType.Comfort: minimum = comfortMin; maximum = comfortMax; break;
                case NeedType.Curiosity: minimum = curiosityMin; maximum = curiosityMax; break;
                default: minimum = 70f; maximum = 100f; break;
            }

            minimum = Mathf.Clamp(minimum, 0f, 100f);
            maximum = Mathf.Clamp(maximum, minimum, 100f);
            return UnityEngine.Random.Range(minimum, maximum);
        }

        public void ClampRanges()
        {
            thirstMin = Mathf.Clamp(thirstMin, 0f, 100f);
            thirstMax = Mathf.Clamp(thirstMax, thirstMin, 100f);
            hungerMin = Mathf.Clamp(hungerMin, 0f, 100f);
            hungerMax = Mathf.Clamp(hungerMax, hungerMin, 100f);
            bladderMin = Mathf.Clamp(bladderMin, 0f, 100f);
            bladderMax = Mathf.Clamp(bladderMax, bladderMin, 100f);
            energyMin = Mathf.Clamp(energyMin, 0f, 100f);
            energyMax = Mathf.Clamp(energyMax, energyMin, 100f);
            socialMin = Mathf.Clamp(socialMin, 0f, 100f);
            socialMax = Mathf.Clamp(socialMax, socialMin, 100f);
            entertainmentMin = Mathf.Clamp(entertainmentMin, 0f, 100f);
            entertainmentMax = Mathf.Clamp(entertainmentMax, entertainmentMin, 100f);
            comfortMin = Mathf.Clamp(comfortMin, 0f, 100f);
            comfortMax = Mathf.Clamp(comfortMax, comfortMin, 100f);
            curiosityMin = Mathf.Clamp(curiosityMin, 0f, 100f);
            curiosityMax = Mathf.Clamp(curiosityMax, curiosityMin, 100f);
        }
    }

    [Serializable]
    private class NeedUrgencyCurves
    {
        private static AnimationCurve CreateCurve(float[] values)
        {
            const int keyCount = 5;
            Keyframe[] keys = new Keyframe[keyCount];

            for (int i = 0; i < keyCount; i++)
            {
                float slope;
                if (i == 0)
                    slope = (values[1] - values[0]) * 4f;
                else if (i == keyCount - 1)
                    slope = (values[i] - values[i - 1]) * 4f;
                else
                    slope = (values[i + 1] - values[i - 1]) * 2f;

                keys[i] = new Keyframe(i * 0.25f, values[i], slope, slope);
            }

            return new AnimationCurve(keys);
        }

        // Values represent utility urgency at 0%, 25%, 50%, 75% and 100% need urgency.
        public AnimationCurve thirst = CreateCurve(new[] { 0f, 0.08f, 0.28f, 0.68f, 1f });
        public AnimationCurve hunger = CreateCurve(new[] { 0f, 0.01f, 0.06f, 0.32f, 1f });
        public AnimationCurve bladder = CreateCurve(new[] { 0f, 0.04f, 0.18f, 0.55f, 1f });
        public AnimationCurve energy = CreateCurve(new[] { 0f, 0.03f, 0.18f, 0.60f, 1f });
        public AnimationCurve social = CreateCurve(new[] { 0.01f, 0.05f, 0.18f, 0.50f, 1f });
        public AnimationCurve entertainment = CreateCurve(new[] { 0.03f, 0.10f, 0.25f, 0.60f, 1f });
        public AnimationCurve comfort = CreateCurve(new[] { 0f, 0.03f, 0.12f, 0.40f, 0.90f });
        public AnimationCurve curiosity = CreateCurve(new[] { 0.03f, 0.08f, 0.20f, 0.45f, 0.85f });

        public AnimationCurve GetCurve(NeedType need)
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
                _ => thirst
            };
        }
    }

    [Serializable]
    private class NeedUtilityWeights
    {
        [Min(0f)] public float thirst = 1.4f;
        [Min(0f)] public float hunger = 0.9f;
        [Min(0f)] public float bladder = 1f;
        [Min(0f)] public float energy = 1f;
        [Min(0f)] public float social = 0.85f;
        [Min(0f)] public float entertainment = 0.75f;
        [Min(0f)] public float comfort = 0.85f;
        [Min(0f)] public float curiosity = 0.7f;

        public float GetWeight(NeedType need)
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
                _ => 1f
            };
        }
    }

    [Header("Initial State")]
    [Tooltip("When enabled, each need receives an independently randomised value when a new visit starts.")]
    [SerializeField] private bool randomizeInitialNeeds = true;
    [SerializeField] private NeedInitialRanges initialNeedRanges = new NeedInitialRanges();
    [Tooltip("Used for every need when random initial values are disabled.")]
    [SerializeField, Range(0f, 100f)] private float initialNeedValue = 100f;

    [Header("Decay Per In-Game Minute")]
    [SerializeField] private NeedDecayRates decayRates = new NeedDecayRates();

    [Header("Urgency Curves")]
    [Tooltip("Each curve maps normalized need urgency (0 = satisfied, 1 = critical) to utility urgency. Curves are evaluated independently for each need.")]
    [SerializeField] private NeedUrgencyCurves urgencyCurves = new NeedUrgencyCurves();

    [Header("Need Utility Weights")]
    [Tooltip("Scales each need's curve-adjusted urgency when comparing interactions. A higher weight makes that need more influential.")]
    [SerializeField] private NeedUtilityWeights utilityWeights = new NeedUtilityWeights();

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
    private readonly System.Collections.Generic.HashSet<NeedType> decayPausedNeeds =
        new System.Collections.Generic.HashSet<NeedType>();

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
    /// Returns curve-adjusted urgency from 0 (no urgency) to 100 (critical).
    /// </summary>
    public float GetUrgency(NeedType need)
    {
        float normalizedUrgency = Mathf.Clamp01((100f - GetValue(need)) / 100f);
        AnimationCurve curve = urgencyCurves != null ? urgencyCurves.GetCurve(need) : null;
        float curvedUrgency = curve != null ? curve.Evaluate(normalizedUrgency) : normalizedUrgency;
        return Mathf.Clamp01(curvedUrgency) * 100f;
    }

    /// <summary>
    /// Returns the relative importance multiplier for a need.
    /// </summary>
    public float GetUtilityWeight(NeedType need)
    {
        return utilityWeights != null ? Mathf.Max(0f, utilityWeights.GetWeight(need)) : 1f;
    }

    /// <summary>
    /// Adds satisfaction to a need. Positive amounts improve the need;
    /// negative amounts reduce it.
    /// </summary>
    public void ModifyNeed(NeedType need, float amount)
    {
        SetValue(need, GetValue(need) + amount);
    }

    /// <summary>
    /// Temporarily prevents one need from decaying while an interaction replenishes it.
    /// Calls are idempotent; ResumeNeedDecay re-enables decay for that need.
    /// </summary>
    public void PauseNeedDecay(NeedType need)
    {
        decayPausedNeeds.Add(need);
    }

    public void ResumeNeedDecay(NeedType need)
    {
        decayPausedNeeds.Remove(need);
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
        if (randomizeInitialNeeds && initialNeedRanges != null)
        {
            foreach (NeedType need in Enum.GetValues(typeof(NeedType)))
                SetValue(need, initialNeedRanges.GetRandomValue(need), false);
        }
        else
        {
            SetAllNeeds(initialNeedValue);
        }

        decayPausedNeeds.Clear();
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
        if (decayPausedNeeds.Contains(need) || ratePerMinute <= 0f || elapsedGameMinutes <= 0f)
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
        if (initialNeedRanges != null)
            initialNeedRanges.ClampRanges();

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