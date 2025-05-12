using System;
using System.Collections.Generic;
using System.Reflection; // For reflection methods
using RimWorld;
using UnityEngine;
using Verse;

[StaticConstructorOnStartup]
public class AiryaIceEquipmentWorldComponent : WorldComponent
{
    public AiryaIceEquipmentWorldComponent(World world) : base(world)
    {
    }
    
    public override void FinalizeInit()
    {
        base.FinalizeInit();
        
        // Update all ice equipment once the world is fully loaded
        LongEventHandler.ExecuteWhenFinished(UpdateAllIceEquipment);
    }
    
    private void UpdateAllIceEquipment()
    {
        // Find all maps
        foreach (Map map in Find.Maps)
        {
            // Process all things on each map
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing is ThingWithComps thingWithComps && 
                    IsAiryaIceEquipment.IsIceEquipment(thingWithComps.def))
                {
                    // Force component ticks
                    var regenComp = thingWithComps.GetComp<CompTemperatureMeltingRegen>();
                    var statsComp = thingWithComps.GetComp<CompTemperatureDependentStats>();
                    
                    if (regenComp != null) regenComp.CompTick();
                    if (statsComp != null) statsComp.CompTick();
                    
                    // Force texture updates if needed
                    if (statsComp != null)
                    {
                        // Access private field using reflection (you'll need to implement this)
                        AppearanceState currentState = GetAppearanceState(statsComp);
                        if (currentState != AppearanceState.Normal)
                        {
                            // Force method using reflection
                            typeof(CompTemperatureDependentStats)
                                .GetMethod("UpdateTexture", BindingFlags.NonPublic | BindingFlags.Instance)
                                ?.Invoke(statsComp, null);
                        }
                    }
                }
            }
        }
        
        Log.Message("[AiryaSeraphs] All ice equipment updated after game load");
    }
    
    // Helper method to access private field using reflection
    private AppearanceState GetAppearanceState(CompTemperatureDependentStats comp)
    {
        try
        {
            var field = typeof(CompTemperatureDependentStats)
                .GetField("currentState", BindingFlags.NonPublic | BindingFlags.Instance);
                
            if (field != null)
            {
                return (AppearanceState)field.GetValue(comp);
            }
        }
        catch {}
        
        return AppearanceState.Normal;
    }
}