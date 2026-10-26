using System;
using System.Collections.Generic;
using UnityEngine;
using MelonLoader;
using GameServer;
using Il2Cpp;
using Il2CppTLD.Placement;

namespace SkyCoop
{
    // Furniture that one player's save keeps switched off while the host has it.
    //
    // A friend's scene load once threw half-way through a cabinet (see LoadFixes); the game had turned the cabinet off
    // for that step, Placeable.OnDisable told PlaceableManager it is inactive, and the save kept it that way: from
    // then on every load switched the cabinet (and the fireplace) off again. A client in the same scene as the host
    // sends the ids of its switched-off furniture and fireplaces; the host answers with the ones it has switched on,
    // and the client turns those on again, which the game then saves as active.
    public static class PlaceableRepair
    {
        public const int EventAsk = 12;
        public const int EventAnswer = 13;
        private const char Sep = '\u0001';

        private static string s_Scene = "";
        private static float s_SceneSince = 0f;
        private static bool s_Done = false;
        private static float s_NextTry = 0f;

        private static List<GameObject> SwitchedOff()
        {
            List<GameObject> Result = new List<GameObject>();
            foreach (Placeable P in UnityEngine.Object.FindObjectsOfType<Placeable>(true))
            {
                if (P != null && !P.gameObject.activeSelf && DecorationSync.IsSyncable(P.gameObject))
                {
                    Result.Add(P.gameObject);
                }
            }
            foreach (FireplaceInteraction F in UnityEngine.Object.FindObjectsOfType<FireplaceInteraction>(true))
            {
                if (F != null && !F.gameObject.activeInHierarchy && DecorationSync.IsSyncable(F.gameObject))
                {
                    // The switched-off object is the fireplace or one of its parents.
                    Transform T = F.transform;
                    while (T != null && T.gameObject.activeSelf)
                    {
                        T = T.parent;
                    }
                    if (T != null && !Result.Contains(T.gameObject))
                    {
                        Result.Add(T.gameObject);
                    }
                }
            }
            return Result;
        }

        public static void EverySecond()
        {
            if (!MyMod.sendMyPosition)
            {
                return;
            }
            if (s_Scene != MyMod.level_guid)
            {
                s_Scene = MyMod.level_guid;
                s_SceneSince = Time.realtimeSinceStartup;
                s_Done = false;
                s_NextTry = 0f;
                return;
            }
            if (s_Done || Time.realtimeSinceStartup - s_SceneSince < 10f || Time.realtimeSinceStartup < s_NextTry)
            {
                return;
            }
            s_NextTry = Time.realtimeSinceStartup + 30f;
            // Only the host's game knows what is switched on, and only for the scene it is in.
            if (MyMod.playersData.Count == 0 || MyMod.playersData[0] == null || MyMod.playersData[0].m_LevelGuid != MyMod.level_guid)
            {
                return;
            }
            List<string> Ids = new List<string>();
            List<string> Names = new List<string>();
            foreach (GameObject Go in SwitchedOff())
            {
                string Id = DecorationSync.GetId(Go);
                if (Id == "")
                {
                    Id = "N" + DecorationSync.GetPath(Go);
                }
                Ids.Add(Id);
                Names.Add(Go.name);
            }
            if (Ids.Count == 0)
            {
                return;
            }
            s_Done = true;
            MelonLogger.Msg("[PlaceableRepair] Switched off here: " + string.Join(", ", Names.ToArray()) + ", asking the host");
            CoopFeatures.Send(EventAsk, MyMod.level_guid + Sep + string.Join(Sep.ToString(), Ids.ToArray()));
        }

        // Host: which of these does the host have switched on?
        public static void OnAsk(int from, string payload)
        {
            string[] F = payload.Split(Sep);
            if (F.Length < 2 || F[0] != MyMod.level_guid)
            {
                return;
            }
            List<string> On = new List<string>();
            for (int i = 1; i < F.Length; i++)
            {
                GameObject Go = F[i].StartsWith("N") ? DecorationSync.FindByPath(F[i].Substring(1)) : DecorationSync.Find(F[i]);
                if (Go != null && Go.activeSelf)
                {
                    On.Add(F[i]);
                }
            }
            MelonLogger.Msg("[PlaceableRepair] Client " + from + " has " + (F.Length - 1) + " switched off, " + On.Count + " are on here");
            if (On.Count > 0)
            {
                ServerSend.COOPEVENT(0, EventAnswer, F[0] + Sep + string.Join(Sep.ToString(), On.ToArray()), from);
            }
        }

        // Client: switch on what the host has on.
        public static void OnAnswer(string payload)
        {
            string[] F = payload.Split(Sep);
            if (F.Length < 2 || F[0] != MyMod.level_guid)
            {
                return;
            }
            for (int i = 1; i < F.Length; i++)
            {
                try
                {
                    GameObject Go = F[i].StartsWith("N") ? DecorationSync.FindByPath(F[i].Substring(1)) : DecorationSync.Find(F[i]);
                    if (Go != null && !Go.activeSelf)
                    {
                        Go.SetActive(true);
                        MelonLogger.Msg("[PlaceableRepair] Switched on " + Go.name);
                    }
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("[PlaceableRepair] " + F[i] + ": " + e.Message);
                }
            }
        }
    }
}
