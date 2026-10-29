using MelonLoader;
using UnityEngine;
using Il2Cpp;

namespace SkyCoop
{
    // Counters for the animal spawning steps, logged every two minutes while online.
    public static class SpawnStats
    {
        // The game's own list (SpawnRegionManager.m_SpawnRegions) stays empty since the Iron Road update.
        public static readonly System.Collections.Generic.List<Comps.SpawnRegionSimple> Regions = new System.Collections.Generic.List<Comps.SpawnRegionSimple>();
        // Online the game's spawn regions are switched off and the mod spawns for them (Comps.SpawnRegionSimple).
        // That ran from SpawnRegion.MaybeReRollActive, which the game called all the time in 2.55; since the
        // Iron Road update it is only called when a scene loads, so no animal ever spawned. Driven from here now.
        private static void DriveRegions()
        {
            s_Before = Regions.Count;
            Regions.RemoveAll(R => R == null || R.m_Region == null);
            if (!MyMod.InOnline())
            {
                return;
            }
            foreach (Comps.SpawnRegionSimple Simple in Regions.ToArray())
            {
                {
                    try
                    {
                        Simple.UpdateFromManager();
                    }
                    catch (System.Exception e)
                    {
                        LastError = e.Message;
                    }
                }
            }
        }

        // The game's own ways to add animals to a region, blocked online like SpawnRegion.Spawn (Patches).
        [HarmonyLib.HarmonyPatch(typeof(SpawnRegion), "AddActiveSpawns")]
        internal static class SpawnRegion_AddActiveSpawns
        {
            private static bool Prefix()
            {
                return !MyMod.InOnline();
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(SpawnRegion), "Respawn")]
        internal static class SpawnRegion_Respawn
        {
            private static bool Prefix()
            {
                return !MyMod.InOnline();
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(SpawnRegion), "MaybeSpawnPendingSerializedRespawn")]
        internal static class SpawnRegion_MaybeSpawnPendingSerializedRespawn
        {
            private static bool Prefix(ref BaseAi __result)
            {
                __result = null;
                return !MyMod.InOnline();
            }
        }

        private static int RegionCount()
        {
            return Regions.Count;
        }

        public static int Calls, RolledOff, Near, Wanted, Banned, Attempts, Nulls, Spawned;
        public static string LastError = "";
        public static string LastSpawn = "";
        private static float s_Next = 0f;
        private static int s_Before = 0;
        private static bool s_ScenesLogged = false;

        public static void EverySecond()
        {
            DriveRegions();
            if (Time.realtimeSinceStartup < s_Next)
            {
                return;
            }
            s_Next = Time.realtimeSinceStartup + 120f;
            if (!s_ScenesLogged && MyMod.InOnline())
            {
                s_ScenesLogged = true;
                string Scenes = "";
                for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                {
                    Scenes += UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name + " ";
                }
                int Named = 0;
                string Sample = "";
                foreach (Transform T in Resources.FindObjectsOfTypeAll<Transform>())
                {
                    if (T != null && T.name.StartsWith("SPAWNREGION"))
                    {
                        Named++;
                        if (Named <= 3)
                        {
                            Sample += T.name + "(" + T.gameObject.scene.name + " active " + T.gameObject.activeInHierarchy + " comps " + T.GetComponents<Component>().Length + ") ";
                        }
                    }
                }
                string Mode = "";
                try
                {
                    Il2CppTLD.Gameplay.GameModeConfig Cfg = ExperienceModeManager.GetCurrentGameModeConfig();
                    Mode = Cfg == null ? "no config" : Cfg.name + " conditions " + (Cfg.m_LoadConditions == null ? -1 : Cfg.m_LoadConditions.Length);
                    if (Cfg != null && Cfg.m_LoadConditions != null)
                    {
                        foreach (Il2CppTLD.Scenes.SceneLoadCondition C in Cfg.m_LoadConditions)
                        {
                            Mode += " " + (C != null ? C.name : "null");
                        }
                    }
                    Il2CppTLD.Scenes.SceneSet Set = GameManager.m_ActiveSceneSet;
                    if (Set != null && Set.m_ConditionalScenes != null)
                    {
                        Mode += " | set " + Set.name + " conditional " + Set.m_ConditionalScenes.Count;
                        foreach (Il2CppTLD.Scenes.ConditionalScene Cs in Set.m_ConditionalScenes)
                        {
                            Mode += " [" + (Cs.m_Condition != null ? Cs.m_Condition.name : "null") + "]";
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Mode += " err " + e.Message;
                }
                MelonLogger.Msg("[SpawnStats] mode " + Mode);
                MelonLogger.Msg("[SpawnStats] scenes: " + Scenes + "| SPAWNREGION objects " + Named + " " + Sample + "| managers " + Resources.FindObjectsOfTypeAll<SpawnRegionManager>().Length);
            }
            MelonLogger.Msg("[SpawnStats] controller " + MyMod.AnimalsController + " regions " + RegionCount()
                + " updates " + Calls + " rolled off " + RolledOff + " near " + Near + " wanted " + Wanted + " banned " + Banned
                + " attempts " + Attempts + " null " + Nulls + " spawned " + Spawned + " live AI " + BaseAiManager.m_BaseAis.Count + " before " + s_Before + " all " + Resources.FindObjectsOfTypeAll<SpawnRegion>().Length
                + (LastError != "" ? " error " + LastError : ""));
            int Alive = 0, Active = 0;
            float Nearest = float.MaxValue;
            string NearestName = "";
            foreach (BaseAi Ai in Resources.FindObjectsOfTypeAll<BaseAi>())
            {
                if (Ai == null || Ai.gameObject == null || !Ai.gameObject.scene.IsValid())
                {
                    continue;
                }
                Alive++;
                if (Ai.gameObject.activeInHierarchy)
                {
                    Active++;
                    float D = Vector3.Distance(Ai.transform.position, GameManager.GetPlayerTransform().position);
                    if (D < Nearest)
                    {
                        Nearest = D;
                        NearestName = Ai.gameObject.name + " " + Ai.GetAiMode();
                    }
                }
            }
            MelonLogger.Msg("[SpawnStats] animals in scene " + Alive + " active " + Active + (Active > 0 ? " nearest " + NearestName + " " + Nearest.ToString("0") + "m" : "") + (LastSpawn != "" ? " | last spawn " + LastSpawn : ""));
            LastSpawn = "";
            Calls = RolledOff = Near = Wanted = Banned = Attempts = Nulls = Spawned = 0;
            LastError = "";
        }
    }
}
