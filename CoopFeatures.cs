using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using MelonLoader;
using GameServer;
using Il2Cpp;

namespace SkyCoop
{
    // Co-op conveniences added in this fork:
    //  - partner markers: name and distance over every player in the scene, also through walls;
    //  - partner notifications: badly hurt, freezing, hypothermia, bleeding, died, searched a container;
    //  - who searched a container (hover text) and no opening a container another player has open;
    //  - periodic resync of the scene state from the host (picked gear, searched containers, furniture);
    //  - autosave every few minutes and right away when the host stops answering;
    //  - settings in UserData\MelonPreferences.cfg, [SkyCoop]; the host's loot and animal values apply to everyone.
    //
    // Everything goes through one packet, COOPEVENT: a kind and a text payload.
    public static class CoopFeatures
    {
        public const int EventVitals = 1;
        public const int EventSettings = 2;
        public const int EventResync = 3;

        private static MelonPreferences_Category s_Category;
        private static MelonPreferences_Entry<float> s_LootMultiplier;
        private static MelonPreferences_Entry<float> s_AnimalMultiplier;
        private static MelonPreferences_Entry<bool> s_Markers;
        private static MelonPreferences_Entry<bool> s_Notifications;
        private static MelonPreferences_Entry<int> s_AutoSaveMinutes;
        private static MelonPreferences_Entry<int> s_ResyncSeconds;

        // Values the host sent; a client uses them instead of its own file.
        private static float s_HostLoot = -1f;
        private static float s_HostAnimals = -1f;

        public static void Init()
        {
            s_Category = MelonPreferences.CreateCategory("SkyCoop", "Sky Co-op");
            s_LootMultiplier = s_Category.CreateEntry("LootMultiplier", 1.5f, "Loot multiplier online (host decides, 1 = as in solo)");
            s_AnimalMultiplier = s_Category.CreateEntry("AnimalSpawnMultiplier", 1.0f, "Chance of animal spawn regions being active (host decides, 1 = as in solo)");
            s_Markers = s_Category.CreateEntry("PartnerMarkers", true, "Show name and distance over other players");
            s_Notifications = s_Category.CreateEntry("PartnerNotifications", true, "Show messages when another player is hurt, freezing, died or searched a container");
            s_AutoSaveMinutes = s_Category.CreateEntry("AutoSaveMinutes", 5, "Save the game every N minutes while online (0 = off)");
            s_ResyncSeconds = s_Category.CreateEntry("ResyncSeconds", 180, "Ask the host for the scene state every N seconds (0 = off)");
            MelonPreferences.Save();
            MelonLogger.Msg("[CoopFeatures] Loot x" + LootMultiplier + ", animals x" + AnimalMultiplier);
        }

        private static float Clamp(float v, float min, float max)
        {
            return Mathf.Clamp(float.IsNaN(v) ? 1f : v, min, max);
        }

        public static float LootMultiplier
        {
            get
            {
                if (MyMod.sendMyPosition && s_HostLoot > 0f)
                {
                    return s_HostLoot;
                }
                return s_LootMultiplier != null ? Clamp(s_LootMultiplier.Value, 1f, 3f) : 1.5f;
            }
        }

        public static float AnimalMultiplier
        {
            get
            {
                if (MyMod.sendMyPosition && s_HostAnimals > 0f)
                {
                    return s_HostAnimals;
                }
                return s_AnimalMultiplier != null ? Clamp(s_AnimalMultiplier.Value, 0f, 3f) : 1f;
            }
        }

        // ---------------------------------------------------------------- packets

        public static void Send(int kind, string payload)
        {
            if (MyMod.iAmHost)
            {
                ServerSend.COOPEVENT(0, kind, payload);
            }
            else if (MyMod.sendMyPosition)
            {
                using (Packet _packet = new Packet((int)ClientPackets.COOPEVENT))
                {
                    _packet.Write(kind);
                    _packet.Write(payload);
                    MyMod.SendUDPData(_packet);
                }
            }
        }

