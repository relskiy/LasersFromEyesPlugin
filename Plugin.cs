using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using AdminToys;
using EyeLasers.Configs;
using EyeLasers.Controllers;
using EyeLasers.Pools;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using LabApi.Loader.Features.Plugins;
using MEC;
using Mirror;
using PlayerRoles;
using PlayerStatsSystem;
using UnityEngine;
using UserSettings.ServerSpecific;

namespace EyeLasers
{
    public class EyeLasersPlugin : Plugin<EyeLasersConfig>
    {
        public override string Name => "LasersFromEyesPlugin";
        public override string Description => "Лазеры из глаз";
        public override string Author => "relskiy";
        public override Version Version => new Version(1, 0, 1);
        public override Version RequiredApiVersion => new Version(1, 1, 7);

        public static EyeLasersPlugin Instance { get; private set; }
        public static ScorchMarkPool ScorchPool { get; private set; }
        private static readonly HttpClient HttpClient = new HttpClient();

        public readonly Dictionary<ReferenceHub, LaserController> ActiveLasers = new Dictionary<ReferenceHub, LaserController>(16);
        public static readonly Dictionary<string, bool> HideModelPreferences = new Dictionary<string, bool>();

        private static PrimitiveObjectToy _primitivePrefab;
        private static LightSourceToy _lightPrefab;
        private CoroutineHandle _laserRoutine;
        private CoroutineHandle _webhookRoutine;

        private static readonly Queue<string> WebhookQueue = new Queue<string>();

        public static readonly int RaycastMask = LayerMask.GetMask("Default", "Player", "Hitbox", "Glass", "Door", "Pickup");
        private static readonly List<Vector3> ObserverPositions = new List<Vector3>(32);
        private static readonly RaycastHit[] RaycastHitsBuffer = new RaycastHit[32];

        public override void Enable()
        {
            Instance = this;
            ScorchPool = new ScorchMarkPool(Config.ScorchPoolSize);
            _laserRoutine = Timing.RunCoroutine(LaserLoop(), Segment.LateUpdate);
            _webhookRoutine = Timing.RunCoroutine(WebhookProcessor());

            PlayerEvents.ChangingRole += OnChangingRole;
            PlayerEvents.ChangedRole += OnChangedRole;
            PlayerEvents.Spawned += OnSpawned;
            PlayerEvents.Dying += OnDying;
            PlayerEvents.Death += OnDeath;
            PlayerEvents.SpawnedRagdoll += OnSpawnedRagdoll;
            PlayerEvents.Left += OnLeft;
            PlayerEvents.Joined += OnJoined;

            ServerEvents.RoundRestarted += OnRoundRestart;
            ServerEvents.RoundEnded += OnRoundEnded;

            if (Config.EnableKeybindSetting)
            {
                RegisterServerSpecificSettings();
                ServerSpecificSettingsSync.ServerOnSettingValueReceived += OnServerSettingValueReceived;
            }
        }

        public override void Disable()
        {
            PlayerEvents.ChangingRole -= OnChangingRole;
            PlayerEvents.ChangedRole -= OnChangedRole;
            PlayerEvents.Spawned -= OnSpawned;
            PlayerEvents.Dying -= OnDying;
            PlayerEvents.Death -= OnDeath;
            PlayerEvents.SpawnedRagdoll -= OnSpawnedRagdoll;
            PlayerEvents.Left -= OnLeft;
            PlayerEvents.Joined -= OnJoined;

            ServerEvents.RoundRestarted -= OnRoundRestart;
            ServerEvents.RoundEnded -= OnRoundEnded;

            if (Config.EnableKeybindSetting)
            {
                ServerSpecificSettingsSync.ServerOnSettingValueReceived -= OnServerSettingValueReceived;
            }

            Timing.KillCoroutines(_laserRoutine);
            Timing.KillCoroutines(_webhookRoutine);
            CleanupAll();
            ScorchPool?.Clear();
            ScorchPool = null;

            Instance = null;
        }

        public static void LogEvent(string message)
        {
            if (Instance == null) return;

            if (Instance.Config.LogToConsole)
            {
                Debug.Log($"[EyeLasers] {message}");
            }

            if (!string.IsNullOrEmpty(Instance.Config.DiscordWebhookUrl))
            {
                WebhookQueue.Enqueue(message);
            }
        }

