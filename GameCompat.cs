#if (!DEDICATED)
using System;
using UnityEngine;
using Il2Cpp;
using Il2CppTLD.PDID;
using Il2CppTLD.Gear;
using Il2CppTLD.Scenes;
using Il2CppTLD.IntBackedUnit;
using Il2CppTLD.Gameplay;
using Il2CppInterop.Runtime;

namespace SkyCoop
{
    // Everything in here exists because The Long Dark moved, renamed or deleted an API that the
    // mod used to call directly. Keeping the shims in one file means that the next time the game
    // shifts under us there is exactly one place to look.
    public static class GameCompat
    {
        // GearItem.m_GearName is gone; a gear item is identified by its Unity object name now.
        // Instances carry a "(Clone)" suffix, and roughly two hundred call sites compare the result
        // against a plain "GEAR_..." name, so the suffix is stripped here rather than trusting the
        // game's own helper to keep doing it.
        public static string StripCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }
            int clone = name.IndexOf("(Clone)", StringComparison.Ordinal);
            if (clone >= 0)
            {
                name = name.Substring(0, clone);
            }
            return name.Trim();
        }

        public static string GetGearName(this GearItem gi)
        {
            if (gi == null)
            {
                return "";
            }
            return StripCloneSuffix(gi.name);
        }

        public static void SetGearName(this GearItem gi, string name)
        {
            if (gi == null)
            {
                return;
            }
            gi.name = name;
        }

        // GearItemObject is a struct wrapping a GearItem plus the name it was spawned under.
        public static string GetGearName(this GearItemObject gio)
        {
            if (!string.IsNullOrEmpty(gio.m_GearItemName))
            {
                return StripCloneSuffix(gio.m_GearItemName);
            }
            return gio.m_GearItem != null ? StripCloneSuffix(gio.m_GearItem.name) : "";
        }

        // ObjectGuid.Set was replaced by the PDID table registration helpers.
        public static void Set(this ObjectGuid guid, string value)
        {
            if (guid == null)
            {
                return;
            }
            ObjectGuid.MaybeAttachObjectGuidAddOrReplace(guid.gameObject, value);
        }

        // GearItem.m_DisplayName / m_LocalizedDisplayName were folded into a DisplayName property.
        public static string GetDisplayName(this GearItem gi)
        {
            if (gi == null)
            {
                return "";
            }
            return gi.DisplayName;
        }

        // PlayerManager.m_InteractiveObjectUnderCrosshair became a lookup that takes a range; the
        // game no longer exposes the one it uses itself, and this matches its reach in practice.
        public const float InteractionRange = 2.5f;

        public static GameObject GetInteractiveObjectUnderCrosshair(this PlayerManager pm)
        {
            if (pm == null)
            {
                return null;
            }
            return pm.GetInteractiveObjectUnderCrosshairs(InteractionRange);
        }

        // Weights and liquid volumes are integer-backed structs now. The mod's wire format stays
        // floating point, so these are the two conversions it needs.
        public static ItemWeight Kilograms(float kilograms)
        {
            return ItemWeight.FromKilograms(kilograms);
        }

        public static float ToKilograms(this ItemWeight weight)
        {
            return weight.ToQuantity(1f);
        }

        public static ItemLiquidVolume Liters(float liters)
        {
            return ItemLiquidVolume.FromLiters(liters);
        }

        public static float ToLiters(this ItemLiquidVolume volume)
        {
            return volume.ToQuantity(1f);
        }

        // A FirstPersonItem no longer lets anyone assign its mesh id or vp_FPSWeapon - the game
        // resolves both from m_FirstPersonObjectName - and its wield audio is a Wwise event asset
        // instead of an event name. Copying the whole first person setup off a stock item that
        // already looks and sounds right is the only supported way to build one at runtime.
        public static void CopyFirstPersonItemSetup(FirstPersonItem target, string referenceGearName)
        {
            if (target == null)
            {
                return;
            }
            GameObject reference = MyMod.GetGearItemObject(referenceGearName);
            FirstPersonItem source = reference != null ? reference.GetComponent<FirstPersonItem>() : null;
            if (source == null)
            {
                return;
            }
            target.m_PlayerStateTransitions = source.m_PlayerStateTransitions;
            target.m_WieldAudioEvent = source.m_WieldAudioEvent;
            target.m_UnwieldAudioEvent = source.m_UnwieldAudioEvent;
            if (string.IsNullOrEmpty(target.m_FirstPersonObjectName))
            {
                target.m_FirstPersonObjectName = source.m_FirstPersonObjectName;
            }
        }

        // GearItem.GetFPSMeshID moved onto the first person item.
        public static int GetFPSMeshID(this GearItem gi)
        {
            if (gi == null || gi.m_FirstPersonItem == null)
            {
                return (int)FPSMeshID.None;
            }
            return gi.m_FirstPersonItem.GetMeshID();
        }

        // Panel_Inventory holds grid entries rather than gear items now.
        public static GearItem GetSelectedGearItem(this Panel_Inventory panel)
        {
            if (panel == null || panel.m_FilteredInventoryList == null)
            {
                return null;
            }
            int index = panel.m_SelectedItemIndex + panel.m_FirstItemDisplayedIndex;
            if (index < 0 || index >= panel.m_FilteredInventoryList.Count)
            {
                return null;
            }
            return panel.m_FilteredInventoryList[index].m_GearItem;
        }

        // PlayerManager.ProcessInspectablePickupItem was folded into the ordinary pickup path.
        public static bool ProcessPickupItemInteractionCompat(this PlayerManager pm, GearItem item)
        {
            if (pm == null || item == null)
            {
                return false;
            }
            return pm.ProcessPickupItemInteraction(item, false, false, false);
        }

        // InstantiateItemInPlayerInventory takes a prefab rather than a name now.
        public static GearItem InstantiateItemInPlayerInventory(this PlayerManager pm, string gearName, int units)
        {
            if (pm == null)
            {
                return null;
            }
            GearItem prefab = MyMod.GetGearItemPrefab(gearName);
            if (prefab == null)
            {
                MelonLoader.MelonLogger.Warning("[SkyCoop] No gear prefab named " + gearName);
                return null;
            }
            return pm.InstantiateItemInPlayerInventory(prefab, units, 1f, PlayerManager.InventoryInstantiateFlags.None);
        }

        // Container.CancelSearch and Harvestable.CancelHarvest are gone; closing the container and
        // cancelling the progress bar is what the game does internally now.
        public static void CancelSearch(this Container container)
        {
            if (container == null)
            {
                return;
            }
            container.BeginContainerClose();
        }

        public static void CancelHarvest(this Harvestable harvestable)
        {
            Panel_GenericProgressBar bar = InterfaceManager.GetPanel<Panel_GenericProgressBar>();
            if (bar != null)
            {
                bar.Cancel();
            }
        }

        // GearItem.Deserialize takes a proxy rather than the serialized string.
        public static void DeserializeFromJson(this GearItem gi, string json)
        {
            if (gi == null || string.IsNullOrEmpty(json))
            {
                return;
            }
            GearItemSaveDataProxy proxy = Utils.DeserializeObject<GearItemSaveDataProxy>(json);
            if (proxy != null)
            {
                gi.Deserialize(proxy, false);
            }
        }

        // Inventory.GetClosestMatchStackable is gone; this walks the inventory the same way it did.
        public static GearItem GetClosestMatchStackable(this Inventory inventory, string gearName, float normalizedCondition)
        {
            if (inventory == null || inventory.m_Items == null)
            {
                return null;
            }
            GearItem best = null;
            float bestDelta = float.MaxValue;
            for (int i = 0; i < inventory.m_Items.Count; i++)
            {
                GearItem candidate = inventory.m_Items[i];
                if (candidate == null || candidate.m_StackableItem == null)
                {
                    continue;
                }
                if (candidate.GetGearName() != StripCloneSuffix(gearName))
                {
                    continue;
                }
                float delta = Mathf.Abs(candidate.GetNormalizedCondition() - normalizedCondition);
                if (delta > candidate.m_StackableItem.m_StackConditionDifferenceConstraint)
                {
                    continue;
                }
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = candidate;
                }
            }
            return best;
        }

        // Utils' texture cache was removed, so the mod keeps its own for the photos it renders.
        private static readonly System.Collections.Generic.Dictionary<string, Texture2D> s_TextureCache =
            new System.Collections.Generic.Dictionary<string, Texture2D>();

        public static Texture2D GetCachedTexture(string key)
        {
            Texture2D tex;
            if (s_TextureCache.TryGetValue(key, out tex) && tex != null)
            {
                return tex;
            }
            return null;
        }

        public static void CacheTexture(string key, Texture2D texture)
        {
            s_TextureCache[key] = texture;
        }

        // FirstPersonItem.m_FPSWeapon became a Try-pattern.
        public static vp_FPSWeapon GetFPSWeapon(this FirstPersonItem fpi)
        {
            if (fpi == null)
            {
                return null;
            }
            vp_FPSWeapon weapon = null;
            fpi.TryGetFPSWeapon(out weapon);
            return weapon;
        }
    }

    // The mod stores a handful of blobs of its own next to the game's save data. The slot-based
    // entry points were replaced by ones that take a resolved SlotData.
    public static class SaveSlotCompat
    {
        private static SlotData ResolveSlot(SaveSlotType gameMode, Episode episode, uint gameId, string name)
        {
            SlotData slot = SaveGameSlots.GetSaveSlotFromName(name);
            if (slot != null)
            {
                return slot;
            }
            slot = SaveGameSlots.CreateSlot(name, gameMode, gameId, episode);
            if (slot != null)
            {
                SaveGameSlots.AddSlotData(slot);
            }
            return slot;
        }

        public static bool SaveDataToSlot(SaveSlotType gameMode, Episode episode, uint gameId, string name, string key, string data)
        {
            try
            {
                SlotData slot = ResolveSlot(gameMode, episode, gameId, name);
                if (slot == null)
                {
                    return false;
                }
                Il2CppSystem.String payload = data ?? "";
                return SaveGameSlots.SaveDataToSlot(slot, key, payload);
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning("[SkyCoop] Could not write \"" + key + "\" to save slot " + name + ": " + e.Message);
                return false;
            }
        }

        public static string LoadDataFromSlot(string name, string key)
        {
            try
            {
                Il2CppSystem.String data = null;
                if (SaveGameSlots.TryLoadDataFromSlot<Il2CppSystem.String>(name, key, out data) && data != null)
                {
                    return data;
                }
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning("[SkyCoop] Could not read \"" + key + "\" from save slot " + name + ": " + e.Message);
            }
            return null;
        }
    }

    // Regions are data assets in the current game, so the mod resolves them through scene names.
    // That is also what makes the Far Territory regions work without a hardcoded enum entry per DLC
    // drop: a region the mod has never heard of still round-trips as its scene name.
    public static class RegionCompat
    {
        public static Shared.GameRegion FromSceneSet(SceneSet set)
        {
            if (set == null)
            {
                return Shared.GameRegion.RandomRegion;
            }
            Shared.GameRegion byName = Shared.GetRegionForSceneName(set.name);
            if (byName != Shared.GameRegion.RandomRegion)
            {
                return byName;
            }
            // SceneSet assets are not always named after their base scene; the region group name is
            // the next best identifier.
            try
            {
                return Shared.GetRegionForSceneName(set.RegionGroupName);
            }
            catch
            {
                return Shared.GameRegion.RandomRegion;
            }
        }

        public static Shared.GameRegion FromSceneName(string sceneName)
        {
            return Shared.GetRegionForSceneName(sceneName);
        }

        public static Shared.GameRegion FromRegionSpecification(RegionSpecification spec)
        {
            if (spec == null)
            {
                return Shared.GameRegion.RandomRegion;
            }
            Shared.GameRegion byName = Shared.GetRegionForSceneName(spec.name);
            if (byName != Shared.GameRegion.RandomRegion)
            {
                return byName;
            }
            try
            {
                if (spec.m_RegionDescription != null)
                {
                    return Shared.GetRegionForSceneName(spec.m_RegionDescription.m_LocalizationID);
                }
            }
            catch
            {
            }
            return Shared.GameRegion.RandomRegion;
        }

        // Regions are assets, so the only way back from an id is to look through the ones the game
        // has loaded. Cached because this runs while the region select menu is being built.
        private static readonly System.Collections.Generic.Dictionary<Shared.GameRegion, RegionSpecification> s_SpecCache =
            new System.Collections.Generic.Dictionary<Shared.GameRegion, RegionSpecification>();

        public static RegionSpecification FindRegionSpecification(Shared.GameRegion region)
        {
            RegionSpecification cached;
            if (s_SpecCache.TryGetValue(region, out cached) && cached != null)
            {
                return cached;
            }

            try
            {
                Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<RegionSpecification> all =
                    Resources.FindObjectsOfTypeAll<RegionSpecification>();
                foreach (RegionSpecification spec in all)
                {
                    Shared.GameRegion resolved = FromRegionSpecification(spec);
                    if (resolved == Shared.GameRegion.RandomRegion)
                    {
                        continue;
                    }
                    s_SpecCache[resolved] = spec;
                }
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning("[SkyCoop] Could not enumerate region specifications: " + e.Message);
            }

            s_SpecCache.TryGetValue(region, out cached);
            return cached;
        }

        public static string GetLocalizedName(Shared.GameRegion region)
        {
            RegionSpecification spec = FindRegionSpecification(region);
            if (spec != null)
            {
                string localized = spec.LocalizedRegionDescription;
                if (!string.IsNullOrEmpty(localized))
                {
                    return localized;
                }
            }
            return ExpeditionBuilder.GetRegionString((int)region);
        }

        public static Shared.GameRegion GetStartRegion()
        {
            return FromRegionSpecification(GameManager.m_StartRegion);
        }

        public static void SetStartRegion(Shared.GameRegion region)
        {
            RegionSpecification spec = FindRegionSpecification(region);
            if (spec != null)
            {
                GameManager.m_StartRegion = spec;
            }
            else
            {
                MelonLoader.MelonLogger.Warning("[SkyCoop] No region asset found for " + region + ", leaving the start region alone");
            }
        }

        // Prefers the weather system's idea of the current region and falls back to the active
        // scene, which is what keeps interiors attributed to the right region.
        public static Shared.GameRegion GetCurrentRegion()
        {
            try
            {
                UniStormWeatherSystem uniStorm = GameManager.GetUniStorm();
                if (uniStorm != null)
                {
                    Shared.GameRegion region = FromSceneSet(uniStorm.m_CurrentRegion);
                    if (region != Shared.GameRegion.RandomRegion)
                    {
                        return region;
                    }
                }
            }
            catch
            {
            }

            string level = MyMod.level_name;
            if (string.IsNullOrEmpty(level))
            {
                level = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            }
            return Shared.GetRegionForSceneName(level);
        }
    }

    // Experience modes are configuration assets now; the panel no longer carries the enum value and
    // ExperienceModeManager takes a config instead of a type.
    public static class ExperienceCompat
    {
        // Il2Cpp IList<T> does not surface Count; it comes from the ICollection<T> it implements.
        private static int CountOf(Il2CppSystem.Collections.Generic.IList<GameModeConfig> list)
        {
            if (list == null)
            {
                return 0;
            }
            Il2CppSystem.Collections.Generic.ICollection<GameModeConfig> collection =
                list.TryCast<Il2CppSystem.Collections.Generic.ICollection<GameModeConfig>>();
            return collection != null ? collection.Count : 0;
        }

        public static GameModeConfig FindGameModeConfig(ExperienceModeType type)
        {
            try
            {
                ExperienceModeManager manager = GameManager.GetExperienceModeManagerComponent();
                if (manager == null)
                {
                    return null;
                }
                Il2CppSystem.Collections.Generic.IList<GameModeConfig> modes = manager.GetAvailableGameModes();
                int count = CountOf(modes);
                for (int i = 0; i < count; i++)
                {
                    GameModeConfig config = modes[i];
                    if (config != null && config.m_XPMode != null && config.m_XPMode.m_ModeType == type)
                    {
                        return config;
                    }
                }
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning("[SkyCoop] Could not look up game mode " + type + ": " + e.Message);
            }
            return null;
        }

        public static void SetExperienceModeType(ExperienceModeType type)
        {
            ExperienceModeManager manager = GameManager.GetExperienceModeManagerComponent();
            GameModeConfig config = FindGameModeConfig(type);
            if (manager != null && config != null)
            {
                manager.SetGameModeConfig(config);
            }
            else
            {
                MelonLoader.MelonLogger.Warning("[SkyCoop] No game mode config for " + type + ", experience mode unchanged");
            }
        }

        // The experience panel builds its items from the available game modes in order, so the
        // selected index is what maps a menu item back to a mode.
        public static ExperienceModeType GetSelectedExperienceMode(Panel_SelectExperience panel)
        {
            if (panel == null)
            {
                return ExperienceModeType.Pilgrim;
            }
            try
            {
                Il2CppSystem.Collections.Generic.IList<GameModeConfig> modes =
                    GameManager.GetExperienceModeManagerComponent().GetAvailableGameModes();
                int index = panel.GetSelectedIndex();
                if (index >= 0 && index < CountOf(modes) && modes[index] != null && modes[index].m_XPMode != null)
                {
                    return modes[index].m_XPMode.m_ModeType;
                }
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Warning("[SkyCoop] Could not read the selected experience mode: " + e.Message);
            }
            return ExperienceModeType.Pilgrim;
        }

        public static bool IsStoryMode()
        {
            ExperienceModeType type = ExperienceModeManager.GetCurrentExperienceModeType();
            return type == ExperienceModeType.Story
                || type == ExperienceModeType.StoryFresh
                || type == ExperienceModeType.StoryHardened;
        }

        public static string GetLocalizedName(ExperienceModeType type)
        {
            GameModeConfig config = FindGameModeConfig(type);
            if (config != null && config.Name != null)
            {
                return config.Name.Text();
            }
            return type.ToString();
        }
    }

    // The GUID registry moved into Il2CppTLD.PDID.PdidTable. The old entry points are kept under
    // their original name so the several dozen call sites do not have to care.
    public static class ObjectGuidManager
    {
        public static GameObject Lookup(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return null;
            }
            return PdidTable.GetGameObject(guid);
        }

        public static string GenerateNewGuidString()
        {
            return PdidTable.GenerateNewID();
        }

        public static void UnRegisterGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return;
            }
            PdidTable.RuntimeUnregister(guid);
        }
    }

    // ds5678's KeyboardUtilities is no longer maintained for current game builds. The mod only ever
    // used it to poll raw key state, which UnityEngine.Input does just as well.
    public static class KeyboardUtilities
    {
        public static class InputManager
        {
            public static bool GetKey(KeyCode key)
            {
                try
                {
                    return Input.GetKey(key);
                }
                catch
                {
                    return false;
                }
            }

            public static bool GetKeyDown(KeyCode key)
            {
                try
                {
                    return Input.GetKeyDown(key);
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
#endif
