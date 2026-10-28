using System;
using System.IO;
using MelonLoader;
using Il2Cpp;

namespace SkyCoop
{
    // Death without a duplicate inventory.
    //
    // On death everything goes into the backpack left on the body (MyMod.DropAll), but the save on disk still is the
    // one from before the death. A player who quit (or crashed) on the death screen loaded that save with the full
    // inventory and took everything from the backpack a second time. The death is marked in a small file next to the
    // mod settings; a save that is loaded with the mark goes through the respawn, and a save written after the respawn
    // removes the mark.
    public static class DeathGuard
    {
        private static bool s_Respawned = false;
        // AutoTest: report the respawn instead of doing it.
        public static string DryRunResult = null;

        private static string MarkFile
        {
            get { return Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "SkyCoopDeath.txt"); }
        }

        private static string SaveName()
        {
            try
            {
                return SaveGameSystem.GetCurrentSaveName() ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        // MyMod.DropAll: the inventory is in the backpack now.
        public static void Mark()
        {
            try
            {
                string Name = SaveName();
                if (Name != "")
                {
                    File.WriteAllText(MarkFile, Name);
                    MelonLogger.Msg("[DeathGuard] Death marked for " + Name);
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[DeathGuard] " + e.Message);
            }
        }

        // MyMod.BeginRespawn: the inventory is the start gear now; the next save may remove the mark.
        public static void Respawned()
        {
            s_Respawned = true;
        }

        public static void OnSaved()
        {
            if (!s_Respawned || MyMod.IsDead)
            {
                return;
            }
            s_Respawned = false;
            try
            {
                if (File.Exists(MarkFile))
                {
                    File.Delete(MarkFile);
                    MelonLogger.Msg("[DeathGuard] Respawn saved, mark removed");
                }
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[DeathGuard] " + e.Message);
            }
        }

        // Once a second in the world: a save from before the death was loaded.
        public static void EverySecond()
        {
            if (s_Respawned || MyMod.IsDead || MyMod.PendingRespawn)
            {
                return;
            }
            try
            {
                if (!File.Exists(MarkFile))
                {
                    return;
                }
                string Name = SaveName();
                if (Name == "" || File.ReadAllText(MarkFile).Trim() != Name)
                {
                    return;
                }
                if (DryRunResult != null)
                {
                    DryRunResult = "would respawn " + Name;
                    File.Delete(MarkFile);
                    return;
                }
                MelonLogger.Msg("[DeathGuard] " + Name + " was saved before the death, respawning");
                CoopFeatures.Notify(Loc.IsRussian ? "Ты умер в прошлый раз: вещи остались в рюкзаке на месте смерти" : "You died last time: your things are in the backpack where you died");
                MyMod.BeginRespawn();
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[DeathGuard] " + e.Message);
            }
        }
    }
}
