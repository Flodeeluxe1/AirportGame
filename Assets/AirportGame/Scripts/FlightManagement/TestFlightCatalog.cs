
using System;
using System.Collections.Generic;
using UnityEngine;

public class TestFlightCatalog : MonoBehaviour
{
    [Header("Test Flight Generation")]
    [SerializeField] private string testAirlineName = "Test Airline";

    [Tooltip("Keeping this seed fixed makes test-flight data " +
             "repeatable across Play Mode sessions.")]
    [SerializeField] private int randomSeed = 271828;

    private readonly List<FlightDefinition> flights =
        new List<FlightDefinition>();

    public IReadOnlyList<FlightDefinition> Flights =>
        flights;

    public int FlightCount => flights.Count;

    private void Awake()
    {
        GenerateTestFlights();
    }

    public void GenerateTestFlights()
    {
        flights.Clear();

        System.Random random =
            new System.Random(randomSeed);

        AddFlights(
            random,
            FlightTimeWindow.Night,
            2
        );

        AddFlights(
            random,
            FlightTimeWindow.Morning,
            6
        );

        AddFlights(
            random,
            FlightTimeWindow.Afternoon,
            2
        );

        AddFlights(
            random,
            FlightTimeWindow.Evening,
            2
        );

        AddPassengerTestFlights();
    }

    private void AddFlights(
        System.Random random,
        FlightTimeWindow window,
        int count)
    {
        Color stripColor =
            GetStripColor(window);

        for (int i = 0; i < count; i++)
        {
            string flightNumber =
                $"AB{101 + flights.Count}";

            // Generate turnaround times in five-minute
            // increments, between 35 and 90 minutes.
            int turnaroundMinutes =
                random.Next(7, 19) * 5;

            AircraftGateSize gateSize =
                (AircraftGateSize)random.Next(0, 4);

            FlightDefinition flight =
                new FlightDefinition(
                    flightNumber,
                    testAirlineName,
                    turnaroundMinutes,
                    gateSize,
                    window,
                    stripColor
                );

            flights.Add(flight);
        }
    }

    private void AddPassengerTestFlights()
    {
        Color morningStripColor =
            GetStripColor(FlightTimeWindow.Morning);

        flights.Add(
            new FlightDefinition(
                "PAX201",
                testAirlineName,
                45,
                AircraftGateSize.Grass,
                FlightTimeWindow.Morning,
                morningStripColor,
                FlightOperationType.PassengerOnly
            )
        );

        flights.Add(
            new FlightDefinition(
                "PAX202",
                testAirlineName,
                50,
                AircraftGateSize.Grass,
                FlightTimeWindow.Morning,
                morningStripColor,
                FlightOperationType.PassengerOnly
            )
        );

        flights.Add(
            new FlightDefinition(
                "PAX203",
                testAirlineName,
                55,
                AircraftGateSize.Grass,
                FlightTimeWindow.Morning,
                morningStripColor,
                FlightOperationType.PassengerOnly
            )
        );
    }

    private Color GetStripColor(
        FlightTimeWindow window)
    {
        switch (window)
        {
            case FlightTimeWindow.Night:
                return new Color32(140, 195, 235, 255);

            case FlightTimeWindow.Morning:
                return new Color32(155, 210, 145, 255);

            case FlightTimeWindow.Afternoon:
                return new Color32(245, 190, 105, 255);

            case FlightTimeWindow.Evening:
                return new Color32(190, 165, 225, 255);
        }

        return Color.white;
    }

    [ContextMenu("Debug Test Flights")]
    private void DebugTestFlights()
    {
        if (flights.Count == 0)
            GenerateTestFlights();

        Debug.Log(
            $"========== TEST FLIGHT CATALOG ({flights.Count}) ==========",
            this
        );

        foreach (FlightDefinition flight in flights)
        {
            FlightDefinition.GetWindowBounds(
                flight.TimeWindow,
                out int startMinutes,
                out int endMinutes
            );

            Debug.Log(
                $"Flight: {flight.FlightNumber} | " +
                $"Type: {flight.OperationLabel} | " +
                $"Airline: {flight.AirlineName} | " +
                $"Window: {flight.TimeWindow} " +
                $"({FormatMinutes(startMinutes)}–{FormatMinutes(endMinutes)}) | " +
                $"Turnaround: {flight.TurnaroundMinutes} min | " +
                $"Required Gate: {flight.RequiredGateSize}",
                this
            );
        }

        Debug.Log(
            "========== END TEST FLIGHT CATALOG ==========",
            this
        );
    }

    private string FormatMinutes(int minutes)
    {
        int hours = minutes / 60;
        int remainingMinutes = minutes % 60;

        return $"{hours:00}:{remainingMinutes:00}";
    }
}
