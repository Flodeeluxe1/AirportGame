
using System;
using System.Collections.Generic;
using UnityEngine;

public class FoundationBoundaryOwnership : MonoBehaviour
{
    [SerializeField] private string ownershipId = "";
    [SerializeField]
    private List<string> savedOwnerIds =
        new List<string>();

    private readonly HashSet<GameObject> ownedObjects =
        new HashSet<GameObject>();

    private readonly HashSet<FoundationBoundaryOwnership> owners =
        new HashSet<FoundationBoundaryOwnership>();

    private EdgeOccupancy edgeOccupancy;

    public string OwnershipId => ownershipId;

    public IReadOnlyList<string> SavedOwnerIds => savedOwnerIds;

    public void Initialize(EdgeOccupancy occupancy)
    {
        if (occupancy != null)
            edgeOccupancy = occupancy;

        EnsureOwnershipId();
    }

    private void EnsureOwnershipId()
    {
        if (string.IsNullOrWhiteSpace(ownershipId))
            ownershipId = Guid.NewGuid().ToString("N");
    }

    public void RestoreOwnershipData(
        string restoredId,
        List<string> restoredOwnerIds)
    {
        if (!string.IsNullOrWhiteSpace(restoredId))
            ownershipId = restoredId;

        EnsureOwnershipId();

        savedOwnerIds = restoredOwnerIds != null
            ? new List<string>(restoredOwnerIds)
            : new List<string>();
    }

    public void RegisterBoundaryObject(GameObject boundaryObject)
    {
        if (boundaryObject == null || boundaryObject == gameObject)
            return;

        EnsureOwnershipId();

        FoundationBoundaryOwnership boundaryOwnership =
            boundaryObject.GetComponent<FoundationBoundaryOwnership>();

        if (boundaryOwnership == null)
        {
            boundaryOwnership =
                boundaryObject.AddComponent<FoundationBoundaryOwnership>();
        }

        boundaryOwnership.EnsureOwnershipId();

        ownedObjects.Add(boundaryObject);

        if (!boundaryOwnership.owners.Contains(this))
            boundaryOwnership.owners.Add(this);

        if (!boundaryOwnership.savedOwnerIds.Contains(ownershipId))
            boundaryOwnership.savedOwnerIds.Add(ownershipId);
    }

    public void UnregisterBoundaryObject(GameObject boundaryObject)
    {
        if (boundaryObject == null)
            return;

        ownedObjects.Remove(boundaryObject);

        FoundationBoundaryOwnership boundaryOwnership =
            boundaryObject.GetComponent<FoundationBoundaryOwnership>();

        if (boundaryOwnership == null)
            return;

        boundaryOwnership.owners.Remove(this);
        boundaryOwnership.savedOwnerIds.Remove(ownershipId);
    }

    public static List<FoundationBoundaryOwnership> GetOwners(
        GameObject boundaryObject)
    {
        List<FoundationBoundaryOwnership> result =
            new List<FoundationBoundaryOwnership>();

        if (boundaryObject == null)
            return result;

        FoundationBoundaryOwnership boundaryOwnership =
            boundaryObject.GetComponent<FoundationBoundaryOwnership>();

        if (boundaryOwnership == null)
            return result;

        foreach (FoundationBoundaryOwnership owner in boundaryOwnership.owners)
        {
            if (owner != null && !result.Contains(owner))
                result.Add(owner);
        }

        return result;
    }

    public void RemoveOwnedBoundaryObjects()
    {
        List<GameObject> objectsToRemove =
            new List<GameObject>(ownedObjects);

        ownedObjects.Clear();

        foreach (GameObject boundaryObject in objectsToRemove)
        {
            if (boundaryObject == null)
                continue;

            FoundationBoundaryOwnership boundaryOwnership =
                boundaryObject.GetComponent<FoundationBoundaryOwnership>();

            if (boundaryOwnership != null)
            {
                boundaryOwnership.owners.Remove(this);
                boundaryOwnership.savedOwnerIds.Remove(ownershipId);

                // Another foundation still owns this boundary.
                if (boundaryOwnership.owners.Count > 0 ||
                    boundaryOwnership.savedOwnerIds.Count > 0)
                {
                    continue;
                }
            }

            if (edgeOccupancy != null)
                edgeOccupancy.ReleaseBuilding(boundaryObject);

            Destroy(boundaryObject);
        }
    }

    public static void ResolveSavedOwnership(
        IEnumerable<FoundationBoundaryOwnership> components)
    {
        if (components == null)
            return;

        List<FoundationBoundaryOwnership> all =
            new List<FoundationBoundaryOwnership>();

        Dictionary<string, FoundationBoundaryOwnership> byId =
            new Dictionary<string, FoundationBoundaryOwnership>();

        foreach (FoundationBoundaryOwnership component in components)
        {
            if (component == null)
                continue;

            component.EnsureOwnershipId();
            component.owners.Clear();
            component.ownedObjects.Clear();

            all.Add(component);

            if (!byId.ContainsKey(component.ownershipId))
                byId.Add(component.ownershipId, component);
        }

        foreach (FoundationBoundaryOwnership boundary in all)
        {
            foreach (string ownerId in boundary.savedOwnerIds)
            {
                if (string.IsNullOrWhiteSpace(ownerId))
                    continue;

                if (!byId.TryGetValue(
                        ownerId,
                        out FoundationBoundaryOwnership owner))
                {
                    continue;
                }

                if (owner == boundary)
                    continue;

                boundary.owners.Add(owner);
                owner.ownedObjects.Add(boundary.gameObject);
            }
        }
    }

    private void OnDestroy()
    {
        // If this is a boundary object, detach it from its foundations.
        if (owners.Count > 0)
        {
            List<FoundationBoundaryOwnership> ownerCopy =
                new List<FoundationBoundaryOwnership>(owners);

            foreach (FoundationBoundaryOwnership owner in ownerCopy)
            {
                if (owner != null)
                {
                    owner.ownedObjects.Remove(gameObject);

                    if (ownerIdIsPresent(owner.ownershipId))
                        savedOwnerIds.Remove(owner.ownershipId);
                }
            }

            owners.Clear();
        }

        // If this is a foundation, clean up the boundaries it owns.
        if (ownedObjects.Count > 0)
            RemoveOwnedBoundaryObjects();
    }

    private bool ownerIdIsPresent(string ownerId)
    {
        return savedOwnerIds.Contains(ownerId);
    }
}
