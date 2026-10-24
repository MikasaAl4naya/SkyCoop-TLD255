using System;
using System.IO;
using UnityEngine;
using Il2Cpp;
using MelonLoader;
using GameServer;
using static SkyCoop.Comps;

namespace SkyCoop
{
    // Unattended two-instance test driver for development copies. It does nothing unless
    // UserData\SkyCoopAutoTest.txt exists; its first line is the role, "host" or "client".
    // The host loads the newest sandbox save and hosts on 26950, the client connects to
    // 127.0.0.1. Each side drops a stick once in the world, logs what it sees of the other side
    // and quits. Every line it writes starts with [AutoTest] so a run can be read back from the log.
    internal static class AutoTest
    {
        public static string Role = null;
        private static int Stage = 0;
        private static float StageStart = 0f;
        private static float InWorldSince = -1f;
        private static float PeerSeenAt = -1f;
        private static int ConnectAttempts = 0;
        private static float NextReport = 0f;
        private static float NextBootSkip = 0f;
        private static int BootSkips = 0;
        private static bool ContainerTried = false;
        private static readonly string SharedFile = Path.Combine(Path.GetTempPath(), "skycoop_autotest_container.txt");

        public static void Init()
        {
            try
            {
                string FlagFile = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "SkyCoopAutoTest.txt");
                if (!File.Exists(FlagFile))
                {
                    return;
                }
                string[] Lines = File.ReadAllLines(FlagFile);
                if (Lines.Length > 0)
                {
                    Role = Lines[0].Trim().ToLowerInvariant();
                }
                Log("Enabled, role " + Role);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[AutoTest] Could not read flag file: " + e.Message);
            }
        }

        private static void Log(string msg)
        {
            MelonLogger.Msg(ConsoleColor.Cyan, "[AutoTest] " + msg);
        }

        private static void NextStage()
        {
            Stage++;
            StageStart = Time.realtimeSinceStartup;
        }

        private static float InStage()
        {
            return Time.realtimeSinceStartup - StageStart;
        }

        private static bool InWorld()
        {
            bool World = MyMod.level_name != "" && MyMod.level_name != "MainMenu" && MyMod.level_name != "Empty"
                && MyMod.level_name != "Boot" && GameManager.m_PlayerObject != null && !GameManager.IsMainMenuActive();
            if (!World)
            {
                InWorldSince = -1f;
                return false;
            }
            if (InWorldSince < 0f)
            {
                InWorldSince = Time.realtimeSinceStartup;
            }
            // Give scene loading and the mod's own post-load work time to settle.
            return Time.realtimeSinceStartup - InWorldSince > 15f;
        }

        public static void Update()
        {
            if (Role == null)
            {
                return;
            }
            try
            {
                SkipBootDisclaimer();
                if (Role == "host")
                {
                    UpdateHost();
                }
                else if (Role == "client")
                {
                    UpdateClient();
                }
            }
            catch (Exception e)
            {
                Log("Stage " + Stage + " failed: " + e);
                Role = null;
            }
        }

        private static void UpdateHost()
        {
            switch (Stage)
            {
                case 0:
                    if (MyMod.level_name == "MainMenu" && GameManager.IsMainMenuActive())
                    {
                        NextStage();
                    }
                    break;
                case 1:
                    if (InStage() < 10f)
                    {
                        return;
                    }
                    Il2CppSystem.Collections.Generic.List<SaveSlotInfo> Slots = SaveGameSystem.GetSortedSaveSlots(Episode.One, SaveSlotType.SANDBOX);
                    if (Slots == null || Slots.Count == 0)
                    {
                        Log("No sandbox save to load, stopping");
                        Role = null;
                        return;
                    }
                    SaveSlotInfo Slot = Slots[0];
                    Log("Hosting from save " + Slot.m_SaveSlotName);
                    MyMod.HostMenuHost();
                    SaveGameSlots.SetBaseNameForSave(Slot.m_SaveSlotName, Slot.m_SaveSlotName);
                    SaveGameSystem.SetCurrentSaveInfo(Slot.m_Episode, Slot.m_GameMode, Slot.m_GameId, Slot.m_SaveSlotName);
                    GameManager.LoadSaveGameSlot(Slot.m_SaveSlotName, Slot.m_SaveChangelistVersion);
                    NextStage();
                    break;
                case 2:
                    if (InWorld() && MyMod.iAmHost)
                    {
                        Log("Host in world " + MyMod.level_name + ", server up, waiting for client");
                        NextStage();
                    }
                    else if (InStage() > 240f)
                    {
                        Log("Host never got into the world with a server running, quitting");
                        Quit();
                    }
                    break;
                case 3:
                    if (PeerConnected(1))
                    {
                        Log("Client 1 connected");
                        NextStage();
                    }
                    else if (InStage() > 1200f)
                    {
                        Log("No client within 20 minutes, quitting");
                        Quit();
                    }
                    break;
                case 4:
                    Report(1);
                    if (!ContainerTried && InStage() > 20f)
                    {
                        ContainerTried = true;
                        OpenNearestContainer();
                    }
                    if (InStage() > 40f)
                    {
                        DropTestItem();
                        NextStage();
                    }
                    break;
                case 5:
                    Report(1);
                    if (InStage() > 80f)
                    {
                        FinalReport(1);
                        Quit();
                    }
                    break;
            }
        }

