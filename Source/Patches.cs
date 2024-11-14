using HarmonyLib;
using NineSolsAPI;
using System;

namespace GodModeBoss;

[HarmonyPatch]
public class Patches {

    //[HarmonyPatch(typeof(Player), nameof(Player.SetStoryWalk))]
    //[HarmonyPrefix]
    //private static bool PatchStoryWalk(ref float walkModifier) {
    //    walkModifier = 1.0f;

    //    return true; // the original method should be executed
    //}

    [HarmonyPatch(typeof(PlayerAbilityData), nameof(PlayerAbilityData.CanUseCheck))]
    [HarmonyPrefix]
    private static bool PatchCanUseCheck(ref PlayerAbilityData __instance, ref Object effectProvider) {
        // Check if the ability name contains "Revive Jade" and GodModeBoss Instance and the isReviveJade value are valid
        if (__instance.name.Contains("Revive Jade") && GodModeBoss.Instance?.isReviveJade?.Value == true) {
            return false; // Prevent original method from executing if isReviveJade is true
        }

        return true; // Execute the original method if conditions are not met
    }

}