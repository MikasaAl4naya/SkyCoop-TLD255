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
    internal static class LoadFixes
    {
        private static int s_Logged = 0;

        [HarmonyLib.HarmonyPatch(typeof(MissionObjectIdentifier), "AddMissionComponentForSaveManagedItem")]
        internal static class MissionObjectIdentifier_AddMissionComponentForSaveManagedItem
        {
            private static Exception Finalizer(Exception __exception, GameObject smObject)
            {
                if (__exception != null && s_Logged < 10)
                {
                    s_Logged++;
                    MelonLogger.Warning("[LoadFixes] Skipped mission data of " + (smObject != null ? smObject.name : "null") + ": " + __exception.Message.Split('\n')[0]);
                }
                return null;
            }
        }
    }
}
