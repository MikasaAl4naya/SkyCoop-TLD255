using System;
using System.Collections.Generic;
using UnityEngine;
using MelonLoader;
using GameServer;
using TinyJSON;
using Il2Cpp;
using Il2CppTLD.Placement;
using UnityEngine.SceneManagement;

namespace SkyCoop
{
    // Safehouse customization (2.55): moving furniture and decorations.
    //
    // When a player finishes placing a decoration, its position is sent to everyone in the same scene,
    // who move their copy of the object. Picking an object up hides it for the others until it is placed again.
    // Objects are matched by the Placeable guid or the ObjectGuid, both baked into the scene, or else by the
    // object's path in the scene hierarchy taken before it was moved. It only works for objects that exist
    // on both sides; a decoration made or brought from elsewhere is not created.
    //
    // Moving goes through the place mesh mode of PlayerManager (StartPlaceMesh ... PlaceMeshInWorld) for any
    // furniture, DecorationItem.FinalizePlacement only covers decorations.
    //
    // The host keeps the last state of every moved object per scene in the server save, sends it to players
    // when they enter that scene and applies it itself, so a move is not lost for whoever was elsewhere.
    public static class DecorationSync
    {
        public class DecorationState
        {
            public string m_Id = "";
            public Vector3 m_Position = new Vector3(0, 0, 0);
            public Quaternion m_Rotation = new Quaternion(0, 0, 0, 1);
            public bool m_Visible = true;
        }

        public static Dictionary<string, Dictionary<string, DecorationState>> Stored = new Dictionary<string, Dictionary<string, DecorationState>>();
        public static bool StoredChanged = false;

        public static void Record(string id, Vector3 pos, Quaternion rot, bool visible, string levelGuid)
        {
            Dictionary<string, DecorationState> Scene;
            if (!Stored.TryGetValue(levelGuid, out Scene))
            {
                Scene = new Dictionary<string, DecorationState>();
                Stored[levelGuid] = Scene;
            }
            DecorationState State = new DecorationState();
            State.m_Id = id;
            State.m_Position = pos;
            State.m_Rotation = rot;
            State.m_Visible = visible;
            Scene[id] = State;
            StoredChanged = true;
        }

        public static void LoadStored(string json)
        {
            Stored = new Dictionary<string, Dictionary<string, DecorationState>>();
            if (string.IsNullOrEmpty(json))
            {
                return;
            }
            try
            {
                Stored = JSON.Load(json).Make<Dictionary<string, Dictionary<string, DecorationState>>>();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[DecorationSync] Could not read saved decorations: " + e.Message);
            }
        }

        public static string DumpStored()
        {
            return JSON.Dump(Stored);
        }

        // Host entering a scene: put objects where players left them while the host was elsewhere.
        public static void ApplyStored(string levelGuid)
        {
            Dictionary<string, DecorationState> Scene;
            if (!Stored.TryGetValue(levelGuid, out Scene))
            {
                return;
            }
            foreach (DecorationState State in Scene.Values)
            {
                Receive(State.m_Id, State.m_Position, State.m_Rotation, State.m_Visible, levelGuid, -1);
            }
        }

        // A player entering a scene gets every stored state of it.
        public static void SendStored(int toClient, string levelGuid)
        {
            Dictionary<string, DecorationState> Scene;
            if (!Stored.TryGetValue(levelGuid, out Scene))
            {
                return;
            }
            foreach (DecorationState State in Scene.Values)
            {
                ServerSend.DECORATIONMOVE(0, State.m_Id, State.m_Position, State.m_Rotation, State.m_Visible, levelGuid, -1, toClient);
            }
        }

        // Id of an object taken when placing it starts, before it is moved or re-parented.
        public static Dictionary<int, string> StartIds = new Dictionary<int, string>();

        public static string GetPath(GameObject go)
        {
            List<string> Parts = new List<string>();
            Transform T = go.transform;
            while (T != null)
            {
                Parts.Insert(0, T.gameObject.name + "#" + T.GetSiblingIndex());
                T = T.parent;
            }
            return go.scene.name + ":" + string.Join("/", Parts);
        }

