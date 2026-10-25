using System.Collections.Generic;
using UnityEngine;
using Il2Cpp;

namespace SkyCoop
{
    // Radial spawners (sticks, stones, branches, some books around a point) pick positions and prefabs with the
    // global UnityEngine.Random while the player walks nearby, and skip spots the player can see. That gave every
    // player a different layout, so a picked stick or broken branch on one side stayed on the other.
    //
    // Each spawn attempt now runs on its own seed made from the world seed, the spawner position and the
    // attempt number, and without the visibility check, so both sides make the same attempts in the same order.
    public static class SeededSpawns
    {
        private static Dictionary<int, int> s_Attempts = new Dictionary<int, int>();
        private static string s_AttemptsScene = "";

        private static int SpawnerKey(RadialObjectSpawner spawner)
        {
            Vector3 P = spawner.transform.position;
            return Shared.StableHash(spawner.gameObject.name + "@" + Mathf.RoundToInt(P.x * 10) + "," + Mathf.RoundToInt(P.y * 10) + "," + Mathf.RoundToInt(P.z * 10));
        }

        private static int NextAttempt(int key)
        {
            if (s_AttemptsScene != MyMod.level_guid)
            {
                s_AttemptsScene = MyMod.level_guid;
                s_Attempts.Clear();
            }
            int Attempt;
            s_Attempts.TryGetValue(key, out Attempt);
            s_Attempts[key] = Attempt + 1;
            return Attempt;
        }

        [HarmonyLib.HarmonyPatch(typeof(RadialObjectSpawner), "SpawnObjectAttempt")]
        internal static class RadialObjectSpawner_SpawnObjectAttempt
        {
            private static void Prefix(RadialObjectSpawner __instance, ref bool doVisCheck, out Random.State __state)
            {
                __state = Random.state;
                doVisCheck = false;
                int Key = SpawnerKey(__instance);
                int Attempt = NextAttempt(Key);
                Random.InitState(GameManager.GetRandomSeed(unchecked(Key + Attempt * 7919)));
            }

            private static void Postfix(Random.State __state)
            {
                Random.state = __state;
            }
        }
    }
}
