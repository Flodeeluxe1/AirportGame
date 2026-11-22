#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CreateNorthstarCargoAirlineAsset
{
    private const string RootFolder = "Assets/AirportGame/ScriptableObjects";
    private const string AirlineFolder = RootFolder + "/Airlines";
    private const string AssetPath = AirlineFolder + "/NorthstarCargo.asset";

    [MenuItem("Airport/Airline Management/Create Northstar Cargo Airline Asset")]
    public static void CreateAsset()
    {
        AirlineDefinition existing =
            AssetDatabase.LoadAssetAtPath<AirlineDefinition>(AssetPath);

        if (existing != null)
        {
            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
            return;
        }

        EnsureFolder("Assets", "AirportGame");
        EnsureFolder("Assets/AirportGame", "ScriptableObjects");
        EnsureFolder(RootFolder, "Airlines");

        AirlineDefinition airline =
            ScriptableObject.CreateInstance<AirlineDefinition>();

        SerializedObject serializedAirline = new SerializedObject(airline);
        serializedAirline.FindProperty("airlineId").stringValue = "northstar_cargo";
        serializedAirline.FindProperty("displayName").stringValue = "Northstar Cargo";
        serializedAirline.FindProperty("operationType").enumValueIndex =
            (int)AirlineOperationType.Cargo;

        serializedAirline.FindProperty("idealLandingAndParkingFee").intValue = 250;
        serializedAirline.FindProperty("maximumAcceptableLandingAndParkingFee").intValue = 750;
        serializedAirline.FindProperty("preferredGateSize").enumValueIndex =
            (int)AircraftGateSize.Grass;
        serializedAirline.FindProperty("desiredSuitableGateCount").intValue = 1;
        serializedAirline.FindProperty("minimumRunwayLengthMeters").floatValue = 100f;
        serializedAirline.FindProperty("desiredCargoStorageCapacity").floatValue = 100f;
        serializedAirline.FindProperty("desiredCargoHandlingThroughputPerHour").floatValue = 10f;
        serializedAirline.FindProperty("minimumPreferredAirportReputation").floatValue = 50f;
        serializedAirline.FindProperty("priceSensitivity").floatValue = 1.25f;

        serializedAirline.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.CreateAsset(airline, AssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        AttachAirlineToOpenScene(airline);

        Selection.activeObject = airline;
        EditorGUIUtility.PingObject(airline);
        Debug.Log("Created Northstar Cargo airline definition at " + AssetPath);
    }

    private static void AttachAirlineToOpenScene(AirlineDefinition airline)
    {
        AirlineAirportSnapshotCollector collector =
            Object.FindAnyObjectByType<AirlineAirportSnapshotCollector>();

        if (collector == null)
        {
            GameObject systemObject = new GameObject("Airline Management System");
            Undo.RegisterCreatedObjectUndo(systemObject, "Create Airline Management System");
            collector = systemObject.AddComponent<AirlineAirportSnapshotCollector>();
        }

        SerializedObject serializedCollector = new SerializedObject(collector);
        SerializedProperty airlineList =
            serializedCollector.FindProperty("airlines");

        bool alreadyAssigned = false;
        for (int i = 0; i < airlineList.arraySize; i++)
        {
            if (airlineList.GetArrayElementAtIndex(i).objectReferenceValue == airline)
            {
                alreadyAssigned = true;
                break;
            }
        }

        if (!alreadyAssigned)
        {
            int index = airlineList.arraySize;
            airlineList.InsertArrayElementAtIndex(index);
            airlineList.GetArrayElementAtIndex(index).objectReferenceValue = airline;
            serializedCollector.ApplyModifiedProperties();
        }

        EditorSceneManager.MarkSceneDirty(collector.gameObject.scene);
    }

    private static void EnsureFolder(string parent, string folderName)
    {
        string path = parent + "/" + folderName;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, folderName);
    }
}
#endif
