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

    // Frost‑bite ExtraDamage entry  → its immutable base value
    private readonly Dictionary<ExtraDamage, int> baseFrost = new();

    // per‑entry fractional buffer so we can add / subtract < 1 pt per tick
    private readonly Dictionary<ExtraDamage, float> fracBuffer = new();

    /* -------------- init / cache ----------------- */

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);

            if (parent is not ThingWithComps eq || eq.def.Verbs.NullOrEmpty()) return;

            foreach (var verb in eq.def.Verbs)
            {
                if (verb.extraMeleeDamages is null) continue;

                foreach (var ex in verb.extraMeleeDamages)
                {
                    if (ex.def != DamageDefOf.Frostbite) continue;

                    if (!baseFrost.ContainsKey(ex))
                    {
                        baseFrost[ex]  = ex.amount;  // remember XML value
                        fracBuffer[ex] = 0f;         // start buffer at 0
                    }
                }
            }
        }

        /* ---------------- per‑tick work --------------- */

        // run each normal tick; inexpensive (only a few maths)
        public override void CompTick()
        {
            if (baseFrost.Count == 0) return;                   // no frostbite entry
            if (parent.ParentHolder is not Pawn wielder
                || !wielder.Spawned) return;                    // not wielded on map

            /* 1. temperature & multiplier */
            float temp   = GenTemperature.GetTemperatureForCell(wielder.Position,
                                                                wielder.Map);
            float mult   = TempMultiplier(temp);                // 1 … 3   (0 above 0 °C)
            int   ticksPerHour = 2500;                          // vanilla constant

            /* 2. walk through every cached extra‑damage entry */
            foreach (var kvp in baseFrost)
            {
                ExtraDamage ex   = kvp.Key;
                int   baseAmt    = kvp.Value;
                float target     = baseAmt * mult;              // where we *should* be
                float diff       = target - ex.amount;          // signed delta
                if (Mathf.Approximately(diff, 0f)) continue;    // already at target

                /* 2a. calculate per‑tick step (+ or −) */
                float stepPerHour = mult;                       // rule: +1×mult per hour
                float stepPerTick = stepPerHour / ticksPerHour;

                // direction
                stepPerTick *= Mathf.Sign(diff);

                /* 2b. accumulate fractional part, apply whenever we pass ±1 */
                fracBuffer[ex] += stepPerTick;
                int deltaWhole  = (int)fracBuffer[ex];          // truncate toward 0

                if (deltaWhole != 0)
                {
                    int newAmount = ex.amount + deltaWhole;

                    // clamp so we never overshoot
                    if (diff > 0) newAmount = Mathf.Min(newAmount, (int)target);
                    else          newAmount = Mathf.Max(newAmount, (int)target);

                    ex.amount     = newAmount;
                    fracBuffer[ex] -= deltaWhole;               // keep remainder
                }
            }
        }

        /* ---------- helper: temp → multiplier --------- */

        ///  0 °C or above → 0 (no frostbite)
        /// -20 °C → ×2   (base rule)
        /// floor at 1, cap at 3
        private static float TempMultiplier(float tC)
        {
            if (tC >= 0) return 0f;

            const float baseLog = 20f;        // -20 gives ×2
            const float maxMult = 3f;

            float raw = 1f + (Mathf.Log(Mathf.Abs(tC) + 1, 10f) /
                            Mathf.Log(baseLog, 10f)) * 2f;

            return Mathf.Clamp(raw, 1f, maxMult);
        }

        // Add this helper method to validate override
        public override IEnumerable<string> CompInspectStringExtra()
        {
            if (baseFrost.Count == 0)
                yield break;

            foreach (var kvp in baseFrost)
            {
                yield return $"Frostbite dmg: {kvp.Key.amount} (base: {kvp.Value})";
                yield break; // only show one line
            }
        }
    }
}
