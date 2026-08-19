using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using AdminToys;
using CommandSystem;
using CustomPlayerEffects;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Loader.Features.Plugins;
using MEC;
using MapGeneration;
using Mirror;
using PlayerRoles;
using PlayerStatsSystem;
using RemoteAdmin;
using UnityEngine;
using UserSettings.ServerSpecific;

namespace EyeLasers
{
    public class EyeLasersConfig
    {
        [Description("Включить ограничение доступа (только игроки из списка/группы)")]
        public bool RestrictAccess { get; set; } = false;

        [Description("Список UserID игроков с доступом (SteamID/DiscordID)")]
        public List<string> AllowedUserIds { get; set; } = new List<string>()
        {
            "76561198000000000@steam"
        };

        [Description("Список групп RemoteAdmin с доступом к лазерам")]
        public List<string> AllowedGroups { get; set; } = new List<string>()
        {
            "owner",
            "admin",
            "donor",
            "vip"
        };

        [Description("Включить нанесение урона при стрельбе")]
        public bool EnableDamage { get; set; } = false;

        [Description("Количество урона за тик")]
        public float DamagePerTick { get; set; } = 15f;

        [Description("Интервал между тиками урона в секундах")]
        public float DamageInterval { get; set; } = 0.15f;

        [Description("Причина смерти")]
        public string DeathReason { get; set; } = "Испепелён лазерным взглядом";

        [Description("Длительность анимации включения (разогрев и прострел луча)")]
        public float StartupDuration { get; set; } = 0.4f;

        [Description("Длительность анимации выключения (остывание)")]
        public float ShutdownDuration { get; set; } = 0.25f;

        [Description("Включить динамическое освещение")]
        public bool EnableLights { get; set; } = true;

        [Description("Интенсивность света в точке попадания")]
        public float ImpactLightIntensity { get; set; } = 5.5f;

        [Description("Радиус света в точке попадания")]
        public float ImpactLightRange { get; set; } = 8.5f;

        [Description("Интенсивность света у глаз")]
        public float EyeLightIntensity { get; set; } = 2.2f;

        [Description("Включить кинетический толчок физических объектов (рэгдоллы, предметы)")]
        public bool EnablePhysicsImpulse { get; set; } = true;

        [Description("Сила отталкивания физических объектов")]
        public float PhysicsImpulseForce { get; set; } = 3.5f;

        [Description("Включить следы выжигания на поверхностях")]
        public bool EnableScorchMarks { get; set; } = true;

        [Description("Включить мерцание света в комнатах от перегрузки")]
        public bool EnableRoomFlicker { get; set; } = true;

        [Description("Включить кнопку в настройках клиента (Server-Specific Settings)")]
        public bool EnableKeybindSetting { get; set; } = true;

        [Description("ID бинда в меню настроек")]
        public int KeybindSettingId { get; set; } = 1050;

        [Description("Текст кнопки в настройках")]
        public string KeybindLabel { get; set; } = "Лазерный взгляд (Вкл/Выкл)";
    }

    public struct LaserOffset
    {
        public float Height;
        public float Forward;
        public float Width;

        public LaserOffset(float height, float forward = 0.125f, float width = 0.0335f)
        {
            Height = height;
            Forward = forward;
            Width = width;
        }
    }

    public enum LaserAnimState
    {
        Ignition,
        Active,
        Dissipation,
        Dead
    }

    public class LaserController
    {
        public ReferenceHub Owner;
        public LaserAnimState State = LaserAnimState.Ignition;

        private float _stateTimer = 0f;
        private float _dwellTimer = 0f;
        private float _nextHeavyUpdateTime = 0f;
        private float _nextDamageTime = 0f;
        private Vector3 _lastHitPosition = Vector3.zero;

        // Лучи
        public PrimitiveObjectToy LeftBeamAura;
        public PrimitiveObjectToy LeftBeamMain;
        public PrimitiveObjectToy LeftBeamCore;

        public PrimitiveObjectToy RightBeamAura;
        public PrimitiveObjectToy RightBeamMain;
        public PrimitiveObjectToy RightBeamCore;

        // Спирали энергии
        public PrimitiveObjectToy[] LeftHelix = new PrimitiveObjectToy[4];
        public PrimitiveObjectToy[] RightHelix = new PrimitiveObjectToy[4];

        // Глазницы
        public PrimitiveObjectToy LeftEyeCore;
        public PrimitiveObjectToy LeftEyeCorona;
        public PrimitiveObjectToy RightEyeCore;
        public PrimitiveObjectToy RightEyeCorona;

        // Точка контакта
        public PrimitiveObjectToy ImpactBlast;
        public PrimitiveObjectToy ImpactPlasma;
        public PrimitiveObjectToy ImpactCore;
        public PrimitiveObjectToy ImpactRing1;
        public PrimitiveObjectToy ImpactRing2;