        public static GameObject FindByPath(string path)
        {
            int Colon = path.IndexOf(':');
            if (Colon < 0)
            {
                return null;
            }
            Scene S = UnityEngine.SceneManagement.SceneManager.GetSceneByName(path.Substring(0, Colon));
            if (!S.IsValid() || !S.isLoaded)
            {
                return null;
            }
            string[] Parts = path.Substring(Colon + 1).Split('/');
            Transform Current = null;
            foreach (string Part in Parts)
            {
                int Hash = Part.LastIndexOf('#');
                string Name = Hash < 0 ? Part : Part.Substring(0, Hash);
                int Index = -1;
                if (Hash >= 0)
                {
                    int.TryParse(Part.Substring(Hash + 1), out Index);
                }
                Transform Next = null;
                if (Current == null)
                {
                    foreach (GameObject Root in S.GetRootGameObjects())
                    {
                        if (Root.name == Name && (Next == null || Root.transform.GetSiblingIndex() == Index))
                        {
                            Next = Root.transform;
                        }
                    }
                } else
                {
                    if (Index >= 0 && Index < Current.childCount && Current.GetChild(Index).name == Name)
                    {
                        Next = Current.GetChild(Index);
                    } else
                    {
                        for (int i = 0; i < Current.childCount; i++)
                        {
                            if (Current.GetChild(i).name == Name)
                            {
                                Next = Current.GetChild(i);
                                break;
                            }
                        }
                    }
                }
                if (Next == null)
                {
                    return null;
                }
                Current = Next;
            }
            return Current != null ? Current.gameObject : null;
        }

        public static string GetId(GameObject go)
        {
            if (go == null)
            {
                return "";
            }
            string Start;
            if (StartIds.TryGetValue(go.GetInstanceID(), out Start))
            {
                return Start;
            }
            Placeable placeable = go.GetComponent<Placeable>();
            if (placeable != null && !string.IsNullOrEmpty(placeable.m_Guid))
            {
                return "P" + placeable.m_Guid;
            }
            ObjectGuid guid = go.GetComponent<ObjectGuid>();
            if (guid != null && !string.IsNullOrEmpty(guid.Get()))
            {
                return "G" + guid.Get();
            }
            // The path only for decorations: other placed things (a new campfire, a snare) are not in the scene
            // on the other side, and a same-named object there would be moved instead.
            if (go.GetComponent<DecorationItem>() == null)
            {
                return "";
            }
            return "N" + GetPath(go);
        }

        public static GameObject Find(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length < 2)
            {
                return null;
            }
            string key = id.Substring(1);
            if (id[0] == 'N')
            {
                return FindByPath(key);
            }
            if (id[0] == 'P')
            {
                // Not through PlaceableManager.s_Placeables: reading it from the mod froze the game.
                foreach (Placeable placeable in UnityEngine.Object.FindObjectsOfType<Placeable>(true))
                {
                    if (placeable != null && placeable.m_Guid == key)
                    {
                        return placeable.gameObject;
                    }
                }
                return null;
            }
            return ObjectGuidManager.Lookup(key);
        }

        public static void Send(GameObject go, bool visible)
        {
            if (go == null || !MyMod.InOnline())
            {
                return;
            }
            if (!IsSyncable(go))
            {
                return;
            }
            string id = GetId(go);
            if (id == "")
            {
                return;
            }
            Vector3 pos = go.transform.position;
            Quaternion rot = go.transform.rotation;
            MelonLogger.Msg("[DecorationSync] Send " + id + " visible " + visible + " at " + pos);
            if (MyMod.iAmHost)
            {
                Record(id, pos, rot, visible, MyMod.level_guid);
                ServerSend.DECORATIONMOVE(0, id, pos, rot, visible, MyMod.level_guid, MyMod.levelid);
            }
            else if (MyMod.sendMyPosition)
            {
                using (Packet packet = new Packet((int)ClientPackets.DECORATIONMOVE))
                {
                    packet.Write(id);
                    packet.Write(pos);
                    packet.Write(rot);
                    packet.Write(visible);
                    packet.Write(MyMod.level_guid);
                    packet.Write(MyMod.levelid);
                    MyMod.SendUDPData(packet);
                }
            }
        }

        public static void SendWithChildren(DecorationItem item, bool visible, int depth = 0)
        {
            if (item == null || depth > 16)
            {
                return;
            }
            Send(item.gameObject, visible);
            if (item.m_DecorationChildren != null)
            {
                foreach (DecorationItem child in item.m_DecorationChildren)
                {
                    SendWithChildren(child, visible, depth + 1);
                }
            }
        }

        // Gear placed in the world (bedroll, lantern...) is synced as a dropped item, and objects that are not
        // in a loaded scene (the player rig, prefab assets) are not furniture.
        public static bool IsSyncable(GameObject go)
        {
            if (go == null || go.GetComponent<GearItem>() != null)
            {
                return false;
            }
            UnityEngine.SceneManagement.Scene S = go.scene;
            return S.IsValid() && S.isLoaded && S.name != "DontDestroyOnLoad";
        }

