#if (!DEDICATED)
using System;
using System.Collections.Generic;
using UnityEngine;
using MelonLoader;
using GameServer;
using Il2Cpp;
using Il2CppTLD.AI;
using Il2CppTLD.BigCarry;
using Il2CppTLD.Trader;
using static SkyCoop.DataStr;

namespace SkyCoop
{
    // Multiplayer support for the Tales from the Far Territory content.
    //
    // Most of the DLC rides on systems the mod already syncs: the new regions are just scene names,
    // the new gear serializes like any other gear, and the new weather (toxic and electrostatic fog)
    // travels inside the existing weather-set sync. What needed new work is the content that carries
    // world state of its own:
    //
    //   * the cougar and the ptarmigan, which are wildlife the animal sync did not know about;
    //   * the cougar's territory threat, which is one shared number per region rather than per player;
    //   * the trader, who has one stock, one trust level and one delivery in flight for the world;
    //   * the travois, a container the players drag around between them.
    //
    // The story challenges are deliberately left alone - they are single player content.
    public static class FarTerritory
    {
        public const string CougarPrefab = "WILDLIFE_Cougar";
        public const string PtarmiganPrefab = "WILDLIFE_Ptarmigan";

        // ------------------------------------------------------------------------------------
        // Wildlife
        // ------------------------------------------------------------------------------------

