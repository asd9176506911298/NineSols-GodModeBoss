using HarmonyLib;

namespace GodModeBoss;

[HarmonyPatch]
public class Patches {

    //[HarmonyPatch(typeof(Player), nameof(Player.SetStoryWalk))]
    //[HarmonyPrefix]
    //private static bool PatchStoryWalk(ref float walkModifier) {
    //    walkModifier = 1.0f;

    //    return true; // the original method should be executed
    //}

}

//public static class MonsterBasePatcher {
//    [HarmonyPrefix]
//    public static bool UpdateAnimatorSpeed(ref MonsterBase __instance) {
//        if (GodModeBoss.Instance.phaseCycleButton.Value)
//            return false;

//        return true;
//    }
//}