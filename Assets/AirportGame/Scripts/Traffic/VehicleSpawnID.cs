
using UnityEngine;

public class VehicleSpawnID : MonoBehaviour
{
    private static int nextId = 1;

    public int SpawnID { get; private set; }

    public static int AssignNextID()
    {
        return nextId++;
    }

    public void InitializeID()
    {
        if (SpawnID == 0)
        {
            SpawnID = AssignNextID();
        }
    }

    private void Awake()
    {
        InitializeID();
    }
}
