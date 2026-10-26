using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using MelonLoader;
using GameServer;
using Il2Cpp;
using Il2CppInterop.Runtime;

namespace SkyCoop
{
    // Shared cooking slots on fires and stoves.
    //
    // A pot, can or skillet on a cooking slot belongs to the player who put it there: their game cooks it as usual.
    // Everyone else gets a copy of its model on the same slot (the slot is taken for them, the hover text says
    // what is cooking and whose it is). Taking the copy asks the owner for the real item: the owner's game removes
    // it and sends its save data, and the taker's game puts the same pot, with the same water or food in it, on the
    // slot. From then on the taker owns it and can pick it up or keep cooking on their own fuel.
    //
    // The owner's slots are checked once a second, so every way of putting a pot on a fire or taking it off
    // (placement, the cooking menu, the fire's radial menu) is covered without hooking each of them.
    // The host keeps the list of pots on slots; a pot whose owner left the scene is given out from that list,
    // and the owner's copy is removed when they come back.
    public static class CookingSync
    {
        public const int EventPlace = 5;
        public const int EventClear = 6;
        public const int EventTake = 7;
        public const int EventGive = 8;
        public const int EventSceneRequest = 9;
        public const int EventReject = 10;
        public const int EventRevoke = 11;

        private const char Sep = '\u0001';
        private const float SlotMatchDistance = 0.35f;

        private class Owned
        {
            public GearItem m_Gear;
            public string m_Sig;
            public float m_Sent;
        }

        private class Known
        {
            public int m_Owner;
            public string m_Payload;
            public string[] m_Fields;
        }

        private class Replica
        {
            public GameObject m_Object;
            public GearPlacePoint m_Slot;
            public string m_Model;
            public string m_Payload;
        }

        // Pots on slots this player owns, by slot key.
        private static readonly Dictionary<string, Owned> s_Owned = new Dictionary<string, Owned>();
        // Pots of other players: on the host every pot on a slot, on a client the ones the host told about.
        private static readonly Dictionary<string, Known> s_Known = new Dictionary<string, Known>();
        private static readonly Dictionary<string, Replica> s_Replicas = new Dictionary<string, Replica>();
        private static readonly Dictionary<int, string> s_ReplicaKeys = new Dictionary<int, string>();
        // Host: slots taken from an owner who was away; that owner's pot is removed when they report it again.
        private static readonly HashSet<string> s_Revoked = new HashSet<string>();
        // Slots the host said are another player's; not claimed again while in this scene.
        private static readonly HashSet<string> s_Rejected = new HashSet<string>();

        private static string s_Scene = "";
        private static float s_SceneSince = 0f;
        private static bool s_SceneRequested = false;
        private static GearPlacePoint[] s_Slots = new GearPlacePoint[0];
        private static float s_SlotsRefreshed = -100f;
        private static float s_TakeAsked = -100f;

        // ---------------------------------------------------------------- helpers

