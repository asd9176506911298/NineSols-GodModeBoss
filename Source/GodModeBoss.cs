using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using NineSolsAPI;
using UnityEngine;
using System.Collections;
using System.Reflection;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace GodModeBoss;

[BepInDependency(NineSolsAPICore.PluginGUID)]
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public class GodModeBoss : BaseUnityPlugin {
    public static GodModeBoss Instance { get; private set; }
    private Harmony mainHarmony = null!;
    private Harmony monsterBasePatcherHarmony = null!;
    private ConfigEntry<bool>[] phaseButtons;
    private ConfigEntry<bool> invincibleButton;
    private ConfigEntry<bool> bossSpeedUpButton;
    public ConfigEntry<bool> phaseCycleButton;

    private bool isCyclingPhases = false;
    private CancellationTokenSource? phaseCycleCancellationTokenSource;

    private void Awake() {
        Log.Init(Logger);
        RCGLifeCycle.DontDestroyForever(gameObject);

        // Initialize the main Harmony instance for GodModeBoss and patch all
        mainHarmony = Harmony.CreateAndPatchAll(typeof(GodModeBoss).Assembly);

        // Additional Harmony instance for conditional MonsterBase patching
        monsterBasePatcherHarmony = new Harmony("MonsterBasePatcher");

        // Check if UpdateAnimatorSpeed exists in MonsterBase and apply patch if it does
        var method = AccessTools.Method(typeof(MonsterBase), "UpdateAnimatorSpeed");
        if (method != null) {
            monsterBasePatcherHarmony.Patch(method, prefix: new HarmonyMethod(typeof(MonsterBasePatcher), nameof(MonsterBasePatcher.UpdateAnimatorSpeed)));
            Logger.LogInfo("UpdateAnimatorSpeed patch applied.");
        } else {
            Logger.LogInfo("UpdateAnimatorSpeed method not found. Skipping patch.");
        }

        // Initialize config entries for phase buttons
        phaseButtons = new[] {
        CreatePhaseButton("Phase 1", 0),
        CreatePhaseButton("Phase 2", 1),
        CreatePhaseButton("Phase 3", 2)
    };

        invincibleButton = Config.Bind("Actions", "Toggle Invincibility", false, "Toggle invincibility for all bosses");
        invincibleButton.SettingChanged += (_, _) => { if (invincibleButton.Value) ToggleInvincibility(); invincibleButton.Value = false; };

        bossSpeedUpButton = Config.Bind("Actions", "Boss SpeedUp", false, "Toggle speed-up for Eigong phases 1 to 3");
        bossSpeedUpButton.SettingChanged += (_, _) => { ToggleBossSpeedUp(bossSpeedUpButton.Value); };

        phaseCycleButton = Config.Bind("Actions", "Cycle Phases 1~3", false, "Automatically cycle phases 1 to 3 every 5 seconds");
        phaseCycleButton.SettingChanged += (_, _) => { TogglePhaseCycle(phaseCycleButton.Value); };

        // Ensure all buttons are initialized to false
        foreach (var button in phaseButtons) {
            button.Value = false;
        }
        invincibleButton.Value = false;
        bossSpeedUpButton.Value = false;
        phaseCycleButton.Value = false;

        Instance = this;

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

    private void ToggleBossSpeedUp(bool enable) {
        foreach (var boss in MonsterManager.Instance.monsterDict.Values) {
            if (boss.tag == "Boss") {
                if (enable) {
                    // Apply a 1.5x multiplier each time, stacking the speed
                    boss.animator.speed *= 1.15f;
                } else {
                    // Reset to the original speed
                    boss.animator.speed = 1;
                }

                ToastManager.Toast($"SpeedUp Multiplier Applied: {boss.animator.speed} for {boss}");
            }
        }
    }


    private void SetEigongSpeed(MonsterBase monster, float speedMultiplier) {
        monster.animator.speed = speedMultiplier;
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

        //monster.VelX = 0f;
    }

    private AttackSensor[] GetAttackSensors(MonsterBase monster) {
        var field = typeof(MonsterBase).GetField("attackSensors", BindingFlags.NonPublic | BindingFlags.Instance) ??
                    typeof(MonsterBase).GetField("_attackSensors", BindingFlags.NonPublic | BindingFlags.Instance);

        return field?.GetValue(monster) as AttackSensor[] ?? new AttackSensor[0];
    }

    private async UniTaskVoid CyclePhases() {
        int phaseIndex = 0;
        isCyclingPhases = true;

        // Create a CancellationToken to stop the loop
        var cancellationToken = phaseCycleCancellationTokenSource?.Token ?? CancellationToken.None;

        while (isCyclingPhases) {
            SetPhase(phaseIndex);
            ToggleBossSpeedUp(bossSpeedUpButton.Value);

            // Cycle through phases 0, 1, 2
            phaseIndex = (phaseIndex + 1) % 3;

            // Wait for 1 second between phase changes
            await UniTask.Delay(1000, cancellationToken: cancellationToken);

            // Check if cancellation was requested
            if (cancellationToken.IsCancellationRequested) {
                break;
            }
        }
    }

    private void TogglePhaseCycle(bool enable) {
        if (enable) {
            // Start the UniTask phase cycle if not already running
            if (!isCyclingPhases) {
                // Create a new CancellationTokenSource and start the cycle
                phaseCycleCancellationTokenSource = new CancellationTokenSource();
                CyclePhases().Forget();
            }
        } else {
            // Stop cycling phases
            isCyclingPhases = false;
            phaseCycleCancellationTokenSource?.Cancel();
            phaseCycleCancellationTokenSource = null;
        }
    }

        private void OnDestroy() {
        // Unpatch both Harmony instances
        mainHarmony.UnpatchSelf();
        monsterBasePatcherHarmony.UnpatchSelf();
    }
}