        // Host: an event from a client.
        public static void OnServerEvent(int from, int kind, string payload)
        {
            if (kind == EventVitals)
            {
                OnVitals(from, payload);
                ServerSend.COOPEVENT(from, kind, payload);
            }
            else if (kind == EventResync)
            {
                SendSceneState(from, payload);
            }
            else if (kind == MapSync.EventMapSurvey)
            {
                MapSync.OnSurvey(from, payload);
                ServerSend.COOPEVENT(from, kind, payload);
            }
            else if (CookingSync.IsCookingEvent(kind))
            {
                CookingSync.OnServerEvent(from, kind, payload);
            }
            else if (kind == PlaceableRepair.EventAsk)
            {
                PlaceableRepair.OnAsk(from, payload);
            }
        }

        // Client: an event from the host or another player.
        public static void OnClientEvent(int from, int kind, string payload)
        {
            if (kind == EventVitals)
            {
                OnVitals(from, payload);
            }
            else if (kind == MapSync.EventMapSurvey)
            {
                MapSync.OnSurvey(from, payload);
            }
            else if (CookingSync.IsCookingEvent(kind))
            {
                CookingSync.OnClientEvent(from, kind, payload);
            }
            else if (kind == PlaceableRepair.EventAnswer)
            {
                PlaceableRepair.OnAnswer(payload);
            }
            else if (kind == EventSettings)
            {
                string[] Parts = payload.Split(';');
                float Loot;
                float Animals;
                if (Parts.Length >= 2 && float.TryParse(Parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out Loot)
                    && float.TryParse(Parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out Animals))
                {
                    s_HostLoot = Clamp(Loot, 1f, 3f);
                    s_HostAnimals = Clamp(Animals, 0f, 3f);
                    MelonLogger.Msg("[CoopFeatures] Host settings: loot x" + s_HostLoot + ", animals x" + s_HostAnimals);
                }
            }
        }

        // Host: sent before the save data, so the client rolls the world loot with the host's multiplier.
        public static void SendSettings(int toClient)
        {
            string Payload = LootMultiplier.ToString(CultureInfo.InvariantCulture) + ";" + AnimalMultiplier.ToString(CultureInfo.InvariantCulture);
            ServerSend.COOPEVENT(0, EventSettings, Payload, toClient);
        }

        // Host: what a client should see in a scene, the same as when it enters it.
        public static void SendSceneState(int toClient, string scene)
        {
            Dictionary<long, DataStr.PickedGearSync> PickedGears = MPSaveManager.LoadPickedGearsData(scene);
            if (PickedGears != null)
            {
                foreach (var item in PickedGears)
                {
                    ServerSend.GEARPICKUP(-1, item.Value, false, toClient);
                }
            }
            Dictionary<string, int> LootedBoxes = MPSaveManager.LoadLootedContainersData(scene);
            if (LootedBoxes != null)
            {
                foreach (var item in LootedBoxes)
                {
                    int State = item.Value;
                    if (MPSaveManager.ContainerNotEmpty(scene, item.Key))
                    {
                        State = 1;
                    }
                    ServerSend.CHANGECONTAINERSTATE(0, item.Key, State, scene, false, toClient);
                }
            }
            DecorationSync.SendStored(toClient, scene);
            MelonLogger.Msg("[CoopFeatures] Resync for client " + toClient + " in " + scene + ": picked " + (PickedGears != null ? PickedGears.Count : 0)
                + ", containers " + (LootedBoxes != null ? LootedBoxes.Count : 0));
        }

        // ---------------------------------------------------------------- names

        public static string PlayerName(int id)
        {
            if (id == ClientUser.myId && MyMod.InOnline())
            {
                return MyMod.MyChatName;
            }
            if (id >= 0 && id < MyMod.playersData.Count && MyMod.playersData[id] != null && !string.IsNullOrEmpty(MyMod.playersData[id].m_Name))
            {
                return MyMod.playersData[id].m_Name;
            }
            return (Loc.IsRussian ? "Игрок " : "Player ") + id;
        }