        // Искры
        public PrimitiveObjectToy[] Sparks = new PrimitiveObjectToy[8];

        // Освещение
        public LightSourceToy ImpactLight;
        public LightSourceToy EyesLight;

        private static readonly Collider[] PhysicsBuffer = new Collider[16];

        public LaserController(ReferenceHub hub)
        {
            Owner = hub;
            SpawnAllObjects();
        }

        public void StartShutdown()
        {
            if (State != LaserAnimState.Dissipation && State != LaserAnimState.Dead)
            {
                State = LaserAnimState.Dissipation;
                _stateTimer = 0f;
            }
        }

        public void SpawnAllObjects()
        {
            EyeLasersConfig cfg = EyeLasersPlugin.Instance.Config;

            LeftBeamAura = EyeLasersPlugin.SpawnPrimitive(UnityEngine.PrimitiveType.Cylinder, new Color(1f, 0.02f, 0.02f, 0.40f));
            LeftBeamMain = EyeLasersPlugin.SpawnPrimitive(UnityEngine.PrimitiveType.Cylinder, new Color(1f, 0.18f, 0.02f, 0.95f));
            LeftBeamCore = EyeLasersPlugin.SpawnPrimitive(UnityEngine.PrimitiveType.Cylinder, new Color(1f, 0.98f, 0.85f, 1f));

            RightBeamAura = EyeLasersPlugin.SpawnPrimitive(UnityEngine.PrimitiveType.Cylinder, new Color(1f, 0.02f, 0.02f, 0.40f));
            RightBeamMain = EyeLasersPlugin.SpawnPrimitive(UnityEngine.PrimitiveType.Cylinder, new Color(1f, 0.18f, 0.02f, 0.95f));
            RightBeamCore = EyeLasersPlugin.SpawnPrimitive(UnityEngine.PrimitiveType.Cylinder, new Color(1f, 0.98f, 0.85f, 1f));

            for (int i = 0; i < 4; i++)
            {
                LeftHelix[i] = EyeLasersPlugin.SpawnPrimitive(UnityEngine.PrimitiveType.Sphere, new Color(1f, 0.8f, 0.2f, 0.9f));
                RightHelix[i] = EyeLasersPlugin.SpawnPrimitive(UnityEngine.PrimitiveType.Sphere, new Color(1f, 0.8f, 0.2f, 0.9f));
            }

            LeftEyeCore = EyeLasersPlugin.SpawnPrimitive(UnityEngine.PrimitiveType.Sphere, new Color(1f, 1f, 0.85f, 1f));
            LeftEyeCorona = EyeLasersPlugin.SpawnPrimitive(UnityEngine.PrimitiveType.Sphere, new Color(1f, 0.25f, 0.02f, 0.75f));
            RightEyeCore = EyeLasersPlugin.SpawnPrimitive(UnityEngine.PrimitiveType.Sphere, new Color(1f, 1f, 0.85f, 1f));
            RightEyeCorona = EyeLasersPlugin.SpawnPrimitive(UnityEngine.PrimitiveType.Sphere, new Color(1f, 0.25f, 0.02f, 0.75f));

            ImpactBlast = EyeLasersPlugin.SpawnPrimitive(UnityEngine.PrimitiveType.Sphere, new Color(1f, 0.08f, 0.02f, 0.55f));
            ImpactPlasma = EyeLasersPlugin.SpawnPrimitive(UnityEngine.PrimitiveType.Sphere, new Color(1f, 0.5f, 0.05f, 0.95f));
            ImpactCore = EyeLasersPlugin.SpawnPrimitive(UnityEngine.PrimitiveType.Sphere, new Color(1f, 1f, 0.9f, 1f));
            ImpactRing1 = EyeLasersPlugin.SpawnPrimitive(UnityEngine.PrimitiveType.Cylinder, new Color(1f, 0.4f, 0.05f, 0.7f));
            ImpactRing2 = EyeLasersPlugin.SpawnPrimitive(UnityEngine.PrimitiveType.Cylinder, new Color(1f, 0.15f, 0.02f, 0.5f));

            for (int i = 0; i < Sparks.Length; i++)
            {
                Color spkColor = (i % 2 == 0) ? new Color(1f, 0.95f, 0.4f, 1f) : new Color(1f, 0.35f, 0.02f, 0.95f);
                Sparks[i] = EyeLasersPlugin.SpawnPrimitive(UnityEngine.PrimitiveType.Sphere, spkColor);
            }

            if (cfg.EnableLights)
            {
                ImpactLight = EyeLasersPlugin.SpawnLight(new Color(1f, 0.3f, 0.05f, 1f), cfg.ImpactLightIntensity, cfg.ImpactLightRange);
                EyesLight = EyeLasersPlugin.SpawnLight(new Color(1f, 0.4f, 0.08f, 1f), cfg.EyeLightIntensity, 3.5f);
            }
        }

