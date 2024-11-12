using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using NineSolsAPI;
using UnityEngine;
using System.Reflection;

namespace GodModeBoss;

[BepInDependency(NineSolsAPICore.PluginGUID)]
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class GodModeBoss : BaseUnityPlugin {

    private Harmony harmony = null!;
    private ConfigEntry<bool>[] phaseButtons;
    private ConfigEntry<bool> invincibleButton;

    private void Awake() {
        Log.Init(Logger);
        RCGLifeCycle.DontDestroyForever(gameObject);

        harmony = Harmony.CreateAndPatchAll(typeof(GodModeBoss).Assembly);

        // Initialize config entries for phase buttons
        phaseButtons = new[] {
            CreatePhaseButton("Phase 1", 0),
            CreatePhaseButton("Phase 2", 1),
            CreatePhaseButton("Phase 3", 2)
        };

        invincibleButton = Config.Bind("Actions", "Toggle Invincibility", false, "Toggle invincibility for all bosses");
        invincibleButton.SettingChanged += (_, _) => { if (invincibleButton.Value) ToggleInvincibility(); invincibleButton.Value = false; };

        // Ensure all buttons are initialized to false
        foreach (var button in phaseButtons) {
            button.Value = false;
        }
        invincibleButton.Value = false;

        Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
    }

    private ConfigEntry<bool> CreatePhaseButton(string name, int phaseIndex) {
        var entry = Config.Bind("Actions", $"Goto {name}", false, $"Trigger {name} for all bosses");
        entry.SettingChanged += (_, _) => { if (entry.Value) SetPhase(phaseIndex); entry.Value = false; };
        entry.Value = false; // Ensure the button is initialized to false
        return entry;
    }

    private void SetPhase(int phaseIndex) {
        foreach (var boss in MonsterManager.Instance.monsterDict.Values) {
            if (boss.tag == "Boss") {
                GotoPhase(boss, phaseIndex);
                ToastManager.Toast($"Goto Phase {phaseIndex + 1} Name:{boss}");
            }
        }
    }

    private void ToggleInvincibility() {
        foreach (var boss in MonsterManager.Instance.monsterDict.Values) {
            if (boss.tag == "Boss" && TryGetMonsterStat(boss, out var monsterStat)) {
                monsterStat.IsLockPostureInvincible = !monsterStat.IsLockPostureInvincible;
                ToastManager.Toast($"Invincible:{monsterStat.IsLockPostureInvincible} Name:{boss}");
            }
        }
    }

    private bool TryGetMonsterStat(MonsterBase monster, out MonsterStat monsterStat) {
        var field = typeof(MonsterBase).GetField("monsterStat") ?? typeof(MonsterBase).GetField("_monsterStat");
        monsterStat = field?.GetValue(monster) as MonsterStat;
        return monsterStat != null;
    }

    private void GotoPhase(MonsterBase monster, int phaseIndex) {
        monster.PhaseIndex = phaseIndex;
        monster.animator.SetInteger(Animator.StringToHash("PhaseIndex"), phaseIndex);
        monster.postureSystem.RestorePosture();
        monster.monsterCore.EnablePushAway();
        SingletonBehaviour<GameCore>.Instance.monsterHpUI.CurrentBossHP?.TempShow();
        monster.monsterCore.DisablePushAway();

        var sensors = GetAttackSensors(monster);
        foreach (var sensor in sensors) {
            sensor?.ClearQueue();
        }

        monster.VelX = 0f;
    }

    private AttackSensor[] GetAttackSensors(MonsterBase monster) {
        // Try to retrieve attack sensors field by either name
        var field = typeof(MonsterBase).GetField("attackSensors", BindingFlags.NonPublic | BindingFlags.Instance) ??
                    typeof(MonsterBase).GetField("_attackSensors", BindingFlags.NonPublic | BindingFlags.Instance);

        return field?.GetValue(monster) as AttackSensor[] ?? new AttackSensor[0];
    }

    private void OnDestroy() {
        harmony.UnpatchSelf();
    }
}
