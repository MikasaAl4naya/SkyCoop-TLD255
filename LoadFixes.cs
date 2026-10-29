using System;
using UnityEngine;
using MelonLoader;
using Il2Cpp;

namespace SkyCoop
{
    // A friend's save threw in MissionObjectIdentifier while loading a scene (a container carried mission data the
    // game could not attach). The exception went up through ContainerManager.Deserialize and stopped
    // SaveGameSystem.LoadSceneData, so the rest of the scene's containers never got their ids: the mod could not
    // match them with the host's, and closing one of them froze the container screen.
    // Skipping only the mission part of that one object lets the rest of the scene load.
    // The method turns the object off while it attaches the mission part and on again after it; when it threw in
    // between, the object stayed off (the friend could not see the moved cabinet), so it is turned back on here.
    internal static class LoadFixes
    {
        private static int s_Logged = 0;

        // Since the Iron Road update a region is a scene set whose survival part (REGION_SANDBOX, with every spawn
        // region of the region) loads only when the current game mode asks for it. The game's own load menu puts the
        // save's game mode in place first; the mod loads saves straight away (hosting, the test slot), so the region
        // loaded with the main menu's mode: no survival part, no spawn regions, no animals.
        internal static void UseGameModeOf(SaveSlotInfo slot)
        {
            Il2CppTLD.Gameplay.GameModeConfig Current = ExperienceModeManager.GetCurrentGameModeConfig();
            MelonLogger.Msg("[LoadFixes] Loading " + (slot != null ? slot.m_SaveSlotName + " mode " + slot.m_XPMode + " config " + (slot.m_GameModeConfig != null ? slot.m_GameModeConfig.name : "null") : "unknown slot")
                + ", current " + (Current != null ? Current.name + " conditions " + (Current.m_LoadConditions != null ? Current.m_LoadConditions.Length : -1) : "none"));
            if (slot == null)
            {
                return;
            }
            if (slot.m_GameModeConfig == null)
            {
                Il2CppTLD.Gameplay.GameModeConfig ByType = ExperienceCompat.FindGameModeConfig(slot.m_XPMode);
                if (ByType != null && (Current == null || Current.name != ByType.name))
                {
                    MelonLogger.Msg("[LoadFixes] Game mode " + (Current != null ? Current.name : "none") + " -> " + ByType.name + " (by type) for " + slot.m_SaveSlotName);
                    GameManager.GetExperienceModeManagerComponent().SetGameModeConfig(ByType);
                }
                return;
            }
            if (Current == null || Current.name != slot.m_GameModeConfig.name)
            {
                MelonLogger.Msg("[LoadFixes] Game mode " + (Current != null ? Current.name : "none") + " -> " + slot.m_GameModeConfig.name + " for " + slot.m_SaveSlotName);
                GameManager.GetExperienceModeManagerComponent().SetGameModeConfig(slot.m_GameModeConfig);
            }
        }

        private static SaveSlotInfo FindSlot(string slotName)
        {
            foreach (SaveSlotType Type in new SaveSlotType[] { SaveSlotType.SANDBOX, SaveSlotType.CHALLENGE, SaveSlotType.STORY })
            {
                Il2CppSystem.Collections.Generic.List<SaveSlotInfo> List = SaveGameSlotHelper.GetSaveSlotInfoList(Type);
                if (List == null)
                {
                    continue;
                }
                for (int i = 0; i < List.Count; i++)
                {
                    if (List[i] != null && List[i].m_SaveSlotName == slotName)
                    {
                        return List[i];
                    }
                }
            }
            return SaveGameSlotHelper.GetCurrentSaveSlotInfo();
        }

        [HarmonyLib.HarmonyPatch(typeof(GameManager), "LoadSaveGameSlot", new Type[] { typeof(string), typeof(int) })]
        internal static class GameManager_LoadSaveGameSlot_Name
        {
            private static void Prefix(string slotName)
            {
                try
                {
                    UseGameModeOf(FindSlot(slotName));
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("[LoadFixes] Game mode for " + slotName + ": " + e.Message);
                }
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(GameManager), "LoadSaveGameSlot", new Type[] { typeof(SaveSlotInfo) })]
        internal static class GameManager_LoadSaveGameSlot_Info
        {
            private static void Prefix(SaveSlotInfo ssi)
            {
                try
                {
                    UseGameModeOf(ssi);
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("[LoadFixes] Game mode: " + e.Message);
                }
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(MissionObjectIdentifier), "AddMissionComponentForSaveManagedItem")]
        internal static class MissionObjectIdentifier_AddMissionComponentForSaveManagedItem
        {
            private static void Prefix(GameObject smObject, out bool __state)
            {
                __state = smObject != null && smObject.activeSelf;
            }

            private static Exception Finalizer(Exception __exception, GameObject smObject, bool __state)
            {
                if (__exception == null)
                {
                    return null;
                }
                bool Restored = false;
                if (smObject != null && __state && !smObject.activeSelf)
                {
                    smObject.SetActive(true);
                    Restored = true;
                }
                if (s_Logged < 10)
                {
                    s_Logged++;
                    MelonLogger.Warning("[LoadFixes] Skipped mission data of " + (smObject != null ? smObject.name : "null") + (Restored ? " (turned back on)" : "") + ": " + __exception.Message.Split('\n')[0]);
                }
                return null;
            }
        }
    }
}
