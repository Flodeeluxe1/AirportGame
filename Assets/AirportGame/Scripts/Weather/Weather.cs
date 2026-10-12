using UnityEngine;

public class Weather : MonoBehaviour
{
    [Header("Wind")]
    [Range(0f, 360f)]
    public float windFromDirectionDegrees = 270f;

    [Min(0f)]
    public float windSpeedKnots = 10f;

    public Vector3 WindFromDirection
    {
        get
        {
            return (
                Quaternion.Euler(
                    0f,
                    windFromDirectionDegrees,
                    0f
                ) * Vector3.forward
            ).normalized;
        }
    }
}