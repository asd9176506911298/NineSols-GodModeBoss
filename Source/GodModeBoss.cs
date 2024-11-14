using BepInEx.Configuration;
using BepInEx;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using NineSolsAPI;
using System.Reflection;
using System.Threading;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace GodModeBoss {
    [BepInDependency(NineSolsAPICore.PluginGUID)]
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class GodModeBoss : BaseUnityPlugin {
        public static GodModeBoss Instance { get; private set; }
        private Harmony mainHarmony = new Harmony("GodModeBoss.Harmony");
        private ConfigEntry<bool>[] phaseButtons = new ConfigEntry<bool>[0];
        private ConfigEntry<bool> invincibleButton;
        private ConfigEntry<bool> phaseCycleButton;
        private ConfigEntry<float> phaseSecond;
        public ConfigEntry<bool> isReviveJade;
        public ConfigEntry<bool> isToast;

        private bool isCyclingPhases;
        private CancellationTokenSource? phaseCycleCancellationTokenSource;

        private void Awake() {
            Log.Init(Logger);
            RCGLifeCycle.DontDestroyForever(gameObject);

            mainHarmony = Harmony.CreateAndPatchAll(typeof(GodModeBoss).Assembly);
            InitializeConfigEntries();

            SceneManager.sceneLoaded += OnSceneLoaded;

            Instance = this;
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }

        private async void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
            StopPhaseCycle(); // Stop any ongoing phase cycling on scene load

            // Check if the phase cycle button is enabled and proceed accordingly
            if (phaseCycleButton?.Value == true) {
                await checkBossActive(); // Check if the boss is active before starting the cycle
                TogglePhaseCycle(true);  // Start phase cycling if the button is enabled
            }

            // Check if the invincible button is enabled and toggle invincibility if true
            if (invincibleButton?.Value == true) {
                ToggleInvincibility();
            }
        }

        private async UniTask checkBossActive() {
            MonsterBase boss;

            while (true) {
                boss = GetBossInstance();

                if (boss != null && boss.IsAlive() && boss.isActiveAndEnabled && boss.gameObject.activeInHierarchy && boss.gameObject.activeSelf) {
                    break;
                }

                await UniTask.Yield();
            }
        }

        private MonsterBase GetBossInstance() {
            foreach (var boss in MonsterManager.Instance.monsterDict.Values) {
                if (boss.tag == "Boss") {
                    return boss;
                }
            }
            return null;
        }

        private void InitializeConfigEntries() {
            phaseButtons = new[] {
                CreatePhaseButton("Phase 1", 0),
                CreatePhaseButton("Phase 2", 1),
                CreatePhaseButton("Phase 3", 2)
            };

            invincibleButton = Config.Bind("Actions", "Toggle Invincibility", false, "Toggle invincibility for all bosses");
            invincibleButton.SettingChanged += (_, _) => ToggleInvincibility();

            phaseCycleButton = Config.Bind("Actions", "Cycle Phases 1~3", false, "Automatically cycle phases 1 to 3 every x seconds");
            phaseCycleButton.SettingChanged += (_, _) => TogglePhaseCycle(phaseCycleButton?.Value ?? false);

            phaseSecond = Config.Bind("Actions", "Change Phases Every Second", 30f, "");
            isReviveJade = Config.Bind("", "Revive Jade Unlimit times", false, "");
            isToast = Config.Bind("", "Is Toast Change Phase", false, "");

            ResetAllButtons();
        }

        private ConfigEntry<bool> CreatePhaseButton(string name, int phaseIndex) {
            var entry = Config.Bind("Actions", $"Goto {name}", false, $"Trigger {name} for all bosses");
            entry.SettingChanged += (_, _) => { if (entry.Value) SetPhase(phaseIndex); entry.Value = false; };
            return entry;
        }

        private void ResetAllButtons() {
            foreach (var button in phaseButtons) button.Value = false;
            invincibleButton.Value = false;
            isReviveJade.Value = false;
            phaseCycleButton.Value = false;
        }

        private void ToggleInvincibility() {
            bool isInvincible;
            foreach (var boss in MonsterManager.Instance.monsterDict.Values) {
                if (boss.tag == "Boss" && TryGetMonsterStat(boss, out var monsterStat)) {
                    // Toggle the invincibility state based on the current state
                    isInvincible = invincibleButton?.Value ?? false;
                    monsterStat.IsLockPostureInvincible = isInvincible;
                    ToastManager.Toast($"Invincible: {isInvincible} Name: {boss.name}");
                }
            }
        }

        private void SetPhase(int phaseIndex) {
            bool showToast = isToast?.Value ?? false;
            foreach (var boss in MonsterManager.Instance.monsterDict.Values) {
                if (boss.tag == "Boss") {
                    GotoPhase(boss, phaseIndex);
                    if (showToast) {
                        ToastManager.Toast($"Goto Phase {phaseIndex + 1} Name:{boss.name}");
                    }
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
            //SingletonBehaviour<GameCore>.Instance.monsterHpUI.CurrentBossHP?.TempShow();
            monster.monsterCore.DisablePushAway();

            ClearAllAttackSensors(monster);
        }

        private void ClearAllAttackSensors(MonsterBase monster) {
            foreach (var sensor in GetAttackSensors(monster)) sensor?.ClearQueue();
        }

        private AttackSensor[] GetAttackSensors(MonsterBase monster) {
            var field = typeof(MonsterBase).GetField("attackSensors", BindingFlags.NonPublic | BindingFlags.Instance) ??
                        typeof(MonsterBase).GetField("_attackSensors", BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(monster) as AttackSensor[] ?? new AttackSensor[0];
        }

        private async UniTaskVoid CyclePhases() {
            int phaseIndex = 0;
            isCyclingPhases = true;
            var cancellationToken = phaseCycleCancellationTokenSource?.Token ?? CancellationToken.None;

            while (isCyclingPhases && !cancellationToken.IsCancellationRequested) {
                SetPhase(phaseIndex);
                phaseIndex = (phaseIndex + 1) % 3;

                // Use the new phase delay value immediately after the current phase change
                await UniTask.Delay((int)(phaseSecond?.Value ?? 0) * 1000, cancellationToken: cancellationToken);
            }
        }



        private void TogglePhaseCycle(bool enable) {
            // If phase cycle is being enabled and isn't already cycling, restart it
            if (enable && !isCyclingPhases) {
                // Cancel any ongoing cycle
                phaseCycleCancellationTokenSource?.Cancel();
                phaseCycleCancellationTokenSource = new CancellationTokenSource();

                // Restart the phase cycle immediately with the new delay value
                CyclePhases().Forget();
            }
            // If phase cycle is being disabled and is cycling, cancel the cycle
            else if (!enable && isCyclingPhases) {
                phaseCycleCancellationTokenSource?.Cancel();
                isCyclingPhases = false;
            }
        }



        private void StopPhaseCycle() {
            if (phaseCycleCancellationTokenSource != null) {
                phaseCycleCancellationTokenSource.Cancel();
                isCyclingPhases = false;
            }
        }

        private void OnDestroy() {
            StopPhaseCycle(); // Ensure phase cycling stops when the plugin is destroyed
            mainHarmony.UnpatchSelf();
            Logger.LogInfo("GodModeBoss plugin unpatched.");
        }
    }
}