        private static void UpdateClient()
        {
            switch (Stage)
            {
                case 0:
                    if (MyMod.level_name == "MainMenu" && GameManager.IsMainMenuActive())
                    {
                        NextStage();
                    }
                    break;
                case 1:
                    if (InStage() < 15f)
                    {
                        return;
                    }
                    if (MyMod.sendMyPosition)
                    {
                        Log("Connected, loading host world");
                        NextStage();
                        return;
                    }
                    if (InStage() > 15f + ConnectAttempts * 30f)
                    {
                        ConnectAttempts++;
                        if (ConnectAttempts > 8)
                        {
                            Log("Could not connect after 8 attempts, quitting");
                            Quit();
                            return;
                        }
                        Log("Connecting to 127.0.0.1, attempt " + ConnectAttempts);
                        ClientUser.DoConnectToIp("127.0.0.1");
                    }
                    break;
                case 2:
                    if (InWorld())
                    {
                        Log("Client in world " + MyMod.level_name);
                        NextStage();
                    }
                    else if (InStage() > 240f)
                    {
                        Log("Client never got into the world, quitting");
                        Quit();
                    }
                    break;
                case 3:
                    Report(0);
                    if (InStage() > 20f)
                    {
                        DropTestItem();
                        NextStage();
                    }
                    break;
                case 4:
                    Report(0);
                    if (InStage() > 60f)
                    {
                        FinalReport(0);
                        Quit();
                    }
                    break;
            }
        }

        // The boot disclaimer waits for a key press.
        private static void SkipBootDisclaimer()
        {
            if (Stage != 0 || Time.realtimeSinceStartup < NextBootSkip)
            {
                return;
            }
            NextBootSkip = Time.realtimeSinceStartup + 3f;
            if (BootSkips >= 2)
            {
                return;
            }
            Panel_Boot Boot = UnityEngine.Object.FindObjectOfType<Panel_Boot>();
            if (Boot != null && Boot.isActiveAndEnabled)
            {
                BootSkips++;
                Log("Skipping boot disclaimer");
                try
                {
                    Boot.OnNextDisclaimerScreen();
                }
                catch (Exception e)
                {
                    // The run script also presses a key in the window, which is enough on its own.
                    Log("Disclaimer skip failed: " + e.Message);
                    BootSkips = 2;
                }
            }
        }

        private static bool PeerConnected(int id)
        {
            return MyMod.playersData.Count > id && MyMod.playersData[id] != null
                && !string.IsNullOrEmpty(MyMod.playersData[id].m_Name) && Server.clients.ContainsKey(id) && Server.clients[id].IsBusy();
        }

        private static void Report(int peer)
        {
            if (Time.realtimeSinceStartup < NextReport)
            {
                return;
            }
            NextReport = Time.realtimeSinceStartup + 10f;
            Vector3 Me = GameManager.GetPlayerTransform().position;
            string Peer = "no data";
            if (MyMod.playersData.Count > peer && MyMod.playersData[peer] != null)
            {
                DataStr.MultiPlayerClientData D = MyMod.playersData[peer];
                Peer = "pos " + D.m_Position + " level " + D.m_Levelid + " dist " + Vector3.Distance(Me, D.m_Position).ToString("0.0");
                if (PeerSeenAt < 0f && MyMod.players.Count > peer && MyMod.players[peer] != null && MyMod.players[peer].activeSelf)
                {
                    PeerSeenAt = Time.realtimeSinceStartup;
                }
            }
            bool ModelActive = MyMod.players.Count > peer && MyMod.players[peer] != null && MyMod.players[peer].activeSelf;
            int Ai = 0;
            foreach (BaseAi A in UnityEngine.Object.FindObjectsOfType<BaseAi>())
            {
                if (A != null && A.gameObject.activeInHierarchy)
                {
                    Ai++;
                }
            }
            string Weather = GameManager.GetWeatherComponent() != null ? GameManager.GetWeatherComponent().GetWeatherStage().ToString() : "?";
            Log("Me " + Me + " | peer " + peer + " " + Peer + " | model active " + ModelActive
                + " | drops seen " + MyMod.DroppedGearsObjs.Count + " | time " + MyMod.OveridedTime
                + " | game time " + GameManager.GetTimeOfDayComponent().GetHour() + ":" + GameManager.GetTimeOfDayComponent().GetMinutes()
                + " | weather " + Weather + " | live AI " + Ai + " | animal actors " + MyMod.ActorsList.Count
                + " | hover " + HoverSummary() + (Role == "client" ? " | container " + ContainerSummary() : ""));
        }

