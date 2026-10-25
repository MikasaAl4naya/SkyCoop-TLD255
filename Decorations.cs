using System;
using System.Collections.Generic;
using UnityEngine;
using MelonLoader;
using GameServer;
using Il2Cpp;
using Il2CppTLD.Placement;

namespace SkyCoop
{
    // Safehouse customization (2.55): moving furniture and decorations.
    //
    // When a player finishes placing a decoration, its position is sent to everyone in the same scene,
    // who move their copy of the object. Picking an object up hides it for the others until it is placed again.
    // Objects are matched by the Placeable guid or the ObjectGuid, both baked into the scene, so it only
    // works for objects that exist on both sides; a decoration made or brought from elsewhere is not created.
    public static class DecorationSync
    {
        public static string GetId(GameObject go)
        {
            if (go == null)
            {
                return "";
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
            return "";
        }

        public static GameObject Find(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length < 2)
            {
                return null;
            }
            string key = id.Substring(1);
            if (id[0] == 'P')
            {
                PlaceableManager.PlaceableInfo info;
                if (PlaceableManager.s_Placeables != null && PlaceableManager.s_Placeables.TryGetValue(key, out info) && info != null && info.m_Handle != null)
                {
                    return info.m_Handle.gameObject;
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
            string id = GetId(go);
            if (id == "")
            {
                return;
            }
            Vector3 pos = go.transform.position;
            Quaternion rot = go.transform.rotation;
            if (MyMod.iAmHost)
            {
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

        public static void Receive(string id, Vector3 pos, Quaternion rot, bool visible, string levelGuid, int levelId)
        {
            if (levelId != MyMod.levelid || levelGuid != MyMod.level_guid)
            {
                return;
            }
            GameObject go = Find(id);
            if (go == null)
            {
                MelonLogger.Msg("[DecorationSync] Object " + id + " not found");
                return;
            }
            go.transform.SetPositionAndRotation(pos, rot);
            if (go.activeSelf != visible)
            {
                go.SetActive(visible);
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
