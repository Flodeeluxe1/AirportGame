
using UnityEngine;

public enum RoomFurnitureType
{
    InstructorDesk,
    StudentSeat
}

public class RoomFurnitureMarker : MonoBehaviour
{
    [SerializeField]
    private RoomFurnitureType furnitureType;

    public RoomFurnitureType FurnitureType => furnitureType;

    public void SetFurnitureType(RoomFurnitureType type)
    {
        furnitureType = type;
    }
}