        // Returns the prefab name for a Far Territory animal, or null for anything else. Instances
        // are named after their prefab with a "(Clone)" suffix and sometimes a numeric one, and the
        // name that comes back is what the other clients will spawn from - so it is derived from the
        // instance rather than hardcoded, and keeps working if Hinterland renames a prefab.
        public static string GetAnimalPrefabName(string instanceName)
        {
            if (string.IsNullOrEmpty(instanceName))
            {
                return null;
            }
            if (instanceName.IndexOf("Cougar", StringComparison.OrdinalIgnoreCase) < 0
                && instanceName.IndexOf("Ptarmigan", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }

            string name = GameCompat.StripCloneSuffix(instanceName);
            int suffix = name.LastIndexOf('_');
            if (suffix > 0 && suffix < name.Length - 1)
            {
                bool allDigits = true;
                for (int i = suffix + 1; i < name.Length; i++)
                {
                    if (!char.IsDigit(name[i]))
                    {
                        allDigits = false;
                        break;
                    }
                }
                if (allDigits)
                {
                    name = name.Substring(0, suffix);
                }
            }
            return name;
        }

        public static bool IsFarTerritoryAnimal(string instanceName)
        {
            return GetAnimalPrefabName(instanceName) != null;
        }

        // Remote animals are puppets: the owning client drives them and every other client has to
        // have the native AI taken off, or the two would fight over the same transform.
        public static void StripAi(GameObject animal)
        {
            if (animal == null)
            {
                return;
            }
            AiCougar cougar = animal.GetComponent<AiCougar>();
            if (cougar != null)
            {
                UnityEngine.Object.Destroy(cougar);
            }
            AiPtarmigan ptarmigan = animal.GetComponent<AiPtarmigan>();
            if (ptarmigan != null)
            {
                UnityEngine.Object.Destroy(ptarmigan);
            }
        }

        public static void SetAiEnabled(GameObject animal, bool enabled)
        {
            if (animal == null)
            {
                return;
            }
            AiCougar cougar = animal.GetComponent<AiCougar>();
            if (cougar != null)
            {
                cougar.enabled = enabled;
            }
            AiPtarmigan ptarmigan = animal.GetComponent<AiPtarmigan>();
            if (ptarmigan != null)
            {
                ptarmigan.enabled = enabled;
            }
        }

        // ------------------------------------------------------------------------------------
        // Cougar territory threat
        // ------------------------------------------------------------------------------------

        public static class CougarSync
        {
            private static CougarManager s_Manager;
            private static float s_NextPush;
            private static string s_LastSnapshot = "";

            // How often the host re-checks whether the threat state changed. The state only moves
            // when someone hunts inside a territory, so this can be lazy.
            private const float PushIntervalSeconds = 5f;

            public static CougarManager GetManager()
            {
                if (s_Manager == null)
                {
                    s_Manager = UnityEngine.Object.FindObjectOfType<CougarManager>();
                }
                return s_Manager;
            }

            public static void OnSceneChanged()
            {
                s_Manager = null;
                s_LastSnapshot = "";
            }

            public static CougarStateSync Capture()
            {
                CougarStateSync state = new CougarStateSync();
                CougarManager manager = GetManager();
                if (manager == null)
                {
                    return state;
                }

                try
                {
                    state.m_Enabled = manager.IsEnabled;
                    state.m_ActiveRegionName = manager.m_ActiveRegionName ?? "";

                    Il2CppSystem.Collections.Generic.Dictionary<string, int> levels = manager.m_CurrentThreatLevelByRegion;
                    Il2CppSystem.Collections.Generic.Dictionary<string, float> cooldowns = manager.m_CurrentThreatCooldownByRegion;
                    if (levels != null)
                    {
                        foreach (Il2CppSystem.Collections.Generic.KeyValuePair<string, int> pair in levels)
                        {
                            float cooldown = 0f;
                            if (cooldowns != null)
                            {
                                cooldowns.TryGetValue(pair.Key, out cooldown);
                            }
                            state.m_RegionNames.Add(pair.Key);
                            state.m_ThreatLevels.Add(pair.Value);
                            state.m_ThreatCooldowns.Add(cooldown);
                        }
                    }
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("[SkyCoop] Could not read the cougar state: " + e.Message);
                }
                return state;
            }

            // Merges rather than replaces, and keeps the higher of the two values for a region.
            //
            // Threat only ever climbs from hunting inside a territory and falls on a timer, so the
            // higher number is always the more recent one. Merging that way means it does not matter
            // which client's report arrives first, and it works the same whether one player is
            // hosting or everyone is on a dedicated server with no authoritative game of its own.
            public static void Apply(CougarStateSync state)
            {
                CougarManager manager = GetManager();
                if (manager == null || state == null)
                {
                    return;
                }

                try
                {
                    if (state.m_Enabled)
                    {
                        manager.IsEnabled = true;
                    }

                    Il2CppSystem.Collections.Generic.Dictionary<string, int> levels =
                        manager.m_CurrentThreatLevelByRegion;
                    Il2CppSystem.Collections.Generic.Dictionary<string, float> cooldowns =
                        manager.m_CurrentThreatCooldownByRegion;
                    if (levels == null)
                    {
                        levels = new Il2CppSystem.Collections.Generic.Dictionary<string, int>();
                        manager.m_CurrentThreatLevelByRegion = levels;
                    }
                    if (cooldowns == null)
                    {
                        cooldowns = new Il2CppSystem.Collections.Generic.Dictionary<string, float>();
                        manager.m_CurrentThreatCooldownByRegion = cooldowns;
                    }

                    for (int i = 0; i < state.m_RegionNames.Count; i++)
                    {
                        string region = state.m_RegionNames[i];

                        int known;
                        if (!levels.TryGetValue(region, out known) || state.m_ThreatLevels[i] > known)
                        {
                            levels[region] = state.m_ThreatLevels[i];
                        }

                        float knownCooldown;
                        if (!cooldowns.TryGetValue(region, out knownCooldown) || state.m_ThreatCooldowns[i] > knownCooldown)
                        {
                            cooldowns[region] = state.m_ThreatCooldowns[i];
                        }
                    }

                    s_LastSnapshot = Fingerprint(Capture());
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("[SkyCoop] Could not apply the cougar state: " + e.Message);
                }
            }

            private static string Fingerprint(CougarStateSync state)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append(state.m_Enabled).Append('|');
                for (int i = 0; i < state.m_RegionNames.Count; i++)
                {
                    sb.Append(state.m_RegionNames[i]).Append(':').Append(state.m_ThreatLevels[i]).Append(';');
                }
                return sb.ToString();
            }

            // Called from the mod's update loop. Any client reports a threat level it has seen
            // rise, which is what keeps the cougar consistent on a dedicated server where no player
            // is the authority.
            public static void MaybePush()
            {
                if (Time.time < s_NextPush)
                {
                    return;
                }
                s_NextPush = Time.time + PushIntervalSeconds;

                if (GetManager() == null)
                {
                    return;
                }

                CougarStateSync state = Capture();
                string fingerprint = Fingerprint(state);
                if (fingerprint == s_LastSnapshot)
                {
                    return;
                }
                s_LastSnapshot = fingerprint;

                if (MyMod.iAmHost)
                {
                    ServerSend.COUGARSTATE(state);
                }
                else if (MyMod.sendMyPosition)
                {
                    using (Packet packet = new Packet((int)ClientPackets.COUGARSTATE))
                    {
                        packet.Write(state);
                        MyMod.SendUDPData(packet);
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------
        // Trader
        // ------------------------------------------------------------------------------------

        public static class TraderSync
        {
            private static TraderManager s_Manager;
            private static float s_NextPush;
            private static string s_LastPushed = "";
            private static bool s_Applying;

            private const float PushIntervalSeconds = 5f;

            public static TraderManager GetManager()
            {
                if (s_Manager == null)
                {
                    s_Manager = UnityEngine.Object.FindObjectOfType<TraderManager>();
                }
                return s_Manager;
            }

            public static void OnSceneChanged()
            {
                s_Manager = null;
                s_LastPushed = "";
            }

            public static string Capture()
            {
                TraderManager manager = GetManager();
                if (manager == null || manager.m_CurrentState == null)
                {
                    return "";
                }
                try
                {
                    return Utils.SerializeObject(manager.m_CurrentState);
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("[SkyCoop] Could not read the trader state: " + e.Message);
                    return "";
                }
            }

            public static void Apply(string serialized)
            {
                TraderManager manager = GetManager();
                if (manager == null || string.IsNullOrEmpty(serialized))
                {
                    return;
                }
                try
                {
                    s_Applying = true;
                    TraderState state = Utils.DeserializeObject<TraderState>(serialized);
                    if (state != null)
                    {
                        manager.m_CurrentState = state;
                        // Remember what we just applied so the host does not immediately echo it back.
                        s_LastPushed = serialized;
                    }
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("[SkyCoop] Could not apply the trader state: " + e.Message);
                }
                finally
                {
                    s_Applying = false;
                }
            }

            public static void MaybePush()
            {
                if (!MyMod.iAmHost || s_Applying || Time.time < s_NextPush)
                {
                    return;
                }
                s_NextPush = Time.time + PushIntervalSeconds;

                string serialized = Capture();
                if (string.IsNullOrEmpty(serialized) || serialized == s_LastPushed)
                {
                    return;
                }
                s_LastPushed = serialized;
                ServerSend.TRADERSTATE(serialized);
            }

            // A client that just changed the trade needs the host to hear about it immediately
            // rather than at the next poll, otherwise two players can spend the same trust.
            public static void PushNow()
            {
                string serialized = Capture();
                if (string.IsNullOrEmpty(serialized))
                {
                    return;
                }
                s_LastPushed = serialized;

                if (MyMod.iAmHost)
                {
                    ServerSend.TRADERSTATE(serialized);
                }
                else if (MyMod.sendMyPosition)
                {
                    using (Packet packet = new Packet((int)ClientPackets.TRADERSTATE))
                    {
                        DataStr.TraderStateSync payload = new DataStr.TraderStateSync();
                        payload.m_SerializedState = serialized;
                        packet.Write(payload);
                        MyMod.SendUDPData(packet);
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------
        // Travois
        // ------------------------------------------------------------------------------------

        public static class TravoisSyncing
        {
            // Travois this client has been told about by someone else, keyed by GUID.
            private static readonly Dictionary<string, DataStr.TravoisSync> s_Remote =
                new Dictionary<string, DataStr.TravoisSync>();
            private static float s_NextPush;

            // Dragging a travois moves it every frame, but it moves at walking pace, so a handful of
            // updates a second is plenty and keeps it off the wire the rest of the time.
            private const float PushIntervalSeconds = 0.2f;

            public static void Clear()
            {
                s_Remote.Clear();
            }

            public static string GetGuid(BigCarryItem item)
            {
                if (item == null)
                {
                    return "";
                }
                ObjectGuid guid = item.gameObject.GetComponent<ObjectGuid>();
                if (guid == null)
                {
                    // A travois spawned at runtime has no persistent id yet; give it one so every
                    // client can agree on which travois is being talked about.
                    ObjectGuid.MaybeAttachObjectGuidAndRegister(item.gameObject, ObjectGuidManager.GenerateNewGuidString());
                    guid = item.gameObject.GetComponent<ObjectGuid>();
                }
                return guid != null ? guid.Get() : "";
            }

            private static DataStr.TravoisSync Describe(BigCarryItem item, int carriedBy)
            {
                DataStr.TravoisSync data = new DataStr.TravoisSync();
                data.m_GUID = GetGuid(item);
                data.m_LevelGUID = MyMod.level_guid;
                data.m_LevelID = MyMod.levelid;
                data.m_Position = item.gameObject.transform.position;
                data.m_Rotation = item.gameObject.transform.rotation;
                data.m_CarriedBy = carriedBy;
                return data;
            }

            public static void Send(BigCarryItem item, int carriedBy)
            {
                if (item == null || !MyMod.InOnline())
                {
                    return;
                }
                DataStr.TravoisSync data = Describe(item, carriedBy);
                if (string.IsNullOrEmpty(data.m_GUID))
                {
                    return;
                }

                if (MyMod.iAmHost)
                {
                    ServerSend.TRAVOISSYNC(0, data);
                }
                else if (MyMod.sendMyPosition)
                {
                    using (Packet packet = new Packet((int)ClientPackets.TRAVOISSYNC))
                    {
                        packet.Write(data);
                        MyMod.SendUDPData(packet);
                    }
                }
            }

            // Streams the position of the travois this client is dragging.
            public static void MaybePushCarried()
            {
                if (!MyMod.InOnline() || Time.time < s_NextPush)
                {
                    return;
                }
                s_NextPush = Time.time + PushIntervalSeconds;

                BigCarrySystem system = BigCarrySystem.Instance;
                if (system == null || system.CarriedItem == null)
                {
                    return;
                }
                Send(system.CarriedItem, ClientUser.myId);
            }

            public static void Receive(DataStr.TravoisSync data)
            {
                if (data == null || string.IsNullOrEmpty(data.m_GUID))
                {
                    return;
                }
                s_Remote[data.m_GUID] = data;

                // Only move a travois that is in the scene this client is standing in.
                if (data.m_LevelID != MyMod.levelid || data.m_LevelGUID != MyMod.level_guid)
                {
                    return;
                }
                if (data.m_CarriedBy == ClientUser.myId)
                {
                    return;
                }

                GameObject travois = ObjectGuidManager.Lookup(data.m_GUID);
                if (travois == null)
                {
                    return;
                }

                BigCarrySystem system = BigCarrySystem.Instance;
                if (system != null && system.CarriedItem != null
                    && system.CarriedItem.gameObject == travois)
                {
                    // Somebody else claims to be hauling what this client has hold of. The other
                    // player wins, otherwise the travois would be in two places at once.
                    system.MaybeDropImmediate();
                }

                travois.transform.position = data.m_Position;
                travois.transform.rotation = data.m_Rotation;
            }
        }

        // ------------------------------------------------------------------------------------
        // Noisemakers
        // ------------------------------------------------------------------------------------

        public static class NoiseMakerSync
        {
            public static void Send(NoiseMakerItem item)
            {
                if (item == null || !MyMod.InOnline())
                {
                    return;
                }
                ObjectGuid guid = item.gameObject.GetComponent<ObjectGuid>();
                if (guid == null)
                {
                    ObjectGuid.MaybeAttachObjectGuidAndRegister(item.gameObject, ObjectGuidManager.GenerateNewGuidString());
                    guid = item.gameObject.GetComponent<ObjectGuid>();
                }
                if (guid == null)
                {
                    return;
                }

                if (MyMod.iAmHost)
                {
                    ServerSend.NOISEMAKERIGNITE(0, guid.Get(), MyMod.level_guid, MyMod.levelid);
                }
                else if (MyMod.sendMyPosition)
                {
                    using (Packet packet = new Packet((int)ClientPackets.NOISEMAKERIGNITE))
                    {
                        packet.Write(guid.Get());
                        packet.Write(MyMod.level_guid);
                        packet.Write(MyMod.levelid);
                        MyMod.SendUDPData(packet);
                    }
                }
            }

            public static void Receive(string guid, string levelGuid, int levelId)
            {
                if (levelId != MyMod.levelid || levelGuid != MyMod.level_guid)
                {
                    return;
                }
                GameObject obj = ObjectGuidManager.Lookup(guid);
                if (obj == null)
                {
                    return;
                }
                NoiseMakerItem item = obj.GetComponent<NoiseMakerItem>();
                if (item != null && !s_Igniting)
                {
                    try
                    {
                        s_Igniting = true;
                        item.Ignite();
                    }
                    finally
                    {
                        s_Igniting = false;
                    }
                }
            }

            // Guards against the ignite we just applied being broadcast straight back out.
            private static bool s_Igniting;

            public static bool IsApplyingRemoteIgnite()
            {
                return s_Igniting;
            }
        }

        // ------------------------------------------------------------------------------------
        // Patches
        // ------------------------------------------------------------------------------------

        // Picking a travois up and putting it down are the moments the other clients need to know
        // about; the position in between is streamed by the update pump.
        [HarmonyLib.HarmonyPatch(typeof(BigCarrySystem), "BeginCarry")]
        internal static class BigCarrySystem_BeginCarry
        {
            private static void Postfix(BigCarrySystem __instance)
            {
                TravoisSyncing.Send(__instance.CarriedItem, ClientUser.myId);
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(BigCarrySystem), "MaybeDrop")]
        internal static class BigCarrySystem_MaybeDrop
        {
            private static void Prefix(BigCarrySystem __instance, out BigCarryItem __state)
            {
                __state = __instance.CarriedItem;
            }

            private static void Postfix(BigCarrySystem __instance, BigCarryItem __state, bool __result)
            {
                if (__result && __state != null)
                {
                    TravoisSyncing.Send(__state, -1);
                }
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(BigCarrySystem), "MaybeDropImmediate")]
        internal static class BigCarrySystem_MaybeDropImmediate
        {
            private static void Prefix(BigCarrySystem __instance, out BigCarryItem __state)
            {
                __state = __instance.CarriedItem;
            }

            private static void Postfix(BigCarrySystem __instance, BigCarryItem __state, bool __result)
            {
                if (__result && __state != null)
                {
                    TravoisSyncing.Send(__state, -1);
                }
            }
        }

        // Every trader action that moves trust, stock or the delivery in flight has to reach the
        // other players, or two of them can spend the same trust on the same crate.
        [HarmonyLib.HarmonyPatch(typeof(TraderManager), "CompleteTrade")]
        internal static class TraderManager_CompleteTrade
        {
            private static void Postfix()
            {
                TraderSync.PushNow();
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(TraderManager), "InitiateTrade")]
        internal static class TraderManager_InitiateTrade
        {
            private static void Postfix()
            {
                TraderSync.PushNow();
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(TraderManager), "CancelTrade")]
        internal static class TraderManager_CancelTrade
        {
            private static void Postfix()
            {
                TraderSync.PushNow();
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(TraderManager), "ContactTrader")]
        internal static class TraderManager_ContactTrader
        {
            private static void Postfix()
            {
                TraderSync.PushNow();
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(TraderManager), "NotifyConversationHad")]
        internal static class TraderManager_NotifyConversationHad
        {
            private static void Postfix()
            {
                TraderSync.PushNow();
            }
        }

        // A lit noisemaker is meant to pull wildlife away from whoever threw it. That only works if
        // it goes off on every client, since each one runs the AI for its own share of the animals.
        [HarmonyLib.HarmonyPatch(typeof(NoiseMakerItem), "Ignite", new Type[] { })]
        internal static class NoiseMakerItem_Ignite
        {
            private static void Postfix(NoiseMakerItem __instance)
            {
                if (!NoiseMakerSync.IsApplyingRemoteIgnite())
                {
                    NoiseMakerSync.Send(__instance);
                }
            }
        }

        // ------------------------------------------------------------------------------------
        // Update pump - called once per frame from MyMod.OnUpdate.
        // ------------------------------------------------------------------------------------

        public static void Update()
        {
            if (!MyMod.InOnline())
            {
                return;
            }
            CougarSync.MaybePush();
            TraderSync.MaybePush();
            TravoisSyncing.MaybePushCarried();
        }

        public static void OnSceneChanged()
        {
            CougarSync.OnSceneChanged();
            TraderSync.OnSceneChanged();
            TravoisSyncing.Clear();
        }
    }
}
#endif
