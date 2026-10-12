
using System;
using System.IO;
using UnityEngine;

public static class AirportSaveFileStore
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumSaveSlots = 10;

    private static string SaveDirectory =>
        Path.Combine(
            Application.persistentDataPath,
            "AirportSaves"
        );

    public static string GetSaveDirectory()
    {
        return SaveDirectory;
    }

    public static bool TrySave(
        int slot,
        AirportSaveData data,
        out string error)
    {
        error = string.Empty;

        if (!IsValidSlot(slot))
        {
            error = $"Save slot must be between 1 and {MaximumSaveSlots}.";
            return false;
        }

        if (data == null)
        {
            error = "Cannot save because the save data is null.";
            return false;
        }

        string path = GetSlotPath(slot);
        string backupPath = path + ".bak";
        string temporaryPath = path + ".tmp";

        try
        {
            Directory.CreateDirectory(SaveDirectory);

            data.schemaVersion = CurrentSchemaVersion;
            data.savedAtUtc = DateTime.UtcNow.ToString("O");

            if (string.IsNullOrWhiteSpace(data.saveName))
                data.saveName = $"Save Slot {slot}";

            // Ensure Unity's JSON serializer receives initialized lists.
            data.EnsureCollections();

            data.gameTimeSeconds =
                Mathf.Clamp(data.gameTimeSeconds, 0, 86399);

            string json = JsonUtility.ToJson(data, true);

            // Write to a temporary file first so an interrupted write
            // does not immediately destroy the current save.
            File.WriteAllText(temporaryPath, json);

            // Validate the temporary file before replacing the save.
            if (!TryReadAndValidate(
                    temporaryPath,
                    out AirportSaveData verifiedData,
                    out string validationError))
            {
                throw new IOException(
                    "The temporary save failed validation: " +
                    validationError
                );
            }

            bool currentSaveIsValid =
                File.Exists(path) &&
                TryReadAndValidate(
                    path,
                    out _,
                    out _
                );

            // Back up only a valid current save. Never replace a known
            // good backup with a potentially damaged primary file.
            if (currentSaveIsValid)
                File.Copy(path, backupPath, true);

            if (File.Exists(path))
                File.Delete(path);

            File.Move(temporaryPath, path);

            Debug.Log(
                $"Airport save written successfully. " +
                $"Slot: {slot} | Name: {verifiedData.saveName} | " +
                $"Path: {path}"
            );

            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;

            // Preserve the backup if a replacement failed after the
            // previous primary save was removed.
            try
            {
                if (!File.Exists(path) && File.Exists(backupPath))
                    File.Copy(backupPath, path, true);
            }
            catch (Exception restoreException)
            {
                Debug.LogError(
                    "AirportSaveFileStore: Could not restore the " +
                    $"previous save: {restoreException.Message}"
                );
            }

            Debug.LogError(
                $"AirportSaveFileStore: Failed to save slot {slot}. " +
                $"{exception}"
            );

            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (Exception cleanupException)
            {
                Debug.LogWarning(
                    "AirportSaveFileStore: Could not remove temporary " +
                    $"save file: {cleanupException.Message}"
                );
            }
        }
    }


    public static bool TryLoad(
        int slot,
        out AirportSaveData data,
        out string error)
    {
        data = null;
        error = string.Empty;

        if (!IsValidSlot(slot))
        {
            error = $"Save slot must be between 1 and {MaximumSaveSlots}.";
            return false;
        }

        string path = GetSlotPath(slot);
        string backupPath = path + ".bak";

        string primaryError;

        if (File.Exists(path))
        {
            if (TryReadAndValidate(
                    path,
                    out data,
                    out primaryError))
            {
                return true;
            }
        }
        else
        {
            primaryError = "The main save file does not exist.";
        }

        // If the primary save is missing or invalid, try the backup.
        if (File.Exists(backupPath))
        {
            if (TryReadAndValidate(
                    backupPath,
                    out data,
                    out string backupError))
            {
                error =
                    $"Loaded the backup for slot {slot}. " +
                    $"Reason: {primaryError}";

                Debug.LogWarning(
                    $"AirportSaveFileStore: {error}"
                );

                return true;
            }

            error =
                $"{primaryError} " +
                $"Backup could not be loaded: {backupError}";
        }
        else
        {
            error =
                $"{primaryError} No backup file exists.";
        }

        Debug.LogError(
            $"AirportSaveFileStore: Failed to load slot {slot}. " +
            error
        );

        data = null;
        return false;
    }

    public static AirportSaveSlotInfo GetSlotInfo(int slot)
    {
        AirportSaveSlotInfo info = new AirportSaveSlotInfo
        {
            slotNumber = slot,
            exists = false,
            isValid = false,
            saveName = string.Empty,
            savedAtUtc = string.Empty,
            placedBuildingCount = 0,
            roadCellCount = 0,
            taxiwayCellCount = 0,
            error = string.Empty
        };

        if (!IsValidSlot(slot))
        {
            info.error =
                $"Slot number must be between 1 and {MaximumSaveSlots}.";

            return info;
        }

        string path = GetSlotPath(slot);
        string backupPath = path + ".bak";

        info.exists =
            File.Exists(path) ||
            File.Exists(backupPath);

        if (!info.exists)
            return info;

        if (TryLoad(slot, out AirportSaveData data, out string error))
        {
            info.isValid = true;
            info.saveName = data.saveName;
            info.savedAtUtc = data.savedAtUtc;

            info.placedBuildingCount =
                data.placedBuildings.Count;

            info.roadCellCount =
                data.roadCells.Count;

            info.taxiwayCellCount =
                data.taxiwayCells.Count;

            // A non-empty message may indicate that the backup
            // was used successfully.
            info.error = error;
        }
        else
        {
            info.isValid = false;
            info.error = error;
        }

        return info;
    }

    public static bool TryDeleteSlot(
        int slot,
        out string error)
    {
        error = string.Empty;

        if (!IsValidSlot(slot))
        {
            error = $"Save slot must be between 1 and {MaximumSaveSlots}.";
            return false;
        }

        string path = GetSlotPath(slot);

        string[] files =
        {
            path,
            path + ".bak",
            path + ".tmp"
        };

        try
        {
            foreach (string file in files)
            {
                if (File.Exists(file))
                    File.Delete(file);
            }

            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;

            Debug.LogError(
                $"AirportSaveFileStore: Could not delete slot {slot}. " +
                exception
            );

            return false;
        }
    }

    private static bool TryReadAndValidate(
        string path,
        out AirportSaveData data,
        out string error)
    {
        data = null;
        error = string.Empty;

        try
        {
            if (!File.Exists(path))
            {
                error = "File does not exist.";
                return false;
            }

            string json = File.ReadAllText(path);

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Save file is empty.";
                return false;
            }

            data = JsonUtility.FromJson<AirportSaveData>(json);

            if (data == null)
            {
                error = "Could not deserialize save data.";
                return false;
            }

            if (data.schemaVersion > CurrentSchemaVersion)
            {
                error =
                    $"Save schema version {data.schemaVersion} is newer " +
                    $"than supported version {CurrentSchemaVersion}.";

                data = null;
                return false;
            }

            if (data.schemaVersion < CurrentSchemaVersion)
            {
                error =
                    $"Save schema version {data.schemaVersion} requires " +
                    "a migration before it can be loaded.";

                data = null;
                return false;
            }

            if (data.gameTimeSeconds < 0 ||
                data.gameTimeSeconds > 86399)
            {
                error = "Saved game time is outside the valid day range.";
                data = null;
                return false;
            }

            data.EnsureCollections();

            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            data = null;
            return false;
        }
    }

    private static string GetSlotPath(int slot)
    {
        return Path.Combine(
            SaveDirectory,
            $"airport_save_{slot:00}.json"
        );
    }

    private static bool IsValidSlot(int slot)
    {
        return slot >= 1 && slot <= MaximumSaveSlots;
    }
}

[Serializable]
public struct AirportSaveSlotInfo
{
    public int slotNumber;
    public bool exists;
    public bool isValid;

    public string saveName;
    public string savedAtUtc;

    public int placedBuildingCount;
    public int roadCellCount;
    public int taxiwayCellCount;

    public string error;
}