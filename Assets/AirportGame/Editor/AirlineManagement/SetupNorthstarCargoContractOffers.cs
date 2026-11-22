#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SetupNorthstarCargoContractOffers
{
    private const string RootFolder = "Assets/AirportGame/ScriptableObjects";
    private const string ContractFolder = RootFolder + "/AirlineContracts";
    private const string AirlinePath = RootFolder + "/Airlines/NorthstarCargo.asset";
    private const string TemplatePath =
        ContractFolder + "/NorthstarCargo_Standard7Day.asset";

    [MenuItem("Airport/Airline Management/Setup Northstar Cargo Contract Offers")]
    public static void Setup()
    {
        AirlineDefinition airline =
            AssetDatabase.LoadAssetAtPath<AirlineDefinition>(AirlinePath);

        if (airline == null)
        {
            Debug.LogError(
                "Northstar Cargo airline asset was not found. Create it first using " +
                "Airport > Airline Management > Create Northstar Cargo Airline Asset.");
            return;
        }

        EnsureFolder("Assets", "AirportGame");
        EnsureFolder("Assets/AirportGame", "ScriptableObjects");
        EnsureFolder(RootFolder, "AirlineContracts");

        AirlineContractTemplate template =
            AssetDatabase.LoadAssetAtPath<AirlineContractTemplate>(TemplatePath);

        if (template == null)
        {
            template = ScriptableObject.CreateInstance<AirlineContractTemplate>();
            AssetDatabase.CreateAsset(template, TemplatePath);
        }

        ConfigureTemplate(template);
        AssignTemplateToAirline(airline, template);

        AirlineAirportSnapshotCollector collector =
            Object.FindAnyObjectByType<AirlineAirportSnapshotCollector>();

        if (collector == null)
        {
            Debug.LogError(
                "No AirlineAirportSnapshotCollector was found in the open scene. " +
                "Run the Northstar Cargo airline setup first.");
            return;
        }

        AirlineContractOfferManager manager =
            collector.GetComponent<AirlineContractOfferManager>();

        if (manager == null)
        {
            manager = Undo.AddComponent<AirlineContractOfferManager>(
                collector.gameObject);
        }

        AirlineContractFlightIntegration integration =
            collector.GetComponent<AirlineContractFlightIntegration>();

        if (integration == null)
        {
            integration = Undo.AddComponent<AirlineContractFlightIntegration>(
                collector.gameObject);
        }

        AirlineContractPerformanceTracker performanceTracker =
            collector.GetComponent<AirlineContractPerformanceTracker>();

        if (performanceTracker == null)
        {
            performanceTracker = Undo.AddComponent<AirlineContractPerformanceTracker>(
                collector.gameObject);
        }

        // Keep the player-facing Airline Management button with the other
        // management buttons on the shared airport UI GameObject.
        FinanceManagementUIController financeUI =
            Object.FindAnyObjectByType<FinanceManagementUIController>();

        if (financeUI == null)
        {
            Debug.LogError(
                "FinanceManagementUIController was not found. The Airline Management " +
                "backend is configured, but its player-facing button could not be added.");
            return;
        }

        AirlineManagementUIController airlineUI =
            financeUI.GetComponent<AirlineManagementUIController>();

        if (airlineUI == null)
        {
            airlineUI = Undo.AddComponent<AirlineManagementUIController>(
                financeUI.gameObject);
        }

        EditorSceneManager.MarkSceneDirty(collector.gameObject.scene);
        EditorSceneManager.MarkSceneDirty(financeUI.gameObject.scene);
        AssetDatabase.SaveAssets();

        Selection.activeObject = template;
        EditorGUIUtility.PingObject(template);

        Debug.Log(
            "Northstar Cargo contract template assigned. AirlineContractOfferManager " +
            "and AirlineContractFlightIntegration are configured, and the Airline Management UI " +
            "has been added beside the existing Finance button.");
    }

    private static void ConfigureTemplate(AirlineContractTemplate template)
    {
        SerializedObject serialized = new SerializedObject(template);
        serialized.FindProperty("templateId").stringValue =
            "northstar_cargo_standard_7_day";
        serialized.FindProperty("displayName").stringValue =
            "Northstar Cargo - Standard 7 Day Contract";
        serialized.FindProperty("durationDays").intValue = 7;
        serialized.FindProperty("turnaroundMinutes").intValue = 60;
        serialized.FindProperty("minimumGateSize").enumValueIndex =
            (int)AircraftGateSize.Grass;
        serialized.FindProperty("minimumRunwayLengthMeters").floatValue = 70f;
        serialized.FindProperty("minimumCargoStorageCapacity").floatValue = 100f;
        serialized.FindProperty("minimumCargoHandlingThroughputPerHour").floatValue = 10f;
        serialized.FindProperty("paymentPerCompletedFlight").intValue = 500;
        serialized.FindProperty("missedFlightPenalty").intValue = 250;
        serialized.FindProperty("completionBonus").intValue = 1500;
        serialized.FindProperty("minimumAirlineInterest").floatValue = 20f;

        SerializedProperty windows = serialized.FindProperty("flightWindows");
        windows.arraySize = 2;

        SerializedProperty morning = windows.GetArrayElementAtIndex(0);
        morning.FindPropertyRelative("window").enumValueIndex =
            (int)AirlineOperatingWindow.Morning;
        morning.FindPropertyRelative("flightsPerDay").intValue = 1;

        SerializedProperty afternoon = windows.GetArrayElementAtIndex(1);
        afternoon.FindPropertyRelative("window").enumValueIndex =
            (int)AirlineOperatingWindow.Afternoon;
        afternoon.FindPropertyRelative("flightsPerDay").intValue = 1;

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(template);
    }

    private static void AssignTemplateToAirline(
        AirlineDefinition airline,
        AirlineContractTemplate template)
    {
        SerializedObject serialized = new SerializedObject(airline);
        SerializedProperty templates = serialized.FindProperty("contractTemplates");

        for (int i = 0; i < templates.arraySize; i++)
        {
            if (templates.GetArrayElementAtIndex(i).objectReferenceValue == template)
                return;
        }

        int index = templates.arraySize;
        templates.InsertArrayElementAtIndex(index);
        templates.GetArrayElementAtIndex(index).objectReferenceValue = template;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(airline);
    }

    private static void EnsureFolder(string parent, string folderName)
    {
        string path = parent + "/" + folderName;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, folderName);
    }
}
#endif
