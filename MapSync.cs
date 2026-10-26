using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using MelonLoader;
using GameServer;
using Il2Cpp;

namespace SkyCoop
{
    // Charcoal map surveys are shared: when one player surveys, the same spot is revealed on everyone's map.
    // The charcoal survey finishing in CharcoalItem.Update (or DetailSurveyComplete, or Panel_Map.DoDetailSurvey)
    // calls Panel_Map.DoNearbyDetailsCheck with the survey radius; that call is caught and sent to the others, who repeat it with the sender's position. It only works in the scene being surveyed,
    // so a player somewhere else keeps the survey and applies it on entering that scene.
    public static class MapSync
    {
        public const int EventMapSurvey = 4;

        private static bool s_InSurvey = false;
        private static bool s_Applying = false;
        private static readonly Dictionary<string, List<string>> s_Pending = new Dictionary<string, List<string>>();
        private static string s_Scene = "";
        private static float s_SceneSince = 0f;

        [HarmonyLib.HarmonyPatch(typeof(Panel_Map), "DoDetailSurvey")]
        internal static class Panel_Map_DoDetailSurvey
        {
            private static void Prefix()
            {
                s_InSurvey = true;
            }

            private static void Finalizer()
            {
                s_InSurvey = false;
            }
        }

        // The survey of a player finishes inside CharcoalItem.Update, not through DoDetailSurvey.
        [HarmonyLib.HarmonyPatch(typeof(CharcoalItem), "Update")]
        internal static class CharcoalItem_Update
        {
            private static void Prefix(CharcoalItem __instance)
            {
                if (__instance.m_IsActive)
                {
                    s_InSurvey = true;
                }
            }

            private static void Finalizer()
            {
                s_InSurvey = false;
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(CharcoalItem), "DetailSurveyComplete")]
        internal static class CharcoalItem_DetailSurveyComplete
        {
            private static void Prefix()
            {
                s_InSurvey = true;
            }

            private static void Finalizer()
            {
                s_InSurvey = false;
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(Panel_Map), "DoNearbyDetailsCheck")]
        internal static class Panel_Map_DoNearbyDetailsCheck
        {
            private static void Postfix(float radius, bool useOverridePosition, Vector3 overridePostion, bool shouldAllowVistaReveals)
            {
                bool Surveying = s_InSurvey || CharcoalItem.m_CharcoalItemInUseForSurvey != null;
                if (!Surveying || s_Applying || !MyMod.InOnline())
                {
                    return;
                }
                try
                {
                    Vector3 P = useOverridePosition ? overridePostion : GameManager.GetPlayerTransform().position;
                    string Payload = string.Join(";", new string[] { MyMod.level_guid, F(P.x), F(P.y), F(P.z), F(radius), shouldAllowVistaReveals ? "1" : "0" });
                    MelonLogger.Msg("[MapSync] Surveyed " + Payload);
                    CoopFeatures.Send(EventMapSurvey, Payload);
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("[MapSync] " + e.Message);
                }
            }
        }

        private static string F(float v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        // A survey from another player (the host relays client surveys to everyone else).
        public static void OnSurvey(int from, string payload)
        {
            string[] Parts = payload.Split(';');
            if (Parts.Length < 6)
            {
                return;
            }
            CoopFeatures.Notify(CoopFeatures.PlayerName(from) + (Loc.IsRussian ? " зарисовал карту" : " mapped the area"));
            if (Parts[0] == MyMod.level_guid && SceneSettled())
            {
                Apply(payload);
                return;
            }
            List<string> List;
            if (!s_Pending.TryGetValue(Parts[0], out List))
            {
                List = new List<string>();
                s_Pending[Parts[0]] = List;
            }
            List.Add(payload);
            MelonLogger.Msg("[MapSync] Survey in " + Parts[0] + " kept until entering it");
        }

        private static bool SceneSettled()
        {
            return s_Scene == MyMod.level_guid && Time.realtimeSinceStartup - s_SceneSince > 10f;
        }

        private static void Apply(string payload)
        {
            string[] Parts = payload.Split(';');
            float X, Y, Z, Radius;
            if (!float.TryParse(Parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out X)
                || !float.TryParse(Parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out Y)
                || !float.TryParse(Parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out Z)
                || !float.TryParse(Parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out Radius))
            {
                return;
            }
            Panel_Map Map = GameCompat.Panel<Panel_Map>();
            if (Map == null)
            {
                return;
            }
            s_Applying = true;
            try
            {
                Map.DoNearbyDetailsCheck(Radius, true, true, new Vector3(X, Y, Z), Parts[5] == "1");
                MelonLogger.Msg("[MapSync] Applied survey " + payload);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[MapSync] Apply failed: " + e.Message);
            }
            finally
            {
                s_Applying = false;
            }
        }

        // Called every second: applies surveys kept for the scene the player is in now.
        public static void EverySecond()
        {
            if (s_Scene != MyMod.level_guid)
            {
                s_Scene = MyMod.level_guid;
                s_SceneSince = Time.realtimeSinceStartup;
                return;
            }
            List<string> List;
            if (SceneSettled() && s_Pending.TryGetValue(s_Scene, out List))
            {
                s_Pending.Remove(s_Scene);
                foreach (string Payload in List)
                {
                    Apply(Payload);
                }
            }
        }
    }
}
