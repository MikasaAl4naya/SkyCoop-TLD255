using System;
using Il2Cpp;

namespace SkyCoop
{
    // The container screen moves an item into the inventory without saying what it was, unlike picking
    // something up from the ground. Show a short HUD line with the item name over the panel.
    public static class ContainerHints
    {
        private static void Show(string displayName, int count)
        {
            if (string.IsNullOrEmpty(displayName))
            {
                return;
            }
            string Text = (Loc.IsRussian ? "Взято: " : "Taken: ") + displayName + (count > 1 ? " x" + count : "");
            HUDMessage.AddMessage(Text, 2f, false, true);
        }

        [HarmonyLib.HarmonyPatch(typeof(Panel_Container), "OnContainerToInventory", new Type[] { typeof(bool) })]
        internal static class Panel_Container_OnContainerToInventory
        {
            private static void Prefix(Panel_Container __instance, out string __state)
            {
                __state = null;
                InventoryGridDataItem Item = __instance.GetCurrentlySelectedItem();
                if (Item != null && __instance.CanMoveItemToInventory(Item))
                {
                    __state = Item.DisplayName;
                }
            }

            private static void Postfix(string __state)
            {
                Show(__state, 1);
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(Panel_Container), "OnMoveAllToInventory", new Type[] { })]
        internal static class Panel_Container_OnMoveAllToInventory
        {
            private static void Prefix(Panel_Container __instance)
            {
                if (__instance.CanMoveAllToInventory())
                {
                    HUDMessage.AddMessage(Loc.IsRussian ? "Взято всё" : "Took everything", 2f, false, true);
                }
            }
        }
    }
}
