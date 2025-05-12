using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection; // For reflection methods
using RimWorld;
using UnityEngine;
using Verse;

namespace AiryaSeraphs
{
    public class CompProperties_TemperatureDependentStats : CompProperties
    {
        // Base armor ratings for armors/helmets
        public float BaseArmorRating_Sharp = 0f;
        public float BaseArmorRating_Blunt = 0f; 
        public float BaseArmorRating_Heat = 0f;
        
        // Texture paths for different states
        public string normalTex = "";
        public string reinforcedTex = "";
        public string damagedTex = "";
        
        public CompProperties_TemperatureDependentStats()
        {
            this.compClass = typeof(CompTemperatureDependentStats);
        }
    }

    /// <summary>
    /// Simple subclass so XML can include <li Class="AiryaSeraphs.ExtraDamageWithBase">.
    /// base_amount holds the designer's baseline damage, but CompTemperatureDependentStats
    /// now caches ed.amount directly, so this field is optional.
    /// </summary>
    public class ExtraDamageWithBase : ExtraDamage
    {
        public float base_amount = -1f; // RimWorld populates this from XML if present
    }

    public class CompTemperatureDependentStats : ThingComp
    {
        /* ---------------- cached data ---------------- */

        // Frost‑bite ExtraDamage entry → its immutable base value
        private readonly Dictionary<ExtraDamage, float> baseFrost = new Dictionary<ExtraDamage, float>();

        // per‑entry fractional buffer so we can add / subtract < 1 pt per tick
        private readonly Dictionary<ExtraDamage, float> fracBuffer = new Dictionary<ExtraDamage, float>();
        
        // Base armor values for apparel
        private float baseArmorRating_Sharp = 0f;
        private float baseArmorRating_Blunt = 0f;
        private float baseArmorRating_Heat = 0f;
        
        // Current appearance state tracking
        private AppearanceState currentState = AppearanceState.Normal;
        
        // Tick counter for performance optimization
        private int tickCounter = 0;
        private const int UPDATE_INTERVAL = 60; // Update every 60 ticks
        
        // Debug flag
        private const bool DEBUG_MODE = true;
        
        // Constants
        private const float MELT_TEMP = 0f;
        private const float REINFORCED_TEMP = -20f;
        private const float MAX_TEMP_EFFECT = -30f;
        private const float REINFORCED_MULTIPLIER = 2.0f;
        private const float MAX_MULTIPLIER = 3.0f;
        
        // Cached current multiplier
        private float currentMultiplier = 1.0f;
        
        // Current appearance state enum
        private enum AppearanceState
        {
            Normal,
            Reinforced,
            Damaged
        }
        
        // Properties for easier access
        public CompProperties_TemperatureDependentStats Props => props as CompProperties_TemperatureDependentStats;
        private Apparel ParentAsApparel => parent as Apparel;
        private bool IsApparel => ParentAsApparel != null;

        /* -------------- init / cache ----------------- */

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);

            // Initialize weapon damage caching
            if (parent is ThingWithComps eq && !eq.def.tools.NullOrEmpty())
            {
                foreach (var tool in eq.def.tools)
                {
                    if (tool.extraMeleeDamages.NullOrEmpty()) continue;

                    foreach (var ex in tool.extraMeleeDamages)
                    {
                        if (ex.def != DamageDefOf.Frostbite) continue;

                        if (!baseFrost.ContainsKey(ex))
                        {
                            baseFrost[ex] = ex.amount;  // Store as float
                            fracBuffer[ex] = 0f;        // start buffer at 0
                            
                            if (DEBUG_MODE) Log.Message($"[Init] {parent.LabelCap}: Cached Frostbite damage: {ex.amount}");
                        }
                    }
                }
            }
            
