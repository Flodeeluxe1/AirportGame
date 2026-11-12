using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "FloorFinishCatalogue",
    menuName = "Airport/Floor Finishes/Catalogue"
)]
public class FloorFinishCatalogue : ScriptableObject
{
    [SerializeField] private FloorFinishDefinition[] finishes;

    [Header("Shared Chunk Rendering")]
    [SerializeField] private Material sharedRenderMaterial;

    private Dictionary<string, int> indicesById;

    public IReadOnlyList<FloorFinishDefinition> Finishes => finishes;
    public Material SharedRenderMaterial => sharedRenderMaterial;

    public int Count => finishes != null ? finishes.Length : 0;

    public FloorFinishDefinition GetAt(int index)
    {
        if (finishes == null || index < 0 || index >= finishes.Length)
            return null;

        return finishes[index];
    }

    public int GetIndex(FloorFinishDefinition finish)
    {
        if (finish == null || string.IsNullOrWhiteSpace(finish.id))
            return -1;

        BuildLookupIfNeeded();

        return indicesById.TryGetValue(finish.id, out int index)
            ? index
            : -1;
    }

    public FloorFinishDefinition FindById(string id)
    {
        int index = GetIndexById(id);
        return GetAt(index);
    }

    public int GetIndexById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return -1;

        BuildLookupIfNeeded();

        return indicesById.TryGetValue(id, out int index)
            ? index
            : -1;
    }

    private void BuildLookupIfNeeded()
    {
        if (indicesById != null)
            return;

        indicesById = new Dictionary<string, int>();

        if (finishes == null)
            return;

        for (int i = 0; i < finishes.Length; i++)
        {
            FloorFinishDefinition finish = finishes[i];

            if (finish == null || string.IsNullOrWhiteSpace(finish.id))
                continue;

            if (indicesById.ContainsKey(finish.id))
            {
                Debug.LogError(
                    $"FloorFinishCatalogue: Duplicate finish ID '{finish.id}'.",
                    this);
                continue;
            }

            indicesById.Add(finish.id, i);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        indicesById = null;
    }
#endif
}
