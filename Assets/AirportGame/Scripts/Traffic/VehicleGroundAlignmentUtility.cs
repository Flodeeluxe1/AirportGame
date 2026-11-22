using UnityEngine;

public static class VehicleGroundAlignmentUtility
{
    public static bool AlignColliderBottomToGround(
        Transform vehicleTransform,
        ref float heightOffset,
        float groundY)
    {
        if (vehicleTransform == null)
            return false;

        BoxCollider boxCollider =
            vehicleTransform.GetComponentInChildren<BoxCollider>();

        if (boxCollider == null || !boxCollider.enabled)
            return false;

        float colliderBottom = boxCollider.bounds.min.y;
        vehicleTransform.position += Vector3.up * (groundY - colliderBottom);
        heightOffset = vehicleTransform.position.y - groundY;
        return true;
    }
}
