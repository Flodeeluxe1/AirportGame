#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public static class InteractionDefinitionEditor
{
    private const string MenuPath =
        "Airport/Utility AI/Create Interaction Definition";

    [MenuItem(MenuPath)]
    private static void CreateInteractionDefinition()
    {
        string selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
        string folderPath = "Assets";

        if (!string.IsNullOrEmpty(selectedPath))
        {
            folderPath = AssetDatabase.IsValidFolder(selectedPath)
                ? selectedPath
                : Path.GetDirectoryName(selectedPath)?.Replace("\\", "/");
        }

        if (string.IsNullOrEmpty(folderPath) ||
            !AssetDatabase.IsValidFolder(folderPath))
        {
            Debug.LogError(
                "InteractionDefinitionEditor: Select a project folder, prefab, " +
                "or Buildable Definition before creating an Interaction Definition.");
            return;
        }

        InteractionDefinition definition =
            ScriptableObject.CreateInstance<InteractionDefinition>();

        string assetPath = AssetDatabase.GenerateUniqueAssetPath(
            folderPath + "/InteractionDefinition.asset");

        AssetDatabase.CreateAsset(definition, assetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = definition;
        EditorGUIUtility.PingObject(definition);
    }
}
#endif
