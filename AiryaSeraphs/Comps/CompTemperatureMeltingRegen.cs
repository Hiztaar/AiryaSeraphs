using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using UnityEngine;
using Verse;




namespace AiryaSeraphs
{
    public class CompProperties_TemperatureMeltingRegen : CompProperties
    {
        public CompProperties_TemperatureMeltingRegen()
        {
            this.compClass = typeof(CompTemperatureMeltingRegen);
        }
    }

    public class CompTemperatureMeltingRegen : ThingComp
    {
        public CompProperties_TemperatureMeltingRegen Props => (CompProperties_TemperatureMeltingRegen)this.props;

        private const float RecoveryPerDayFactor = 1f; // Multiplier for recovery
        private const float TicksPerDay = 60000f;
        private const float BaseLogarithmicThreshold = 20f; // Temperature for 1.5x multiplier
        private const float MaxFrostbiteMultiplier = 3f; // Cap multiplier

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);

            if (!IsAiryaIceEquipment.IsIceEquipment(parent.def))
            {
                Log.Warning($"CompTemperatureMeltingRegen applied to non-enchanted ice equipment: {parent.LabelCap}. This may be unintended.");
            }
        }
        private bool TryGetAmbientTemperature(out float ambientC)
        {
            ambientC = 20f;                          // default baseline
            Map  map = parent.MapHeld ?? (parent.ParentHolder as Pawn)?.MapHeld;
            IntVec3 pos = parent.PositionHeld.IsValid
                            ? parent.PositionHeld
                            : (parent.ParentHolder as Pawn)?.PositionHeld ?? IntVec3.Invalid;

            if (map == null || !pos.IsValid)
                return false;                        // not spawned anywhere yet

            ambientC = GenTemperature.GetTemperatureForCell(pos, map);
            return true;
        }
        public float GetAdjustedDeteriorationRate(float baseRate)
        {
            if (parent == null || parent.Map == null)
                return baseRate;

            float temperature = GenTemperature.GetTemperatureForCell(parent.Position, parent.Map);

            if (temperature > 0.99)
            {
                // Increase deterioration rate based on temperature
                return baseRate + (2 * temperature);
            }
            else if (temperature < -0.99)
            {
                // Reduce deterioration based on temperature
                return Mathf.Max(0, baseRate - (2 * Mathf.Abs(temperature)));
            }

            return baseRate; // Neutral temperature
        }

        // private int ticksSinceLastUpdate = 0;

        private float accumulatedDeterioration = 0f;

        public void ApplyTemperatureDeteriorationForEquippedItem()
        {
            // Resolve the map and position even when the item is worn
            Map map = parent.MapHeld ?? (parent.ParentHolder as Pawn)?.MapHeld;
            IntVec3 pos = parent.PositionHeld.IsValid ? parent.PositionHeld
                                                    : (parent.ParentHolder as Pawn)?.PositionHeld ?? IntVec3.Invalid;
            if (map == null || !pos.IsValid)
            {
                Log.Message($"[Temperature] Equipped {parent.LabelCap}: !pos.IsValid");
                return;   // still nothing we can work with                                       
            }
            if (!IsAiryaIceEquipment.IsIceEquipment(parent.def))
            {
                Log.Message($"[Temperature] Equipped {parent.LabelCap}: !IsAiryaIceEquipment.IsIceEquipment(parent.def)");
                return; // Skip non-ice weapons
            }
            // Get current temperature at the item's position
            float temperature = GenTemperature.GetTemperatureForCell(parent.Position, parent.MapHeld);

            if (temperature > 0)
            {
                // Calculate deterioration rate per tick
                float deteriorationRate = (2 * temperature) / TicksPerDay;
                accumulatedDeterioration += deteriorationRate;

                if (accumulatedDeterioration >= 1f)
                {
                    int damage = Mathf.FloorToInt(accumulatedDeterioration);
                    parent.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, damage));
                    accumulatedDeterioration -= damage;
                    Log.Message($"[Temperature] Equipped {parent.LabelCap}: {damage} HP deterioration applied. Remaining accumulated: {accumulatedDeterioration}");
                }
            }
            else if (temperature < 0)
            {
                // Calculate recovery rate per tick
                float recoveryRate = (-temperature * RecoveryPerDayFactor) / TicksPerDay;
                accumulatedDeterioration -= recoveryRate;

                if (accumulatedDeterioration <= -1f)
                {
                    int recovery = Mathf.FloorToInt(-accumulatedDeterioration);
                    parent.HitPoints = Mathf.Min(parent.MaxHitPoints, parent.HitPoints + recovery);
                    accumulatedDeterioration += recovery;
                    Log.Message($"[Temperature] Equipped {parent.LabelCap}: {recovery} HP restored. Remaining accumulated: {accumulatedDeterioration}");
                }
            }
            else
            {
                Log.Message($"[Temperature] Equipped {parent.LabelCap} at neutral temperature: no effect.");
            }
        }


        private float accumulatedRecovery = 0f; // Tracks fractional recovery across ticks

        public void ApplyTemperatureRecovery()
        {
            //if (parent == null || parent.Map == null)
                //return;

            // Resolve the map and position even when the item is worn
            Map map = parent.MapHeld ?? (parent.ParentHolder as Pawn)?.MapHeld;
            IntVec3 pos = parent.PositionHeld.IsValid ? parent.PositionHeld
                                                    : (parent.ParentHolder as Pawn)?.PositionHeld ?? IntVec3.Invalid;
            if (map == null || !pos.IsValid)
            {
                Log.Message($"[Temperature] Equipped {parent.LabelCap}: !pos.IsValid");
                return;   // still nothing we can work with                                       
            }
            if (!IsAiryaIceEquipment.IsIceEquipment(parent.def))
            {
                Log.Message($"[Temperature] Equipped {parent.LabelCap}: !IsAiryaIceEquipment.IsIceEquipment(parent.def)");
                return; // Skip non-ice weapons
            }

            // Get the current temperature
            float temperature = GenTemperature.GetTemperatureForCell(parent.Position, parent.Map);

            // Ensure the temperature is below 0 for recovery
            if (temperature < 0)
            {
                // Get the quality multiplier based on the deterioration rate factor
                float qualityMultiplier = GetQualityRecoveryFactor();

                // Recovery calculation with quality multiplier
                float recoveryRatePerTick = (Mathf.Abs(temperature) * 2 * qualityMultiplier) / 60000f; // Adjust recovery by quality
                accumulatedRecovery += recoveryRatePerTick;

                // Debug log for insights
                Log.Message($"[Harmony] Recovery Rate: {recoveryRatePerTick} HP/tick. Accumulated Recovery: {accumulatedRecovery}. Quality Multiplier: {qualityMultiplier}");

                // Apply recovery if accumulated value reaches 1 or more
                if (accumulatedRecovery >= 1f && parent.HitPoints < parent.MaxHitPoints)
                {
                    int recovery = Mathf.FloorToInt(accumulatedRecovery); // Recover whole HP points
                    accumulatedRecovery -= recovery; // Subtract applied recovery from the accumulation

                    parent.HitPoints = Mathf.Min(parent.MaxHitPoints, parent.HitPoints + recovery);
                    Log.Message($"[Harmony] Restored {recovery} HP to {parent.LabelCap}. Current HP: {parent.HitPoints}/{parent.MaxHitPoints}");
                }
            }
            else
            {
                // Reset accumulated recovery if temperature is not negative
                accumulatedRecovery = 0f;
                Log.Message($"[Harmony] No recovery applied. Temperature: {temperature}°C.");
            }
        }

        private float GetQualityRecoveryFactor()
        {
            var compQuality = parent.TryGetComp<CompQuality>();
            if (compQuality == null)
                return 1f; // Default to no multiplier if quality is not defined

            // Traditional switch statement for C# 7.3 and earlier
            switch (compQuality.Quality)
            {
                case QualityCategory.Awful:
                    return 0.5f;
                case QualityCategory.Poor:
                    return 0.66f;
                case QualityCategory.Normal:
                    return 1f;
                case QualityCategory.Good:
                    return 1.25f;
                case QualityCategory.Excellent:
                    return 1.66f; // (1 / 0.6)
                case QualityCategory.Masterwork:
                    return 3.33f; // (1 / 0.3)
                case QualityCategory.Legendary:
                    return 10f; // (1 / 0.1)
                default:
                    return 1f;
            }
        }

        public override void CompTick()
        {
            base.CompTick();
            /*
            if (parent == null || parent.Map == null)
                return;
            */
            ApplyTemperatureRecovery(); // Call the recovery logic
        }
    }
}