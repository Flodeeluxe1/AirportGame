using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class ATCManualHoldingPoint : MonoBehaviour
{
    private static readonly HashSet<ATCManualHoldingPoint> registeredPoints =
        new HashSet<ATCManualHoldingPoint>();

    public static IReadOnlyCollection<ATCManualHoldingPoint> RegisteredPoints =>
        registeredPoints;

    public static int RegistryVersion { get; private set; }

    [Header("Traffic Rules")]
    [Tooltip("Allows aircraft travelling in the same direction to share this controlled section.")]
    [SerializeField] private bool allowSameDirectionTraffic = true;

    [Min(0.25f)]
    [Tooltip("Distance around this point used to determine when an aircraft has entered the controlled area, in grid cells.")]
    [SerializeField] private float controlRadiusCells = 1.5f;

    public bool AllowSameDirectionTraffic => allowSameDirectionTraffic;
    public float ControlRadiusCells => controlRadiusCells;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry()
    {
        registeredPoints.Clear();
        RegistryVersion = 0;
    }

    private void OnEnable()
    {
        if (registeredPoints.Add(this))
            RegistryVersion++;
    }

    private void OnDisable()
    {
        if (registeredPoints.Remove(this))
            RegistryVersion++;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = allowSameDirectionTraffic
            ? new Color(1f, 0.75f, 0.1f, 1f)
            : Color.red;

        Gizmos.DrawWireSphere(transform.position, 0.6f);
        Gizmos.DrawLine(
            transform.position,
            transform.position + transform.forward * 1.25f);
    }
}