            // Initialize armor base values
            if (IsApparel && Props != null)
            {
                baseArmorRating_Sharp = Props.BaseArmorRating_Sharp;
                baseArmorRating_Blunt = Props.BaseArmorRating_Blunt;
                baseArmorRating_Heat = Props.BaseArmorRating_Heat;
                
                if (DEBUG_MODE) Log.Message($"[Init] {parent.LabelCap}: Cached armor values - Sharp: {baseArmorRating_Sharp}, Blunt: {baseArmorRating_Blunt}, Heat: {baseArmorRating_Heat}");
            }
        }

        /* ---------------- per‑tick work --------------- */

        // run each normal tick; inexpensive (only a few maths)
        public override void CompTick()
        {
            base.CompTick();
            
            // Use tick counter for performance
            tickCounter++;
            if (tickCounter < UPDATE_INTERVAL)
            {
                if (DEBUG_MODE) Log.Message($"[Temperature Dependent tickCounter] {parent.LabelCap}: {tickCounter}/{UPDATE_INTERVAL}");
                return;
            }
            tickCounter = 0;
            
            // Skip if not an ice item
            if (!IsAiryaIceEquipment.IsIceEquipment(parent.def))
            {
                if (DEBUG_MODE) Log.Message($"[IsAiryaIceEquipment] {parent.LabelCap}: false");
                return;
            }
            // Get current temperature for the item
            if (!TryGetAmbientTemperature(out float temperature))
            {
                if (DEBUG_MODE) Log.Message($"[TryGetAmbientTemperature] {parent.LabelCap}: {temperature}");
                return;
            }
                
            // Calculate multiplier based on temperature
            float mult = CalculateMultiplier(temperature);
            currentMultiplier = mult;
            
            if (DEBUG_MODE) Log.Message($"[Tick] {parent.LabelCap}: Temp={temperature:F1}°C, Mult={mult:F2}x");
            
            // Update weapon frostbite damage
            UpdateWeaponFrostbiteDamage(mult);
            
            // Update armor stats
            UpdateArmorStats(mult);
            
            // Update appearance based on temperature and health
            UpdateAppearance(temperature);
        }
        
        /// <summary>
        /// Gets the ambient temperature for the item, whether it's on the ground, in storage, or equipped/worn
        /// </summary>
        private bool TryGetAmbientTemperature(out float ambientC)
        {
            ambientC = 20f; // default baseline
            
            // Case 1: Item directly on the map
            if (parent.MapHeld != null && parent.PositionHeld.IsValid)
            {
                ambientC = GenTemperature.GetTemperatureForCell(parent.PositionHeld, parent.MapHeld);
                if (DEBUG_MODE) Log.Message($"[Temp] {parent.LabelCap}: On ground at {ambientC:F1}°C");
                return true;
            }
            
            // Case 2: Item in a storage building
            if (parent.ParentHolder is Building_Storage storage && storage.Map != null)
            {
                ambientC = GenTemperature.GetTemperatureForCell(storage.Position, storage.Map);
                if (DEBUG_MODE) Log.Message($"[Temp] {parent.LabelCap}: In storage at {ambientC:F1}°C");
                return true;
            }
            
            // Case 3: Item carried as apparel
            if (parent is Apparel apparel && apparel.Wearer != null && apparel.Wearer.Spawned)
            {
                ambientC = GenTemperature.GetTemperatureForCell(apparel.Wearer.Position, apparel.Wearer.Map);
                if (DEBUG_MODE) Log.Message($"[Temp] {parent.LabelCap}: Worn by {apparel.Wearer.LabelCap} at {ambientC:F1}°C");
                return true;
            }
            
            // Case 4: Item carried as equipment
            if (parent.ParentHolder is Pawn_EquipmentTracker equipment && equipment.pawn != null && equipment.pawn.Spawned)
            {
                ambientC = GenTemperature.GetTemperatureForCell(equipment.pawn.Position, equipment.pawn.Map);
                if (DEBUG_MODE) Log.Message($"[Temp] {parent.LabelCap}: Equipped by {equipment.pawn.LabelCap} at {ambientC:F1}°C");
                return true;
            }
            
            // Case 5: Item in inventory
            if (parent.ParentHolder is Pawn_InventoryTracker inventory && inventory.pawn != null && inventory.pawn.Spawned)
            {
                ambientC = GenTemperature.GetTemperatureForCell(inventory.pawn.Position, inventory.pawn.Map);
                if (DEBUG_MODE) Log.Message($"[Temp] {parent.LabelCap}: In inventory at {ambientC:F1}°C");
                return true;
            }
            
            // Case 6: Direct pawn holder (fallback)
            if (parent.ParentHolder is Pawn pawn && pawn.Spawned)
            {
                ambientC = GenTemperature.GetTemperatureForCell(pawn.Position, pawn.Map);
                if (DEBUG_MODE) Log.Message($"[Temp] {parent.LabelCap}: Held by pawn at {ambientC:F1}°C");
                return true;
            }
            
            // Couldn't find valid temperature
            if (DEBUG_MODE) Log.Message($"[Temp] {parent.LabelCap}: No valid temperature found");
            return false;
        }
        
        /// <summary>
        /// Calculate multiplier based on temperature using logarithmic curve
        /// </summary>
        private float CalculateMultiplier(float tC)
        {
            if (tC >= 0) return 1.0f; // Base multiplier at or above 0°C
            
            float normalizedTemp;
            float result;
            
            if (tC > REINFORCED_TEMP) // Between 0°C and -20°C
            {
                // Normalize temperature to 0-1 range
                normalizedTemp = Mathf.InverseLerp(0f, REINFORCED_TEMP, tC);
                
                // Apply logarithmic curve for faster increase near -20°C
                result = 1.0f + (REINFORCED_MULTIPLIER - 1.0f) * 
                       Mathf.Log10(1 + 9f * normalizedTemp);
            }
            else // Between -20°C and lower
            {
                // Normalize temperature 
                normalizedTemp = Mathf.InverseLerp(REINFORCED_TEMP, MAX_TEMP_EFFECT, tC);
                if (normalizedTemp > 1f) normalizedTemp = 1f; // Cap at 1.0
                
                // Apply inverted logarithmic curve for slower increase at extreme cold
                float inverseCurve = 1f - Mathf.Log10(1 + 9f * (1f - normalizedTemp));
                result = REINFORCED_MULTIPLIER + (MAX_MULTIPLIER - REINFORCED_MULTIPLIER) * 
                       (1f - inverseCurve);
            }
            
            return Mathf.Clamp(result, 1f, MAX_MULTIPLIER);
        }
        
        /// <summary>
        /// Update weapon Frostbite damage based on temperature multiplier
        /// </summary>
        private void UpdateWeaponFrostbiteDamage(float temperature)
        {
            if (baseFrost.Count == 0) return; // No Frostbite damage to update
            
            // For weapons above 0°C, reset Frostbite to base value
            bool resetToBase = temperature >= 0f;
            
            // Calculate temperature coefficient
            float tempCoef = CalculateMultiplier(temperature);
            
            // Walk through all cached Frostbite damage entries
            foreach (var kvp in baseFrost)
            {
                ExtraDamage ex = kvp.Key;
                float baseAmt = kvp.Value;
                
                // Target value based on temperature coefficient
                float targetValue = resetToBase ? baseAmt : baseAmt * tempCoef;
                
                // Current value
                float currentValue = ex.amount;
                
                // Skip if already at target value
                if (Math.Abs(currentValue - targetValue) < 0.01f) continue;
                
                // Calculate change direction
                bool increasing = targetValue > currentValue;
                
                // Calculate change amount - 1% of original per tick when cold
                float changeAmount;
                
                if (increasing)
                {
                    // When getting colder - 1% of base value * coefficient
                    changeAmount = baseAmt * 0.01f * tempCoef;
                    
                    // Make sure we don't exceed target
                    currentValue = Math.Min(targetValue, currentValue + changeAmount);
                }
                else
                {
                    // When getting warmer - slower decrease 
                    // Inverted coefficient: higher = slower decrease
                    float decreaseCoef = 1f + (tempCoef - 1f) * 0.5f; // Halve the coefficient impact
                    changeAmount = baseAmt * 0.01f * decreaseCoef;
                    
                    // Make sure we don't go below target
                    currentValue = Math.Max(targetValue, currentValue - changeAmount);
                }
                
                // Apply the new value
                ex.amount = currentValue;
                
                if (DEBUG_MODE && Find.TickManager.TicksGame % 250 == 0)
                {
                    Log.Message($"[Weapon] {parent.LabelCap}: Frostbite dmg updated to {ex.amount:F2} (target: {targetValue:F2}, base: {baseAmt:F2})");
                }
            }
        }

        
        /// <summary>
        /// Update armor stats based on temperature multiplier
        /// </summary>
        // 3. Update the armor stats calculation method
        private void UpdateArmorStats(float temperature)
        {
            // Skip if not apparel or no props
            if (!IsApparel || Props == null) return;
            
            // Calculate temperature coefficient
            float tempCoef = CalculateMultiplier(temperature);
            
            // Update the armor stats based on the multiplier
            StatDef[] statDefs = { StatDefOf.ArmorRating_Sharp, StatDefOf.ArmorRating_Blunt, StatDefOf.ArmorRating_Heat };
            float[] baseValues = { baseArmorRating_Sharp, baseArmorRating_Blunt, baseArmorRating_Heat };
            
            for (int i = 0; i < statDefs.Length; i++)
            {
                if (baseValues[i] > 0)
                {
                    // Current value
                    float currentValue = parent.GetStatValue(statDefs[i]);
                    
                    // Target value based on temperature coefficient
                    float targetValue = baseValues[i] * tempCoef;
                    
                    // Skip if already at target value
                    if (Math.Abs(currentValue - targetValue) < 0.01f) continue;
                    
                    // Calculate change direction
                    bool increasing = targetValue > currentValue;
                    
                    // Calculate change amount - 1% of original per tick
                    float changeAmount;
                    
                    if (increasing)
                    {
                        // When getting colder - 1% of base value * coefficient
                        changeAmount = baseValues[i] * 0.01f * tempCoef;
                        
                        // Make sure we don't exceed target
                        currentValue = Math.Min(targetValue, currentValue + changeAmount);
                    }
                    else
                    {
                        // When getting warmer - slower decrease
                        // Inverted coefficient: higher = slower decrease
                        float decreaseCoef = 1f + (tempCoef - 1f) * 0.5f; // Halve the coefficient impact
                        changeAmount = baseValues[i] * 0.01f * decreaseCoef;
                        
                        // Make sure we don't go below target
                        currentValue = Math.Max(targetValue, currentValue - changeAmount);
                    }
                    
                    // Update the stat value
                    parent.def.SetStatBaseValue(statDefs[i], currentValue);
                    
                    if (DEBUG_MODE && Find.TickManager.TicksGame % 250 == 0)
                    {
                        Log.Message($"[Armor] {parent.LabelCap}: {statDefs[i].defName} updated to {currentValue:F2} (target: {targetValue:F2}, base: {baseValues[i]:F2})");
                    }
                    
                    // Force refresh of stats on wearer if possible
                    if (ParentAsApparel?.Wearer != null)
                    {
                        // Try to refresh stats
                        MethodInfo refreshMethod = typeof(Pawn).GetMethod("ClearCaches", 
                            BindingFlags.Instance | BindingFlags.NonPublic);
                        if (refreshMethod != null)
                        {
                            refreshMethod.Invoke(ParentAsApparel.Wearer, null);
                        }
                    }
                }
            }
        }

        
        /// <summary>
        /// Update appearance based on temperature and health
        /// </summary>
        private void UpdateAppearance(float temperature)
        {
            // Skip if no texture paths defined
            if (Props == null || string.IsNullOrEmpty(Props.normalTex) || 
                string.IsNullOrEmpty(Props.reinforcedTex) || 
                string.IsNullOrEmpty(Props.damagedTex)) return;
            
            // Determine appearance state based on effective resistance values
            AppearanceState newState;
            
            // For apparel, check actual armor values vs base values
            if (IsApparel)
            {
                // Check if any armor rating has actually reached 2x its base value
                bool hasReachedReinforcedResistance = false;
                
                if (baseArmorRating_Sharp > 0)
                {
                    float currentSharp = parent.GetStatValue(StatDefOf.ArmorRating_Sharp);
                    if (currentSharp >= baseArmorRating_Sharp * REINFORCED_MULTIPLIER)
                        hasReachedReinforcedResistance = true;
                }
                
                if (baseArmorRating_Blunt > 0 && !hasReachedReinforcedResistance)
                {
                    float currentBlunt = parent.GetStatValue(StatDefOf.ArmorRating_Blunt);
                    if (currentBlunt >= baseArmorRating_Blunt * REINFORCED_MULTIPLIER)
                        hasReachedReinforcedResistance = true;
                }
                
                if (baseArmorRating_Heat > 0 && !hasReachedReinforcedResistance)
                {
                    float currentHeat = parent.GetStatValue(StatDefOf.ArmorRating_Heat);
                    if (currentHeat >= baseArmorRating_Heat * REINFORCED_MULTIPLIER)
                        hasReachedReinforcedResistance = true;
                }
                
                // For reinforced state: need cold AND actual resistance at 2x
                if (temperature <= REINFORCED_TEMP && hasReachedReinforcedResistance)
                {
                    newState = AppearanceState.Reinforced;
                }
                // For damaged state: warm and below 50% health
                else if (temperature > MELT_TEMP && parent.HitPoints < parent.MaxHitPoints * 0.5f)
                {
                    newState = AppearanceState.Damaged;
                }
                // Otherwise normal
                else
                {
                    newState = AppearanceState.Normal;
                }
            }
            // For weapons, just use the multiplier directly
            else
            {
                // For reinforced state: need cold AND multiplier at 2x
                if (temperature <= REINFORCED_TEMP && currentMultiplier >= REINFORCED_MULTIPLIER)
                {
                    newState = AppearanceState.Reinforced;
                }
                // For damaged state: warm and below 50% health
                else if (temperature > MELT_TEMP && parent.HitPoints < parent.MaxHitPoints * 0.5f)
                {
                    newState = AppearanceState.Damaged;
                }
                // Otherwise normal
                else
                {
                    newState = AppearanceState.Normal;
                }
            }
            
            // Only update if state changed
            if (newState != currentState)
            {
                if (DEBUG_MODE)
                {
                    if (IsApparel)
                    {
                        Log.Message($"[Appearance] {parent.LabelCap} changing from {currentState} to {newState}");
                        if (baseArmorRating_Sharp > 0)
                            Log.Message($"  - Sharp: {parent.GetStatValue(StatDefOf.ArmorRating_Sharp):F2} (base: {baseArmorRating_Sharp:F2}, needed: {baseArmorRating_Sharp * REINFORCED_MULTIPLIER:F2})");
                        if (baseArmorRating_Blunt > 0)
                            Log.Message($"  - Blunt: {parent.GetStatValue(StatDefOf.ArmorRating_Blunt):F2} (base: {baseArmorRating_Blunt:F2}, needed: {baseArmorRating_Blunt * REINFORCED_MULTIPLIER:F2})");
                        if (baseArmorRating_Heat > 0)
                            Log.Message($"  - Heat: {parent.GetStatValue(StatDefOf.ArmorRating_Heat):F2} (base: {baseArmorRating_Heat:F2}, needed: {baseArmorRating_Heat * REINFORCED_MULTIPLIER:F2})");
                    }
                    else
                    {
                        Log.Message($"[Appearance] {parent.LabelCap} changing from {currentState} to {newState} (multiplier: {currentMultiplier:F2})");
                    }
                }
                
                currentState = newState;
                UpdateTexture();
            }
        }
        
        /// <summary>
        /// Update the texture based on current state
        /// </summary>
        private void UpdateTexture()
        {
            string texPath = GetTexturePath();
            if (string.IsNullOrEmpty(texPath)) return;
            
            // Update graphic data using a different approach
            if (parent.Graphic != null && parent.Graphic.path != texPath)
            {
                try
                {
                    // Create a new graphic
                    Graphic newGraphic = GraphicDatabase.Get(
                        parent.def.graphicData.graphicClass,
                        texPath,
                        parent.Graphic.Shader,
                        parent.def.graphicData.drawSize,
                        parent.DrawColor,
                        parent.DrawColorTwo);
                    
                    // Use proper method to change graphic
                    typeof(Thing).GetField("graphicInt", 
                                         BindingFlags.NonPublic | 
                                         BindingFlags.Instance)?.SetValue(parent, newGraphic);
                    
                    // Update apparel graphics on pawn if needed
                    if (IsApparel && ParentAsApparel.Wearer != null && !ParentAsApparel.Wearer.Dead)
                    {
                        // Use this method which exists in RimWorld
                        ParentAsApparel.Wearer.Drawer.renderer.SetAllGraphicsDirty();
                        
                        // Request portrait update
                        PortraitsCache.SetDirty(ParentAsApparel.Wearer);
                        
                        if (DEBUG_MODE) Log.Message($"[Graphic] Updated graphics for {parent.LabelCap} on {ParentAsApparel.Wearer.LabelCap}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Error updating texture for {parent.LabelCap}: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Get the appropriate texture path based on current state
        /// </summary>
        private string GetTexturePath()
        {
            switch (currentState)
            {
                case AppearanceState.Reinforced:
                    return Props.reinforcedTex;
                case AppearanceState.Damaged:
                    return Props.damagedTex;
                default:
                    return Props.normalTex;
            }
        }

        // Fixed method to return a string instead of IEnumerable<string>
        public override string CompInspectStringExtra()
        {
            StringBuilder sb = new StringBuilder();
            
            // Try to get current temperature
            if (TryGetAmbientTemperature(out float temperature))
                sb.AppendLine($"Temperature: {temperature:F1}°C");
                
            sb.AppendLine($"Effect multiplier: {currentMultiplier:F2}x");
            sb.AppendLine($"Appearance: {currentState}");
                
            // Show armor stats for apparel
            if (IsApparel && Props != null)
            {
                if (baseArmorRating_Sharp > 0)
                    sb.AppendLine($"Sharp armor: {parent.GetStatValue(StatDefOf.ArmorRating_Sharp):F2} (base: {baseArmorRating_Sharp:F2})");
                
                if (baseArmorRating_Blunt > 0)
                    sb.AppendLine($"Blunt armor: {parent.GetStatValue(StatDefOf.ArmorRating_Blunt):F2} (base: {baseArmorRating_Blunt:F2})");
                
                if (baseArmorRating_Heat > 0)
                    sb.AppendLine($"Heat armor: {parent.GetStatValue(StatDefOf.ArmorRating_Heat):F2} (base: {baseArmorRating_Heat:F2})");
            }
            // Show Frostbite damage for weapons
            else if (baseFrost.Count > 0)
            {
                foreach (var kvp in baseFrost)
                {
                    sb.AppendLine($"Frostbite dmg: {kvp.Key.amount:F2} (base: {kvp.Value:F2})");
                    break; // only show one line
                }
            }
            
            return sb.ToString().TrimEndNewlines();
        }
        
        // 1. First, enhance the PostExposeData method in CompTemperatureDependentStats:

        public override void PostExposeData()
        {
            base.PostExposeData();
            
            // Save/load appearance state
            Scribe_Values.Look(ref currentState, "currentState", AppearanceState.Normal);
            
            // Save/load current multiplier
            Scribe_Values.Look(ref currentMultiplier, "currentMultiplier", 1.0f);
            
            // Save/load base armor values 
            Scribe_Values.Look(ref baseArmorRating_Sharp, "baseArmorRating_Sharp", 0f);
            Scribe_Values.Look(ref baseArmorRating_Blunt, "baseArmorRating_Blunt", 0f);
            Scribe_Values.Look(ref baseArmorRating_Heat, "baseArmorRating_Heat", 0f);
            
            // Save the fractional buffers for weapon damage
            if (baseFrost.Count > 0)
            {
                // We can't directly save dictionaries with ExtraDamage as keys
                // So we'll convert to string identifiers based on tool+damage type
                
                // Save counts first to know how many to load
                int frostCount = baseFrost.Count;
                Scribe_Values.Look(ref frostCount, "frostDamageCount", 0);
                
                if (Scribe.mode == LoadSaveMode.Saving)
                {
                    // Saving - convert dictionaries to lists for storage
                    List<float> baseValues = new List<float>();
                    List<float> currentValues = new List<float>();
                    List<float> fracValues = new List<float>();
                    
                    foreach (var kvp in baseFrost)
                    {
                        baseValues.Add(kvp.Value);
                        currentValues.Add(kvp.Key.amount);
                        fracValues.Add(fracBuffer[kvp.Key]);
                    }
                    
                    // Save the lists
                    Scribe_Collections.Look(ref baseValues, "frostBaseValues", LookMode.Value);
                    Scribe_Collections.Look(ref currentValues, "frostCurrentValues", LookMode.Value);
                    Scribe_Collections.Look(ref fracValues, "frostFractionValues", LookMode.Value);
                }
                else if (Scribe.mode == LoadSaveMode.LoadingVars)
                {
                    // Loading - prepare lists
                    List<float> baseValues = null;
                    List<float> currentValues = null;
                    List<float> fracValues = null;
                    
                    // Load the lists
                    Scribe_Collections.Look(ref baseValues, "frostBaseValues", LookMode.Value);
                    Scribe_Collections.Look(ref currentValues, "frostCurrentValues", LookMode.Value);
                    Scribe_Collections.Look(ref fracValues, "frostFractionValues", LookMode.Value);
                    
                    // Rebuild dictionaries if we have valid data
                    if (baseValues != null && currentValues != null && fracValues != null &&
                        baseValues.Count == frostCount && currentValues.Count == frostCount && fracValues.Count == frostCount)
                    {
                        // Clear existing data (will be rebuilt from the XML anyway)
                        baseFrost.Clear();
                        fracBuffer.Clear();
                        
                        // We'll restore values once all components are loaded and tools are initialized
                        // Store the values for application in PostLoadGame
                        loadedBaseValues = baseValues;
                        loadedCurrentValues = currentValues;
                        loadedFracValues = fracValues;
                        
                        // Force immediate application of values after loading
                        LongEventHandler.ExecuteWhenFinished(ApplyLoadedValues);
                    }
                }
            }
            
            // For items in the world when loading, ensure their state is visible
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Force texture updates for loaded items
                if (currentState != AppearanceState.Normal)
                {
                    UpdateTexture();
                }
                
                // Force a component tick to restore stats
                tickCounter = 0;
                LongEventHandler.ExecuteWhenFinished(() => CompTick());
            }
        }

        // Add these fields to store loaded values
        private List<float> loadedBaseValues = null;
        private List<float> loadedCurrentValues = null;
        private List<float> loadedFracValues = null;

        // Add this method to apply loaded values
        private void ApplyLoadedValues()
        {
            try
            {
                if (loadedBaseValues == null || loadedCurrentValues == null || loadedFracValues == null)
                    return;
                    
                if (parent == null || parent.def == null || parent.def.tools == null)
                    return;
                    
                // Find all Frostbite damage entries
                int index = 0;
                foreach (var tool in parent.def.tools)
                {
                    if (tool.extraMeleeDamages == null) continue;
                    
                    foreach (var ex in tool.extraMeleeDamages)
                    {
                        if (ex.def != DamageDefOf.Frostbite) continue;
                        
                        if (index < loadedBaseValues.Count)
                        {
                            // Store base value
                            baseFrost[ex] = loadedBaseValues[index];
                            
                            // Restore actual damage value
                            ex.amount = loadedCurrentValues[index];
                            
                            // Restore fractional buffer
                            fracBuffer[ex] = loadedFracValues[index];
                            
                            // Move to next values
                            index++;
                        }
                    }
                }
                
                if (DEBUG_MODE)
                {
                    Log.Message($"[AiryaSeraphs] Restored {index} Frostbite damage values for {parent.LabelCap}");
                    foreach (var kvp in baseFrost)
                    {
                        Log.Message($"  - Base: {kvp.Value}, Current: {kvp.Key.amount}, Buffer: {fracBuffer[kvp.Key]}");
                    }
                }
                
                // Clear loaded values to free memory
                loadedBaseValues = null;
                loadedCurrentValues = null;
                loadedFracValues = null;
            }
            catch (Exception ex)
            {
                Log.Error($"[AiryaSeraphs] Error applying loaded values: {ex}");
            }
        }
    }
}