        private static bool OtherPlayerInScene(int i)
        {
            return i != ClientUser.myId && i < MyMod.playersData.Count && MyMod.playersData[i] != null
                && !string.IsNullOrEmpty(MyMod.playersData[i].m_Name) && MyMod.playersData[i].m_LevelGuid == MyMod.level_guid;
        }

        // ---------------------------------------------------------------- notifications

        private class Vitals
        {
            public bool m_Hurt;
            public bool m_Freezing;
            public bool m_Hypothermia;
            public bool m_Bleeding;
            public bool m_Dead;
        }
        private static Dictionary<int, Vitals> s_Vitals = new Dictionary<int, Vitals>();

        public static void Notify(string text)
        {
            if (s_Notifications != null && !s_Notifications.Value)
            {
                return;
            }
            MelonLogger.Msg("[CoopFeatures] " + text);
            HUDMessage.AddMessage(text, 4f, false, true);
        }

        private static string MyVitals()
        {
            int Hp = Mathf.RoundToInt(GameManager.GetConditionComponent().GetNormalizedCondition() * 100f);
            bool Freezing = GameManager.GetFreezingComponent() != null && GameManager.GetFreezingComponent().IsFreezing();
            bool Hypo = GameManager.GetHypothermiaComponent() != null && GameManager.GetHypothermiaComponent().HasHypothermia();
            bool Bleeding = GameManager.GetBloodLossComponent() != null && GameManager.GetBloodLossComponent().HasBloodLoss();
            return Hp + ";" + (Freezing ? 1 : 0) + ";" + (Hypo ? 1 : 0) + ";" + (Bleeding ? 1 : 0) + ";" + (MyMod.IsDead ? 1 : 0);
        }

        // Edges only: one message when a state starts, a new one only after it ended.
        private static void OnVitals(int from, string payload)
        {
            string[] P = payload.Split(';');
            if (P.Length < 5 || from == ClientUser.myId)
            {
                return;
            }
            int Hp;
            int.TryParse(P[0], out Hp);
            Vitals Old;
            if (!s_Vitals.TryGetValue(from, out Old))
            {
                Old = new Vitals();
                s_Vitals[from] = Old;
            }
            string Name = PlayerName(from);
            bool Ru = Loc.IsRussian;
            bool Dead = P[4] == "1";
            bool Hurt = Hp < 30 || (Old.m_Hurt && Hp < 40);
            bool Freezing = P[1] == "1";
            bool Hypo = P[2] == "1";
            bool Bleeding = P[3] == "1";
            if (Dead && !Old.m_Dead)
            {
                Notify(Name + (Ru ? " погиб" : " died"));
            }
            if (!Dead)
            {
                if (Hurt && !Old.m_Hurt)
                {
                    Notify(Name + (Ru ? " тяжело ранен (" : " is badly hurt (") + Hp + "%)");
                }
                if (Bleeding && !Old.m_Bleeding)
                {
                    Notify(Name + (Ru ? " истекает кровью" : " is bleeding"));
                }
                if (Hypo && !Old.m_Hypothermia)
                {
                    Notify(Name + (Ru ? ": переохлаждение" : ": hypothermia"));
                }
                else if (Freezing && !Old.m_Freezing)
                {
                    Notify(Name + (Ru ? " замерзает" : " is freezing"));
                }
            }
            Old.m_Dead = Dead;
            Old.m_Hurt = Hurt;
            Old.m_Freezing = Freezing;
            Old.m_Hypothermia = Hypo;
            Old.m_Bleeding = Bleeding;
        }

        // ---------------------------------------------------------------- containers

        public static void SetLooter(string guid, int from, bool wasInspected)
        {
            if (from == ClientUser.myId)
            {
                return;
            }
            GameObject Obj = ObjectGuidManager.Lookup(guid);
            if (Obj == null)
            {
                return;
            }
            Comps.ContainersSync Con = Obj.GetComponent<Comps.ContainersSync>();
            if (Con != null)
            {
                Con.m_Looter = PlayerName(from);
            }
            if (!wasInspected)
            {
                Container Box = Obj.GetComponent<Container>();
                string What = Box != null && Box.m_LocalizedDisplayName != null ? Box.m_LocalizedDisplayName.Text() : "";
                Notify(PlayerName(from) + (Loc.IsRussian ? " обыскал " : " searched ") + What);
            }
        }