        // How many of the other side's dropped items would show a name under the crosshair.
        private static string HoverSummary()
        {
            int Total = 0;
            int Named = 0;
            foreach (var Pair in MyMod.DroppedGearsObjs)
            {
                if (Pair.Value == null)
                {
                    continue;
                }
                Total++;
                Il2CppTLD.Interactions.BaseInteraction Bi = Pair.Value.GetComponent<Il2CppTLD.Interactions.BaseInteraction>();
                if (Bi != null && !string.IsNullOrEmpty(Bi.HoverText))
                {
                    Named++;
                }
            }
            return Named + "/" + Total;
        }

        // Host side: open (and so loot-mark) the nearest uninspected container, and leave its GUID
        // where the client on the same machine can find it.
        private static void OpenNearestContainer()
        {
            Vector3 Me = GameManager.GetPlayerTransform().position;
            Container Best = null;
            float BestDist = 9999f;
            foreach (Container C in UnityEngine.Object.FindObjectsOfType<Container>())
            {
                if (C == null || C.m_Inspected || C.GetComponent<ObjectGuid>() == null)
                {
                    continue;
                }
                float D = Vector3.Distance(Me, C.transform.position);
                if (D < BestDist)
                {
                    BestDist = D;
                    Best = C;
                }
            }
            if (Best == null)
            {
                Log("No uninspected container in the scene");
                return;
            }
            string Guid = Best.GetComponent<ObjectGuid>().Get();
            Log("Opening container " + Best.gameObject.name + " " + Guid + " at " + BestDist.ToString("0") + " m, items " + Best.m_Items.Count);
            try
            {
                File.WriteAllText(SharedFile, Guid);
            }
            catch (Exception) { }
            Best.BeginContainerOpen();
            Best.BeginContainerClose();
            Log("Container after open: inspected " + Best.m_Inspected + ", items " + Best.m_Items.Count);
        }

        // Client side: what the container the host opened looks like here.
        private static string ContainerSummary()
        {
            try
            {
                if (!File.Exists(SharedFile))
                {
                    return "-";
                }
                string Guid = File.ReadAllText(SharedFile).Trim();
                GameObject Obj = ObjectGuidManager.Lookup(Guid);
                if (Obj == null || Obj.GetComponent<Container>() == null)
                {
                    return Guid + " not found";
                }
                Container C = Obj.GetComponent<Container>();
                ContainersSync Cs = Obj.GetComponent<ContainersSync>();
                return Guid + " inspected " + C.m_Inspected + " items " + C.m_Items.Count + " synced-empty " + (Cs != null ? Cs.m_Empty.ToString() : "n/a");
            }
            catch (Exception e)
            {
                return "error " + e.Message;
            }
        }

        private static void DropTestItem()
        {
            GearItem Prefab = MyMod.GetGearItemPrefab("GEAR_Stick");
            if (Prefab == null)
            {
                Log("No GEAR_Stick prefab, cannot drop");
                return;
            }
            GearItem Stick = GameManager.GetPlayerManagerComponent().InstantiateItemAtPlayersFeet(Prefab, 1);
            if (Stick == null)
            {
                Log("Could not spawn a stick");
                return;
            }
            MyMod.SendDropItem(Stick, 0, 0, true);
            Log("Dropped a stick at " + Stick.transform.position);
        }

        private static void FinalReport(int peer)
        {
            bool ModelActive = MyMod.players.Count > peer && MyMod.players[peer] != null && MyMod.players[peer].activeSelf;
            Log("RESULT role " + Role + " peer model seen " + (PeerSeenAt > 0f) + " model active now " + ModelActive
                + " drops from others " + MyMod.DroppedGearsObjs.Count + " own doll active " + (MyMod.MyPlayerDoll != null && MyMod.MyPlayerDoll.activeSelf));
        }

        private static void Quit()
        {
            Log("Quitting");
            Role = null;
            Application.Quit();
        }
    }
}
