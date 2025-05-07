using HarmonyLib;
using RimWorld;
using System; // Add this for the Type class
using Verse;
using AiryaSeraphs;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Reflection;
using static UnityEngine.GraphicsBuffer;
using static Verse.DamageWorker;

namespace AiryaSeraphs
{
    [StaticConstructorOnStartup]
    internal static class HarmonyPatches
    {
        static HarmonyPatches()
        {
            Log.Message("Initializing AiryaSeraphs Harmony patches...");
            Harmony harmony = new Harmony("AiryaSeraphs");

            try
            {
                harmony.PatchAll();
                Log.Message("AiryaSeraphs Harmony patches initialized.");
            }
            catch (Exception ex)
            {
                Log.Error($"[AiryaSeraphs] Error during patching: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }

    // --------------------- MELTING REGEN -------------------------//
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Tick))]
    public static class Pawn_Tick_Patch
    {
        public static void Postfix(Pawn __instance)
        {
            if (__instance.equipment == null)
                return;

            foreach (var eq in __instance.equipment.AllEquipmentListForReading)
            {
                var comp = eq.TryGetComp<CompTemperatureMeltingRegen>();
                comp?.ApplyTemperatureDeteriorationForEquippedItem();
            }
        }
    }

    [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.InitializeComps))]
    public static class ThingWithComps_InitializeComps_Patch
    {
        public static void Postfix(ThingWithComps __instance)
        {
            if (IsAiryaIceEquipment.IsIceEquipment(__instance.def) && !__instance.AllComps.Any(c => c is CompTemperatureMeltingRegen))
            {
                var comp = new CompTemperatureMeltingRegen
                {
                    parent = __instance
                };
                comp.Initialize(new CompProperties());
                __instance.AllComps.Add(comp);
                Log.Message($"[Dynamic Add] CompTemperatureMeltingRegen added to Ice Equipment: {__instance.LabelCap}.");
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), "Notify_EquipmentAdded")]
    public static class EquipmentAdded_Patch
    {
        public static void Postfix(ThingWithComps eq)
        {
            if (eq.TryGetComp<CompTemperatureMeltingRegen>() is CompTemperatureMeltingRegen comp)
            {
                comp.ApplyTemperatureDeteriorationForEquippedItem();
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_EquipmentRemoved))]
    public static class EquipmentRemoved_Patch
    {
        public static void Postfix(ThingWithComps eq)
        {
            if (eq.TryGetComp<CompTemperatureMeltingRegen>() is CompTemperatureMeltingRegen comp)
            {
                Log.Message($"[Temperature] Removed equipped item: {eq.LabelCap}.");
            }
        }
    }

    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValueUnfinalized))]
    public static class StatWorker_GetValueUnfinalized_Patch
    {
        public static void Postfix(StatWorker __instance, ref float __result, StatRequest req)
        {
            // Use reflection to access the private `stat` field of StatWorker
            var statField = AccessTools.Field(typeof(StatWorker), "stat");
            StatDef stat = statField.GetValue(__instance) as StatDef;

            if (stat == null)
            {
                Log.Warning("[Harmony] Unable to access StatDef in StatWorker.");
                return;
            }

            // Check if the stat is DeteriorationRate
            if (stat == StatDefOf.DeteriorationRate && req.Thing is ThingWithComps thing)
            {
                var comp = thing.TryGetComp<CompTemperatureMeltingRegen>();
                if (comp != null)
                {
                    // Apply adjusted deterioration rate logic
                    __result = comp.GetAdjustedDeteriorationRate(__result);

                    // Optional: Apply recovery logic for negative temperature
                    comp.ApplyTemperatureRecovery();
                }
            }
        }
    }
    // --------------------- END MELTING REGEN ---------------------//
}