        private static string F(float v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static float P(string s)
        {
            float v;
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            return v;
        }

        private static string Key(string scene, Vector3 pos)
        {
            return scene + "@" + Mathf.RoundToInt(pos.x * 10) + "," + Mathf.RoundToInt(pos.y * 10) + "," + Mathf.RoundToInt(pos.z * 10);
        }

        private static string Join(params string[] fields)
        {
            return string.Join(Sep.ToString(), fields);
        }

        private static string[] Split(string payload)
        {
            return payload.Split(Sep);
        }

        private static bool Ru
        {
            get { return Loc.IsRussian; }
        }

        private static bool SceneSettled()
        {
            return s_Scene == MyMod.level_guid && Time.realtimeSinceStartup - s_SceneSince > 8f;
        }

        private static void Route(int kind, string payload)
        {
            if (MyMod.iAmHost)
            {
                OnServerEvent(0, kind, payload);
            }
            else
            {
                CoopFeatures.Send(kind, payload);
            }
        }

        private static void RefreshSlots()
        {
            if (Time.realtimeSinceStartup - s_SlotsRefreshed < 5f)
            {
                return;
            }
            s_SlotsRefreshed = Time.realtimeSinceStartup;
            s_Slots = UnityEngine.Object.FindObjectsOfType<GearPlacePoint>();
        }

        private static GearPlacePoint FindSlot(Vector3 pos)
        {
            GearPlacePoint Best = null;
            float BestDist = SlotMatchDistance;
            foreach (GearPlacePoint Gpp in UnityEngine.Object.FindObjectsOfType<GearPlacePoint>(true))
            {
                if (Gpp == null)
                {
                    continue;
                }
                float Dist = Vector3.Distance(Gpp.transform.position, pos);
                if (Dist < BestDist)
                {
                    BestDist = Dist;
                    Best = Gpp;
                }
            }
            return Best;
        }

        private static bool IsReplicaSlot(GearPlacePoint gpp)
        {
            foreach (Replica R in s_Replicas.Values)
            {
                if (R.m_Slot == gpp)
                {
                    return true;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------- what is on a slot

        private static string StateText(CookingPotItem pot)
        {
            CookingPotItem.CookingState State = pot.GetCookingState();
            if (State == CookingPotItem.CookingState.Ready)
            {
                return Ru ? "готово" : "ready";
            }
            if (State == CookingPotItem.CookingState.Ruined)
            {
                return Ru ? "испорчено" : "ruined";
            }
            float Percent = pot.m_PercentCooked <= 1.001f ? pot.m_PercentCooked * 100f : pot.m_PercentCooked;
            string Text = (Ru ? "готовится " : "cooking ") + Mathf.Clamp(Mathf.RoundToInt(Percent), 0, 100) + "%";
            int Minutes = Mathf.CeilToInt(pot.m_MinutesUntilCooked);
            if (Minutes > 0)
            {
                Text += Ru ? ", ещё " + Minutes + " мин" : ", " + Minutes + " min left";
            }
            return Text;
        }

        private static string Label(GearItem gi)
        {
            CookingPotItem Pot = gi.m_CookingPotItem;
            GearItem Food = Pot.m_GearItemBeingCooked;
            string Name = Pot.IsDummyPot() && Food != null ? "" : gi.DisplayName;
            string Content;
            if (Food != null)
            {
                Content = Food.DisplayName + ", " + StateText(Pot);
            }
            else if (Pot.m_LitersWaterBeingBoiled != GameCompat.Liters(0f))
            {
                Content = (Ru ? "вода " : "water ") + Pot.m_LitersWaterBeingBoiled.ToStringMetric(1) + ", " + StateText(Pot);
            }
            else if (Pot.m_LitersSnowBeingMelted != GameCompat.Liters(0f))
            {
                Content = (Ru ? "снег " : "snow ") + Pot.m_LitersSnowBeingMelted.ToStringMetric(1) + ", " + StateText(Pot);
            }
            else
            {
                Content = Ru ? "пусто" : "empty";
            }
            return Name == "" ? Content : Name + ": " + Content;
        }

        private static string Sig(GearItem gi)
        {
            CookingPotItem Pot = gi.m_CookingPotItem;
            GearItem Food = Pot.m_GearItemBeingCooked;
            return Pot.GetCookingState() + "|" + Mathf.RoundToInt(Pot.m_PercentCooked * (Pot.m_PercentCooked <= 1.001f ? 10f : 0.1f))
                + "|" + (Food != null ? Food.GetGearName() : "") + "|" + Pot.m_LitersWaterBeingBoiled.ToStringMetric(2) + "|" + Pot.m_LitersSnowBeingMelted.ToStringMetric(2);
        }

        private static string[] Describe(GearItem gi)
        {
            GearItem Food = gi.m_CookingPotItem.m_GearItemBeingCooked;
            return new string[] {
                gi.GetGearName(), gi.SerializeToString(),
                Food != null ? Food.GetGearName() : "", Food != null ? Food.SerializeToString() : "",
                Label(gi) };
        }

        // ---------------------------------------------------------------- owner side

        public static void EverySecond()
        {
            if (s_Scene != MyMod.level_guid)
            {
                s_Scene = MyMod.level_guid;
                s_SceneSince = Time.realtimeSinceStartup;
                s_SceneRequested = false;
                s_SlotsRefreshed = -100f;
                s_Owned.Clear();
                s_Rejected.Clear();
                s_Replicas.Clear();
                s_ReplicaKeys.Clear();
                return;
            }
            if (!SceneSettled())
            {
                return;
            }
            try
            {
                if (!s_SceneRequested)
                {
                    s_SceneRequested = true;
                    if (MyMod.sendMyPosition)
                    {
                        CoopFeatures.Send(EventSceneRequest, MyMod.level_guid);
                    }
                }
                CheckOwnSlots();
                SyncReplicas();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[CookingSync] " + e.Message);
            }
        }

        // Pots, cans and the game's dummy pots that stand on a cooking slot here. The pot knows its slot; the slot's
        // own m_PlacedGear is not set for everything the cooking menu puts on a fire.
        private static List<KeyValuePair<GearPlacePoint, GearItem>> PotsOnSlots()
        {
            List<KeyValuePair<GearPlacePoint, GearItem>> Result = new List<KeyValuePair<GearPlacePoint, GearItem>>();
            HashSet<int> Added = new HashSet<int>();
            foreach (CookingPotItem Pot in UnityEngine.Object.FindObjectsOfType<CookingPotItem>())
            {
                if (Pot == null || Pot.m_GearItem == null)
                {
                    continue;
                }
                GearPlacePoint Gpp = Pot.m_GearPlacePointAttachedTo;
                if (Gpp == null)
                {
                    Gpp = Pot.GetGearPlacePoint();
                }
                if (Gpp == null || !Gpp.gameObject.activeInHierarchy || IsReplicaSlot(Gpp) || !Added.Add(Pot.m_GearItem.GetInstanceID()))
                {
                    continue;
                }
                Result.Add(new KeyValuePair<GearPlacePoint, GearItem>(Gpp, Pot.m_GearItem));
            }
            RefreshSlots();
            foreach (GearPlacePoint Gpp in s_Slots)
            {
                if (Gpp != null && Gpp.gameObject.activeInHierarchy && Gpp.m_PlacedGear != null && Gpp.m_PlacedGear.m_CookingPotItem != null
                    && !IsReplicaSlot(Gpp) && Added.Add(Gpp.m_PlacedGear.GetInstanceID()))
                {
                    Result.Add(new KeyValuePair<GearPlacePoint, GearItem>(Gpp, Gpp.m_PlacedGear));
                }
            }
            return Result;
        }

        private static string s_LastPotsLog = "";

        private static void CheckOwnSlots()
        {
            HashSet<string> Seen = new HashSet<string>();
            List<KeyValuePair<GearPlacePoint, GearItem>> Pots = PotsOnSlots();
            string PotsLog = Pots.Count + " on slots: ";
            foreach (KeyValuePair<GearPlacePoint, GearItem> Pair in Pots)
            {
                GearItem Food = Pair.Value.m_CookingPotItem.m_GearItemBeingCooked;
                PotsLog += Pair.Value.GetGearName() + (Food != null ? "+" + Food.GetGearName() : "") + (PlacedByPlayer(Pair.Value) ? "" : " (scene)") + " ";
            }
            if (PotsLog != s_LastPotsLog)
            {
                s_LastPotsLog = PotsLog;
                MelonLogger.Msg("[CookingSync] " + PotsLog);
            }
            foreach (KeyValuePair<GearPlacePoint, GearItem> Pair in Pots)
            {
                GearPlacePoint Gpp = Pair.Key;
                GearItem Gi = Pair.Value;
                if (!PlacedByPlayer(Gi))
                {
                    continue;
                }
                string K = Key(MyMod.level_guid, Gpp.transform.position);
                if (s_Rejected.Contains(K))
                {
                    continue;
                }
                Seen.Add(K);
                string S = Sig(Gi);
                Owned O;
                if (!s_Owned.TryGetValue(K, out O))
                {
                    O = new Owned();
                    s_Owned[K] = O;
                }
                else if (O.m_Gear == Gi && O.m_Sig == S && Time.realtimeSinceStartup - O.m_Sent < 30f)
                {
                    continue;
                }
                O.m_Gear = Gi;
                O.m_Sig = S;
                O.m_Sent = Time.realtimeSinceStartup;
                Vector3 Pos = Gpp.transform.position;
                string[] D = Describe(Gi);
                Route(EventPlace, Join(K, MyMod.level_guid, F(Pos.x), F(Pos.y), F(Pos.z), D[0], D[1], D[2], D[3], D[4]));
            }
            List<string> Gone = new List<string>();
            foreach (string K in s_Owned.Keys)
            {
                if (!Seen.Contains(K))
                {
                    Gone.Add(K);
                }
            }
            foreach (string K in Gone)
            {
                s_Owned.Remove(K);
                Route(EventClear, Join(K, MyMod.level_guid));
            }
        }

        // A pot that stands on a stove from the start is the same for everyone and stays out of this.
        // The game's dummy pot for food cooked straight on the fire never was in an inventory, the food was.
        private static bool PlacedByPlayer(GearItem gi)
        {
            GearItem Food = gi.m_CookingPotItem.m_GearItemBeingCooked;
            return gi.m_BeenInPlayerInventory || (Food != null && Food.m_BeenInPlayerInventory);
        }

        private static void RemoveOwnGear(GearItem gi)
        {
            GearItem Food = gi.m_CookingPotItem != null ? gi.m_CookingPotItem.m_GearItemBeingCooked : null;
            if (Food != null)
            {
                GearManager.DestroyGearObject(Food);
            }
            GearManager.DestroyGearObject(gi);
        }

        // Owner: another player takes the pot from this player's slot.
        private static void OnTakeRequest(int taker, string payload)
        {
            string[] F0 = Split(payload);
            string K = F0[0];
            Known Rec;
            string[] Pos = null;
            if (s_Known.TryGetValue(K, out Rec))
            {
                Pos = Rec.m_Fields;
            }
            GearPlacePoint Slot = null;
            GearItem Gi = null;
            if (F0[1] == MyMod.level_guid)
            {
                foreach (KeyValuePair<GearPlacePoint, GearItem> Pair in PotsOnSlots())
                {
                    if (Key(MyMod.level_guid, Pair.Key.transform.position) == K)
                    {
                        Slot = Pair.Key;
                        Gi = Pair.Value;
                    }
                }
            }
            if (Gi == null)
            {
                MelonLogger.Msg("[CookingSync] Take " + K + ": nothing on that slot here");
                Route(EventGive, Join(K, F0[1], "", "", "", "", "", "", "", taker.ToString()));
                return;
            }
            Vector3 P0 = Slot.transform.position;
            string[] D = Describe(Gi);
            s_Owned.Remove(K);
            RemoveOwnGear(Gi);
            MelonLogger.Msg("[CookingSync] Gave " + D[0] + " " + D[2] + " on " + K + " to " + taker);
            Route(EventClear, Join(K, F0[1]));
            Route(EventGive, Join(K, F0[1], F(P0.x), F(P0.y), F(P0.z), D[0], D[1], D[2], D[3], taker.ToString()));
            CoopFeatures.Notify(CoopFeatures.PlayerName(taker) + (Ru ? " забрал " : " took ") + D[4]);
        }

        // Taker: the owner sent the pot, put the same one on the slot here.
        private static void OnGive(string payload)
        {
            string[] F0 = Split(payload);
            if (F0.Length < 10 || string.IsNullOrEmpty(F0[5]))
            {
                CoopFeatures.Notify(Ru ? "Не получилось забрать: этого уже нет на огне" : "Could not take it, it is not on the fire any more");
                return;
            }
            if (F0[1] != MyMod.level_guid)
            {
                return;
            }
            Vector3 Pos = new Vector3(P(F0[2]), P(F0[3]), P(F0[4]));
            DestroyReplica(F0[0]);
            GearPlacePoint Slot = FindSlot(Pos);
            if (Slot == null)
            {
                MelonLogger.Warning("[CookingSync] No slot at " + F0[0]);
                return;
            }
            Slot.gameObject.SetActive(true);
            GearItem Food = null;
            if (!string.IsNullOrEmpty(F0[7]))
            {
                Food = Spawn(F0[7], F0[8], Slot.transform.position, Slot.transform.rotation);
            }
            GearItem Pot = Spawn(F0[5], F0[6], Slot.transform.position, Slot.transform.rotation);
            if (Pot == null)
            {
                if (Food != null)
                {
                    GearManager.DestroyGearObject(Food);
                }
                return;
            }
            try
            {
                Slot.PlaceGear(Pot, true);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[CookingSync] PlaceGear: " + e.Message);
            }
            s_SlotsRefreshed = -100f;
            MelonLogger.Msg("[CookingSync] Took " + F0[5] + " " + F0[7] + " on " + F0[0]);
            CoopFeatures.Notify(Ru ? "Теперь это твоё, можно забирать" : "It is yours now, you can take it");
        }

        private static GearItem Spawn(string name, string json, Vector3 pos, Quaternion rot)
        {
            GameObject Reference = MyMod.GetGearItemObject(name);
            if (Reference == null)
            {
                MelonLogger.Warning("[CookingSync] No gear prefab " + name);
                return null;
            }
            GameObject Obj = UnityEngine.Object.Instantiate<GameObject>(Reference, pos, rot);
            DisableObjectForXPMode.RemoveDisabler(Obj);
            Obj.name = MyMod.CloneTrimer(Obj.name);
            GearItem Gi = Obj.GetComponent<GearItem>();
            if (!string.IsNullOrEmpty(json))
            {
                Gi.DeserializeFromJson(json);
            }
            Gi.m_BeenInPlayerInventory = true;
            Obj.transform.position = pos;
            Obj.transform.rotation = rot;
            return Gi;
        }

        // ---------------------------------------------------------------- copies of other players' pots

        private static void SyncReplicas()
        {
            foreach (KeyValuePair<string, Known> Pair in s_Known)
            {
                Known Rec = Pair.Value;
                if (Rec.m_Fields[1] != MyMod.level_guid || Rec.m_Owner == MyId())
                {
                    continue;
                }
                Replica R;
                if (s_Replicas.TryGetValue(Pair.Key, out R) && R.m_Object != null)
                {
                    if (R.m_Payload != Rec.m_Payload)
                    {
                        if (R.m_Model == Model(Rec.m_Fields))
                        {
                            R.m_Payload = Rec.m_Payload;
                            SetHover(R.m_Object, Rec);
                        }
                        else
                        {
                            DestroyReplica(Pair.Key);
                            MakeReplica(Pair.Key, Rec);
                        }
                    }
                    continue;
                }
                MakeReplica(Pair.Key, Rec);
            }
            List<string> Stale = new List<string>();
            foreach (KeyValuePair<string, Replica> Pair in s_Replicas)
            {
                Known Rec;
                if (!s_Known.TryGetValue(Pair.Key, out Rec) || Rec.m_Fields[1] != MyMod.level_guid || Pair.Value.m_Object == null)
                {
                    Stale.Add(Pair.Key);
                }
            }
            foreach (string K in Stale)
            {
                DestroyReplica(K);
            }
        }

        private static int MyId()
        {
            return MyMod.iAmHost ? 0 : ClientUser.myId;
        }

        private static string Model(string[] f)
        {
            return f[5] == "GEAR_CookingPotDummy" && f[7] != "" ? f[7] : f[5];
        }

        private static void SetHover(GameObject obj, Known rec)
        {
            Il2CppTLD.Interactions.SimpleInteraction Si = obj.GetComponent<Il2CppTLD.Interactions.SimpleInteraction>();
            if (Si == null)
            {
                return;
            }
            string Text = rec.m_Fields[9] + " (" + CoopFeatures.PlayerName(rec.m_Owner) + ")";
            Si.m_DefaultHoverText = new LocalizedString { m_LocalizationID = Text };
            Si.HoverText = Text;
        }

        private static void MakeReplica(string key, Known rec)
        {
            Vector3 Pos = new Vector3(P(rec.m_Fields[2]), P(rec.m_Fields[3]), P(rec.m_Fields[4]));
            GearPlacePoint Slot = FindSlot(Pos);
            if (Slot == null)
            {
                return;
            }
            if (Slot.m_PlacedGear != null)
            {
                // Both put something on the same slot at once; each keeps their own.
                return;
            }
            string ModelName = Model(rec.m_Fields);
            GameObject Reference = MyMod.GetGearItemObject(ModelName);
            if (Reference == null)
            {
                return;
            }
            GameObject Obj = UnityEngine.Object.Instantiate<GameObject>(Reference, Slot.transform.position, Slot.transform.rotation);
            Obj.name = MyMod.CloneTrimer(Obj.name) + "_CoopCopy";
            foreach (Component Com in Obj.GetComponents<Component>())
            {
                string ComName = Com.GetIl2CppType().Name;
                if (ComName != Il2CppType.Of<BoxCollider>().Name
                    && ComName != Il2CppType.Of<SphereCollider>().Name
                    && ComName != Il2CppType.Of<CapsuleCollider>().Name
                    && ComName != Il2CppType.Of<MeshCollider>().Name
                    && ComName != MyMod.PhysicMaterialTypeName
                    && ComName != Il2CppType.Of<MeshFilter>().Name
                    && ComName != Il2CppType.Of<LODGroup>().Name
                    && ComName != Il2CppType.Of<Transform>().Name
                    && ComName != Il2CppType.Of<MeshRenderer>().Name
                    && ComName != Il2CppType.Of<SkinnedMeshRenderer>().Name)
                {
                    UnityEngine.Object.Destroy(Com);
                }
            }
            Rigidbody Rb = Obj.GetComponent<Rigidbody>();
            if (Rb != null)
            {
                UnityEngine.Object.Destroy(Rb);
            }
            GameCompat.EnsureInteraction(Obj);
            SetHover(Obj, rec);
            // The slot is taken: nothing of this player's can be put there while the copy stands on it.
            Slot.gameObject.SetActive(false);
            Replica R = new Replica();
            R.m_Object = Obj;
            R.m_Slot = Slot;
            R.m_Model = ModelName;
            R.m_Payload = rec.m_Payload;
            s_Replicas[key] = R;
            s_ReplicaKeys[Obj.GetInstanceID()] = key;
            s_SlotsRefreshed = -100f;
        }

        private static void DestroyReplica(string key)
        {
            Replica R;
            if (!s_Replicas.TryGetValue(key, out R))
            {
                return;
            }
            s_Replicas.Remove(key);
            if (R.m_Object != null)
            {
                s_ReplicaKeys.Remove(R.m_Object.GetInstanceID());
                UnityEngine.Object.Destroy(R.m_Object);
            }
            if (R.m_Slot != null)
            {
                R.m_Slot.gameObject.SetActive(true);
            }
            s_SlotsRefreshed = -100f;
        }

        // Boiling water or cooking from the menu drops the can or pot from the inventory onto the slot. The mod's
        // drop sync turned that drop into a dropped item at the player's feet, so the can fell out every time.
        [HarmonyLib.HarmonyPatch(typeof(GearPlacePoint), "DropAndPlaceItem")]
        internal static class GearPlacePoint_DropAndPlaceItem
        {
            private static void Prefix(GearItem newPlacedItem)
            {
                if (MyMod.InOnline() && newPlacedItem != null && newPlacedItem.GetComponent<Comps.IgnoreDropOverride>() == null)
                {
                    newPlacedItem.gameObject.AddComponent<Comps.IgnoreDropOverride>();
                }
            }

            private static void Finalizer(GearItem newPlacedItem)
            {
                if (newPlacedItem != null && newPlacedItem.GetComponent<Comps.IgnoreDropOverride>() != null)
                {
                    UnityEngine.Object.Destroy(newPlacedItem.GetComponent<Comps.IgnoreDropOverride>());
                }
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(PlayerManager), "InteractiveObjectsProcessInteraction")]
        internal static class PlayerManager_InteractiveObjectsProcessInteraction
        {
            private static bool Prefix(PlayerManager __instance, ref bool __result)
            {
                GameObject Obj = __instance.GetInteractiveObjectUnderCrosshair();
                string K;
                if (Obj == null || !s_ReplicaKeys.TryGetValue(Obj.GetInstanceID(), out K))
                {
                    return true;
                }
                __result = true;
                if (Time.realtimeSinceStartup - s_TakeAsked > 2f)
                {
                    s_TakeAsked = Time.realtimeSinceStartup;
                    MelonLogger.Msg("[CookingSync] Asking to take " + K);
                    Route(EventTake, Join(K, MyMod.level_guid));
                }
                return false;
            }
        }

        // ---------------------------------------------------------------- events

        private static void Remember(int owner, string payload)
        {
            string[] F0 = Split(payload);
            if (F0.Length < 10)
            {
                return;
            }
            Known Rec = new Known();
            Rec.m_Owner = owner;
            Rec.m_Payload = payload;
            Rec.m_Fields = F0;
            s_Known[F0[0]] = Rec;
        }

        private static void Forget(string key)
        {
            s_Known.Remove(key);
            DestroyReplica(key);
        }

        private static bool OwnerHere(int owner, string scene)
        {
            if (owner == 0)
            {
                return MyMod.level_guid == scene;
            }
            return Server.clients.ContainsKey(owner) && Server.clients[owner] != null && Server.clients[owner].IsBusy()
                && owner < MyMod.playersData.Count && MyMod.playersData[owner] != null && MyMod.playersData[owner].m_LevelGuid == scene;
        }

        private static void SendTo(int to, int from, int kind, string payload)
        {
            if (to == 0)
            {
                OnClientEvent(from, kind, payload);
            }
            else
            {
                ServerSend.COOPEVENT(from, kind, payload, to);
            }
        }

        private static void Broadcast(int from, int kind, string payload)
        {
            ServerSend.COOPEVENT(from, kind, payload);
            if (from != 0)
            {
                OnClientEvent(from, kind, payload);
            }
        }

        // Host: every cooking event goes through here, also the host's own.
        public static void OnServerEvent(int from, int kind, string payload)
        {
            string[] F0 = Split(payload);
            string K = F0[0];
            Known Rec;
            if (kind == EventPlace)
            {
                if (s_Revoked.Remove(from + "#" + K))
                {
                    SendTo(from, 0, EventRevoke, Join(K, F0[1]));
                    return;
                }
                if (s_Known.TryGetValue(K, out Rec) && Rec.m_Owner != from && OwnerHere(Rec.m_Owner, Rec.m_Fields[1]))
                {
                    SendTo(from, Rec.m_Owner, EventReject, Join(K, F0[1]));
                    return;
                }
                Broadcast(from, kind, payload);
                Remember(from, payload);
            }
            else if (kind == EventClear)
            {
                if (s_Known.TryGetValue(K, out Rec) && Rec.m_Owner == from)
                {
                    Broadcast(from, kind, payload);
                    Forget(K);
                }
            }
            else if (kind == EventTake)
            {
                if (!s_Known.TryGetValue(K, out Rec))
                {
                    SendTo(from, 0, EventGive, Join(K, F0[1], "", "", "", "", "", "", "", from.ToString()));
                    return;
                }
                if (OwnerHere(Rec.m_Owner, Rec.m_Fields[1]))
                {
                    SendTo(Rec.m_Owner, from, EventTake, payload);
                    return;
                }
                // The owner is somewhere else: give it out from the list and remove the owner's pot later.
                string[] R = Rec.m_Fields;
                s_Revoked.Add(Rec.m_Owner + "#" + K);
                Broadcast(Rec.m_Owner == 0 ? -1 : Rec.m_Owner, EventClear, Join(K, R[1]));
                Forget(K);
                SendTo(from, Rec.m_Owner, EventGive, Join(K, R[1], R[2], R[3], R[4], R[5], R[6], R[7], R[8], from.ToString()));
                MelonLogger.Msg("[CookingSync] " + K + " given to " + from + " from the list, owner " + Rec.m_Owner + " is away");
            }
            else if (kind == EventGive)
            {
                int Taker;
                if (F0.Length >= 10 && int.TryParse(F0[9], out Taker))
                {
                    SendTo(Taker, from, kind, payload);
                }
            }
            else if (kind == EventSceneRequest)
            {
                foreach (Known Each in s_Known.Values)
                {
                    if (Each.m_Fields[1] == payload && Each.m_Owner != from)
                    {
                        SendTo(from, Each.m_Owner, EventPlace, Each.m_Payload);
                    }
                }
            }
        }

        // Client (and the host for events meant for it): an event from another player.
        public static void OnClientEvent(int from, int kind, string payload)
        {
            string[] F0 = Split(payload);
            if (kind == EventPlace)
            {
                if (!MyMod.iAmHost)
                {
                    Remember(from, payload);
                }
            }
            else if (kind == EventClear)
            {
                if (!MyMod.iAmHost)
                {
                    Forget(F0[0]);
                }
                else
                {
                    DestroyReplica(F0[0]);
                }
            }
            else if (kind == EventTake)
            {
                OnTakeRequest(from, payload);
            }
            else if (kind == EventGive)
            {
                OnGive(payload);
            }
            else if (kind == EventReject)
            {
                s_Rejected.Add(F0[0]);
                s_Owned.Remove(F0[0]);
                CoopFeatures.Notify(Ru ? "Эта ячейка уже занята у " + CoopFeatures.PlayerName(from) : "That slot is already taken by " + CoopFeatures.PlayerName(from));
            }
            else if (kind == EventRevoke)
            {
                foreach (KeyValuePair<GearPlacePoint, GearItem> Pair in PotsOnSlots())
                {
                    if (Key(MyMod.level_guid, Pair.Key.transform.position) == F0[0])
                    {
                        MelonLogger.Msg("[CookingSync] " + F0[0] + " was taken while away, removing it");
                        RemoveOwnGear(Pair.Value);
                    }
                }
                s_Owned.Remove(F0[0]);
            }
        }

        public static bool IsCookingEvent(int kind)
        {
            return kind >= EventPlace && kind <= EventRevoke;
        }

        // AutoTest: take the first copy of another player's pot, as if the player pressed on it.
        public static string TestTakeFirst()
        {
            foreach (KeyValuePair<string, Replica> Pair in s_Replicas)
            {
                if (Pair.Value.m_Object != null)
                {
                    Il2CppTLD.Interactions.SimpleInteraction Si = Pair.Value.m_Object.GetComponent<Il2CppTLD.Interactions.SimpleInteraction>();
                    string Hover = Si != null ? Si.HoverText : "";
                    Route(EventTake, Join(Pair.Key, MyMod.level_guid));
                    return Pair.Key + " [" + Hover + "] slot active " + (Pair.Value.m_Slot != null && Pair.Value.m_Slot.gameObject.activeSelf);
                }
            }
            return null;
        }

        public static string Summary()
        {
            return "owned " + s_Owned.Count + " known " + s_Known.Count + " copies " + s_Replicas.Count;
        }
    }
}
