using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace DuplicateItemFix;

internal static class Repair
{
    private const string GearPrefabPrefix = "GEAR_";

    private const float RadiusTolerance = 0.25f;

    private const int SplineSamplesPerSegment = 32;

    internal static bool Enabled { get; set; } = true;

    private static long s_RunCount;
    private static long s_RemovedDuplicates;
    private static long s_RemovedImpostors;
    private static long s_SpawnersTouched;

    private static Dictionary<RadialObjectSpawner, Vector3[]?>? s_Loci;
    private static HashSet<RadialObjectSpawner>? s_BeachcombingChildren;

    private static string Key(string? prefabName, Vector3 p)
    {
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0}|{1:F3},{2:F3},{3:F3}", prefabName ?? string.Empty, p.x, p.y, p.z);
    }

    private static void CollectBeachcombingChildren()
    {
        s_BeachcombingChildren ??= new HashSet<RadialObjectSpawner>();
        s_BeachcombingChildren.Clear();

        var instances = Il2CppTLD.Gameplay.BeachcombingSpawner.s_Instances;
        if (instances == null)
        {
            return;
        }

        for (int i = 0; i < instances.Count; i++)
        {
            Il2CppTLD.Gameplay.BeachcombingSpawner parent = instances[i];
            if (parent == null || parent.m_ChildSpawners == null)
            {
                continue;
            }

            for (int j = 0; j < parent.m_ChildSpawners.Count; j++)
            {
                RadialObjectSpawner child = parent.m_ChildSpawners[j];
                if (child != null)
                {
                    s_BeachcombingChildren.Add(child);
                }
            }
        }
    }

    private static bool IsRepairable(string? prefabName, string? authoredPrefabName)
    {
        return prefabName != null
            && prefabName.StartsWith(GearPrefabPrefix, StringComparison.Ordinal)
            && (authoredPrefabName == null
                || string.Equals(prefabName, authoredPrefabName, StringComparison.Ordinal));
    }

    private static bool IsBeachcombing(RadialObjectSpawner spawner)
    {
        return spawner.IsTidelineSpawner
            || (s_BeachcombingChildren != null && s_BeachcombingChildren.Contains(spawner));
    }

    private static bool IsEligible(RadialObjectSpawner spawner, out string? authored, out bool arbitrate)
    {
        GameObject prefab = spawner.m_ObjectToSpawn;
        authored = prefab == null ? null : prefab.name;
        arbitrate = true;

        if (spawner.m_Spline != null)
        {
            if (IsBeachcombing(spawner))
            {

                arbitrate = false;
                return true;
            }

            return Locus(spawner) != null;
        }

        return spawner.m_LootTableData == null && authored != null;
    }

    private static Vector3[]? Locus(RadialObjectSpawner spawner)
    {
        Il2CppSuperSplines.Spline spline = spawner.m_Spline;
        if (spline == null)
        {
            return null;
        }

        s_Loci ??= new Dictionary<RadialObjectSpawner, Vector3[]?>();
        if (s_Loci.TryGetValue(spawner, out Vector3[]? cached))
        {
            return cached;
        }

        Vector3[]? points = null;
        int segments = spline.SegmentCount;
        if (spline.HasNodes && segments > 0)
        {
            int count = (segments * SplineSamplesPerSegment) + 1;
            points = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                points[i] = spline.GetPositionOnSpline((float)i / (count - 1));
            }
        }

        s_Loci[spawner] = points;
        return points;
    }

    private static bool Contains(RadialObjectSpawner spawner, Vector3 position)
    {
        return PlanarDistance(spawner, position) <= spawner.m_MaxRadius + RadiusTolerance;
    }

    private static float PlanarDistance(RadialObjectSpawner spawner, Vector3 position)
    {
        Vector3[]? locus = Locus(spawner);
        if (locus == null)
        {
            Vector3 center = spawner.transform.position;
            float dx = position.x - center.x;
            float dz = position.z - center.z;
            return Mathf.Sqrt((dx * dx) + (dz * dz));
        }

        float best = float.MaxValue;
        for (int i = 0; i < locus.Length - 1; i++)
        {
            float ax = locus[i].x;
            float az = locus[i].z;
            float sx = locus[i + 1].x - ax;
            float sz = locus[i + 1].z - az;
            float len2 = (sx * sx) + (sz * sz);

            float t = 0f;
            if (len2 > 0f)
            {
                t = (((position.x - ax) * sx) + ((position.z - az) * sz)) / len2;
                t = t < 0f ? 0f : (t > 1f ? 1f : t);
            }

            float qx = position.x - (ax + (t * sx));
            float qz = position.z - (az + (t * sz));
            float d = Mathf.Sqrt((qx * qx) + (qz * qz));
            if (d < best)
            {
                best = d;
            }
        }

        return best;
    }

    private sealed class Claim
    {
        internal RadialObjectSpawner Spawner = null!;
        internal int Index;
        internal Vector3 Position;
    }

    internal static void RepairRegion()
    {
        if (!Enabled)
        {
            return;
        }

        var registered = RadialSpawnManager.m_RadialSpawnObjects;
        if (registered == null)
        {
            return;
        }

        s_RunCount++;
        var touched = new HashSet<RadialObjectSpawner>();
        int removedDuplicates = 0;
        int removedImpostors = 0;

        CollectBeachcombingChildren();

        var claims = new Dictionary<string, List<Claim>>();

        for (int i = 0; i < registered.Count; i++)
        {
            RadialObjectSpawner spawner = registered[i];
            if (spawner == null || spawner.m_PendingSpawns == null)
            {
                continue;
            }

            if (!IsEligible(spawner, out string? authored, out bool arbitrate))
            {
                continue;
            }

            var seen = new HashSet<string>();
            for (int j = spawner.m_PendingSpawns.Count - 1; j >= 0; j--)
            {
                PendingSpawnData pending = spawner.m_PendingSpawns[j];
                if (!IsRepairable(pending.m_PrefabName, authored))
                {
                    continue;
                }

                string key = Key(pending.m_PrefabName, pending.m_Position);
                if (!seen.Add(key))
                {

                    spawner.m_PendingSpawns.RemoveAt(j);
                    removedDuplicates++;
                    touched.Add(spawner);
                }
            }

            if (!arbitrate)
            {
                continue;
            }

            for (int j = 0; j < spawner.m_PendingSpawns.Count; j++)
            {
                PendingSpawnData pending = spawner.m_PendingSpawns[j];
                if (!IsRepairable(pending.m_PrefabName, authored))
                {
                    continue;
                }

                string key = Key(pending.m_PrefabName, pending.m_Position);
                if (!claims.TryGetValue(key, out List<Claim>? list))
                {
                    list = new List<Claim>();
                    claims[key] = list;
                }

                list.Add(new Claim { Spawner = spawner, Index = j, Position = pending.m_Position });
            }
        }

        var losers = new Dictionary<RadialObjectSpawner, List<int>>();

        foreach (var pair in claims)
        {
            List<Claim> list = pair.Value;
            if (list.Count < 2)
            {
                continue;
            }

            Claim keep = ChooseOwner(list);
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], keep))
                {
                    continue;
                }

                if (!losers.TryGetValue(list[i].Spawner, out List<int>? indices))
                {
                    indices = new List<int>();
                    losers[list[i].Spawner] = indices;
                }

                indices.Add(list[i].Index);
            }
        }

        foreach (var pair in losers)
        {
            List<int> indices = pair.Value;

            indices.Sort();
            for (int i = indices.Count - 1; i >= 0; i--)
            {
                pair.Key.m_PendingSpawns.RemoveAt(indices[i]);
                removedImpostors++;
            }

            touched.Add(pair.Key);
        }

        s_RemovedDuplicates += removedDuplicates;
        s_RemovedImpostors += removedImpostors;
        s_SpawnersTouched += touched.Count;

        int totalRemoved = removedDuplicates + removedImpostors;

        int splinesExamined = s_Loci?.Count ?? 0;

        s_Loci?.Clear();
        s_BeachcombingChildren?.Clear();

        if (totalRemoved > 0)
        {
            string scene = GameManager.m_ActiveScene;
            if (string.IsNullOrEmpty(scene))
            {
                scene = "this region";
            }

            Mod.Log.Msg(
                $"Repaired {scene}: removed {totalRemoved} corrupt spawner " +
                $"{(totalRemoved == 1 ? "entry" : "entries")} " +
                $"({removedDuplicates} duplicated, {removedImpostors} misclaimed) from " +
                $"{touched.Count} {(touched.Count == 1 ? "spawner" : "spawners")}. " +
                "The game refills them itself.");
        }
    }

    private static int CountContested(Dictionary<string, List<Claim>> claims)
    {
        int n = 0;
        foreach (var pair in claims)
        {
            if (pair.Value.Count > 1)
            {
                n++;
            }
        }

        return n;
    }

    private static Claim ChooseOwner(List<Claim> list)
    {
        Claim? plausible = null;
        int plausibleCount = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (Contains(list[i].Spawner, list[i].Position))
            {
                plausibleCount++;
                plausible = list[i];
            }
        }

        if (plausibleCount == 1 && plausible != null)
        {
            return plausible;
        }

        Claim nearest = list[0];
        float best = PlanarDistance(nearest.Spawner, nearest.Position);
        for (int i = 1; i < list.Count; i++)
        {
            float d = PlanarDistance(list[i].Spawner, list[i].Position);
            if (d < best)
            {
                best = d;
                nearest = list[i];
            }
        }

        return nearest;
    }

    internal static string Describe()
    {
        return $"repair-enabled={Enabled} repair-runs={s_RunCount} " +
               $"repair-duplicates-removed={s_RemovedDuplicates} " +
               $"repair-impostors-removed={s_RemovedImpostors} " +
               $"repair-spawners-touched={s_SpawnersTouched}";
    }
}

[HarmonyPatch(typeof(RadialSpawnManager), nameof(RadialSpawnManager.DeserializeAll), typeof(string))]
internal static class RadialSpawnManagerRepairPatch
{

    private static void Postfix()
    {
        try
        {
            Repair.RepairRegion();
        }
        catch (Exception ex)
        {

            Mod.Log.Error($"{Mod.RunMarker} repair-failed {ex.GetType().Name}: {ex.Message}");
        }
    }
}
