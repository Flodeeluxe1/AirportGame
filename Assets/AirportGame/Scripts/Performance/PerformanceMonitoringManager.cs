using System;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.InputSystem;

/// <summary>
/// Lightweight runtime performance overlay. Add this component to an empty
/// GameObject to monitor frame rate, frame-time spikes, memory and GC activity.
/// </summary>
[DisallowMultipleComponent]
public class PerformanceMonitoringManager : MonoBehaviour
{
    [Header("Sampling")]
    [SerializeField, Min(0.1f)] private float sampleInterval = 1f;
    [SerializeField, Min(1f)] private float frameSpikeThresholdMs = 33.3f;
    [SerializeField] private bool startVisible = true;

    [Header("Overlay")]
    [SerializeField] private Vector2 overlayPosition = new Vector2(12f, 12f);
    [SerializeField] private float overlayWidth = 340f;

    private float sampleElapsed;
    private float sampleFrameTime;
    private int sampleFrameCount;
    private float minimumFrameTime = float.MaxValue;
    private float maximumFrameTime;
    private int spikeCount;
    private int totalSpikeCount;
    private int lastGen0Collections;
    private int lastGen1Collections;
    private int lastGen2Collections;
    private int gen0Collections;
    private int gen1Collections;
    private int gen2Collections;
    private long managedMemoryBytes;
    private long allocatedMemoryBytes;
    private long reservedMemoryBytes;
    private float averageFps;
    private float minimumFps;
    private float maximumFps;
    private float averageFrameTimeMs;
    private float worstFrameTimeMs;
    private string cachedOverlayText;
    private bool isVisible;

    private void Awake()
    {
        isVisible = startVisible;
        lastGen0Collections = GC.CollectionCount(0);
        lastGen1Collections = GC.CollectionCount(1);
        lastGen2Collections = GC.CollectionCount(2);
        UpdateMemoryStats();
        UpdateOverlayText();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
            isVisible = !isVisible;

        float frameTime = Time.unscaledDeltaTime;
        if (frameTime <= 0f)
            return;

        sampleElapsed += frameTime;
        sampleFrameTime += frameTime;
        sampleFrameCount++;

        minimumFrameTime = Mathf.Min(minimumFrameTime, frameTime);
        maximumFrameTime = Mathf.Max(maximumFrameTime, frameTime);

        if (frameTime * 1000f >= frameSpikeThresholdMs)
        {
            spikeCount++;
            totalSpikeCount++;
        }

        if (sampleElapsed >= sampleInterval)
            CompleteSample();
    }

    private void CompleteSample()
    {
        if (sampleFrameCount > 0)
        {
            averageFrameTimeMs = sampleFrameTime / sampleFrameCount * 1000f;
            averageFps = sampleFrameTime > 0f
                ? sampleFrameCount / sampleFrameTime
                : 0f;
            minimumFps = maximumFrameTime > 0f
                ? 1f / maximumFrameTime
                : 0f;
            maximumFps = minimumFrameTime > 0f
                ? 1f / minimumFrameTime
                : 0f;
            worstFrameTimeMs = maximumFrameTime * 1000f;
        }

        gen0Collections = GC.CollectionCount(0) - lastGen0Collections;
        gen1Collections = GC.CollectionCount(1) - lastGen1Collections;
        gen2Collections = GC.CollectionCount(2) - lastGen2Collections;
        lastGen0Collections += gen0Collections;
        lastGen1Collections += gen1Collections;
        lastGen2Collections += gen2Collections;

        UpdateMemoryStats();
        UpdateOverlayText();

        sampleElapsed = 0f;
        sampleFrameTime = 0f;
        sampleFrameCount = 0;
        minimumFrameTime = float.MaxValue;
        maximumFrameTime = 0f;
        spikeCount = 0;
    }

    private void UpdateMemoryStats()
    {
        managedMemoryBytes = GC.GetTotalMemory(false);
        allocatedMemoryBytes = Profiler.GetTotalAllocatedMemoryLong();
        reservedMemoryBytes = Profiler.GetTotalReservedMemoryLong();
    }

    private void UpdateOverlayText()
    {
        cachedOverlayText =
            "PERFORMANCE MONITOR  (F8: toggle)\n" +
            $"FPS: {averageFps:0.0} avg   {minimumFps:0.0} min   {maximumFps:0.0} max\n" +
            $"Frame: {averageFrameTimeMs:0.0} ms avg   {worstFrameTimeMs:0.0} ms worst\n" +
            $"Spikes (>{frameSpikeThresholdMs:0} ms): {spikeCount} this sample   {totalSpikeCount} total\n" +
            $"GC collections: Gen0 {gen0Collections}   Gen1 {gen1Collections}   Gen2 {gen2Collections}\n" +
            $"Managed heap: {FormatBytes(managedMemoryBytes)}\n" +
            $"Unity allocated: {FormatBytes(allocatedMemoryBytes)}\n" +
            $"Unity reserved: {FormatBytes(reservedMemoryBytes)}";
    }

    private static string FormatBytes(long bytes)
    {
        const float kilobyte = 1024f;
        const float megabyte = kilobyte * 1024f;
        const float gigabyte = megabyte * 1024f;

        if (bytes >= gigabyte)
            return (bytes / gigabyte).ToString("0.00") + " GB";
        if (bytes >= megabyte)
            return (bytes / megabyte).ToString("0.0") + " MB";
        if (bytes >= kilobyte)
            return (bytes / kilobyte).ToString("0.0") + " KB";
        return bytes + " B";
    }

    private void OnGUI()
    {
        if (!isVisible)
            return;

        GUI.depth = -100;
        GUIStyle style = GUI.skin.box;
        style.alignment = TextAnchor.UpperLeft;
        style.fontSize = 13;
        style.richText = false;

        float height = 176f;
        GUI.Box(
            new Rect(overlayPosition.x, overlayPosition.y, overlayWidth, height),
            cachedOverlayText,
            style);
    }

    private void OnValidate()
    {
        sampleInterval = Mathf.Max(0.1f, sampleInterval);
        frameSpikeThresholdMs = Mathf.Max(1f, frameSpikeThresholdMs);
        overlayWidth = Mathf.Max(240f, overlayWidth);
    }
}
