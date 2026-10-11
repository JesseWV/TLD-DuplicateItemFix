using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;

namespace DuplicateItemFix;

internal static class GearRepair
{
    internal static bool Enabled { get; set; } = true;

    private static long s_RunCount;
    private static long s_RemovedDuplicates;
    private static long s_GroupsCollapsed;
    private static long s_HeldBackByGuard;

    private static string? ArrayFieldText(string payload, string field)
    {
        string needle = "\"" + field + "\":[";
        int i = payload.IndexOf(needle, StringComparison.Ordinal);
        if (i < 0)
        {
            return null;
        }

        int start = i + needle.Length;
        int end = payload.IndexOf(']', start);
        return end < 0 ? null : payload.Substring(start, end - start);
    }

    private static bool EverOwned(string payload)
    {
        return payload.IndexOf("\"m_HasBeenOwnedByPlayer\":true", StringComparison.Ordinal) >= 0
            || payload.IndexOf("\"m_BeenInPlayerInventoryProxy\":true", StringComparison.Ordinal) >= 0;
    }

    private static bool IsAuthored(string payload)
    {
        return payload.IndexOf("\"m_ObjectGuidSerialized\"", StringComparison.Ordinal) >= 0;
    }

    internal static void RepairGearList(GearSaveList list)
    {
        if (!Enabled || list == null)
        {
            return;
        }

        Il2CppSystem.Collections.Generic.List<GearSaveData> items = list.m_SerializedItems;
        if (items == null || items.Count < 2)
        {
            return;
        }

        s_RunCount++;

        var groups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var payloads = new string?[items.Count];

        for (int i = 0; i < items.Count; i++)
        {
            GearSaveData entry = items[i];
            string? payload = entry?.m_SearializedGear;
            payloads[i] = payload;
            if (payload == null)
            {
                continue;
            }

            string? position = ArrayFieldText(payload, "m_Position");
            string? rotation = ArrayFieldText(payload, "m_Rotation");
            if (position == null || rotation == null)
            {

                continue;
            }

            string key = (entry!.m_PrefabName ?? string.Empty) + "|" + position + "|" + rotation;
            if (!groups.TryGetValue(key, out List<int>? members))
            {
                members = new List<int>();
                groups[key] = members;
            }

            members.Add(i);
        }

        var doomed = new List<int>();
        int groupsCollapsed = 0;
        int heldBack = 0;

        foreach (KeyValuePair<string, List<int>> group in groups)
        {
            List<int> members = group.Value;
            if (members.Count < 2)
            {
                continue;
            }

            bool blocked = false;
            for (int m = 0; m < members.Count; m++)
            {
                string payload = payloads[members[m]]!;
                if (EverOwned(payload) || IsAuthored(payload))
                {
                    blocked = true;
                    break;
                }
            }

            if (blocked)
            {
                heldBack += members.Count - 1;
                continue;
            }

            groupsCollapsed++;

            for (int m = 1; m < members.Count; m++)
            {
                doomed.Add(members[m]);
            }
        }

        s_HeldBackByGuard += heldBack;

        if (doomed.Count == 0)
        {
            return;
        }

        doomed.Sort();
        for (int d = doomed.Count - 1; d >= 0; d--)
        {
            items.RemoveAt(doomed[d]);
        }

        s_RemovedDuplicates += doomed.Count;
        s_GroupsCollapsed += groupsCollapsed;

        string scene = GameManager.m_ActiveScene;
        if (string.IsNullOrEmpty(scene))
        {
            scene = "this region";
        }

        Mod.Log.Msg(
            $"Repaired {scene}: removed {doomed.Count} duplicate world-item " +
            $"{(doomed.Count == 1 ? "record" : "records")} from {groupsCollapsed} " +
            $"{(groupsCollapsed == 1 ? "item" : "items")} recorded more than once.");
    }

    internal static string Describe()
    {
        return $"gear-repair-enabled={Enabled} gear-repair-runs={s_RunCount} " +
               $"gear-duplicates-removed={s_RemovedDuplicates} " +
               $"gear-groups-collapsed={s_GroupsCollapsed} " +
               $"gear-held-back-by-guard={s_HeldBackByGuard}";
    }
}

[HarmonyPatch(typeof(GearManager), nameof(GearManager.Deserialize),
    typeof(GearSaveList), typeof(Il2CppSystem.Collections.Generic.List<GearItem>))]
internal static class GearManagerRepairPatch
{
    private static void Prefix(GearSaveList list)
    {
        try
        {
            GearRepair.RepairGearList(list);
        }
        catch (Exception ex)
        {

            Mod.Log.Error($"{Mod.RunMarker} gear-repair-failed {ex.GetType().Name}: {ex.Message}");
        }
    }
}