        public static void Receive(string id, Vector3 pos, Quaternion rot, bool visible, string levelGuid, int levelId)
        {
            if ((levelId != -1 && levelId != MyMod.levelid) || levelGuid != MyMod.level_guid)
            {
                return;
            }
            try
            {
                GameObject go = Find(id);
                if (go == null || !IsSyncable(go))
                {
                    MelonLogger.Msg("[DecorationSync] Object " + id + " not found");
                    return;
                }
                if (Vector3.Distance(go.transform.position, pos) < 0.01f && Quaternion.Angle(go.transform.rotation, rot) < 0.5f && go.activeSelf == visible)
                {
                    return;
                }
                MelonLogger.Msg("[DecorationSync] Move " + go.name + " visible " + visible + " to " + pos);
                go.transform.SetPositionAndRotation(pos, rot);
                // Only hide here. Activating an object the game keeps inactive could start its setup again.
                if (!visible && go.activeSelf)
                {
                    go.SetActive(false);
                }
                MelonLogger.Msg("[DecorationSync] Moved " + go.name);
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[DecorationSync] Could not move " + id + ": " + e.Message);
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(DecorationItem), "FinalizePlacement", new Type[] { typeof(bool) })]
        internal static class DecorationItem_FinalizePlacement
        {
            private static void Postfix(DecorationItem __instance, bool success)
            {
                if (success)
                {
                    SendWithChildren(__instance, true);
                }
            }
        }

        public static void SendPlaced(GameObject go)
        {
            if (go == null)
            {
                return;
            }
            DecorationItem item = go.GetComponent<DecorationItem>();
            if (item != null)
            {
                SendWithChildren(item, go.activeInHierarchy);
            } else
            {
                Send(go, go.activeInHierarchy);
            }
        }

        // A cabinet showed red everywhere while moving it; this lists what it touches when the move starts:
        // its own child objects, and colliders of other objects inside its bounds.
        private static void LogPlacementBlockers(GameObject go)
        {
            try
            {
                Bounds B = new Bounds(go.transform.position, Vector3.zero);
                List<string> Own = new List<string>();
                foreach (Collider C in go.GetComponentsInChildren<Collider>(true))
                {
                    B.Encapsulate(C.bounds);
                    if (C.gameObject != go)
                    {
                        Own.Add(C.gameObject.name + (C.enabled && C.gameObject.activeInHierarchy ? "" : " (off)"));
                    }
                }
                List<string> Other = new List<string>();
                foreach (Collider C in Physics.OverlapBox(B.center, B.extents * 0.95f, go.transform.rotation))
                {
                    if (C != null && !C.transform.IsChildOf(go.transform))
                    {
                        Other.Add(C.gameObject.name + "@" + C.gameObject.layer);
                    }
                }
                MelonLogger.Msg("[DecorationSync] " + go.name + " children " + Own.Count + ": " + string.Join(", ", Own.ToArray())
                    + " | touching " + Other.Count + ": " + string.Join(", ", Other.ToArray()));
            }
            catch (Exception e)
            {
                MelonLogger.Msg("[DecorationSync] Could not list placement blockers: " + e.Message);
            }
        }

        private static void RememberStart(GameObject objectToPlace)
        {
            if (objectToPlace == null || !MyMod.InOnline() || !IsSyncable(objectToPlace))
            {
                return;
            }
            int Key = objectToPlace.GetInstanceID();
            StartIds.Remove(Key);
            string Id = GetId(objectToPlace);
            if (StartIds.Count > 256)
            {
                StartIds.Clear();
            }
            StartIds[Key] = Id;
            MelonLogger.Msg("[DecorationSync] Start placing " + Id);
            LogPlacementBlockers(objectToPlace);
        }

        [HarmonyLib.HarmonyPatch(typeof(PlayerManager), "StartPlaceMesh", new Type[] { typeof(GameObject), typeof(float), typeof(PlaceMeshFlags), typeof(PlaceMeshRules) })]
        internal static class PlayerManager_StartPlaceMesh
        {
            private static void Prefix(GameObject objectToPlace)
            {
                RememberStart(objectToPlace);
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(PlayerManager), "StartPlaceMesh", new Type[] { typeof(GameObject), typeof(PlaceMeshFlags), typeof(PlaceMeshRules) })]
        internal static class PlayerManager_StartPlaceMesh2
        {
            private static void Prefix(GameObject objectToPlace)
            {
                RememberStart(objectToPlace);
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(PlayerManager), "PlaceMeshInWorld", new Type[] { })]
        internal static class PlayerManager_PlaceMeshInWorld
        {
            private static void Prefix(PlayerManager __instance, out GameObject __state)
            {
                __state = __instance.m_ObjectToPlace;
            }

            private static void Postfix(GameObject __state)
            {
                if (__state != null && MyMod.InOnline())
                {
                    SendPlaced(__state);
                }
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(DecorationItem), "OnPickup", new Type[] { })]
        internal static class DecorationItem_OnPickup
        {
            private static void Postfix(DecorationItem __instance)
            {
                if (__instance != null && !__instance.gameObject.activeInHierarchy)
                {
                    Send(__instance.gameObject, false);
                }
            }
        }
    }
}
