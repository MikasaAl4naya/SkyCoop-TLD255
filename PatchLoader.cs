#if (!DEDICATED)
using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;

namespace SkyCoop
{
    // MelonLoader normally applies every [HarmonyPatch] in the assembly in one go. One patch whose
    // target method Hinterland renamed is then enough to abort the whole mod load, which is how a
    // single game update used to take the mod offline entirely.
    //
    // The assembly opts out of that (see HarmonyDontPatchAll in AssemblyInfo.cs) and patches class
    // by class instead: a patch that no longer fits the game is reported and skipped, and everything
    // else still runs. The mod loses one feature rather than all of them.
    public static class PatchLoader
    {
        public static readonly List<string> FailedPatches = new List<string>();
        public static int AppliedPatches { get; private set; }

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (harmony == null)
            {
                MelonLogger.Error("[SkyCoop] No Harmony instance to patch with.");
                return;
            }

            AppliedPatches = 0;
            FailedPatches.Clear();

            Type[] types;
            try
            {
                types = typeof(PatchLoader).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = Array.FindAll(e.Types, t => t != null);
            }

            foreach (Type type in types)
            {
                if (!HasHarmonyPatchAttribute(type))
                {
                    continue;
                }

                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    AppliedPatches++;
                }
                catch (Exception e)
                {
                    string name = type.FullName + ": " + Describe(e);
                    FailedPatches.Add(name);
                    MelonLogger.Warning("[SkyCoop] Skipped patch " + name);
                }
            }

            if (FailedPatches.Count == 0)
            {
                MelonLogger.Msg(System.ConsoleColor.Green, "[SkyCoop] Applied " + AppliedPatches + " patch classes.");
            }
            else
            {
                MelonLogger.Warning("[SkyCoop] Applied " + AppliedPatches + " patch classes, " + FailedPatches.Count
                    + " did not fit this game version. The features behind them are disabled; the rest of the mod works.");
                MelonLogger.Warning("[SkyCoop] This usually means the game updated. Check for a newer SkyCoop build.");
            }
        }

        private static bool HasHarmonyPatchAttribute(Type type)
        {
            try
            {
                return type.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), true).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static string Describe(Exception e)
        {
            while (e.InnerException != null)
            {
                e = e.InnerException;
            }
            return e.Message;
        }
    }
}
#endif
