using UnityEditor;
using UnityEngine;

/// <summary>
/// Shows the live aggregate waste values for a WasteDisposal in the Inspector.
/// </summary>
[CustomEditor(typeof(WasteDisposal))]
public class WasteDisposalEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        WasteDisposal disposal = (WasteDisposal)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Live Waste Summary", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.IntField("Detected Dumpsters", disposal.DumpsterCount);
            EditorGUILayout.FloatField("Current Waste", disposal.CurrentWaste);
            EditorGUILayout.FloatField("Maximum Waste Capacity", disposal.MaximumWasteCapacity);
            EditorGUILayout.FloatField("Remaining Capacity", disposal.RemainingWasteCapacity);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Individual Dumpsters", EditorStyles.boldLabel);

        var dumpsters = disposal.Dumpsters;
        if (dumpsters.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "No WasteDumpster components were found among this object's children.",
                MessageType.Warning);
            return;
        }

        using (new EditorGUI.DisabledScope(true))
        {
            foreach (WasteDumpster dumpster in dumpsters)
            {
                if (dumpster == null)
                    continue;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(dumpster.name, EditorStyles.boldLabel);
                EditorGUILayout.FloatField("Current Waste", dumpster.CurrentWaste);
                EditorGUILayout.FloatField("Maximum Capacity", dumpster.MaximumCapacity);
                EditorGUILayout.EndVertical();
            }
        }

        if (dumpsters.Count >= disposal.MaximumDumpsterCount)
        {
            EditorGUILayout.HelpBox(
                "The configured dumpster limit has been reached. Additional child dumpsters will not be included.",
                MessageType.Info);
        }

        if (GUI.changed)
            Repaint();
    }
}
