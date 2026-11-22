using UnityEngine;

/// <summary>
/// Shared bounded time-budget helpers for movement systems that must process
/// simulation time independently of Unity's rendered-frame and physics clocks.
/// </summary>
public static class SimulationMovementUtility
{
    /// <summary>
    /// Adds a valid simulation-time delta to a movement system's pending budget.
    /// </summary>
    public static void AccumulateTime(ref float pendingTime, float deltaTime)
    {
        if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            return;

        pendingTime += deltaTime;
    }

    /// <summary>
    /// Removes and returns the next bounded movement step from a pending budget.
    /// The caller chooses the step size and maximum number of steps per frame.
    /// </summary>
    public static bool TryConsumeStep(
        ref float pendingTime,
        float maximumStep,
        out float step)
    {
        step = 0f;

        if (float.IsNaN(pendingTime) || float.IsInfinity(pendingTime))
        {
            pendingTime = 0f;
            return false;
        }

        if (pendingTime <= 0f ||
            maximumStep <= 0f ||
            float.IsNaN(maximumStep) ||
            float.IsInfinity(maximumStep))
        {
            return false;
        }

        step = Mathf.Min(pendingTime, maximumStep);
        pendingTime = Mathf.Max(0f, pendingTime - step);
        return step > 0f;
    }
}