        public void Update(Vector3 leftEye, Vector3 rightEye, Vector3 hitPoint, Vector3 hitNormal, Collider hitCollider, float deltaTime)
        {
            EnsureObjectsExist();
            _stateTimer += deltaTime;

            EyeLasersConfig cfg = EyeLasersPlugin.Instance.Config;
            float time = Time.time;

            float beamProgress = 1f;
            float intensity = 1f;

            switch (State)
            {
                case LaserAnimState.Ignition:
                    float ignitionProgress = Mathf.Clamp01(_stateTimer / cfg.StartupDuration);

                    if (ignitionProgress < 0.35f)
                    {
                        intensity = ignitionProgress / 0.35f;
                        beamProgress = 0f;
                    }
                    else
                    {
                        intensity = 1f;
                        beamProgress = (ignitionProgress - 0.35f) / 0.65f;
                    }

                    if (_stateTimer >= cfg.StartupDuration)
                        State = LaserAnimState.Active;
                    break;

                case LaserAnimState.Active:
                    beamProgress = 1f;
                    intensity = 1f;
                    break;

                case LaserAnimState.Dissipation:
                    float dissProgress = Mathf.Clamp01(_stateTimer / cfg.ShutdownDuration);
                    intensity = 1f - dissProgress;
                    beamProgress = 1f - dissProgress;

                    if (_stateTimer >= cfg.ShutdownDuration)
                    {
                        State = LaserAnimState.Dead;
                        return;
                    }
                    break;

                case LaserAnimState.Dead:
                    return;
            }

            Vector3 animatedHitLeft = Vector3.Lerp(leftEye, hitPoint, beamProgress);
            Vector3 animatedHitRight = Vector3.Lerp(rightEye, hitPoint, beamProgress);

            // 1. Анимация лучей
            if (beamProgress > 0.01f)
            {
                float beamJitter = (1f + (Mathf.Sin(time * 45f) * 0.07f)) * intensity;
                UpdateTripleBeam(LeftBeamAura, LeftBeamMain, LeftBeamCore, leftEye, animatedHitLeft, beamJitter, intensity);
                UpdateTripleBeam(RightBeamAura, RightBeamMain, RightBeamCore, rightEye, animatedHitRight, beamJitter, intensity);

                UpdateHelix(LeftHelix, leftEye, animatedHitLeft, time, 0f, intensity);
                UpdateHelix(RightHelix, rightEye, animatedHitRight, time, Mathf.PI, intensity);
            }
            else
            {
                HideBeams();
            }

            // 2. Пульсация глазниц
            float eyeCoreSize = (0.012f + (Mathf.Sin(time * 30f) * 0.002f)) * intensity;
            float eyeCoronaSize = (0.026f + (Mathf.Sin(time * 20f) * 0.004f)) * intensity;

            if (LeftEyeCore != null) { LeftEyeCore.transform.position = leftEye; LeftEyeCore.transform.localScale = Vector3.one * eyeCoreSize; }
            if (LeftEyeCorona != null) { LeftEyeCorona.transform.position = leftEye; LeftEyeCorona.transform.localScale = Vector3.one * eyeCoronaSize; }
            if (RightEyeCore != null) { RightEyeCore.transform.position = rightEye; RightEyeCore.transform.localScale = Vector3.one * eyeCoreSize; }
            if (RightEyeCorona != null) { RightEyeCorona.transform.position = rightEye; RightEyeCorona.transform.localScale = Vector3.one * eyeCoronaSize; }

            // 3. Эпицентр контакта и тяжелые вычисления
            if (beamProgress >= 0.95f && intensity > 0.1f)
            {
                float blastPulse = (0.18f + (Mathf.PingPong(time * 5f, 0.06f))) * intensity;
                float plasmaPulse = (0.09f + (Mathf.Sin(time * 35f) * 0.02f)) * intensity;
                float corePulse = (0.045f + (Mathf.Sin(time * 55f) * 0.008f)) * intensity;

                if (ImpactBlast != null) { ImpactBlast.transform.position = hitPoint + (hitNormal * 0.02f); ImpactBlast.transform.localScale = Vector3.one * blastPulse; }
                if (ImpactPlasma != null) { ImpactPlasma.transform.position = hitPoint + (hitNormal * 0.035f); ImpactPlasma.transform.localScale = Vector3.one * plasmaPulse; }
                if (ImpactCore != null) { ImpactCore.transform.position = hitPoint + (hitNormal * 0.05f); ImpactCore.transform.localScale = Vector3.one * corePulse; }

                UpdateImpactRing(ImpactRing1, hitPoint, hitNormal, time, 0f, 0.26f * intensity);
                UpdateImpactRing(ImpactRing2, hitPoint, hitNormal, time, 0.5f, 0.38f * intensity);

                for (int i = 0; i < Sparks.Length; i++)
                {
                    if (Sparks[i] != null)
                    {
                        float spkSpeed = 14f + (i * 2f);
                        float angle = (time * spkSpeed) + (i * (Mathf.PI * 2f / Sparks.Length));
                        float radius = (0.04f + (Mathf.PingPong(time * 6f + i, 0.09f))) * intensity;
                        float heightOffset = 0.02f + (Mathf.PingPong(time * 9f + (i * 0.5f), 0.14f));

                        Vector3 rightTangent = Vector3.Cross(hitNormal, Vector3.up).normalized;
                        if (rightTangent == Vector3.zero) rightTangent = Vector3.right;
                        Vector3 upTangent = Vector3.Cross(hitNormal, rightTangent).normalized;

                        Vector3 sparkPos = hitPoint + (rightTangent * Mathf.Cos(angle) * radius)
                                                    + (upTangent * Mathf.Sin(angle) * radius)
                                                    + (hitNormal * heightOffset);

                        Sparks[i].transform.position = sparkPos;
                        Sparks[i].transform.localScale = Vector3.one * ((0.010f + Mathf.Sin(time * 35f + i) * 0.003f) * intensity);
                    }
                }

                if (ImpactLight != null)
                {
                    ImpactLight.transform.position = hitPoint + (hitNormal * 0.25f);
                    ImpactLight.LightIntensity = (cfg.ImpactLightIntensity + Mathf.PingPong(time * 8f, 2f)) * intensity;
                }

                // Оптимизированный блок тяжелых операций (8 раз в секунду)
                if (time >= _nextHeavyUpdateTime)
                {
                    _nextHeavyUpdateTime = time + 0.12f;

                    // Урон (если включен в конфиге)
                    if (cfg.EnableDamage && time >= _nextDamageTime && hitCollider != null)
                    {
                        ReferenceHub target = ReferenceHub.GetHub(hitCollider.transform.root.gameObject);
                        if (target != null && target != Owner && EyeLasersPlugin.IsAlive(target))
                        {
                            target.playerStats.DealDamage(new CustomReasonDamageHandler(cfg.DeathReason, cfg.DamagePerTick));
                            target.playerEffectsController.EnableEffect<Burned>(1.2f, true);
                            _nextDamageTime = time + cfg.DamageInterval;
                        }
                    }

                    // Кинетический импульс
                    if (cfg.EnablePhysicsImpulse)
                    {
                        int count = Physics.OverlapSphereNonAlloc(hitPoint, 1.2f, PhysicsBuffer);
                        for (int i = 0; i < count; i++)
                        {
                            var col = PhysicsBuffer[i];
                            if (col != null && col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic)
                            {
                                col.attachedRigidbody.AddForce((hitPoint - Owner.PlayerCameraReference.position).normalized * cfg.PhysicsImpulseForce, ForceMode.Impulse);
                            }
                        }
                    }

                    // Следы выжигания
                    if (cfg.EnableScorchMarks)
                    {
                        if (Vector3.Distance(hitPoint, _lastHitPosition) < 0.25f)
                        {
                            _dwellTimer += 0.12f;
                            if (_dwellTimer >= 0.36f)
                            {
                                EyeLasersPlugin.SpawnScorchMark(hitPoint, hitNormal);
                                _dwellTimer = 0f;
                            }
                        }
                        else
                        {
                            _dwellTimer = 0f;
                        }
                        _lastHitPosition = hitPoint;
                    }

                    // Мерцание освещения в комнате
                    if (cfg.EnableRoomFlicker && hitCollider != null)
                    {
                        var room = hitCollider.GetComponentInParent<RoomIdentifier>();
                        if (room != null && room.TryGetComponent<RoomLightController>(out var rlc))
                        {
                            rlc.ServerFlickerLights(0.15f);
                        }
                    }
                }
            }
            else
            {
                HideImpact();
            }

            if (EyesLight != null)
            {
                EyesLight.transform.position = (leftEye + rightEye) * 0.5f;
                EyesLight.LightIntensity = (cfg.EyeLightIntensity + Mathf.PingPong(time * 6f, 0.8f)) * intensity;
            }
        }

