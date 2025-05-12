using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        // Constants for calculation
        private const float TicksPerDay = 60000f;
        private const float MELT_TEMP = 0f;
        private const float REINFORCED_TEMP = -20f;
        
        // Base rate constants
        private const float hourlyMeltingRatePerDegree = 0.01f; // 1% per hour per degree C
        private const float hourlyRegenRatePerDegree = 0.005f; // 0.5% per hour per degree C
        
        // Accumulation tracking for fractional changes
        private float accumulatedDeterioration = 0f;
        private float accumulatedRecovery = 0f;
        
        // Logging control
        private int logCounter = 0;
        private const int LOG_INTERVAL = 500; // Log every 500 ticks to avoid spam
        
        // Track the last time healing was applied
        private int lastHealTick = 0;
        
        // Debug flag - set to true for testing
        private const bool DEBUG_MODE = true;
        
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            
            // Log initialization
            if (DEBUG_MODE) Log.Message($"[AiryaSeraphs] MeltingRegen setup: {parent.LabelCap}, HP: {parent.HitPoints}/{parent.MaxHitPoints}");
            
            // Apply test damage if at full health for testing
            if (DEBUG_MODE && !respawningAfterLoad && parent.HitPoints == parent.MaxHitPoints)
            {
                parent.HitPoints = parent.MaxHitPoints - 5;
                Log.Message($"[DEBUG] Applied test damage to {parent.LabelCap}, HP now: {parent.HitPoints}/{parent.MaxHitPoints}");
            }
        }

        /// <summary>
        /// Get the ambient temperature for the item in any location
        /// </summary>
        private bool TryGetAmbientTemperature(out float ambientC)
        {
            ambientC = 20f; // default baseline
            
            // Case 1: Item directly on the map
            if (parent.MapHeld != null && parent.PositionHeld.IsValid)
            {
                ambientC = GenTemperature.GetTemperatureForCell(parent.PositionHeld, parent.MapHeld);
                return true;
            }
            
            // Case 2: Item in a storage building
            if (parent.ParentHolder is Building_Storage storage && storage.Map != null)
            {
                ambientC = GenTemperature.GetTemperatureForCell(storage.Position, storage.Map);
                return true;
            }
            
            // Case 3: Item carried as apparel
            if (parent is Apparel apparel && apparel.Wearer != null && apparel.Wearer.Spawned)
            {
                ambientC = GenTemperature.GetTemperatureForCell(apparel.Wearer.Position, apparel.Wearer.Map);
                return true;
            }
            
            // Case 4: Item carried as equipment
            if (parent.ParentHolder is Pawn_EquipmentTracker equipment && equipment.pawn != null && equipment.pawn.Spawned)
            {
                ambientC = GenTemperature.GetTemperatureForCell(equipment.pawn.Position, equipment.pawn.Map);
                return true;
            }
            
            // Case 5: Item in inventory
            if (parent.ParentHolder is Pawn_InventoryTracker inventory && inventory.pawn != null && inventory.pawn.Spawned)
            {
                ambientC = GenTemperature.GetTemperatureForCell(inventory.pawn.Position, inventory.pawn.Map);
                return true;
            }
            
            // Case 6: Direct pawn holder (fallback)
            if (parent.ParentHolder is Pawn pawn && pawn.Spawned)
            {
                ambientC = GenTemperature.GetTemperatureForCell(pawn.Position, pawn.Map);
                return true;
            }
            
            // Couldn't find valid temperature
            return false;
        }
        
        /// <summary>
        /// Calculate temp coefficient for regeneration/melting rates
        /// </summary>
        private float CalculateTempCoefficient(float temp)
        {
            if (temp >= 0f) return 1f; // Base multiplier at or above 0°C
            
            // At -20°C, coefficient reaches 2.0×
            if (temp <= -20f) return 2f;
            
            // Between 0°C and -20°C: linear progression for more realistic progression
            float normalizedTemp = Mathf.InverseLerp(0f, -20f, temp);
            return 1f + normalizedTemp; // Linear scale from 1.0 to 2.0
        }

        /// <summary>
        /// Apply temperature-based effects on EVERY tick for real-time responsiveness
        /// </summary>
        public void ApplyTemperatureEffects()
        {
            // Skip if not an ice item or invalid parent
            if (!IsAiryaIceEquipment.IsIceEquipment(parent.def) || parent == null || parent.Destroyed)
                return;
                
            // Get ambient temperature
            if (!TryGetAmbientTemperature(out float temperature))
                return;
                
            // Determine if we should log this tick
            bool shouldLog = DEBUG_MODE && (++logCounter >= LOG_INTERVAL);
            if (shouldLog) logCounter = 0;

            // Calculate temperature coefficient for rates
            float tempCoef = CalculateTempCoefficient(temperature);
            
            // Case 1: Hot temperature - melting
            if (temperature > 0)
            {
                // Skip if already at minimum HP
                if (parent.HitPoints <= 1)
                    return;
                    
                // Calculate melt rates
                float meltingPerDayPerDegree = hourlyMeltingRatePerDegree * 24f; // 24% per day per degree
                float deteriorationPerDay = temperature * meltingPerDayPerDegree * parent.MaxHitPoints;
                float deteriorationPerTick = deteriorationPerDay / TicksPerDay;
                
                // Add to accumulator
                accumulatedDeterioration += deteriorationPerTick;
                
                // Apply damage when accumulated enough
                if (accumulatedDeterioration >= 0.01f) // Lower threshold for responsiveness
                {
                    // Calculate damage as percentage of max HP
                    int damageAmount = Mathf.Max(1, Mathf.FloorToInt(accumulatedDeterioration));
                    
                    // Apply damage directly to HitPoints (minimum 1 HP)
                    int oldHP = parent.HitPoints;
                    parent.HitPoints = Mathf.Max(1, parent.HitPoints - damageAmount);
                    accumulatedDeterioration -= damageAmount;
                    
                    // Log on damage application
                    if (parent.HitPoints != oldHP)
                    {
                        Log.Message($"[AiryaSeraphs] {parent.LabelCap} melted: Lost {damageAmount} HP at {temperature:F1}°C. Now: {parent.HitPoints}/{parent.MaxHitPoints}");
                    }
                }
                
                // Debug logging
                if (shouldLog)
                {
                    Log.Message($"[AiryaSeraphs] {parent.LabelCap} at {temperature:F1}°C: Melting at {deteriorationPerTick*60000:F1} HP/day, accumulated: {accumulatedDeterioration:F3}");
                }
            }
            // Case 2: Cold temperature - regeneration
            else if (temperature < 0)
            {
                // Skip if already at maximum HP
                if (parent.HitPoints >= parent.MaxHitPoints)
                    return;
                    
                // Calculate regeneration rates
                float qualityFactor = GetQualityRecoveryFactor();
                float regenPerDayPerDegree = hourlyRegenRatePerDegree * 24f; // 12% per day per degree
                float regenPerDay = Mathf.Abs(temperature) * regenPerDayPerDegree * parent.MaxHitPoints * tempCoef * qualityFactor;
                float regenPerTick = regenPerDay / TicksPerDay;
                
                // Add to accumulator
                accumulatedRecovery += regenPerTick;
                
                // Apply healing when accumulated enough
                if (accumulatedRecovery >= 0.01f) // Lower threshold for responsiveness
                {
                    // Calculate healing (min 1 HP)
                    int healAmount = Mathf.Max(1, Mathf.FloorToInt(accumulatedRecovery));
                    
                    // Apply healing
                    int oldHP = parent.HitPoints;
                    parent.HitPoints = Mathf.Min(parent.MaxHitPoints, parent.HitPoints + healAmount);
                    accumulatedRecovery -= healAmount;
                    
                    // Record successful healing
                    if (parent.HitPoints != oldHP)
                    {
                        lastHealTick = Find.TickManager.TicksGame;
                        Log.Message($"[AiryaSeraphs] {parent.LabelCap} regenerated: Gained {healAmount} HP at {temperature:F1}°C. Now: {parent.HitPoints}/{parent.MaxHitPoints}");
                    }
                }
                
                // Debug logging
                if (shouldLog)
                {
                    Log.Message($"[AiryaSeraphs] {parent.LabelCap} at {temperature:F1}°C: Regenerating at {regenPerTick*60000:F1} HP/day (coef: {tempCoef:F1}, quality: {qualityFactor:F1}), accumulated: {accumulatedRecovery:F3}");
                }
            }
            // Case 3: Neutral temperature (exactly 0°C)
            else
            {
                // Reset accumulators at neutral temperature
                if (accumulatedDeterioration > 0) accumulatedDeterioration = 0f;
                if (accumulatedRecovery > 0) accumulatedRecovery = 0f;
                
                if (shouldLog) Log.Message($"[AiryaSeraphs] {parent.LabelCap} at neutral temperature: No effect");
            }
        }

        /// <summary>
        /// Get quality-based regeneration multiplier
        /// </summary>
        private float GetQualityRecoveryFactor()
        {
            var compQuality = parent.TryGetComp<CompQuality>();
            if (compQuality == null)
                return 2f; // Higher default for better regeneration
                
            // Quality scaling for regeneration speed
            switch (compQuality.Quality)
            {
                case QualityCategory.Awful:     return 1.0f;
                case QualityCategory.Poor:      return 1.5f;
                case QualityCategory.Normal:    return 2.0f;
                case QualityCategory.Good:      return 3.0f;
                case QualityCategory.Excellent: return 5.0f;
                case QualityCategory.Masterwork: return 8.0f;
                case QualityCategory.Legendary: return 15.0f;
                default: return 2.0f;
            }
        }
        
        /// <summary>
        /// Force healing for testing purposes
        /// </summary>
        public void ForceHeal(int amount = 5)
        {
            if (parent.HitPoints < parent.MaxHitPoints)
            {
                int oldHP = parent.HitPoints;
                parent.HitPoints = Mathf.Min(parent.MaxHitPoints, parent.HitPoints + amount);
                Log.Message($"[TEST] Forced healing on {parent.LabelCap}: {oldHP} → {parent.HitPoints}/{parent.MaxHitPoints}");
            }
            else
            {
                Log.Message($"[TEST] Can't heal {parent.LabelCap}: already at max HP ({parent.MaxHitPoints})");
            }
        }

        // Process EVERY TICK for maximum responsiveness
        public override void CompTick()
        {
            base.CompTick();
            
            // Process every single tick - no intervals
            ApplyTemperatureEffects();
        }
        
        // Update the inspection string to show the time until fully melted/regenerated
        public override string CompInspectStringExtra()
        {
            // Skip if not an ice item
            if (!IsAiryaIceEquipment.IsIceEquipment(parent.def))
                return null;
                    
            if (!TryGetAmbientTemperature(out float temperature))
                return "Temperature: Unknown";
            
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Temperature: {temperature:F1}°C");
            
            float tempCoef = CalculateTempCoefficient(temperature);
            
            if (temperature > 0)
            {
                float meltRate = temperature * hourlyMeltingRatePerDegree * parent.MaxHitPoints; // HP per hour
                float hoursToMelt = (parent.HitPoints - 1) / meltRate; // Time until unusable (1 HP)
                
                sb.AppendLine($"Melting: {meltRate:F1} HP/hour");
                
                if (hoursToMelt < 24)
                    sb.AppendLine($"Unusable in: {hoursToMelt:F1} hours");
                else
                    sb.AppendLine($"Unusable in: {hoursToMelt/24:F1} days");
                    
                if (accumulatedDeterioration > 0)
                {
                    float meltingPerDayPerDegree = hourlyMeltingRatePerDegree * 24f;
                    float deteriorationPerDay = temperature * meltingPerDayPerDegree * parent.MaxHitPoints;
                    float deteriorationPerTick = deteriorationPerDay / TicksPerDay;
                    sb.AppendLine($"Next damage in: {(1.0f - accumulatedDeterioration) / deteriorationPerTick / 2500:F1} hours");
                }
            }
            else if (temperature < 0)
            {
                float qualityFactor = GetQualityRecoveryFactor();
                float regenRate = Mathf.Abs(temperature) * hourlyRegenRatePerDegree * parent.MaxHitPoints * tempCoef * qualityFactor; // HP per hour
                
                if (parent.HitPoints < parent.MaxHitPoints)
                {
                    float hoursToFullHeal = (parent.MaxHitPoints - parent.HitPoints) / regenRate; // Time until full HP
                    
                    sb.AppendLine($"Regenerating: {regenRate:F1} HP/hour");
                    
                    if (hoursToFullHeal < 24)
                        sb.AppendLine($"Fully repaired in: {hoursToFullHeal:F1} hours");
                    else
                        sb.AppendLine($"Fully repaired in: {hoursToFullHeal/24:F1} days");
                        
                    if (accumulatedRecovery > 0)
                    {
                        float regenPerDayPerDegree = hourlyRegenRatePerDegree * 24f;
                        float regenPerDay = Mathf.Abs(temperature) * regenPerDayPerDegree * parent.MaxHitPoints * tempCoef * qualityFactor;
                        float regenPerTick = regenPerDay / TicksPerDay;
                        sb.AppendLine($"Next repair in: {(1.0f - accumulatedRecovery) / regenPerTick / 2500:F1} hours");
                    }
                }
                else
                {
                    sb.AppendLine($"Regenerating: {regenRate:F1} HP/hour (max HP)");
                }
                
                sb.AppendLine($"Temp boost: {tempCoef:F1}x, Quality: {qualityFactor:F1}x");
            }
                    
            sb.Append($"HP: {parent.HitPoints}/{parent.MaxHitPoints}");
            
            return sb.ToString();
        }
        
        public override void PostExposeData()
        {
            base.PostExposeData();
            
            // Save/load accumulated values
            Scribe_Values.Look(ref accumulatedDeterioration, "accumulatedDeterioration", 0f);
            Scribe_Values.Look(ref accumulatedRecovery, "accumulatedRecovery", 0f);
            Scribe_Values.Look(ref lastHealTick, "lastHealTick", 0);
            
            // Force immediate processing after loading
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Force immediate effect application after loading
                LongEventHandler.ExecuteWhenFinished(() => {
                    if (IsAiryaIceEquipment.IsIceEquipment(parent.def))
                    {
                        ApplyTemperatureEffects();
                        
                        if (DEBUG_MODE)
                        {
                            Log.Message($"[AiryaSeraphs] Restored MeltingRegen state for {parent.LabelCap}");
                            Log.Message($"  - HP: {parent.HitPoints}/{parent.MaxHitPoints}");
                        }
                    }
                });
            }
        }
    }
}