        // Another player in this scene has this container open.
        public static string BusyBy(Container box)
        {
            if (box == null || box.GetComponent<ObjectGuid>() == null || !MyMod.InOnline())
            {
                return null;
            }
            string Guid = box.GetComponent<ObjectGuid>().Get();
            for (int i = 0; i < MyMod.playersData.Count; i++)
            {
                if (OtherPlayerInScene(i) && MyMod.playersData[i].m_Container != null && MyMod.playersData[i].m_Container.m_Guid == Guid)
                {
                    return PlayerName(i);
                }
            }
            return null;
        }

        private static bool BlockIfBusy(Container box)
        {
            string By = BusyBy(box);
            if (By == null)
            {
                return false;
            }
            HUDMessage.AddMessage(By + (Loc.IsRussian ? " сейчас роется в этом ящике" : " is using this container"), 2f, false, true);
            GameAudioManager.PlayGUIError();
            return true;
        }

        [HarmonyLib.HarmonyPatch(typeof(ContainerInteraction), "PerformInteraction")]
        internal static class ContainerInteraction_PerformInteraction
        {
            private static bool Prefix(ContainerInteraction __instance, ref bool __result)
            {
                if (BlockIfBusy(__instance.m_Container))
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(ContainerInteraction), "PerformHold")]
        internal static class ContainerInteraction_PerformHold
        {
            private static bool Prefix(ContainerInteraction __instance, ref bool __result)
            {
                if (BlockIfBusy(__instance.m_Container))
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        // ---------------------------------------------------------------- markers

        private static GUIStyle s_Style;
        private static GUIStyle s_Shadow;

        // For logs and the test driver: the markers that would be drawn now.
        public static string MarkerSummary()
        {
            Camera Cam = GameManager.GetMainCamera();
            if (Cam == null || GameManager.m_PlayerObject == null)
            {
                return "-";
            }
            Vector3 Me = GameManager.GetPlayerTransform().position;
            string Out = "";
            for (int i = 0; i < MyMod.playersData.Count; i++)
            {
                if (!OtherPlayerInScene(i))
                {
                    continue;
                }
                Vector3 Screen = Cam.WorldToScreenPoint(MyMod.playersData[i].m_Position + Vector3.up * 2.1f);
                Out += PlayerName(i) + " " + Mathf.RoundToInt(Vector3.Distance(Me, MyMod.playersData[i].m_Position)) + "m" + (Screen.z > 0f ? " on screen" : " behind") + "; ";
            }
            return Out == "" ? "none" : Out;
        }

        public static void OnGUI()
        {
            if (s_Markers == null || !s_Markers.Value || !MyMod.InOnline() || GameManager.IsMainMenuActive() || GameManager.m_PlayerObject == null)
            {
                return;
            }
            Camera Cam = GameManager.GetMainCamera();
            if (Cam == null)
            {
                return;
            }
            if (s_Style == null)
            {
                s_Style = new GUIStyle(GUI.skin.label);
                s_Style.alignment = TextAnchor.MiddleCenter;
                s_Style.fontSize = 14;
                s_Style.normal.textColor = new Color(1f, 0.95f, 0.8f, 0.9f);
                s_Shadow = new GUIStyle(s_Style);
                s_Shadow.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
            }
            Vector3 Me = GameManager.GetPlayerTransform().position;
            for (int i = 0; i < MyMod.playersData.Count; i++)
            {
                if (!OtherPlayerInScene(i))
                {
                    continue;
                }
                Vector3 Pos = MyMod.playersData[i].m_Position;
                if (i < MyMod.players.Count && MyMod.players[i] != null && MyMod.players[i].activeInHierarchy)
                {
                    Pos = MyMod.players[i].transform.position;
                }
                float Dist = Vector3.Distance(Me, Pos);
                if (Dist < 3f)
                {
                    continue;
                }
                Vector3 Screen = Cam.WorldToScreenPoint(Pos + Vector3.up * 2.1f);
                if (Screen.z <= 0f)
                {
                    continue;
                }
                string Text = PlayerName(i) + "\n" + Mathf.RoundToInt(Dist) + (Loc.IsRussian ? " м" : " m");
                if (MyMod.playersData[i].m_Dead)
                {
                    Text += Loc.IsRussian ? "\n(погиб)" : "\n(dead)";
                }
                Rect R = new Rect(Screen.x - 100f, UnityEngine.Screen.height - Screen.y - 20f, 200f, 50f);
                GUI.Label(new Rect(R.x + 1f, R.y + 1f, R.width, R.height), Text, s_Shadow);
                GUI.Label(R, Text, s_Style);
            }
        }

        // ---------------------------------------------------------------- timers

        private static float s_NextVitals = 0f;
        private static float s_NextResync = 0f;
        private static float s_NextAutoSave = 0f;
        private static bool s_HostSilentSaved = false;
        private static float s_InWorldSince = -1f;

        private static bool InWorld()
        {
            bool World = MyMod.InOnline() && MyMod.level_name != "" && MyMod.level_name != "MainMenu" && MyMod.level_name != "Empty"
                && MyMod.level_name != "Boot" && GameManager.m_PlayerObject != null && !GameManager.IsMainMenuActive();
            if (!World)
            {
                s_InWorldSince = -1f;
                return false;
            }
            if (s_InWorldSince < 0f)
            {
                s_InWorldSince = Time.realtimeSinceStartup;
            }
            return Time.realtimeSinceStartup - s_InWorldSince > 20f;
        }

        // Not in the middle of something the save would cut or that the game would refuse to save in.
        private static bool CanAutoSave()
        {
            PlayerManager Pm = GameManager.GetPlayerManagerComponent();
            if (Pm == null || Pm.GetControlMode() != PlayerControlMode.Normal || Pm.IsInspectModeActive() || Pm.PlayerIsSleeping() || Pm.IsInPlacementMode())
            {
                return false;
            }
            return !MyMod.IsDead && !GameManager.m_IsPaused;
        }

        public static void EverySecond()
        {
            try
            {
                if (!InWorld())
                {
                    return;
                }
                float Now = Time.realtimeSinceStartup;
                MapSync.EverySecond();
                CookingSync.EverySecond();
                PlaceableRepair.EverySecond();
                DeathGuard.EverySecond();
                if (Now > s_NextVitals)
                {
                    s_NextVitals = Now + 2f;
                    Send(EventVitals, MyVitals());
                }
                if (MyMod.sendMyPosition && s_ResyncSeconds.Value > 0 && Now > s_NextResync)
                {
                    if (s_NextResync > 0f)
                    {
                        Send(EventResync, MyMod.level_guid);
                    }
                    s_NextResync = Now + s_ResyncSeconds.Value;
                }
                if (s_AutoSaveMinutes.Value > 0 && Now > s_NextAutoSave)
                {
                    if (s_NextAutoSave > 0f && CanAutoSave())
                    {
                        MelonLogger.Msg("[CoopFeatures] Autosave");
                        GameManager.ForceSaveGame();
                        s_NextAutoSave = Now + s_AutoSaveMinutes.Value * 60f;
                    }
                    else if (s_NextAutoSave <= 0f)
                    {
                        s_NextAutoSave = Now + s_AutoSaveMinutes.Value * 60f;
                    }
                }
                if (MyMod.sendMyPosition && MyMod.LastResponceTime > 0f)
                {
                    float Silent = Time.time - MyMod.LastResponceTime;
                    if (Silent > 30f && !s_HostSilentSaved && CanAutoSave())
                    {
                        s_HostSilentSaved = true;
                        GameManager.ForceSaveGame();
                        Notify(Loc.IsRussian ? "Хост не отвечает. Игра сохранена, при переподключении всё останется." : "The host is not answering. The game is saved, reconnecting keeps your progress.");
                    }
                    else if (Silent < 5f)
                    {
                        s_HostSilentSaved = false;
                    }
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[CoopFeatures] " + e.Message);
            }
        }
    }
}
