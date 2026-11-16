#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Read-only Inspector and Scene view diagnostics for the hobby pilot Utility AI.
/// </summary>
[CustomEditor(typeof(PilotInteractionController))]
public class PilotAIDebuggerEditor : Editor
{
    private static readonly UtilityNeeds.NeedType[] Needs =
    {
        UtilityNeeds.NeedType.Thirst,
        UtilityNeeds.NeedType.Hunger,
        UtilityNeeds.NeedType.Bladder,
        UtilityNeeds.NeedType.Energy,
        UtilityNeeds.NeedType.Social,
        UtilityNeeds.NeedType.Entertainment,
        UtilityNeeds.NeedType.Comfort,
        UtilityNeeds.NeedType.Boredom
    };

    private SerializedProperty currentStatus;
    private SerializedProperty currentInteractionName;

    private void OnEnable()
    {
        currentStatus = serializedObject.FindProperty("currentStatus");
        currentInteractionName = serializedObject.FindProperty("currentInteractionName");
        EditorApplication.update += Repaint;
    }

    private void OnDisable()
    {
        EditorApplication.update -= Repaint;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "currentStatus", "currentInteractionName");
        serializedObject.ApplyModifiedProperties();

        PilotInteractionController controller =
            (PilotInteractionController)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("AI Runtime Debug", EditorStyles.boldLabel);
        DrawReadOnlyField("AI Status",
            currentStatus != null ? currentStatus.stringValue : controller.CurrentStatus);
        DrawReadOnlyField("Current Activity",
            currentInteractionName != null ? currentInteractionName.stringValue : controller.CurrentInteractionName);

        PedestrianMovementController movement =
            controller.GetComponent<PedestrianMovementController>();
        if (movement != null)
        {
            DrawReadOnlyField("Movement", movement.MovementStatus);
            DrawReadOnlyField("Moving", movement.IsMoving ? "Yes" : "No");
            DrawReadOnlyField("Path Progress",
                movement.CurrentWaypointIndex + " / " + movement.WaypointCount);
            if (movement.HasPath)
                DrawReadOnlyField("Destination", movement.Destination.ToString("F1"));
        }

        UtilityNeeds needs = controller.GetComponent<UtilityNeeds>();
        if (needs != null)
            DrawNeeds(needs);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Interaction Shortlist",
            EditorStyles.boldLabel);

        var candidates = controller.DebugCandidates;
        if (candidates == null || candidates.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "No decision shortlist yet. The pilot refreshes its decision periodically when free to choose an activity.",
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.LabelField("Top candidates from the latest decision",
                EditorStyles.miniLabel);
            foreach (var candidate in candidates)
            {
                if (candidate == null || candidate.Definition == null)
                    continue;

                string title = candidate.Definition.DisplayName +
                    (candidate.Selected ? "  [SELECTED]" : string.Empty);
                EditorGUILayout.LabelField(title,
                    EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Utility: " + candidate.Utility.ToString("F2") +
                    "   Final score: " + candidate.Score.ToString("F2"));
                EditorGUILayout.LabelField(
                    "Travel: " + candidate.TravelTime.ToString("F1") + "s" +
                    "   Queue: " + candidate.WaitingTime.ToString("F1") + "s" +
                    "   Service: " + candidate.Definition.DurationSeconds.ToString("F1") + "s",
                    EditorStyles.miniLabel);
                if (candidate.Provider != null)
                    EditorGUILayout.LabelField("Provider: " +
                        candidate.Provider.name, EditorStyles.miniLabel);
            }
        }

        EditorGUILayout.HelpBox(
            "Diagnostics are read-only. Candidate scores show the latest decision cycle; the weighted selection can choose any member of the shortlist.",
            MessageType.None);
    }

    private static void DrawNeeds(UtilityNeeds needs)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Needs", EditorStyles.boldLabel);

        foreach (UtilityNeeds.NeedType need in Needs)
        {
            float value = needs.GetValue(need);
            float urgency = needs.GetUrgency(need);
            float weight = needs.GetUtilityWeight(need);

            Rect rect = EditorGUILayout.GetControlRect(false, 20f);
            Rect barRect = new Rect(rect.x, rect.y + 2f, rect.width, 16f);
            EditorGUI.ProgressBar(barRect, value / 100f,
                need + "  " + value.ToString("F0") +
                "   | Urgency " + urgency.ToString("F0") +
                "   | Weight " + weight.ToString("F2"));
        }
    }

    private static void DrawReadOnlyField(string label, string value)
    {
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.TextField(label, value ?? string.Empty);
    }

    private void OnSceneGUI()
    {
        PilotInteractionController controller =
            (PilotInteractionController)target;
        Vector3 origin = controller.transform.position;

        PedestrianMovementController movement =
            controller.GetComponent<PedestrianMovementController>();
        if (movement != null && movement.MovementPath != null &&
            movement.MovementPath.Count > 0)
        {
            Handles.color = Color.cyan;
            Vector3 previous = origin;
            foreach (Vector3 waypoint in movement.MovementPath)
            {
                Handles.DrawLine(previous, waypoint);
                Handles.SphereHandleCap(0, waypoint, Quaternion.identity,
                    0.12f, EventType.Repaint);
                previous = waypoint;
            }
            Handles.color = Color.white;
            Handles.DrawWireCube(movement.Destination, Vector3.one * 0.25f);
        }

        var candidates = controller.DebugCandidates;
        if (candidates == null)
            return;

        foreach (var candidate in candidates)
        {
            if (candidate == null || candidate.Provider == null)
                continue;

            Handles.color = candidate.Selected ? Color.green : Color.gray;
            Vector3 targetPosition = candidate.Provider.InteractionPosition;
            Handles.DrawDottedLine(origin, targetPosition, 4f);
            Handles.Label(targetPosition + Vector3.up * 0.5f,
                candidate.Definition != null
                    ? candidate.Definition.DisplayName + " (" +
                      candidate.Score.ToString("F2") + ")"
                    : "Interaction");
        }
    }
}

[CustomEditor(typeof(UtilityNeeds))]
public class UtilityNeedsDebuggerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        UtilityNeeds needs = (UtilityNeeds)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Live Need Values", EditorStyles.boldLabel);

        foreach (UtilityNeeds.NeedType need in
                 System.Enum.GetValues(typeof(UtilityNeeds.NeedType)))
        {
            float value = needs.GetValue(need);
            float urgency = needs.GetUrgency(need);
            Rect rect = EditorGUILayout.GetControlRect(false, 20f);
            EditorGUI.ProgressBar(rect, value / 100f,
                need + "  " + value.ToString("F0") +
                "   | Urgency " + urgency.ToString("F0"));
        }
    }
}
#endif