        private void HideBeams()
        {
            if (LeftBeamAura != null) LeftBeamAura.transform.localScale = Vector3.zero;
            if (LeftBeamMain != null) LeftBeamMain.transform.localScale = Vector3.zero;
            if (LeftBeamCore != null) LeftBeamCore.transform.localScale = Vector3.zero;
            if (RightBeamAura != null) RightBeamAura.transform.localScale = Vector3.zero;
            if (RightBeamMain != null) RightBeamMain.transform.localScale = Vector3.zero;
            if (RightBeamCore != null) RightBeamCore.transform.localScale = Vector3.zero;

            foreach (var h in LeftHelix) if (h != null) h.transform.localScale = Vector3.zero;
            foreach (var h in RightHelix) if (h != null) h.transform.localScale = Vector3.zero;
        }

        private void HideImpact()
        {
            if (ImpactBlast != null) ImpactBlast.transform.localScale = Vector3.zero;
            if (ImpactPlasma != null) ImpactPlasma.transform.localScale = Vector3.zero;
            if (ImpactCore != null) ImpactCore.transform.localScale = Vector3.zero;
            if (ImpactRing1 != null) ImpactRing1.transform.localScale = Vector3.zero;
            if (ImpactRing2 != null) ImpactRing2.transform.localScale = Vector3.zero;
            foreach (var s in Sparks) if (s != null) s.transform.localScale = Vector3.zero;
            if (ImpactLight != null) ImpactLight.LightIntensity = 0f;
        }

