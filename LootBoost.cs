using UnityEngine;
using Il2Cpp;

namespace SkyCoop
{
    // Co-op splits one world's loot between the players, so online there is more of it: containers roll more
    // items and are empty less often, and random spawn groups (one of several spots holds the item) enable more
    // of their objects. Nothing is duplicated, every extra item is a normal one that only one player can take.
    //
    // The fractional part is rounded up or down by a roll seeded from the world seed and the object position,
    // so every player gets the same result: 1 item x1.5 is 2 items in about half of the places.
    public static class LootBoost
    {
        private static float Multiplier
        {
            get { return CoopFeatures.LootMultiplier; }
        }

        private static int Seed(Component c)
        {
            Vector3 P = c.transform.position;
            return Shared.StableHash(c.gameObject.name + "@" + Mathf.RoundToInt(P.x * 10) + "," + Mathf.RoundToInt(P.y * 10) + "," + Mathf.RoundToInt(P.z * 10)) ^ Shared.WorldSeed();
        }

        private static int Scale(int value, System.Random rng)
        {
            float Scaled = value * Multiplier;
            int Whole = Mathf.FloorToInt(Scaled);
            return Whole + (rng.NextDouble() < Scaled - Whole ? 1 : 0);
        }

        private struct ContainerRolls
        {
            public int m_Min;
            public int m_Max;
            public int m_MinLocked;
            public int m_MaxLocked;
            public float m_ChanceEmpty;
        }

        [HarmonyLib.HarmonyPatch(typeof(Container), "PopulateWithRandomGear")]
        internal static class Container_PopulateWithRandomGear
        {
            private static void Prefix(Container __instance, out ContainerRolls __state)
            {
                __state = new ContainerRolls();
                __state.m_Min = __instance.m_MinRandomItems;
                __state.m_Max = __instance.m_MaxRandomItems;
                __state.m_MinLocked = __instance.m_MinRandomItemsLocked;
                __state.m_MaxLocked = __instance.m_MaxRandomItemsLocked;
                __state.m_ChanceEmpty = __instance.m_ChanceEmpty;
                if (!MyMod.InOnline())
                {
                    return;
                }
                System.Random Rng = new System.Random(Seed(__instance));
                __instance.m_MinRandomItems = Scale(__state.m_Min, Rng);
                __instance.m_MaxRandomItems = Mathf.Max(__instance.m_MinRandomItems, Scale(__state.m_Max, Rng));
                __instance.m_MinRandomItemsLocked = Scale(__state.m_MinLocked, Rng);
                __instance.m_MaxRandomItemsLocked = Mathf.Max(__instance.m_MinRandomItemsLocked, Scale(__state.m_MaxLocked, Rng));
                __instance.m_ChanceEmpty = __state.m_ChanceEmpty / Multiplier;
            }

            // The prefab values are put back so a later roll does not multiply them again.
            private static void Postfix(Container __instance, ContainerRolls __state)
            {
                __instance.m_MinRandomItems = __state.m_Min;
                __instance.m_MaxRandomItems = __state.m_Max;
                __instance.m_MinRandomItemsLocked = __state.m_MinLocked;
                __instance.m_MaxRandomItemsLocked = __state.m_MaxLocked;
                __instance.m_ChanceEmpty = __state.m_ChanceEmpty;
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(RandomSpawnObject), "GetNumObjectsToEnableCurrentXPMode")]
        internal static class RandomSpawnObject_GetNumObjectsToEnableCurrentXPMode
        {
            private static void Postfix(RandomSpawnObject __instance, ref int __result)
            {
                if (!MyMod.InOnline() || __result <= 0 || __instance.m_ObjectList == null)
                {
                    return;
                }
                __result = Mathf.Min(__instance.m_ObjectList.Length, Scale(__result, new System.Random(Seed(__instance))));
            }
        }
    }
}
