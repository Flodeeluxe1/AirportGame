
using UnityEngine;

public enum FlightTimeWindow
{
    Night,
    Morning,
    Afternoon,
    Evening
}

[System.Serializable]
public class FlightDefinition
{
    public string FlightNumber { get; }
    public string AirlineName { get; }
    public int TurnaroundMinutes { get; }
    public AircraftGateSize RequiredGateSize { get; }
    public FlightTimeWindow TimeWindow { get; }
    public Color StripColor { get; }

    public FlightDefinition(
        string flightNumber,
        string airlineName,
        int turnaroundMinutes,
        AircraftGateSize requiredGateSize,
        FlightTimeWindow timeWindow,
        Color stripColor)
    {
        FlightNumber = flightNumber;
        AirlineName = airlineName;
        TurnaroundMinutes = turnaroundMinutes;
        RequiredGateSize = requiredGateSize;
        TimeWindow = timeWindow;
        StripColor = stripColor;
    }

    public static void GetWindowBounds(
        FlightTimeWindow window,
        out int startMinutes,
        out int endMinutes)
    {
        switch (window)
        {
            case FlightTimeWindow.Night:
                startMinutes = 0;
                endMinutes = 5 * 60;
                break;

            case FlightTimeWindow.Morning:
                startMinutes = 5 * 60;
                endMinutes = 12 * 60;
                break;

            case FlightTimeWindow.Afternoon:
                startMinutes = 12 * 60;
                endMinutes = 18 * 60;
                break;

            case FlightTimeWindow.Evening:
                startMinutes = 18 * 60;
                endMinutes = 24 * 60;
                break;

            default:
                startMinutes = 0;
                endMinutes = 0;
                break;
        }
    }

    public bool AllowsStartTime(int startMinuteOfDay)
    {
        GetWindowBounds(
            TimeWindow,
            out int startMinutes,
            out int endMinutes);

        return startMinuteOfDay >= startMinutes &&
               startMinuteOfDay < endMinutes;
    }
}