        private void UpdateTripleBeam(PrimitiveObjectToy aura, PrimitiveObjectToy main, PrimitiveObjectToy core, Vector3 start, Vector3 end, float jitter, float alpha)
        {
            Vector3 direction = end - start;
            float distance = direction.magnitude;
            if (distance < 0.05f) return;

            Vector3 midPoint = start + (direction * 0.5f);
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);

            if (aura != null)
            {
                aura.transform.position = midPoint;
                aura.transform.rotation = rotation;
                aura.transform.localScale = new Vector3(0.026f * jitter, distance * 0.5f, 0.026f * jitter);
            }

            if (main != null)
            {
                main.transform.position = midPoint;
                main.transform.rotation = rotation;
                main.transform.localScale = new Vector3(0.013f * jitter, distance * 0.5f, 0.013f * jitter);
            }

            if (core != null)
            {
                core.transform.position = midPoint;
                core.transform.rotation = rotation;
                core.transform.localScale = new Vector3(0.0045f * alpha, distance * 0.5f, 0.0045f * alpha);
            }
        }

        private void UpdateHelix(PrimitiveObjectToy[] helix, Vector3 start, Vector3 end, float time, float phaseOffset, float alpha)
        {
            Vector3 dir = end - start;
            float dist = dir.magnitude;
            if (dist < 0.05f) return;

            Vector3 forward = dir.normalized;
            Vector3 right = Vector3.Cross(forward, Vector3.up).normalized;
            if (right == Vector3.zero) right = Vector3.right;
            Vector3 up = Vector3.Cross(forward, right).normalized;

            for (int i = 0; i < helix.Length; i++)
            {
                if (helix[i] == null) continue;

                float t = Mathf.Repeat((time * 2.2f) + (i * 0.25f), 1f);
                float angle = (t * Mathf.PI * 8f) + phaseOffset;
                float spiralRadius = Mathf.Sin(t * Mathf.PI) * 0.035f * alpha;

                Vector3 pointOnLine = Vector3.Lerp(start, end, t);
                Vector3 helixPos = pointOnLine + (right * Mathf.Cos(angle) * spiralRadius) + (up * Mathf.Sin(angle) * spiralRadius);

                helix[i].transform.position = helixPos;
                helix[i].transform.localScale = Vector3.one * ((0.012f + Mathf.Sin(t * Mathf.PI) * 0.006f) * alpha);
            }
        }

        private void UpdateImpactRing(PrimitiveObjectToy ring, Vector3 hitPoint, Vector3 hitNormal, float time, float timeOffset, float maxScale)
        {
            if (ring == null || maxScale <= 0.001f) return;

            float progress = Mathf.Repeat((time * 2f) + timeOffset, 1f);
            float currentRadius = progress * maxScale;

            ring.transform.position = hitPoint + (hitNormal * (0.015f + (progress * 0.02f)));
            ring.transform.rotation = Quaternion.FromToRotation(Vector3.up, hitNormal);
            ring.transform.localScale = new Vector3(currentRadius, 0.002f, currentRadius);
        }

        public void SetVisible(bool state)
        {
            if (!state)
            {
                HideBeams();
                HideImpact();
                if (LeftEyeCore != null) LeftEyeCore.transform.localScale = Vector3.zero;
                if (LeftEyeCorona != null) LeftEyeCorona.transform.localScale = Vector3.zero;
                if (RightEyeCore != null) RightEyeCore.transform.localScale = Vector3.zero;
                if (RightEyeCorona != null) RightEyeCorona.transform.localScale = Vector3.zero;
                if (EyesLight != null) EyesLight.LightIntensity = 0f;
            }
        }

