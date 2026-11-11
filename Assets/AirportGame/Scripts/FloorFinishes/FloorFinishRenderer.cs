using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class FloorFinishRenderer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GridManager gridManager;
    [SerializeField] private GridSurface gridSurface;
    [SerializeField] private FloorFinishCatalogue catalogue;

    [Header("Chunk Rendering")]
    [Min(8)]
    [SerializeField] private int chunkSize = 32;

    [Tooltip("Small vertical offset above the foundation Tilemap to prevent z-fighting.")]
    [SerializeField] private float surfaceOffset = 0.015f;

    private int[,] finishIndices;
    private readonly Dictionary<Vector2Int, ChunkVisual> chunks =
        new Dictionary<Vector2Int, ChunkVisual>();

    private int GridWidth => gridManager != null ? gridManager.Width : 0;
    private int GridHeight => gridManager != null ? gridManager.Height : 0;

    private sealed class ChunkVisual
    {
        public GameObject gameObject;
        public Mesh mesh;
    }

    private void Awake()
    {
        ResolveReferences();
        InitializeCellData();
    }

    private void OnDestroy()
    {
        foreach (ChunkVisual chunk in chunks.Values)
        {
            if (chunk == null)
                continue;

            if (chunk.mesh != null)
                Destroy(chunk.mesh);

            if (chunk.gameObject != null)
                Destroy(chunk.gameObject);
        }

        chunks.Clear();
    }

    private void ResolveReferences()
    {
        if (gridManager == null)
            gridManager = FindFirstObjectByType<GridManager>();

        if (gridSurface == null && gridManager != null)
            gridSurface = gridManager.GetComponent<GridSurface>();
    }

    private void InitializeCellData()
    {
        if (gridManager == null)
        {
            Debug.LogError("FloorFinishRenderer: GridManager is missing.", this);
            return;
        }

        finishIndices = new int[gridManager.Width, gridManager.Height];

        for (int x = 0; x < gridManager.Width; x++)
        {
            for (int z = 0; z < gridManager.Height; z++)
                finishIndices[x, z] = -1;
        }
    }

    public bool SetFinish(Vector2Int cell, FloorFinishDefinition finish)
    {
        if (!IsValidCell(cell) || gridSurface == null || catalogue == null)
            return false;

        if (gridSurface.GetSurface(cell) != BuildSurface.Foundation)
            return false;

        int finishIndex = catalogue.GetIndex(finish);
        if (finishIndex < 0 || finish.material == null)
            return false;

        if (finishIndices[cell.x, cell.y] == finishIndex)
            return true;

        finishIndices[cell.x, cell.y] = finishIndex;
        RebuildChunk(GetChunkCoordinate(cell));
        return true;
    }

    public bool ClearFinish(Vector2Int cell)
    {
        if (!IsValidCell(cell))
            return false;

        if (finishIndices[cell.x, cell.y] < 0)
            return true;

        finishIndices[cell.x, cell.y] = -1;
        RebuildChunk(GetChunkCoordinate(cell));
        return true;
    }

    public void ClearAllFinishes()
    {
        if (finishIndices == null)
            return;

        for (int x = 0; x < GridWidth; x++)
        {
            for (int z = 0; z < GridHeight; z++)
                finishIndices[x, z] = -1;
        }

        foreach (ChunkVisual chunk in chunks.Values)
        {
            if (chunk == null)
                continue;

            if (chunk.mesh != null)
                Destroy(chunk.mesh);

            if (chunk.gameObject != null)
                Destroy(chunk.gameObject);
        }

        chunks.Clear();
    }

    public string GetFinishId(Vector2Int cell)
    {
        if (!IsValidCell(cell) || catalogue == null)
            return string.Empty;

        FloorFinishDefinition finish =
            catalogue.GetAt(finishIndices[cell.x, cell.y]);

        return finish != null ? finish.id : string.Empty;
    }

    public bool RestoreFinish(Vector2Int cell, string finishId)
    {
        if (!IsValidCell(cell) || gridSurface == null || catalogue == null)
            return false;

        if (gridSurface.GetSurface(cell) != BuildSurface.Foundation)
            return false;

        FloorFinishDefinition finish = catalogue.FindById(finishId);
        if (finish == null || finish.material == null)
            return false;

        int finishIndex = catalogue.GetIndex(finish);
        if (finishIndex < 0)
            return false;

        finishIndices[cell.x, cell.y] = finishIndex;
        RebuildChunk(GetChunkCoordinate(cell));
        return true;
    }

    public List<FloorFinishCellSaveData> GetSaveData()
    {
        List<FloorFinishCellSaveData> saved =
            new List<FloorFinishCellSaveData>();

        if (finishIndices == null || catalogue == null)
            return saved;

        for (int x = 0; x < GridWidth; x++)
        {
            for (int z = 0; z < GridHeight; z++)
            {
                int index = finishIndices[x, z];
                FloorFinishDefinition finish = catalogue.GetAt(index);

                if (finish == null || string.IsNullOrWhiteSpace(finish.id))
                    continue;

                saved.Add(new FloorFinishCellSaveData
                {
                    x = x,
                    z = z,
                    finishId = finish.id
                });
            }
        }

        return saved;
    }

    public void RestoreSaveData(List<FloorFinishCellSaveData> savedCells)
    {
        ClearAllFinishes();

        if (savedCells == null)
            return;

        foreach (FloorFinishCellSaveData saved in savedCells)
        {
            if (saved == null)
                continue;

            RestoreFinish(
                new Vector2Int(saved.x, saved.z),
                saved.finishId);
        }
    }

    public void RemoveFinishesInArea(Vector2Int origin, int width, int depth)
    {
        if (finishIndices == null)
            return;

        HashSet<Vector2Int> affectedChunks = new HashSet<Vector2Int>();

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                Vector2Int cell = origin + new Vector2Int(x, z);

                if (!IsValidCell(cell))
                    continue;

                if (finishIndices[cell.x, cell.y] < 0)
                    continue;

                finishIndices[cell.x, cell.y] = -1;
                affectedChunks.Add(GetChunkCoordinate(cell));
            }
        }

        foreach (Vector2Int chunk in affectedChunks)
            RebuildChunk(chunk);
    }

    private bool IsValidCell(Vector2Int cell)
    {
        return finishIndices != null &&
               cell.x >= 0 && cell.x < GridWidth &&
               cell.y >= 0 && cell.y < GridHeight;
    }

    private Vector2Int GetChunkCoordinate(Vector2Int cell)
    {
        return new Vector2Int(
            cell.x / Mathf.Max(8, chunkSize),
            cell.y / Mathf.Max(8, chunkSize));
    }

    private void RebuildChunk(Vector2Int chunkCoordinate)
    {
        if (catalogue == null || gridManager == null || finishIndices == null)
            return;

        int startX = chunkCoordinate.x * Mathf.Max(8, chunkSize);
        int startZ = chunkCoordinate.y * Mathf.Max(8, chunkSize);
        int endX = Mathf.Min(startX + Mathf.Max(8, chunkSize), GridWidth);
        int endZ = Mathf.Min(startZ + Mathf.Max(8, chunkSize), GridHeight);

        Dictionary<int, List<Vector3>> verticesByFinish =
            new Dictionary<int, List<Vector3>>();
        Dictionary<int, List<Vector2>> uvsByFinish =
            new Dictionary<int, List<Vector2>>();
        Dictionary<int, List<int>> trianglesByFinish =
            new Dictionary<int, List<int>>();

        float cellSize = gridManager.CellSize;

        for (int x = startX; x < endX; x++)
        {
            for (int z = startZ; z < endZ; z++)
            {
                int finishIndex = finishIndices[x, z];
                FloorFinishDefinition finish = catalogue.GetAt(finishIndex);

                if (finish == null || finish.material == null)
                    continue;

                if (!verticesByFinish.TryGetValue(finishIndex, out List<Vector3> vertices))
                {
                    vertices = new List<Vector3>();
                    verticesByFinish.Add(finishIndex, vertices);
                    uvsByFinish.Add(finishIndex, new List<Vector2>());
                    trianglesByFinish.Add(finishIndex, new List<int>());
                }

                List<Vector2> uvs = uvsByFinish[finishIndex];
                List<int> triangles = trianglesByFinish[finishIndex];

                float localX = (x - startX) * cellSize;
                float localZ = (z - startZ) * cellSize;
                int vertexStart = vertices.Count;

                vertices.Add(new Vector3(localX, surfaceOffset, localZ));
                vertices.Add(new Vector3(localX, surfaceOffset, localZ + cellSize));
                vertices.Add(new Vector3(localX + cellSize, surfaceOffset, localZ + cellSize));
                vertices.Add(new Vector3(localX + cellSize, surfaceOffset, localZ));

                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(1f, 0f));

                triangles.Add(vertexStart);
                triangles.Add(vertexStart + 1);
                triangles.Add(vertexStart + 2);
                triangles.Add(vertexStart);
                triangles.Add(vertexStart + 2);
                triangles.Add(vertexStart + 3);
            }
        }

        if (verticesByFinish.Count == 0)
        {
            RemoveChunkVisual(chunkCoordinate);
            return;
        }

        List<int> orderedFinishIndices =
            new List<int>(verticesByFinish.Keys);
        orderedFinishIndices.Sort();

        List<Material> materials = new List<Material>();
        List<Vector3> allVertices = new List<Vector3>();
        List<Vector2> allUvs = new List<Vector2>();
        Mesh mesh = new Mesh
        {
            name = $"FloorFinishChunk_{chunkCoordinate.x}_{chunkCoordinate.y}",
            indexFormat = IndexFormat.UInt32
        };

        foreach (int finishIndex in orderedFinishIndices)
        {
            FloorFinishDefinition finish = catalogue.GetAt(finishIndex);
            List<Vector3> vertices = verticesByFinish[finishIndex];
            List<Vector2> uvs = uvsByFinish[finishIndex];
            List<int> triangles = trianglesByFinish[finishIndex];

            int vertexOffset = allVertices.Count;
            allVertices.AddRange(vertices);
            allUvs.AddRange(uvs);

            for (int i = 0; i < triangles.Count; i++)
                triangles[i] += vertexOffset;

            mesh.subMeshCount++;
            mesh.SetTriangles(triangles, mesh.subMeshCount - 1, false);
            materials.Add(finish.material);
        }

        mesh.SetVertices(allVertices);
        mesh.SetUVs(0, allUvs);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        ChunkVisual visual = GetOrCreateChunkVisual(chunkCoordinate);
        if (visual.mesh != null)
            Destroy(visual.mesh);

        visual.mesh = mesh;

        MeshFilter filter = visual.gameObject.GetComponent<MeshFilter>();
        MeshRenderer renderer = visual.gameObject.GetComponent<MeshRenderer>();

        filter.sharedMesh = mesh;
        renderer.sharedMaterials = materials.ToArray();

        Vector2Int chunkOrigin = new Vector2Int(startX, startZ);
        visual.gameObject.transform.position =
            gridManager.GridToWorld(chunkOrigin);
    }

    private ChunkVisual GetOrCreateChunkVisual(Vector2Int coordinate)
    {
        if (chunks.TryGetValue(coordinate, out ChunkVisual existing))
            return existing;

        GameObject chunkObject = new GameObject(
            $"Floor Finish Chunk {coordinate.x},{coordinate.y}");

        chunkObject.transform.SetParent(transform, true);

        MeshFilter filter = chunkObject.AddComponent<MeshFilter>();
        MeshRenderer renderer = chunkObject.AddComponent<MeshRenderer>();

        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;

        ChunkVisual created = new ChunkVisual
        {
            gameObject = chunkObject
        };

        chunks.Add(coordinate, created);
        return created;
    }

    private void RemoveChunkVisual(Vector2Int coordinate)
    {
        if (!chunks.TryGetValue(coordinate, out ChunkVisual visual))
            return;

        if (visual.mesh != null)
            Destroy(visual.mesh);

        if (visual.gameObject != null)
            Destroy(visual.gameObject);

        chunks.Remove(coordinate);
    }
}
