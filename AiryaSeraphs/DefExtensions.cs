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
}