        private void EnsureObjectsExist()
        {
            if (LeftBeamAura == null || LeftBeamMain == null || LeftBeamCore == null ||
                RightBeamAura == null || RightBeamMain == null || RightBeamCore == null ||
                LeftEyeCore == null || LeftEyeCorona == null || RightEyeCore == null || RightEyeCorona == null ||
                ImpactBlast == null || ImpactPlasma == null || ImpactCore == null ||
                ImpactRing1 == null || ImpactRing2 == null ||
                LeftHelix.Any(h => h == null) || RightHelix.Any(h => h == null) || Sparks.Any(s => s == null))
            {
                Destroy();
                SpawnAllObjects();
            }
        }

        public void Destroy()
        {
            DestroyObject(LeftBeamAura);
            DestroyObject(LeftBeamMain);
            DestroyObject(LeftBeamCore);
            DestroyObject(RightBeamAura);
            DestroyObject(RightBeamMain);
            DestroyObject(RightBeamCore);

            DestroyObject(LeftEyeCore);
            DestroyObject(LeftEyeCorona);
            DestroyObject(RightEyeCore);
            DestroyObject(RightEyeCorona);

            DestroyObject(ImpactBlast);
            DestroyObject(ImpactPlasma);
            DestroyObject(ImpactCore);
            DestroyObject(ImpactRing1);
            DestroyObject(ImpactRing2);

            for (int i = 0; i < 4; i++)
            {
                DestroyObject(LeftHelix[i]);
                DestroyObject(RightHelix[i]);
            }

            for (int i = 0; i < Sparks.Length; i++)
            {
                DestroyObject(Sparks[i]);
            }

            if (ImpactLight != null && ImpactLight.gameObject != null) NetworkServer.Destroy(ImpactLight.gameObject);
            if (EyesLight != null && EyesLight.gameObject != null) NetworkServer.Destroy(EyesLight.gameObject);
        }