        private static IEnumerator<float> WebhookProcessor()
        {
            while (true)
            {
                if (WebhookQueue.Count > 0 && !string.IsNullOrEmpty(Instance?.Config?.DiscordWebhookUrl))
                {
                    StringBuilder sb = new StringBuilder();
                    int count = 0;
                    while (WebhookQueue.Count > 0 && count < 5)
                    {
                        if (sb.Length > 0) sb.Append("\n");
                        sb.Append(WebhookQueue.Dequeue());
                        count++;
                    }

                    string content = sb.ToString();
                    string json = $"{{\"content\": \"{content.Replace("\"", "\\\"").Replace("\n", "\\n")}\"}}";
                    var stringContent = new StringContent(json, Encoding.UTF8, "application/json");

                    var task = HttpClient.PostAsync(Instance.Config.DiscordWebhookUrl, stringContent);
                    while (!task.IsCompleted)
                    {
                        yield return Timing.WaitForOneFrame;
                    }

                    yield return Timing.WaitForSeconds(1.5f);
                }
                else
                {
                    yield return Timing.WaitForSeconds(0.5f);
                }
            }
        }

        private void RegisterServerSpecificSettings()
        {
            var header = new SSGroupHeader("EyeLasers / Лазерный взгляд");

            var toggleSetting = new SSKeybindSetting(
                Config.KeybindSettingId,
                Config.KeybindLabel,
                KeyCode.None
            );

            var hideModelToggle = new SSTwoButtonsSetting(
                Config.HideModelToggleSettingId,
                Config.HideModelToggleLabel,
                "Выкл",
                "Вкл",
                false,
                "Скрыть геометрию лучей и оставить только динамический свет"
            );

            List<ServerSpecificSettingBase> list = ServerSpecificSettingsSync.DefinedSettings != null
                ? ServerSpecificSettingsSync.DefinedSettings.ToList()
                : new List<ServerSpecificSettingBase>();

            list.RemoveAll(s => s != null && (s.SettingId == Config.KeybindSettingId || s.SettingId == Config.HideModelToggleSettingId));
            list.Add(header);
            list.Add(toggleSetting);
            list.Add(hideModelToggle);
            ServerSpecificSettingsSync.DefinedSettings = list.ToArray();
            ServerSpecificSettingsSync.SendToAll();
        }

        private void OnJoined(PlayerJoinedEventArgs ev)
        {
            if (Config.EnableKeybindSetting && ev.Player?.ReferenceHub != null)
            {
                ServerSpecificSettingsSync.SendToPlayer(ev.Player.ReferenceHub);
            }
        }

        private void OnSpawned(PlayerSpawnedEventArgs ev)
        {
            if (ev.Player?.ReferenceHub != null)
                RemoveLaserInstant(ev.Player.ReferenceHub);
        }

        private void OnServerSettingValueReceived(ReferenceHub hub, ServerSpecificSettingBase setting)
        {
            if (setting == null || hub == null) return;

            if (setting is SSKeybindSetting keybind && keybind.SettingId == Config.KeybindSettingId)
            {
                if (keybind.SyncIsPressed)
                {
                    HandleToggle(hub);
                }
            }
            else if (setting is SSTwoButtonsSetting twoButtons && twoButtons.SettingId == Config.HideModelToggleSettingId)
            {
                SetHideModelsState(hub, twoButtons.SyncIsB);
            }
        }

        public void SetHideModelsState(ReferenceHub hub, bool state)
        {
            if (hub?.authManager != null)
            {
                HideModelPreferences[hub.authManager.UserId] = state;
            }

            if (ActiveLasers.TryGetValue(hub, out LaserController controller))
            {
                if (controller != null && !controller.IsDestroyed)
                {
                    float timer = controller.AutoDisableTimer;
                    controller.Destroy();
                    ActiveLasers.Remove(hub);

                    var newCtrl = new LaserController(hub, state);
                    newCtrl.AutoDisableTimer = timer;
                    ActiveLasers[hub] = newCtrl;
                }
            }
        }

        private void OnChangingRole(PlayerChangingRoleEventArgs ev)
        {
            if (ev.Player?.ReferenceHub != null)
                RemoveLaserInstant(ev.Player.ReferenceHub);
        }

        private void OnChangedRole(PlayerChangedRoleEventArgs ev)
        {
            if (ev.Player?.ReferenceHub != null)
            {
                if (!IsAlive(ev.Player.ReferenceHub) || !TryGetRoleOffsets(ev.Player.Role, out _))
                {
                    RemoveLaserInstant(ev.Player.ReferenceHub);
                }
            }
        }

        private void OnDying(PlayerDyingEventArgs ev)
        {
            if (ev.Player?.ReferenceHub != null)
                RemoveLaserInstant(ev.Player.ReferenceHub);
        }

        private void OnDeath(PlayerDeathEventArgs ev)
        {
            if (ev.Player?.ReferenceHub != null)
                RemoveLaserInstant(ev.Player.ReferenceHub);
        }

        private void OnSpawnedRagdoll(PlayerSpawnedRagdollEventArgs ev)
        {
            if (ev.Player?.ReferenceHub != null)
                RemoveLaserInstant(ev.Player.ReferenceHub);
        }

        private void OnLeft(PlayerLeftEventArgs ev)
        {
            if (ev.Player?.ReferenceHub != null)
                RemoveLaserInstant(ev.Player.ReferenceHub);
        }

        private void OnRoundRestart()
        {
            CleanupAll();
            ScorchPool?.Clear();
            WebhookQueue.Clear();
        }

        private void OnRoundEnded(RoundEndedEventArgs ev)
        {
            CleanupAll();
            ScorchPool?.Clear();
        }

        public void CleanupAll()
        {
            foreach (var controller in ActiveLasers.Values)
            {
                try { controller?.Destroy(); } catch { }
            }
            ActiveLasers.Clear();
        }

        public void HandleToggle(ReferenceHub hub, float duration = -1f)
        {
            if (!HasPermission(hub)) return;

            if (ActiveLasers.TryGetValue(hub, out LaserController controller))
            {
                if (controller == null || controller.IsDestroyed || controller.State == LaserAnimState.Dead)
                {
                    ActiveLasers.Remove(hub);
                    CheckAndAdd(hub, duration);
                }
                else
                {
                    RemoveLaser(hub);
                }
            }
            else
            {
                CheckAndAdd(hub, duration);
            }
        }

        public void HandleToggleHide(ReferenceHub hub)
        {
            if (ActiveLasers.TryGetValue(hub, out LaserController controller))
            {
                if (controller != null && !controller.IsDestroyed)
                {
                    bool newState = !controller.HideModels;
                    SetHideModelsState(hub, newState);
                }
            }
        }

        private void CheckAndAdd(ReferenceHub hub, float duration)
        {
            if (IsAlive(hub) && TryGetRoleOffsets(hub.roleManager.CurrentRole.RoleTypeId, out _))
            {
                AddLaser(hub, duration);
            }
        }

        public static bool HasPermission(ReferenceHub hub)
        {
            if (!Instance.Config.RestrictAccess) return true;
            if (hub == null) return true;
            if (hub.serverRoles.BypassMode || hub.serverRoles.Permissions > 0) return true;

            if (Instance.Config.AllowedUserIds != null && Instance.Config.AllowedUserIds.Contains(hub.authManager.UserId))
            {
                return true;
            }

            if (Instance.Config.AllowedGroups != null && !string.IsNullOrEmpty(hub.serverRoles.Group?.BadgeText))
            {
                string groupName = hub.serverRoles.Group.BadgeText.ToLower();
                if (Instance.Config.AllowedGroups.Any(g => g.ToLower() == groupName))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetRoleOffsets(RoleTypeId role, out LaserOffset offset)
        {
            switch (role)
            {
                case RoleTypeId.None:
                case RoleTypeId.Spectator:
                case RoleTypeId.Overwatch:
                case RoleTypeId.Filmmaker:
                    offset = default;
                    return false;

                case RoleTypeId.Tutorial:
                    offset = new LaserOffset(0.03f, 0.125f, 0.0335f);
                    return true;

                case RoleTypeId.ClassD:
                    offset = new LaserOffset(0.02f, 0.125f, 0.0335f);
                    return true;

                case RoleTypeId.Scientist:
                case RoleTypeId.FacilityGuard:
                case RoleTypeId.NtfCaptain:
                case RoleTypeId.NtfSergeant:
                case RoleTypeId.NtfSpecialist:
                case RoleTypeId.NtfPrivate:
                case RoleTypeId.ChaosConscript:
                case RoleTypeId.ChaosRifleman:
                case RoleTypeId.ChaosMarauder:
                case RoleTypeId.ChaosRepressor:
                    offset = new LaserOffset(0.016f, 0.125f, 0.0335f);
                    return true;

                case RoleTypeId.Scp106:
                    offset = new LaserOffset(0.23f, 0.125f, 0.0335f);
                    return true;

                case RoleTypeId.Scp049:
                    offset = new LaserOffset(0.07f, 0.03f, 0.0335f);
                    return true;

                case RoleTypeId.Scp3114:
                    offset = new LaserOffset(0.03f, 0.125f, 0.0335f);
                    return true;

                case RoleTypeId.Scp0492:
                    offset = new LaserOffset(0.02f, 0.125f, 0.0335f);
                    return true;

                case RoleTypeId.Scp096:
                    offset = new LaserOffset(0.15f, 0.125f, 0.0335f);
                    return true;

                case RoleTypeId.Scp939:
                    offset = new LaserOffset(0.05f, 0.15f, 0.04f);
                    return true;

                default:
                    offset = new LaserOffset(0.02f, 0.125f, 0.0335f);
                    return true;
            }
        }

        public static PrimitiveObjectToy SpawnPrimitive(UnityEngine.PrimitiveType type, Color color)
        {
            if (_primitivePrefab == null)
            {
                foreach (GameObject prefab in NetworkClient.prefabs.Values)
                {
                    if (prefab != null && prefab.TryGetComponent<PrimitiveObjectToy>(out var toy))
                    {
                        _primitivePrefab = toy;
                        break;
                    }
                }
            }

            if (_primitivePrefab == null) return null;

            PrimitiveObjectToy primitive = UnityEngine.Object.Instantiate(_primitivePrefab);
            primitive.PrimitiveType = type;
            primitive.MaterialColor = color;
            primitive.PrimitiveFlags = PrimitiveFlags.Visible;
            primitive.MovementSmoothing = 0;

            foreach (var col in primitive.GetComponentsInChildren<Collider>())
            {
                col.enabled = false;
            }

            NetworkServer.Spawn(primitive.gameObject);
            return primitive;
        }

        public static LightSourceToy SpawnLight(Color color, float intensity, float range)
        {
            if (_lightPrefab == null)
            {
                foreach (GameObject prefab in NetworkClient.prefabs.Values)
                {
                    if (prefab != null && prefab.TryGetComponent<LightSourceToy>(out var toy))
                    {
                        _lightPrefab = toy;
                        break;
                    }
                }
            }

            if (_lightPrefab == null) return null;

            LightSourceToy light = UnityEngine.Object.Instantiate(_lightPrefab);
            light.LightColor = color;
            light.LightIntensity = intensity;
            light.LightRange = range;
            light.MovementSmoothing = 0;

            NetworkServer.Spawn(light.gameObject);
            return light;
        }

        public static bool IsAlive(ReferenceHub hub)
        {
            if (hub == null || hub.gameObject == null || hub.roleManager == null || hub.roleManager.CurrentRole == null)
                return false;

            RoleTypeId role = hub.roleManager.CurrentRole.RoleTypeId;
            if (role == RoleTypeId.None || role == RoleTypeId.Spectator || role == RoleTypeId.Overwatch)
                return false;

            if (hub.playerStats != null && hub.playerStats.TryGetModule<HealthStat>(out var healthStat))
            {
                if (healthStat.CurValue <= 0f) return false;
            }

            return true;
        }

        public void AddLaser(ReferenceHub hub, float duration = -1f)
        {
            if (hub == null) return;

            if (ActiveLasers.TryGetValue(hub, out LaserController existing))
            {
                if (existing != null && !existing.IsDestroyed && existing.State != LaserAnimState.Dead)
                {
                    if (existing.State == LaserAnimState.Dissipation)
                    {
                        existing.State = LaserAnimState.Ignition;
                    }
                    if (duration > 0f) existing.AutoDisableTimer = duration;
                    return;
                }

                existing?.Destroy();
                ActiveLasers.Remove(hub);
            }

            bool hidePref = false;
            if (hub.authManager != null && HideModelPreferences.TryGetValue(hub.authManager.UserId, out bool pref))
            {
                hidePref = pref;
            }

            var ctrl = new LaserController(hub, hidePref);
            if (duration > 0f) ctrl.AutoDisableTimer = duration;
            ActiveLasers[hub] = ctrl;
        }

        public void RemoveLaser(ReferenceHub hub)
        {
            if (ActiveLasers.TryGetValue(hub, out LaserController controller))
            {
                controller.StartShutdown();
            }
        }

        public void RemoveLaserInstant(ReferenceHub hub)
        {
            if (ActiveLasers.TryGetValue(hub, out LaserController controller))
            {
                controller.Destroy();
                ActiveLasers.Remove(hub);
            }
        }

        private IEnumerator<float> LaserLoop()
        {
            float lastTime = Time.time;
            List<ReferenceHub> toRemove = new List<ReferenceHub>(8);

            while (true)
            {
                try
                {
                    if (ActiveLasers.Count > 0)
                    {
                        float deltaTime = Time.time - lastTime;
                        toRemove.Clear();

                        ObserverPositions.Clear();
                        foreach (ReferenceHub obsHub in ReferenceHub.AllHubs)
                        {
                            if (obsHub != null && obsHub.PlayerCameraReference != null)
                            {
                                ObserverPositions.Add(obsHub.PlayerCameraReference.position);
                            }
                        }

                        foreach (var pair in ActiveLasers)
                        {
                            ReferenceHub hub = pair.Key;
                            LaserController controller = pair.Value;

                            try
                            {
                                if (controller == null || controller.IsDestroyed || controller.State == LaserAnimState.Dead || !IsAlive(hub) || hub.PlayerCameraReference == null)
                                {
                                    controller?.Destroy();
                                    toRemove.Add(hub);
                                    continue;
                                }

                                RoleTypeId currentRole = hub.roleManager.CurrentRole.RoleTypeId;

                                if (!TryGetRoleOffsets(currentRole, out LaserOffset offsets))
                                {
                                    controller.SetVisible(false);
                                    continue;
                                }

                                Transform cam = hub.PlayerCameraReference;
                                Vector3 eyeLevelCenter = cam.position + (cam.forward * offsets.Forward) + (cam.up * offsets.Height);
                                Vector3 leftEye = eyeLevelCenter - (cam.right * offsets.Width);
                                Vector3 rightEye = eyeLevelCenter + (cam.right * offsets.Width);

                                Vector3 hitPoint;
                                Vector3 hitNormal = -cam.forward;
                                Collider hitCollider = null;

                                int hitCount = Physics.RaycastNonAlloc(new Ray(cam.position, cam.forward), RaycastHitsBuffer, 120f, RaycastMask);
                                float closestDist = float.MaxValue;
                                int bestIndex = -1;
                                Transform shooterRoot = hub.transform.root;

                                for (int i = 0; i < hitCount; i++)
                                {
                                    Collider col = RaycastHitsBuffer[i].collider;
                                    if (col == null) continue;
                                    if (col.transform.root == shooterRoot || col.gameObject == hub.gameObject) continue;

                                    if (RaycastHitsBuffer[i].distance < closestDist)
                                    {
                                        closestDist = RaycastHitsBuffer[i].distance;
                                        bestIndex = i;
                                    }
                                }

                                if (bestIndex != -1)
                                {
                                    hitPoint = RaycastHitsBuffer[bestIndex].point;
                                    hitNormal = RaycastHitsBuffer[bestIndex].normal;
                                    hitCollider = RaycastHitsBuffer[bestIndex].collider;
                                }
                                else
                                {
                                    hitPoint = cam.position + (cam.forward * 100f);
                                }

                                bool hasNearbyObservers = false;
                                Vector3 shooterPos = cam.position;
                                for (int i = 0; i < ObserverPositions.Count; i++)
                                {
                                    Vector3 obsPos = ObserverPositions[i];
                                    if ((obsPos - shooterPos).sqrMagnitude < 784f || (obsPos - hitPoint).sqrMagnitude < 784f)
                                    {
                                        hasNearbyObservers = true;
                                        break;
                                    }
                                }

                                controller.Update(leftEye, rightEye, hitPoint, hitNormal, hitCollider, deltaTime, hasNearbyObservers);
                            }
                            catch (Exception ex)
                            {
                                Debug.LogError($"[EyeLasers] Loop error on player: {ex}");
                                try { controller?.Destroy(); } catch { }
                                toRemove.Add(hub);
                            }
                        }

                        if (toRemove.Count > 0)
                        {
                            for (int i = 0; i < toRemove.Count; i++)
                            {
                                ActiveLasers.Remove(toRemove[i]);
                            }
                        }
                    }

                    lastTime = Time.time;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[EyeLasers] Critical error in LaserLoop: {ex}");
                }

                yield return Timing.WaitForOneFrame;
            }
        }
    }
}