using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace AiryaSeraphs
{
    [StaticConstructorOnStartup]
    public static class HarmonyPatches
    {
        static HarmonyPatches()
        {
            Harmony harmony = new Harmony("com.airyaseraphs.rimworld");
            
            // Patch for equipment equipping
            harmony.Patch(
                original: AccessTools.Method(typeof(Pawn_EquipmentTracker), "Notify_EquipmentAdded"),
                postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Notify_EquipmentAdded_Postfix))
            );
            
            // Patch for apparel equipping
            harmony.Patch(
                original: AccessTools.Method(typeof(Pawn_ApparelTracker), "Notify_ApparelAdded"),
                postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Notify_ApparelAdded_Postfix))
            );
            
            // Patch for temperature change
            harmony.Patch(
                original: AccessTools.Method(typeof(MapTemperature), "MapTemperatureTick"),
                postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(MapTemperatureTick_Postfix))
            );
            
            // NEW: Add direct thing tick patch to catch ALL ice equipment
            harmony.Patch(
                original: AccessTools.Method(typeof(ThingWithComps), "Tick"),
                postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(ThingWithComps_Tick_Postfix))
            );
            
            // NEW: Add inspection string patch for useful debug info
            harmony.Patch(
                original: AccessTools.Method(typeof(ThingWithComps), "GetInspectString"),
                postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(GetInspectString_Postfix))
            );
            
            Log.Message("[AiryaSeraphs] Harmony patches applied successfully");
        }
        
        // Force update ice equipment when equipped
        public static void Notify_EquipmentAdded_Postfix(ThingWithComps eq)
        {
            if (eq == null) return;
            
            // Check if this is ice equipment
            if (!IsAiryaIceEquipment.IsIceEquipment(eq.def)) return;
            
            // Log the equipping for debug
            Log.Message($"[AiryaSeraphs] Ice equipment equipped: {eq.LabelCap}");
            
            // Force component tick to update appearance and stats
            var comp = eq.GetComp<CompTemperatureDependentStats>();
            if (comp != null)
            {
                comp.CompTick();
            }
            
            var meltComp = eq.GetComp<CompTemperatureMeltingRegen>();
            if (meltComp != null)
            {
                meltComp.CompTick();
            }
        }
        
        // Force update ice apparel when worn
        public static void Notify_ApparelAdded_Postfix(Apparel apparel)
        {
            if (apparel == null) return;
            
            // Check if this is ice equipment
            if (!IsAiryaIceEquipment.IsIceEquipment(apparel.def)) return;
            
            // Log the equipping for debug
            Log.Message($"[AiryaSeraphs] Ice apparel equipped: {apparel.LabelCap}");
            
            // Force component tick to update appearance and stats
            var comp = apparel.GetComp<CompTemperatureDependentStats>();
            if (comp != null)
            {
                comp.CompTick();
            }
            
            var meltComp = apparel.GetComp<CompTemperatureMeltingRegen>();
            if (meltComp != null)
            {
                meltComp.CompTick();
            }
        }
        
        // Temperature change response - MUCH more frequent now
        private static int tickCounter = 0;
        private const int UPDATE_INTERVAL = 60; // Increased frequency: check every 60 ticks (1 second)
        
        public static void MapTemperatureTick_Postfix(Map ___map)
        {
            // Only update periodically to save performance
            tickCounter++;
            if (tickCounter < UPDATE_INTERVAL) return;
            tickCounter = 0;
            
            if (___map == null) return;
            
            // Find all pawns with ice equipment
            foreach (Pawn pawn in ___map.mapPawns.AllPawnsSpawned)
            {
                // Check equipment
                if (pawn.equipment?.Primary != null && 
                    IsAiryaIceEquipment.IsIceEquipment(pawn.equipment.Primary.def))
                {
                    // Force update
                    var comp = pawn.equipment.Primary.GetComp<CompTemperatureDependentStats>();
                    if (comp != null) comp.CompTick();
                    
                    var meltComp = pawn.equipment.Primary.GetComp<CompTemperatureMeltingRegen>();
                    if (meltComp != null) meltComp.CompTick();
                }
                
                // Check apparel
                if (pawn.apparel != null)
                {
                    foreach (Apparel apparel in pawn.apparel.WornApparel)
                    {
                        if (IsAiryaIceEquipment.IsIceEquipment(apparel.def))
                        {
                            // Force update
                            var comp = apparel.GetComp<CompTemperatureDependentStats>();
                            if (comp != null) comp.CompTick();
                            
                            var meltComp = apparel.GetComp<CompTemperatureMeltingRegen>();
                            if (meltComp != null) meltComp.CompTick();
                        }
                    }
                }
            }
            
            // NEW: Also check for ice equipment on the ground
            foreach (Thing thing in ___map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (thing is ThingWithComps thingWithComps && 
                    IsAiryaIceEquipment.IsIceEquipment(thingWithComps.def))
                {
                    // Force update
                    var comp = thingWithComps.GetComp<CompTemperatureDependentStats>();
                    if (comp != null) comp.CompTick();
                    
                    var meltComp = thingWithComps.GetComp<CompTemperatureMeltingRegen>();
                    if (meltComp != null) meltComp.CompTick();
                }
            }
        }
        
        // NEW: Direct patch to handle ALL ice equipment ticking
        // This is a crucial addition to make regeneration work properly
        public static void ThingWithComps_Tick_Postfix(ThingWithComps __instance)
        {
            // Skip non-ice equipment for performance
            if (!IsAiryaIceEquipment.IsIceEquipment(__instance.def)) return;
            
            // Only process occasionally based on thing ID to spread processing load
            if (__instance.thingIDNumber % 10 != Find.TickManager.TicksGame % 10) return;
            
            // Force melting/regeneration component to tick regardless of where the item is
            var meltComp = __instance.GetComp<CompTemperatureMeltingRegen>();
            if (meltComp != null)
            {
                // Force an immediate tick by resetting tick counter
                typeof(CompTemperatureMeltingRegen)
                    .GetField("tickCounter", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.SetValue(meltComp, 0);
                
                meltComp.CompTick();
            }
        }
        
        // NEW: Enhanced inspection with temperature effects
        public static void GetInspectString_Postfix(ThingWithComps __instance, ref string __result)
        {
            // Skip if not ice equipment
            if (!IsAiryaIceEquipment.IsIceEquipment(__instance.def)) return;
            
            // Get the current temperature
            float temperature = 21f; // Default room temperature
            Map map = __instance.MapHeld;
            IntVec3 position = __instance.PositionHeld;
            
            if (map != null && position.IsValid)
            {
                temperature = GenTemperature.GetTemperatureForCell(position, map);
            }
            else if (__instance is Apparel apparel && apparel.Wearer != null && apparel.Wearer.Spawned)
            {
                temperature = GenTemperature.GetTemperatureForCell(apparel.Wearer.Position, apparel.Wearer.Map);
            }
            else if (__instance.ParentHolder is Pawn_EquipmentTracker eq && eq.pawn != null && eq.pawn.Spawned)
            {
                temperature = GenTemperature.GetTemperatureForCell(eq.pawn.Position, eq.pawn.Map);
            }
            
            // Add temperature info
            __result += $"\nTemperature: {temperature:F1}°C";
            
            // Show temperature effect
            if (temperature > 0)
            {
                __result += $"\nMelting: {temperature * 0.3f:F1} HP/minute";
            }
            else if (temperature < 0)
            {
                float qualityFactor = GetQualityFactor(__instance);
                __result += $"\nRegenerating: {Mathf.Abs(temperature) * 0.5f * qualityFactor:F1} HP/minute";
            }
            
            // Always show HP status
            __result += $"\nHP: {__instance.HitPoints}/{__instance.MaxHitPoints}";
        }
        
        // Helper method to get quality factor
        private static float GetQualityFactor(ThingWithComps thing)
        {
            var compQuality = thing.TryGetComp<CompQuality>();
            if (compQuality == null) return 1f;
            
            switch (compQuality.Quality)
            {
                case QualityCategory.Awful: return 0.5f;
                case QualityCategory.Poor: return 0.75f;
                case QualityCategory.Normal: return 1f;
                case QualityCategory.Good: return 1.5f;
                case QualityCategory.Excellent: return 2f;
                case QualityCategory.Masterwork: return 3f;
                case QualityCategory.Legendary: return 5f;
                default: return 1f;
            }
        }
    }
}

/*
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
} */
