using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using NineSolsAPI;
using UnityEngine;

namespace GodModeBoss;

[BepInDependency(NineSolsAPICore.PluginGUID)]
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class GodModeBoss : BaseUnityPlugin {
    // https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/4_configuration.html
    private ConfigEntry<bool> enableSomethingConfig = null!;
    private ConfigEntry<KeyboardShortcut> somethingKeyboardShortcut = null!;

    private Harmony harmony = null!;

    private void Awake() {
        Log.Init(Logger);
        RCGLifeCycle.DontDestroyForever(gameObject);

        harmony = Harmony.CreateAndPatchAll(typeof(GodModeBoss).Assembly);

        KeybindManager.Add(this, TestMethod, () => somethingKeyboardShortcut.Value);

        Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
    }


    private void TestMethod() {
        if (!enableSomethingConfig.Value) return;
        ToastManager.Toast("Shortcut activated");

    }

    private void OnDestroy() {

        harmony.UnpatchSelf();
    }
}