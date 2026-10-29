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
        // Second line of the flag file: the save slot the host loads (so a run never touches the real game).
        public static string Slot = null;
        private static string TestBox = null;
        private static bool BusySeen = false;
        private static bool CoopStepOpen = false;
        private static bool CoopStepTake = false;
        private static bool CoopStepHurt = false;
        private static bool CoopStepHeal = false;
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
        private static readonly string CookFile = Path.Combine(Path.GetTempPath(), "skycoop_autotest_cook.txt");
        private static GameObject TestSlot = null;
        private static string TestSlotAt = null;
        private static GearItem TestPot = null;
        private static bool MapDone = false;
        private static CharcoalItem SurveyCoal = null;
        private static bool MapClosed = false;
        private static bool CookPlaced = false;
        private static string TakeInfo = null;
        private static float TakeAt = -1f;
        private static bool TakeChecked = false;
        private static bool ClientSaveCreated = false;
        private static bool RejectDone = false;
        private static bool RejectChecked = false;
        private static bool DeathDone = false;

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
                if (Lines.Length > 1 && Lines[1].Trim() != "")
                {
                    Slot = Lines[1].Trim();
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
                if (Role == "dumpui")
                {
                    DumpBundleTexts();
                    return;
                }
                SkipBootDisclaimer();
                if (Role == "uitest")
                {
                    UpdateUiTest();
                    return;
                }
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

        // Writes every UI text baked into the asset bundle to %TEMP%\skycoop_ui_texts.txt, so the
        // strings can be added to Loc without unpacking the bundle by hand.
        private static void DumpBundleTexts()
        {
            if (MyMod.LoadedBundle == null)
            {
                return;
            }
            System.Text.StringBuilder Out = new System.Text.StringBuilder();
            foreach (UnityEngine.Object Asset in MyMod.LoadedBundle.LoadAllAssets())
            {
                GameObject Prefab = Asset == null ? null : Asset.TryCast<GameObject>();
                if (Prefab == null)
                {
                    continue;
                }
                foreach (UnityEngine.UI.Text T in Prefab.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                {
                    if (T != null && !string.IsNullOrWhiteSpace(T.text))
                    {
                        Out.AppendLine(Prefab.name + "\t" + T.gameObject.name + "\t" + T.text.Replace("\n", "\\n"));
                    }
                }
            }
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "skycoop_ui_texts.txt"), Out.ToString());
            Log("Dumped bundle texts");
            Role = null;
            Application.Quit();
        }

        // Walks the multiplayer menus and a popup, logging the row labels. "SHOT <name>" lines tell
        // the runner script when to take a screenshot.
        private static void UpdateUiTest()
        {
            if (Stage == 0)
            {
                if (!GameManager.IsMainMenuActive() || MyMod.m_Panel_MainMenu == null || !MyMod.m_Panel_MainMenu.isActiveAndEnabled)
                {
                    StageStart = Time.realtimeSinceStartup;
                    return;
                }
                if (InStage() < 8f)
                {
                    return;
                }
                LogRows("Main menu", MyMod.m_Panel_MainMenu.m_BasicMenu);
                Log("SHOT main");
                NextStage();
            }
            else if (Stage == 1 && InStage() > 3f)
            {
                MyMod.m_Panel_Sandbox = InterfaceManager.LoadPanel<Panel_Sandbox>();
                InterfaceManager.TrySetPanelEnabled<Panel_Sandbox>(true);
                MyMod.m_Panel_Sandbox.m_TitleLocalizationId = "MULTIPLAYER";
                MyMod.m_Panel_Sandbox.m_BasicMenu.UpdateTitle("MULTIPLAYER");
                MenuChange.ChangeMenuItems("Multiplayer");
                NextStage();
            }
            else if (Stage == 2 && InStage() > 2f)
            {
                LogRows("Multiplayer menu", MyMod.m_Panel_Sandbox.m_BasicMenu);
                Log("SHOT multiplayer");
                NextStage();
            }
            else if (Stage == 3 && InStage() > 3f)
            {
                MenuChange.ChangeMenuItems("MultiProfileSettings");
                NextStage();
            }
            else if (Stage == 4 && InStage() > 2f)
            {
                LogRows("Profile menu", MyMod.m_Panel_Sandbox.m_BasicMenu);
                Log("SHOT profile");
                NextStage();
            }
            else if (Stage == 5 && InStage() > 3f)
            {
                MenuChange.ChangeMenuItems("Multiplayer");
                MenuChange.DispatchSandboxMenuClick(1);
                NextStage();
            }
            else if (Stage == 6 && InStage() > 2f)
            {
                Log("SHOT host");
                NextStage();
            }
            else if (Stage == 7 && InStage() > 3f)
            {
                if (MyMod.UIHostMenu != null)
                {
                    MyMod.UIHostMenu.SetActive(false);
                }
                MyMod.DoOKMessage("Connection failed", "Server is full!");
                NextStage();
            }
            else if (Stage == 8 && InStage() > 2f)
            {
                Log("SHOT popup");
                NextStage();
            }
            else if (Stage == 9 && InStage() > 3f)
            {
                Log("UI test done");
                Role = null;
                Application.Quit();
            }
        }

        private static void LogRows(string tag, BasicMenu menu)
        {
            string Line = tag + ":";
            if (menu != null && menu.m_MenuItems != null)
            {
                for (int i = 0; i < menu.m_MenuItems.Count; i++)
                {
                    BasicMenu.BasicMenuItemView Row = menu.m_MenuItems[i];
                    if (Row != null && Row.m_Display != null && Row.m_Display.activeSelf && Row.m_Label != null)
                    {
                        Line += " [" + Row.m_Label.text + "]";
                    }
                }
                if (menu.m_DescriptionLabel != null)
                {
                    Line += " desc=" + menu.m_DescriptionLabel.text;
                }
            }
            Log(Line);
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
                    if (AutoTest.Slot == "new")
                    {
                        // A fresh Stalker game in Mystery Lake: deer, wolves and rabbits around the start, and the
                        // mod's own new-game path. Each run makes its own test slot, existing saves stay as they are.
                        Log("Hosting a new game in Mystery Lake");
                        MyMod.HostMenuHost();
                        MyMod.ForceCreateSlotForPlayingSilently((int)Shared.GameRegion.MysteryLake, (int)ExperienceModeType.Stalker, 0, "SkyCoopAutoTest");
                        NextStage();
                        break;
                    }
                    Il2CppSystem.Collections.Generic.List<SaveSlotInfo> Slots = SaveGameSystem.GetSortedSaveSlots(Episode.One, SaveSlotType.SANDBOX);
                    if (Slots == null || Slots.Count == 0)
                    {
                        Log("No sandbox save to load, stopping");
                        Role = null;
                        return;
                    }
                    SaveSlotInfo Slot = Slots[0];
                    if (AutoTest.Slot != null)
                    {
                        Slot = null;
                        for (int i = 0; i < Slots.Count; i++)
                        {
                            if (Slots[i].m_SaveSlotName == AutoTest.Slot)
                            {
                                Slot = Slots[i];
                            }
                        }
                        if (Slot == null)
                        {
                            Log("No save slot " + AutoTest.Slot + ", stopping");
                            Role = null;
                            return;
                        }
                    }
                    Log("Hosting from save " + Slot.m_SaveSlotName);
                    MyMod.HostMenuHost();
                    SaveGameSlots.SetBaseNameForSave(Slot.m_SaveSlotName, Slot.m_SaveSlotName);
                    SaveGameSystem.SetCurrentSaveInfo(Slot.m_Episode, Slot.m_GameMode, Slot.m_GameId, Slot.m_SaveSlotName);
                    LoadFixes.UseGameModeOf(Slot);
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
                    HostCoopChecks();
                    if (!ContainerTried && InStage() > 20f)
                    {
                        ContainerTried = true;
                        SearchContainerForTest();
                    }
                    if (InStage() > 40f)
                    {
                        DropTestItem();
                        NextStage();
                    }
                    break;
                case 5:
                    Report(1);
                    HostCoopChecks();
                    HostMapAndCookSteps();
                    if (InStage() > 200f)
                    {
                        Log("RESULT host busy lock seen " + BusySeen + " stash data length " + StashLength());
                        Log("RESULT host cooking " + CookingSync.Summary() + " test pot still here " + (TestPot != null));
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
                    // A new host world: the mod asks for a survivor before it makes the client's save.
                    if (!ClientSaveCreated && MyMod.PendingSave != null && InterfaceManager.IsPanelEnabled<Panel_SelectSurvivor>() && InStage() > 3f)
                    {
                        ClientSaveCreated = true;
                        Log("Creating the client save for the host's new world");
                        InterfaceManager.TrySetPanelEnabled<Panel_SelectSurvivor>(false);
                        MyMod.ForcedCreateSave(MyMod.PendingSave);
                    }
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
                    ClientCoopSteps();
                    ClientCookSteps();
                    if (InStage() > 220f)
                    {
                        Log("RESULT client death guard [" + DeathGuard.DryRunResult + "]");
                        Log("RESULT client repair " + (OffPlaceable != null ? OffPlaceable.name + " active " + OffPlaceable.activeSelf : "no placeable"));
                        Log("RESULT client cooking " + CookingSync.Summary() + " took " + TakeInfo + " slot has " + (TestSlot != null && TestSlot.GetComponent<GearPlacePoint>().m_PlacedGear != null ? TestSlot.GetComponent<GearPlacePoint>().m_PlacedGear.GetGearName() : "nothing"));
                        FinalReport(0);
                        Quit();
                    }
                    break;
            }
        }

        // Host: search the nearest unsearched container that rolls something, keep half of it in there
        // (as if the rest was taken) and tell the client which one it is.
        private static void SearchContainerForTest()
        {
            Vector3 Me = GameManager.GetPlayerTransform().position;
            System.Collections.Generic.List<Container> Boxes = new System.Collections.Generic.List<Container>();
            foreach (Container C in UnityEngine.Object.FindObjectsOfType<Container>())
            {
                if (C != null && !C.m_Inspected && C.GetComponent<ObjectGuid>() != null && C.GetComponent<Comps.DeathDropContainer>() == null)
                {
                    Boxes.Add(C);
                }
            }
            Boxes.Sort((a, b) => Vector3.Distance(Me, a.transform.position).CompareTo(Vector3.Distance(Me, b.transform.position)));
            foreach (Container C in Boxes)
            {
                C.InstantiateContents();
                if (C.m_Items.Count < 2)
                {
                    continue;
                }
                TestBox = C.GetComponent<ObjectGuid>().Get();
                int Before = C.m_Items.Count;
                GearItem Taken = C.m_Items[0].m_GearItem;
                if (Taken != null)
                {
                    C.RemoveGear(Taken);
                    UnityEngine.Object.Destroy(Taken.gameObject);
                }
                MyMod.FinishSearchReveal(C);
                File.WriteAllText(SharedFile, TestBox);
                Log("Searched " + C.gameObject.name + " " + TestBox + " rolled " + Before + " items, left " + (Before - 1) + ", stash data length " + StashLength());
                return;
            }
            Log("No unsearched container with 2+ items in the scene");
        }

        // A cooking slot at the same spot in both games: an outdoor scene has no stove at hand.
        private static void MakeTestSlot(Vector3 pos)
        {
            string At = pos.ToString("F2");
            if (TestSlot != null && TestSlotAt == At)
            {
                return;
            }
            if (TestSlot != null)
            {
                UnityEngine.Object.Destroy(TestSlot);
            }
            TestSlot = new GameObject("SkyCoopTestSlot");
            TestSlot.transform.position = pos;
            TestSlot.AddComponent<GearPlacePoint>();
            TestSlotAt = At;
            Log("Test cooking slot at " + At);
        }

        private static void HostMapAndCookSteps()
        {
            if (!MapDone && InStage() > 10f)
            {
                MapDone = true;
                try
                {
                    // The way a player does it: the charcoal survey, finished by CharcoalItem.Update.
                    // Skips the start checks (weather, hands) and lets CharcoalItem.Update finish it on the next frame.
                    GearItem Coal = GameManager.GetPlayerManagerComponent().AddItemCONSOLE("GEAR_Charcoal", 1);
                    CharcoalItem Item = Coal.m_CharcoalItem;
                    Item.m_IsActive = true;
                    CharcoalItem.m_CharcoalItemInUseForSurvey = Item;
                    Item.m_TimeSpentSurveying = Item.GetSurveyRealSeconds() + 1f;
                    SurveyCoal = Item;
                    Log("Charcoal survey started, object active " + Coal.gameObject.activeInHierarchy);
                }
                catch (Exception e)
                {
                    Log("Map survey failed: " + e.Message);
                }
            }
            if (MapDone && SurveyCoal != null && InStage() > 16f)
            {
                if (SurveyCoal.m_IsActive)
                {
                    // Update did not run on it (an item in the inventory can be switched off): finish it the other way.
                    Log("Survey still active, finishing with DetailSurveyComplete");
                    SurveyCoal.DetailSurveyComplete();
                }
                SurveyCoal = null;
            }
            if (MapDone && !MapClosed && InStage() > 22f)
            {
                MapClosed = true;
                GameCompat.Panel<Panel_Map>().Enable(false);
            }
            if (!CookPlaced && InStage() > 25f)
            {
                CookPlaced = true;
                Transform Me = GameManager.GetPlayerTransform();
                Vector3 Pos = Me.position + Me.forward * 1.2f + Vector3.up * 0.3f;
                MakeTestSlot(Pos);
                File.WriteAllText(CookFile, Pos.x.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" + Pos.y.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";" + Pos.z.ToString(System.Globalization.CultureInfo.InvariantCulture));
                // Boiling water from the menu: the can is dropped onto the slot. It must not become a dropped item,
                // and the other player gets a copy of it on the same slot.
                GearItem Can = GameManager.GetPlayerManagerComponent().AddItemCONSOLE("GEAR_RecycledCan", 1);
                TestPot = Can;
                try
                {
                    TestSlot.GetComponent<GearPlacePoint>().DropAndPlaceItem(Can);
                }
                catch (Exception)
                {
                    // The bare test slot has no collider for SetPlacePoint; the can is placed before that.
                }
                Log("RESULT water can after DropAndPlaceItem: exists " + (Can != null) + " in inventory " + (Can != null && Can.m_InPlayerInventory));
            }
        }

        private static GameObject OffPlaceable = null;

        private static void ClientCookSteps()
        {
            // A cabinet the save keeps switched off: the host has it on, so it comes back.
            if (OffPlaceable == null && InStage() > 2f)
            {
                foreach (Il2CppTLD.Placement.Placeable P in UnityEngine.Object.FindObjectsOfType<Il2CppTLD.Placement.Placeable>())
                {
                    if (P != null && P.gameObject.activeSelf && DecorationSync.IsSyncable(P.gameObject))
                    {
                        OffPlaceable = P.gameObject;
                        OffPlaceable.SetActive(false);
                        Log("Switched off " + OffPlaceable.name + " to test the repair");
                        break;
                    }
                }
            }
            if (OffPlaceable != null && InStage() > 100f && InStage() < 101f)
            {
                Log("Repair: " + OffPlaceable.name + " active " + OffPlaceable.activeSelf);
            }
            if (File.Exists(CookFile))
            {
                string[] Parts = File.ReadAllText(CookFile).Split(';');
                if (Parts.Length == 3)
                {
                    System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
                    MakeTestSlot(new Vector3(float.Parse(Parts[0], Inv), float.Parse(Parts[1], Inv), float.Parse(Parts[2], Inv)));
                }
            }
            if (TakeInfo == null && TestSlot != null && InStage() > 60f)
            {
                TakeInfo = CookingSync.TestTakeFirst();
                if (TakeInfo != null)
                {
                    TakeAt = InStage();
                    Log("Taking pot copy " + TakeInfo);
                }
            }
            if (TakeInfo != null && !TakeChecked && InStage() > TakeAt + 15f)
            {
                TakeChecked = true;
                GearItem Placed = TestSlot.GetComponent<GearPlacePoint>().m_PlacedGear;
                Log("After take: slot has " + (Placed != null ? Placed.GetGearName() + " snow " + Placed.m_CookingPotItem.m_LitersSnowBeingMelted.ToStringMetric(1) : "nothing") + ", " + CookingSync.Summary());
            }
            if (TakeChecked && !RejectDone && InStage() > TakeAt + 30f)
            {
                RejectDone = true;
                Log("Reject on own slot: " + CookingSync.TestRejectOwned() + ", cans in inventory " + GameManager.GetInventoryComponent().NumGearInInventory("GEAR_RecycledCan", false));
            }
            if (RejectDone && !RejectChecked && InStage() > TakeAt + 35f)
            {
                RejectChecked = true;
                GearItem Placed = TestSlot.GetComponent<GearPlacePoint>().m_PlacedGear;
                Log("After reject: slot has " + (Placed != null ? Placed.GetGearName() : "nothing") + ", cans in inventory " + GameManager.GetInventoryComponent().NumGearInInventory("GEAR_RecycledCan", false) + ", " + CookingSync.Summary());
            }
            if (!DeathDone && InStage() > 190f)
            {
                DeathDone = true;
                DeathGuard.DryRunResult = "";
                DeathGuard.Mark();
            }
        }

        private static int StashLength()
        {
            if (TestBox == null)
            {
                return -1;
            }
            string Data = MPSaveManager.LoadContainer(MyMod.level_guid, TestBox);
            return Data == null ? -1 : Data.Length;
        }

        private static void HostCoopChecks()
        {
            if (TestBox == null)
            {
                return;
            }
            GameObject Obj = ObjectGuidManager.Lookup(TestBox);
            if (Obj != null && Obj.GetComponent<Container>() != null)
            {
                string By = CoopFeatures.BusyBy(Obj.GetComponent<Container>());
                if (By != null && !BusySeen)
                {
                    BusySeen = true;
                    Log("Container busy by " + By + ", stash data length " + StashLength());
                }
            }
        }

        // Client: look at the container the host searched, open it as a stash, take one item, close it,
        // then get badly hurt for a while so the host shows a notification.
        private static void ClientCoopSteps()
        {
            if (TestBox == null && File.Exists(SharedFile))
            {
                TestBox = File.ReadAllText(SharedFile).Trim();
            }
            GameObject Obj = TestBox == null ? null : ObjectGuidManager.Lookup(TestBox);
            Container Box = Obj == null ? null : Obj.GetComponent<Container>();
            if (!CoopStepOpen && InStage() > 30f)
            {
                CoopStepOpen = true;
                if (Box == null)
                {
                    Log("Test container " + TestBox + " not found");
                    return;
                }
                ContainerInteraction Ci = Obj.GetComponent<ContainerInteraction>();
                Comps.ContainersSync Cs = Obj.GetComponent<Comps.ContainersSync>();
                Log("Test container inspected " + Box.m_Inspected + " looter " + (Cs != null ? Cs.m_Looter : "n/a") + " hover [" + (Ci != null ? Ci.GetHoverText().Replace("\n", " | ") : "n/a") + "]");
                Pathes.Container_UsingSyncOpen.Postfix(Box);
                MyMod.OpenFakeContainer(Box);
                Log("Opening test container as a stash");
            }
            if (CoopStepOpen && !CoopStepTake && InStage() > 45f && Box != null)
            {
                CoopStepTake = true;
                int Count = Box.m_Items.Count;
                if (Count > 0 && Box.m_Items[0].m_GearItem != null)
                {
                    GearItem Taken = Box.m_Items[0].m_GearItem;
                    string Name = Taken.GetGearName();
                    Box.RemoveGear(Taken);
                    UnityEngine.Object.Destroy(Taken.gameObject);
                    Log("Stash had " + Count + " items, took " + Name);
                }
                else
                {
                    Log("Stash had " + Count + " items");
                }
                MyMod.CloseFakeContainer(Box);
            }
            if (!CoopStepHurt && InStage() > 60f)
            {
                CoopStepHurt = true;
                Condition Con = GameManager.GetConditionComponent();
                Con.m_CurrentHP = Con.m_MaxHP * 0.2f;
                Log("Set own condition to 20%");
            }
            if (!CoopStepHeal && InStage() > 75f)
            {
                CoopStepHeal = true;
                Condition Con = GameManager.GetConditionComponent();
                Con.m_CurrentHP = Con.m_MaxHP;
                Log("Condition back to 100%");
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
                + " | hover " + HoverSummary() + (Role == "client" ? " | container " + ContainerSummary() : "")
                + " | seed probe " + GameManager.GetRandomSeed(12345) + " | loot " + LootSummary() + " | markers " + CoopFeatures.MarkerSummary());
        }

        // Loose world gear (not carried, not another player's drop): count and an order-independent
        // fingerprint of names and rounded positions, to compare what each side spawned.
        private static string LootSummary()
        {
            System.Collections.Generic.HashSet<IntPtr> Drops = new System.Collections.Generic.HashSet<IntPtr>();
            foreach (var Pair in MyMod.DroppedGearsObjs)
            {
                if (Pair.Value != null)
                {
                    Drops.Add(Pair.Value.Pointer);
                }
            }
            int Count = 0;
            long Sig = 0;
            foreach (GearItem G in UnityEngine.Object.FindObjectsOfType<GearItem>())
            {
                if (G == null || G.m_InPlayerInventory || !G.gameObject.activeInHierarchy || Drops.Contains(G.gameObject.Pointer))
                {
                    continue;
                }
                Vector3 P = G.transform.position;
                Count++;
                Sig += Shared.StableHash(G.GetGearName() + "@" + Mathf.RoundToInt(P.x) + "," + Mathf.RoundToInt(P.z));
            }
            return Count + " sig " + Sig;
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
            GearItem Stick = GameManager.GetPlayerManagerComponent().InstantiateItemAtPlayersFeet("GEAR_Stick", 1);
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
