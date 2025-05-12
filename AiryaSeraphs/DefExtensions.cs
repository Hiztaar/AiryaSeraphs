using System;
using System.Collections.Generic;
using Verse;

namespace AiryaSeraphs
{
    // Extension class to identify ice equipment
    public class IsAiryaIceEquipment : DefModExtension
    {
        public bool AiryaIceEquipment = false;
        
        // Helper method to check if a def is ice equipment
        public static bool IsIceEquipment(ThingDef def)
        {
            if (def == null) return false;
            
            var extension = def.GetModExtension<IsAiryaIceEquipment>();
            return extension != null && extension.AiryaIceEquipment;
        }
    }
}

/*
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace AiryaSeraphs
{
    public class IsAiryaIceEquipment : DefModExtension
    {
        public bool AiryaIceEquipment = false;

        public static bool IsIceEquipment(ThingDef def)
        {
            return def.HasModExtension<IsAiryaIceEquipment>() && def.GetModExtension<IsAiryaIceEquipment>().AiryaIceEquipment;
        }
    }
}*/