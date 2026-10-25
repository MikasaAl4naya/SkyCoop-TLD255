using System;
using Il2Cpp;

namespace SkyCoop
{
    // Picking gear up from the ground goes through inspect mode, which moves the item in front of the camera
    // before the pickup is processed. The pickup is matched on the other side by where the item lay, so keep
    // that position from the moment inspect mode starts (MyMod.GetPickupSpawn).
    public static class PickupPositions
    {
        [HarmonyLib.HarmonyPatch(typeof(PlayerManager), "EnterInspectGearMode", new Type[] { typeof(GearItem), typeof(Container), typeof(IceFishingHole), typeof(Harvestable), typeof(CookingPotItem) })]
        internal static class PlayerManager_EnterInspectGearMode
        {
            private static void Prefix(GearItem gear, Container c)
            {
                if (gear == null || c != null || gear.m_BeenInPlayerInventory || gear.m_InsideContainer || !MyMod.InOnline())
                {
                    return;
                }
                if (MyMod.InspectStartPositions.Count > 64)
                {
                    MyMod.InspectStartPositions.Clear();
                }
                MyMod.InspectStartPositions[gear.m_InstanceID] = gear.transform.position;
            }
        }
    }
}
