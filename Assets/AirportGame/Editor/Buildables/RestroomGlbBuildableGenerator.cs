using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RestroomGlbBuildableGenerator
{
    private const string Root =
        "Assets/AirportGame/Prefabs/Buildables/Airport/Restrooms & Hygiene/";

    private struct Item
    {
        public string folder;
        public string modelFile;
        public string prefabFile;
        public string definitionFile;
        public string id;
        public string displayName;
        public int cost;

        public Item(string folder, string modelFile, string prefabFile,
            string definitionFile, string id, string displayName, int cost)
        {
            this.folder = folder;
            this.modelFile = modelFile;
            this.prefabFile = prefabFile;
            this.definitionFile = definitionFile;
            this.id = id;
            this.displayName = displayName;
            this.cost = cost;
        }
    }

    private static readonly Item[] Items =
    {
        new Item("Toilet", "Toilet-a.glb", "Toilet.prefab",
            "BD_toilet.asset", "airport.restrooms.toilet", "Toilet", 120),
        new Item("Toilet Stall", "Toilet Stall.glb", "Toilet Stall.prefab",
            "BD_toilet_stall.asset", "airport.restrooms.toilet_stall",
            "Toilet Stall", 180),
        new Item("Toilet Exit Door", "Toilet Exit Door.glb",
            "Toilet Exit Door.prefab", "BD_toilet_exit_door.asset",
            "airport.restrooms.toilet_exit_door", "Toilet Exit Door", 100),
        new Item("Plastic Cabin", "plasticCabin.glb", "Plastic Cabin.prefab",
            "BD_plastic_cabin.asset", "airport.restrooms.plastic_cabin",
            "Plastic Cabin", 250)
    };

    [MenuItem("Airport/Buildables/Create Restroom GLB Prefabs")]
    public static void CreateRestroomBuildables()
    {
        int createdPrefabs = 0;
        int createdDefinitions = 0;

        foreach (Item item in Items)
        {
            string folderPath = Root + item.folder + "/";
            string modelPath = folderPath + item.modelFile;
            string prefabPath = folderPath + item.prefabFile;
            string definitionPath = folderPath + item.definitionFile;

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogError("Restroom buildable generator: Could not load GLB model at " + modelPath);
                continue;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                GameObject root = new GameObject(item.displayName);
                GameObject modelChild = PrefabUtility.InstantiatePrefab(model) as GameObject;

                if (modelChild == null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                    Debug.LogError("Restroom buildable generator: Could not instantiate " + modelPath);
                    continue;
                }

                modelChild.name = item.displayName + " Model";
                modelChild.transform.SetParent(root.transform, false);
                modelChild.transform.localPosition = Vector3.zero;
                modelChild.transform.localRotation = Quaternion.identity;
                modelChild.transform.localScale = Vector3.one;

                prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                UnityEngine.Object.DestroyImmediate(root);

                if (prefab == null)
                {
                    Debug.LogError("Restroom buildable generator: Failed to save prefab " + prefabPath);
                    continue;
                }

                createdPrefabs++;
            }

            BuildableDefinition definition =
                AssetDatabase.LoadAssetAtPath<BuildableDefinition>(definitionPath);

            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<BuildableDefinition>();
                definition.id = item.id;
                definition.displayName = item.displayName;
                definition.prefab = prefab;
                definition.placementType = PlacementType.Cell;
                definition.requiredSurface = BuildSurface.Foundation;
                definition.width = 1;
                definition.depth = 1;
                definition.edgeLength = 1;
                definition.canRotate = true;
                definition.buildCost = item.cost;
                definition.blocksPathfinding = false;
                definition.menuCategory = "Restrooms & Hygiene";
                definition.category = BuildCategory.Airport;
                definition.roomFurnitureRole = RoomFurnitureRole.None;

                AssetDatabase.CreateAsset(definition, definitionPath);
                createdDefinitions++;
            }
            else if (definition.prefab == null)
            {
                definition.prefab = prefab;
                EditorUtility.SetDirty(definition);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        BuildableCatalogueBuilder.RebuildCatalogue();

        Debug.Log("Restroom GLB generation complete. Created " +
            createdPrefabs + " prefab wrappers and " + createdDefinitions +
            " Buildable Definitions. Check the initial 1x1 footprints in the Inspector.");
    }
}