        private void DestroyObject(PrimitiveObjectToy toy)
        {
            if (toy != null && toy.gameObject != null)
            {
                NetworkServer.Destroy(toy.gameObject);
            }
        }
    }

    public class EyeLasersPlugin : Plugin<EyeLasersConfig>
    {
        public override string Name => "EyeLasers";
        public override string Description => "Лазеры из глаз";
        public override string Author => "relskiy";
        public override Version Version => new Version(1, 0, 0);
        public override Version RequiredApiVersion => new Version(1, 1, 7);

        public static EyeLasersPlugin Instance { get; private set; }
        public readonly Dictionary<ReferenceHub, LaserController> ActiveLasers = new Dictionary<ReferenceHub, LaserController>();

        private static PrimitiveObjectToy _primitivePrefab;
        private static LightSourceToy _lightPrefab;
        private CoroutineHandle _laserRoutine;

        public override void Enable()
        {
            Instance = this;
            _laserRoutine = Timing.RunCoroutine(LaserLoop());

            PlayerEvents.ChangingRole += OnChangingRole;
            PlayerEvents.Dying += OnDying;
            PlayerEvents.Left += OnLeft;

            if (Config.EnableKeybindSetting)
            {
                RegisterServerSpecificSettings();
                ServerSpecificSettingsSync.ServerOnSettingValueReceived += OnServerSettingValueReceived;
            }
        }

        public override void Disable()
        {
            PlayerEvents.ChangingRole -= OnChangingRole;
            PlayerEvents.Dying -= OnDying;
            PlayerEvents.Left -= OnLeft;

            if (Config.EnableKeybindSetting)
            {
                ServerSpecificSettingsSync.ServerOnSettingValueReceived -= OnServerSettingValueReceived;
            }

            Timing.KillCoroutines(_laserRoutine);

            foreach (var controller in ActiveLasers.Values)
            {
                controller.Destroy();
            }
            ActiveLasers.Clear();

            Instance = null;
        }

        private void RegisterServerSpecificSettings()
        {
            var setting = new SSKeybindSetting(
                Config.KeybindSettingId,
                Config.KeybindLabel,
                KeyCode.None
            );

            if (ServerSpecificSettingsSync.DefinedSettings == null)
            {
                ServerSpecificSettingsSync.DefinedSettings = new ServerSpecificSettingBase[] { setting };
            }
            else if (!ServerSpecificSettingsSync.DefinedSettings.Any(s => s != null && s.SettingId == Config.KeybindSettingId))
            {
                var list = ServerSpecificSettingsSync.DefinedSettings.ToList();
                list.Add(setting);
                ServerSpecificSettingsSync.DefinedSettings = list.ToArray();
            }
        }

        private void OnServerSettingValueReceived(ReferenceHub hub, ServerSpecificSettingBase setting)
        {
            if (setting is SSKeybindSetting keybind && keybind.SettingId == Config.KeybindSettingId)
            {
                if (keybind.SyncIsPressed)
                {
                    HandleToggle(hub);
                }
            }
        }

        private void OnChangingRole(PlayerChangingRoleEventArgs ev)
        {
            if (ev.Player?.ReferenceHub != null)
                RemoveLaserInstant(ev.Player.ReferenceHub);
        }

        private void OnDying(PlayerDyingEventArgs ev)
        {
            if (ev.Player?.ReferenceHub != null)
                RemoveLaserInstant(ev.Player.ReferenceHub);
        }

        private void OnLeft(PlayerLeftEventArgs ev)
        {
            if (ev.Player?.ReferenceHub != null)
                RemoveLaserInstant(ev.Player.ReferenceHub);
        }

        public void HandleToggle(ReferenceHub hub)
        {
            if (!HasPermission(hub))
                return;

            if (ActiveLasers.ContainsKey(hub))
            {
                RemoveLaser(hub);
            }
            else
            {
                if (IsAlive(hub) && TryGetRoleOffsets(hub.roleManager.CurrentRole.RoleTypeId, out _))
                {
                    AddLaser(hub);
                }
            }
        }

        public static bool HasPermission(ReferenceHub hub)
        {
            if (!Instance.Config.RestrictAccess)
                return true;

            if (hub == null)
                return true;

            if (hub.serverRoles.BypassMode || hub.serverRoles.Permissions > 0)
                return true;

            if (Instance.Config.AllowedUserIds != null && Instance.Config.AllowedUserIds.Contains(hub.authManager.UserId))
                return true;

            if (Instance.Config.AllowedGroups != null && !string.IsNullOrEmpty(hub.serverRoles.Group?.BadgeText))
            {
                string groupName = hub.serverRoles.Group.BadgeText.ToLower();
                if (Instance.Config.AllowedGroups.Any(g => g.ToLower() == groupName))
                    return true;
            }

            return false;
        }

        public static bool TryGetRoleOffsets(RoleTypeId role, out LaserOffset offset)
        {
            switch (role)
            {
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

                default:
                    offset = default;
                    return false;
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
            primitive.MovementSmoothing = 60;

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
            light.MovementSmoothing = 60;

            NetworkServer.Spawn(light.gameObject);
            return light;
        }

        public static void SpawnScorchMark(Vector3 point, Vector3 normal)
        {
            PrimitiveObjectToy toy = SpawnPrimitive(UnityEngine.PrimitiveType.Cylinder, new Color(0.03f, 0.03f, 0.03f, 0.95f));
            if (toy == null) return;

            toy.transform.position = point + (normal * 0.005f);
            toy.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal);
            toy.transform.localScale = new Vector3(0.08f, 0.001f, 0.08f);

            Timing.CallDelayed(8f, () =>
            {
                if (toy != null && toy.gameObject != null)
                    NetworkServer.Destroy(toy.gameObject);
            });
        }

        public static bool IsAlive(ReferenceHub hub)
        {
            if (hub == null || hub.roleManager == null || hub.roleManager.CurrentRole == null)
                return false;

            RoleTypeId role = hub.roleManager.CurrentRole.RoleTypeId;
            return role != RoleTypeId.None && role != RoleTypeId.Spectator && role != RoleTypeId.Overwatch;
        }

        public void AddLaser(ReferenceHub hub)
        {
            if (ActiveLasers.TryGetValue(hub, out LaserController existing))
            {
                if (existing.State == LaserAnimState.Dissipation)
                {
                    existing.State = LaserAnimState.Ignition;
                }
                return;
            }

            ActiveLasers.Add(hub, new LaserController(hub));
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
                float deltaTime = Time.time - lastTime;
                lastTime = Time.time;
                toRemove.Clear();

                foreach (var pair in ActiveLasers)
                {
                    ReferenceHub hub = pair.Key;
                    LaserController controller = pair.Value;

                    if (!IsAlive(hub) || controller.State == LaserAnimState.Dead)
                    {
                        controller.Destroy();
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
                    if (cam == null)
                        continue;

                    Vector3 eyeLevelCenter = cam.position + (cam.forward * offsets.Forward) + (cam.up * offsets.Height);
                    Vector3 leftEye = eyeLevelCenter - (cam.right * offsets.Width);
                    Vector3 rightEye = eyeLevelCenter + (cam.right * offsets.Width);

                    Vector3 hitPoint;
                    Vector3 hitNormal = -cam.forward;
                    Collider hitCollider = null;
                    Vector3 rayOrigin = cam.position + (cam.forward * 0.35f);

                    if (Physics.Raycast(rayOrigin, cam.forward, out RaycastHit hit, 120f, LayerMask.GetMask("Default", "Player", "Hitbox", "Glass", "Door")))
                    {
                        hitPoint = hit.point;
                        hitNormal = hit.normal;
                        hitCollider = hit.collider;
                    }
                    else
                    {
                        hitPoint = cam.position + (cam.forward * 100f);
                    }

                    controller.Update(leftEye, rightEye, hitPoint, hitNormal, hitCollider, deltaTime);
                }

                if (toRemove.Count > 0)
                {
                    for (int i = 0; i < toRemove.Count; i++)
                    {
                        ActiveLasers.Remove(toRemove[i]);
                    }
                }

                yield return Timing.WaitForOneFrame;
            }
        }
    }

    [CommandHandler(typeof(ClientCommandHandler))]
    [CommandHandler(typeof(RemoteAdminCommandHandler))]
    public class LasersCommand : ICommand
    {
        public string Command => "lasers";
        public string[] Aliases => new[] { "laser", "eyelasers" };
        public string Description => "Управление лазерами: .lasers on | .lasers off | .lasers give <id/all>";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            ReferenceHub senderHub = null;

            if (sender is PlayerCommandSender pcs)
            {
                senderHub = pcs.ReferenceHub;
            }

            if (!EyeLasersPlugin.HasPermission(senderHub))
            {
                response = "У вас нет прав на использование этой команды.";
                return false;
            }

            if (arguments.Count == 0)
            {
                response = "Команды:\n.lasers on - Включить лазеры себе\n.lasers off - Выключить лазеры себе\n.lasers give <id/all> - Выдать/забрать лазеры игроку по ID или всем";
                return false;
            }

            string subCommand = arguments.At(0).ToLower();

            switch (subCommand)
            {
                case "on":
                    if (senderHub == null)
                    {
                        response = "Команда доступна только игроку.";
                        return false;
                    }

                    if (!EyeLasersPlugin.IsAlive(senderHub))
                    {
                        response = "Вы должны быть живы.";
                        return false;
                    }

                    if (!EyeLasersPlugin.TryGetRoleOffsets(senderHub.roleManager.CurrentRole.RoleTypeId, out _))
                    {
                        response = "Ваша текущая роль не поддерживает лазеры из глаз.";
                        return false;
                    }

                    EyeLasersPlugin.Instance.AddLaser(senderHub);
                    response = "Запуск лазерного взгляда...";
                    return true;

                case "off":
                    if (senderHub == null)
                    {
                        response = "Команда доступна только игроку.";
                        return false;
                    }

                    EyeLasersPlugin.Instance.RemoveLaser(senderHub);
                    response = "Отключение лазерного взгляда...";
                    return true;

                case "give":
                    if (arguments.Count < 2)
                    {
                        response = "Использование: .lasers give <ID игрока / all>";
                        return false;
                    }

                    string targetArg = arguments.At(1).ToLower();

                    if (targetArg == "all")
                    {
                        int count = 0;
                        foreach (ReferenceHub hub in ReferenceHub.AllHubs)
                        {
                            if (EyeLasersPlugin.IsAlive(hub) && EyeLasersPlugin.TryGetRoleOffsets(hub.roleManager.CurrentRole.RoleTypeId, out _))
                            {
                                EyeLasersPlugin.Instance.AddLaser(hub);
                                count++;
                            }
                        }

                        response = $"Лазеры запущены для {count} чел.";
                        return true;
                    }

                    if (int.TryParse(targetArg, out int targetId))
                    {
                        ReferenceHub targetHub = ReferenceHub.AllHubs.FirstOrDefault(h => h.PlayerId == targetId);
                        if (targetHub == null)
                        {
                            response = $"Игрок с ID {targetId} не найден.";
                            return false;
                        }

                        if (!EyeLasersPlugin.IsAlive(targetHub))
                        {
                            response = $"Игрок {targetHub.nicknameSync.MyNick} мёртв.";
                            return false;
                        }

                        if (!EyeLasersPlugin.TryGetRoleOffsets(targetHub.roleManager.CurrentRole.RoleTypeId, out _))
                        {
                            response = $"Роль игрока {targetHub.nicknameSync.MyNick} не поддерживает лазеры.";
                            return false;
                        }

                        if (EyeLasersPlugin.Instance.ActiveLasers.ContainsKey(targetHub))
                        {
                            EyeLasersPlugin.Instance.RemoveLaser(targetHub);
                            response = $"Лазеры отключаются для {targetHub.nicknameSync.MyNick}.";
                        }
                        else
                        {
                            EyeLasersPlugin.Instance.AddLaser(targetHub);
                            response = $"Лазеры активируются для {targetHub.nicknameSync.MyNick}.";
                        }

                        return true;
                    }

                    response = "Некорректный ID игрока.";
                    return false;

                default:
                    response = "Неизвестная команда. Доступно: on, off, give <id/all>";
                    return false;
            }
        }
    }
}
