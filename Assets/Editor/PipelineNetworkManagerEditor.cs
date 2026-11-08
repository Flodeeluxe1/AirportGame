using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PipelineNetworkManager))]
public sealed class PipelineNetworkManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        PipelineNetworkManager manager = (PipelineNetworkManager)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Live Fuel Inventory", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Enter Play Mode to view the current pipeline fuel reserves.",
                MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("Connected Networks", manager.NetworkCount.ToString());

        float totalFuel = 0f;
        float totalCapacity = 0f;

        for (int i = 0; i < manager.Networks.Count; i++)
        {
            PipelineNetworkManager.PipelineNetwork network = manager.Networks[i];
            if (network == null)
                continue;

            totalFuel += network.CurrentFuelLitres;
            totalCapacity += network.CapacityLitres;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"Network {i + 1}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Stored Fuel",
                $"{network.CurrentFuelLitres:N0} / {network.CapacityLitres:N0} L");
            EditorGUILayout.LabelField(
                "Available Capacity",
                $"{network.RemainingCapacityLitres:N0} L");
            EditorGUILayout.LabelField("Pipe Cells", network.PipeCells.Count.ToString());
            EditorGUILayout.LabelField("Connectors", network.Connectors.Count.ToString());
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(
            "Total Network Fuel",
            $"{totalFuel:N0} / {totalCapacity:N0} L",
            EditorStyles.boldLabel);

        if (Application.isPlaying)
            Repaint();
    }
}
