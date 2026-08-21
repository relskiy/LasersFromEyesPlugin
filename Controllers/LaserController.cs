using AdminToys;
using CustomPlayerEffects;
using EyeLasers.Configs;
using Footprinting;
using InventorySystem;
using InventorySystem.Items.Pickups;
using InventorySystem.Items.ThrowableProjectiles;
using MapGeneration;
using Mirror;
using PlayerRoles;
using PlayerStatsSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Utils;

namespace EyeLasers.Controllers
{
    public class LaserController
    {
        public static readonly Color ColorBeamAura = new Color(1f, 0.02f, 0.08f, 0.26f);
        public static readonly Color ColorBeamMain = new Color(1f, 0.35f, 0.02f, 0.88f);
        public static readonly Color ColorCore = new Color(1f, 1f, 0.98f, 1f);
        public static readonly Color ColorCorona = new Color(1f, 0.25f, 0.02f, 0.85f);
        public static readonly Color ColorFlare = new Color(1f, 0.6f, 0.1f, 0.8f);
        public static readonly Color ColorGyro = new Color(1f, 0.5f, 0.1f, 0.6f);
        public static readonly Color ColorHalo = new Color(1f, 0.15f, 0.02f, 0.12f);
        public static readonly Color ColorDiamond = new Color(1f, 0.9f, 0.4f, 0.85f);
        public static readonly Color ColorArc = new Color(0.9f, 0.95f, 1f, 0.9f);
        public static readonly Color ColorRibbon = new Color(1f, 0.15f, 0.02f, 0.55f);
        public static readonly Color ColorBlast = new Color(1f, 0.05f, 0.02f, 0.55f);
        public static readonly Color ColorPlasma = new Color(1f, 0.45f, 0.05f, 0.95f);
        public static readonly Color ColorShield = new Color(1f, 0.7f, 0.15f, 0.2f);
        public static readonly Color ColorMoltenCore = new Color(1f, 0.85f, 0.2f, 0.95f);
        public static readonly Color ColorMoltenRim = new Color(0.85f, 0.1f, 0.02f, 0.85f);
        public static readonly Color ColorRing = new Color(1f, 0.45f, 0.05f, 0.75f);
        public static readonly Color ColorSparkYellow = new Color(1f, 1f, 0.85f, 1f);
        public static readonly Color ColorSparkOrange = new Color(1f, 0.45f, 0.05f, 1f);
        public static readonly Color ColorEmber = new Color(1f, 0.3f, 0.02f, 0.85f);
        public static readonly Color ColorPlume = new Color(0.12f, 0.08f, 0.06f, 0.35f);
        public static readonly Color ColorRicochet = new Color(1f, 0.5f, 0.1f, 0.75f);
        public static readonly Color ColorNode = new Color(1f, 0.9f, 0.3f, 0.85f);
        public static readonly Color ColorScorch = new Color(0.015f, 0.015f, 0.015f, 0.95f);

        public static readonly Vector3 ScaleScorch = new Vector3(0.10f, 0.001f, 0.10f);

        public ReferenceHub Owner;
        public LaserAnimState State = LaserAnimState.Ignition;
        public bool IsDestroyed { get; private set; } = false;
        public bool HideModels { get; private set; } = false;
        public float AutoDisableTimer { get; set; } = -1f;

        private readonly List<GameObject> _spawnedObjects = new List<GameObject>(32);
        private readonly List<PrimitiveObjectToy> _spawnedPrimitives = new List<PrimitiveObjectToy>(32);
        private static readonly Collider[] OverlapBuffer = new Collider[128];

        private float _stateTimer = 0f;
        private float _dwellTimer = 0f;
        private float _nextHeavyUpdateTime = 0f;
        private float _nextDamageTime = 0f;
        private float _nextEnvironmentDamageTime = 0f;
        private Vector3 _lastHitPosition = Vector3.zero;

        public PrimitiveObjectToy LeftBeamAura;
        public PrimitiveObjectToy LeftBeamMain;
        public PrimitiveObjectToy LeftBeamCore;
        public PrimitiveObjectToy RightBeamAura;
        public PrimitiveObjectToy RightBeamMain;
        public PrimitiveObjectToy RightBeamCore;

        public PrimitiveObjectToy LeftEyeCore;
        public PrimitiveObjectToy LeftEyeCorona;
        public PrimitiveObjectToy RightEyeCore;
        public PrimitiveObjectToy RightEyeCorona;

        public PrimitiveObjectToy LeftAnamorphicFlare;
        public PrimitiveObjectToy RightAnamorphicFlare;
        public PrimitiveObjectToy LeftGyroRing;
        public PrimitiveObjectToy RightGyroRing;
        public PrimitiveObjectToy HeadIonizationHalo;

        public PrimitiveObjectToy[] ShockDiamonds = new PrimitiveObjectToy[4];
        public PrimitiveObjectToy TeslaArc;
        public PrimitiveObjectToy[] EnergyRibbons = new PrimitiveObjectToy[3];

        public PrimitiveObjectToy ImpactBlast;
        public PrimitiveObjectToy ImpactPlasma;
        public PrimitiveObjectToy ImpactCore;
        public PrimitiveObjectToy ImpactDistortionShield;
        public PrimitiveObjectToy MoltenPoolCore;
        public PrimitiveObjectToy MoltenPoolRim;

        public PrimitiveObjectToy[] ImpactRings = new PrimitiveObjectToy[2];
        public PrimitiveObjectToy[] Sparks = new PrimitiveObjectToy[6];
        public PrimitiveObjectToy[] Embers = new PrimitiveObjectToy[4];
        public PrimitiveObjectToy[] ThermalPlume = new PrimitiveObjectToy[2];
        public PrimitiveObjectToy RicochetJet;

        public PrimitiveObjectToy[] TargetPlasmaNodes = new PrimitiveObjectToy[3];
        public PrimitiveObjectToy PunchThroughFlash;

        public LightSourceToy ImpactLight;
        public LightSourceToy EyesLight;

        public LaserController(ReferenceHub hub, bool initialHideModels = false)
        {
            Owner = hub;
            SpawnAllObjects();
            if (initialHideModels)
            {
                ApplyLocalVisibility(true);
            }
        }

        private PrimitiveObjectToy CreatePrimitive(UnityEngine.PrimitiveType type, Color color)
        {
            var toy = EyeLasersPlugin.SpawnPrimitive(type, color);
            if (toy != null && toy.gameObject != null)
            {
                _spawnedObjects.Add(toy.gameObject);
                _spawnedPrimitives.Add(toy);
            }
            return toy;
        }

        private LightSourceToy CreateLight(Color color, float intensity, float range)
        {
            var light = EyeLasersPlugin.SpawnLight(color, intensity, range);
            if (light != null && light.gameObject != null)
            {
                _spawnedObjects.Add(light.gameObject);
            }
            return light;
        }

        public void ApplyLocalVisibility(bool hide)
        {
            HideModels = hide;
            if (Owner == null || Owner.connectionToClient == null) return;

            for (int i = 0; i < _spawnedPrimitives.Count; i++)
            {
                var toy = _spawnedPrimitives[i];
                if (toy == null) continue;

                try
                {
                    if (hide)
                    {
                        Owner.connectionToClient.Send(new ObjectDestroyMessage { netId = toy.netId });
                    }
                }
                catch { }
            }
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

            LeftBeamAura = CreatePrimitive(UnityEngine.PrimitiveType.Cylinder, ColorBeamAura);
            LeftBeamMain = CreatePrimitive(UnityEngine.PrimitiveType.Cylinder, ColorBeamMain);
            LeftBeamCore = CreatePrimitive(UnityEngine.PrimitiveType.Cylinder, ColorCore);

            RightBeamAura = CreatePrimitive(UnityEngine.PrimitiveType.Cylinder, ColorBeamAura);
            RightBeamMain = CreatePrimitive(UnityEngine.PrimitiveType.Cylinder, ColorBeamMain);
            RightBeamCore = CreatePrimitive(UnityEngine.PrimitiveType.Cylinder, ColorCore);

            LeftEyeCore = CreatePrimitive(UnityEngine.PrimitiveType.Sphere, ColorCore);
            LeftEyeCorona = CreatePrimitive(UnityEngine.PrimitiveType.Sphere, ColorCorona);
            RightEyeCore = CreatePrimitive(UnityEngine.PrimitiveType.Sphere, ColorCore);
            RightEyeCorona = CreatePrimitive(UnityEngine.PrimitiveType.Sphere, ColorCorona);

            LeftAnamorphicFlare = CreatePrimitive(UnityEngine.PrimitiveType.Cube, ColorFlare);
            RightAnamorphicFlare = CreatePrimitive(UnityEngine.PrimitiveType.Cube, ColorFlare);
            LeftGyroRing = CreatePrimitive(UnityEngine.PrimitiveType.Cylinder, ColorGyro);
            RightGyroRing = CreatePrimitive(UnityEngine.PrimitiveType.Cylinder, ColorGyro);
            HeadIonizationHalo = CreatePrimitive(UnityEngine.PrimitiveType.Sphere, ColorHalo);

            for (int i = 0; i < ShockDiamonds.Length; i++)
            {
                ShockDiamonds[i] = CreatePrimitive(UnityEngine.PrimitiveType.Cube, ColorDiamond);
            }

            TeslaArc = CreatePrimitive(UnityEngine.PrimitiveType.Cylinder, ColorArc);

            for (int i = 0; i < EnergyRibbons.Length; i++)
            {
                EnergyRibbons[i] = CreatePrimitive(UnityEngine.PrimitiveType.Cube, ColorRibbon);
            }

            ImpactBlast = CreatePrimitive(UnityEngine.PrimitiveType.Sphere, ColorBlast);
            ImpactPlasma = CreatePrimitive(UnityEngine.PrimitiveType.Sphere, ColorPlasma);
            ImpactCore = CreatePrimitive(UnityEngine.PrimitiveType.Sphere, ColorCore);
            ImpactDistortionShield = CreatePrimitive(UnityEngine.PrimitiveType.Sphere, ColorShield);

            MoltenPoolCore = CreatePrimitive(UnityEngine.PrimitiveType.Cylinder, ColorMoltenCore);
            MoltenPoolRim = CreatePrimitive(UnityEngine.PrimitiveType.Cylinder, ColorMoltenRim);

            for (int i = 0; i < ImpactRings.Length; i++)
            {
                ImpactRings[i] = CreatePrimitive(UnityEngine.PrimitiveType.Cylinder, ColorRing);
            }

            for (int i = 0; i < Sparks.Length; i++)
            {
                Color spkColor = (i % 2 == 0) ? ColorSparkYellow : ColorSparkOrange;
                Sparks[i] = CreatePrimitive(UnityEngine.PrimitiveType.Sphere, spkColor);
            }

            for (int i = 0; i < Embers.Length; i++)
            {
                Embers[i] = CreatePrimitive(UnityEngine.PrimitiveType.Sphere, ColorEmber);
            }

            for (int i = 0; i < ThermalPlume.Length; i++)
            {
                ThermalPlume[i] = CreatePrimitive(UnityEngine.PrimitiveType.Sphere, ColorPlume);
            }

            RicochetJet = CreatePrimitive(UnityEngine.PrimitiveType.Cylinder, ColorRicochet);

            for (int i = 0; i < TargetPlasmaNodes.Length; i++)
            {
                TargetPlasmaNodes[i] = CreatePrimitive(UnityEngine.PrimitiveType.Sphere, ColorNode);
            }

            PunchThroughFlash = CreatePrimitive(UnityEngine.PrimitiveType.Sphere, ColorFlare);

            if (cfg.EnableLights)
            {
                ImpactLight = CreateLight(new Color(1f, 0.35f, 0.08f, 1f), cfg.ImpactLightIntensity, cfg.ImpactLightRange);
                EyesLight = CreateLight(new Color(1f, 0.45f, 0.1f, 1f), cfg.EyeLightIntensity, 4.0f);
            }
        }

        public void Update(Vector3 leftEye, Vector3 rightEye, Vector3 hitPoint, Vector3 hitNormal, Collider hitCollider, float deltaTime, bool hasNearbyObservers)
        {
            if (IsDestroyed || State == LaserAnimState.Dead) return;

            _stateTimer += deltaTime;
            EyeLasersConfig cfg = EyeLasersPlugin.Instance.Config;
            float time = Time.time;

            if (AutoDisableTimer > 0f)
            {
                AutoDisableTimer -= deltaTime;
                if (AutoDisableTimer <= 0f)
                {
                    StartShutdown();
                }
            }

            float beamProgress = 1f;
            float intensity = 1f;

            if (State == LaserAnimState.Ignition)
            {
                float ignitionProgress = Mathf.Clamp01(_stateTimer / Mathf.Max(0.01f, cfg.StartupDuration));
                if (ignitionProgress < 0.25f)
                {
                    intensity = ignitionProgress / 0.25f;
                    beamProgress = 0f;
                }
                else
                {
                    intensity = 1f;
                    beamProgress = Mathf.SmoothStep(0f, 1f, (ignitionProgress - 0.25f) / 0.75f);
                }

                if (_stateTimer >= cfg.StartupDuration)
                {
                    State = LaserAnimState.Active;
                }
            }
            else if (State == LaserAnimState.Active)
            {
                beamProgress = 1f;
                intensity = 1f;
            }
            else if (State == LaserAnimState.Dissipation)
            {
                float dissProgress = Mathf.Clamp01(_stateTimer / Mathf.Max(0.01f, cfg.ShutdownDuration));
                intensity = 1f - dissProgress;
                beamProgress = 1f - Mathf.Pow(dissProgress, 2f);

                if (_stateTimer >= cfg.ShutdownDuration)
                {
                    State = LaserAnimState.Dead;
                    Destroy();
                    return;
                }
            }

            Vector3 eyeCenter = (leftEye + rightEye) * 0.5f;
            Transform cam = Owner.PlayerCameraReference;
            Vector3 camForward = cam.forward;

            Vector3 animatedHitLeft = Vector3.Lerp(leftEye, hitPoint, beamProgress);
            Vector3 animatedHitRight = Vector3.Lerp(rightEye, hitPoint, beamProgress);

            if (!HideModels)
            {
                if (beamProgress > 0.01f)
                {
                    float beamJitter = (1f + (Mathf.Sin(time * 50f) * 0.06f)) * intensity;

                    UpdateTripleBeam(LeftBeamAura, LeftBeamMain, LeftBeamCore, leftEye, animatedHitLeft, beamJitter, intensity);
                    UpdateTripleBeam(RightBeamAura, RightBeamMain, RightBeamCore, rightEye, animatedHitRight, beamJitter, intensity);

                    if (hasNearbyObservers)
                    {
                        UpdateShockDiamonds(ShockDiamonds, leftEye, animatedHitLeft, time, intensity);
                        UpdateTeslaArc(TeslaArc, leftEye, rightEye, animatedHitLeft, animatedHitRight, time, intensity);
                        UpdatePlasmaRibbons(EnergyRibbons, eyeCenter, hitPoint, time, intensity);
                    }
                }
                else
                {
                    HideBeams();
                }

                float eyeCorePulse = 0.018f * intensity;
                float eyeCoronaPulse = (0.036f + (Mathf.Sin(time * 25f) * 0.005f)) * intensity;

                SetTransformSafe(LeftEyeCore, leftEye, Quaternion.identity, Vector3.one * eyeCorePulse);
                SetTransformSafe(LeftEyeCorona, leftEye, Quaternion.identity, Vector3.one * eyeCoronaPulse);
                SetTransformSafe(RightEyeCore, rightEye, Quaternion.identity, Vector3.one * eyeCorePulse);
                SetTransformSafe(RightEyeCorona, rightEye, Quaternion.identity, Vector3.one * eyeCoronaPulse);

                if (hasNearbyObservers && intensity > 0.1f)
                {
                    Quaternion flareRot = Quaternion.LookRotation(camForward, cam.up);
                    float flareWidth = 0.25f * intensity;
                    SetTransformSafe(LeftAnamorphicFlare, leftEye + (camForward * 0.02f), flareRot, new Vector3(flareWidth, 0.0025f, 0.0025f));
                    SetTransformSafe(RightAnamorphicFlare, rightEye + (camForward * 0.02f), flareRot, new Vector3(flareWidth, 0.0025f, 0.0025f));

                    Quaternion gyroRotL = Quaternion.Euler(time * 200f, time * 120f, 0f);
                    Quaternion gyroRotR = Quaternion.Euler(-time * 200f, -time * 120f, 0f);
                    Vector3 ringScale = new Vector3(0.05f, 0.001f, 0.05f) * intensity;

                    SetTransformSafe(LeftGyroRing, leftEye + (camForward * 0.035f), gyroRotL, ringScale);
                    SetTransformSafe(RightGyroRing, rightEye + (camForward * 0.035f), gyroRotR, ringScale);

                    SetTransformSafe(HeadIonizationHalo, eyeCenter - (camForward * 0.05f), Quaternion.identity, Vector3.one * (0.42f * intensity));
                }
                else
                {
                    SetTransformSafe(LeftAnamorphicFlare, Vector3.zero, Quaternion.identity, Vector3.zero);
                    SetTransformSafe(RightAnamorphicFlare, Vector3.zero, Quaternion.identity, Vector3.zero);
                    SetTransformSafe(LeftGyroRing, Vector3.zero, Quaternion.identity, Vector3.zero);
                    SetTransformSafe(RightGyroRing, Vector3.zero, Quaternion.identity, Vector3.zero);
                    SetTransformSafe(HeadIonizationHalo, Vector3.zero, Quaternion.identity, Vector3.zero);
                }
            }
            else
            {
                HideBeams();
                SetTransformSafe(LeftEyeCore, Vector3.zero, Quaternion.identity, Vector3.zero);
                SetTransformSafe(LeftEyeCorona, Vector3.zero, Quaternion.identity, Vector3.zero);
                SetTransformSafe(RightEyeCore, Vector3.zero, Quaternion.identity, Vector3.zero);
                SetTransformSafe(RightEyeCorona, Vector3.zero, Quaternion.identity, Vector3.zero);
                SetTransformSafe(LeftAnamorphicFlare, Vector3.zero, Quaternion.identity, Vector3.zero);
                SetTransformSafe(RightAnamorphicFlare, Vector3.zero, Quaternion.identity, Vector3.zero);
                SetTransformSafe(LeftGyroRing, Vector3.zero, Quaternion.identity, Vector3.zero);
                SetTransformSafe(RightGyroRing, Vector3.zero, Quaternion.identity, Vector3.zero);
                SetTransformSafe(HeadIonizationHalo, Vector3.zero, Quaternion.identity, Vector3.zero);
            }

            ReferenceHub hitPlayerTarget = null;
            if (hitCollider != null)
            {
                hitPlayerTarget = ReferenceHub.GetHub(hitCollider.gameObject);
                if (hitPlayerTarget == null && hitCollider.transform.root != null)
                {
                    hitPlayerTarget = ReferenceHub.GetHub(hitCollider.transform.root.gameObject);
                }
            }

            if (beamProgress >= 0.95f && intensity > 0.1f)
            {
                if (!HideModels)
                {
                    float blastPulse = (0.26f + Mathf.PingPong(time * 6f, 0.08f)) * intensity;
                    float plasmaPulse = 0.14f * intensity;
                    float corePulse = 0.07f * intensity;
                    float shieldPulse = (0.42f + (Mathf.Sin(time * 18f) * 0.05f)) * intensity;

                    SetTransformSafe(ImpactBlast, hitPoint + (hitNormal * 0.02f), Quaternion.identity, Vector3.one * blastPulse);
                    SetTransformSafe(ImpactPlasma, hitPoint + (hitNormal * 0.035f), Quaternion.identity, Vector3.one * plasmaPulse);
                    SetTransformSafe(ImpactCore, hitPoint + (hitNormal * 0.05f), Quaternion.identity, Vector3.one * corePulse);
                    SetTransformSafe(ImpactDistortionShield, hitPoint + (hitNormal * 0.01f), Quaternion.identity, Vector3.one * shieldPulse);

                    Quaternion surfaceRot = Quaternion.FromToRotation(Vector3.up, hitNormal);
                    float moltenCoreScale = 0.13f * intensity;
                    float moltenRimScale = 0.25f * intensity;
                    SetTransformSafe(MoltenPoolCore, hitPoint + (hitNormal * 0.008f), surfaceRot, new Vector3(moltenCoreScale, 0.001f, moltenCoreScale));
                    SetTransformSafe(MoltenPoolRim, hitPoint + (hitNormal * 0.004f), surfaceRot, new Vector3(moltenRimScale, 0.001f, moltenRimScale));

                    if (hasNearbyObservers)
                    {
                        for (int i = 0; i < ImpactRings.Length; i++)
                        {
                            UpdateImpactRing(ImpactRings[i], hitPoint, hitNormal, time, i * 0.5f, (0.6f + (i * 0.25f)) * intensity, 2.2f + i);
                        }
                        UpdateSparks(Sparks, hitPoint, hitNormal, time, intensity);
                        UpdateEmbers(Embers, hitPoint, time, intensity);
                        UpdateThermalPlume(ThermalPlume, hitPoint, hitNormal, time, intensity);
                        UpdateRicochetJet(RicochetJet, hitPoint, hitNormal, time, intensity);
                    }

                    if (hitPlayerTarget != null && hitPlayerTarget != Owner && EyeLasersPlugin.IsAlive(hitPlayerTarget))
                    {
                        UpdateTargetCocoon(TargetPlasmaNodes, PunchThroughFlash, hitPlayerTarget, camForward, time, intensity);
                    }
                    else
                    {
                        HideTargetEffects();
                    }
                }
                else
                {
                    HideImpact();
                    HideTargetEffects();
                }

                if (ImpactLight != null)
                {
                    ImpactLight.transform.position = hitPoint + (hitNormal * 0.35f);
                    ImpactLight.LightIntensity = (cfg.ImpactLightIntensity + Mathf.PingPong(time * 12f, 2.0f)) * intensity;
                }

                if (time >= _nextHeavyUpdateTime)
                {
                    _nextHeavyUpdateTime = time + 0.08f;

                    if (cfg.DetonateExplosives && Owner != null && Owner.PlayerCameraReference != null)
                    {
                        CheckAndDetonateExplosives(eyeCenter, hitPoint, hitCollider);
                    }

                    if (cfg.EnableDamage && hitPlayerTarget != null && hitPlayerTarget != Owner && EyeLasersPlugin.IsAlive(hitPlayerTarget))
                    {
                        if (time >= _nextDamageTime)
                        {
                            _nextDamageTime = time + cfg.DamageInterval;

                            bool isScp = hitPlayerTarget.IsSCP();
                            float damage = isScp ? cfg.DamagePerTickScp : cfg.DamagePerTickHuman;

                            hitPlayerTarget.playerStats.DealDamage(new CustomReasonDamageHandler(cfg.DeathReason, damage));

                            if (!EyeLasersPlugin.IsAlive(hitPlayerTarget))
                            {
                                EyeLasersPlugin.LogEvent($"[KILL] {Owner.nicknameSync.MyNick} испепелил {hitPlayerTarget.nicknameSync.MyNick} ({hitPlayerTarget.roleManager.CurrentRole.RoleTypeId})");
                            }
                        }
                    }

                    if (cfg.EnableScorchMarks)
                    {
                        if (Vector3.Distance(hitPoint, _lastHitPosition) < 0.25f)
                        {
                            _dwellTimer += 0.08f;
                            if (_dwellTimer >= 0.28f)
                            {
                                EyeLasersPlugin.ScorchPool?.Spawn(hitPoint, hitNormal);
                                _dwellTimer = 0f;
                            }
                        }
                        else
                        {
                            _dwellTimer = 0f;
                        }
                        _lastHitPosition = hitPoint;
                    }

                    if (time >= _nextEnvironmentDamageTime && hitCollider != null)
                    {
                        _nextEnvironmentDamageTime = time + 0.15f;
                        HandleEnvironmentDamage(hitCollider, hitPoint);
                    }
                }
            }
            else
            {
                HideImpact();
                HideTargetEffects();
            }

            if (EyesLight != null)
            {
                EyesLight.transform.position = eyeCenter;
                EyesLight.LightIntensity = (cfg.EyeLightIntensity + Mathf.PingPong(time * 6f, 1.0f)) * intensity;
            }
        }

        private void UpdateShockDiamonds(PrimitiveObjectToy[] diamonds, Vector3 start, Vector3 end, float time, float intensity)
        {
            Vector3 dir = end - start;
            float dist = dir.magnitude;
            if (dist < 0.2f) return;

            Vector3 forward = dir.normalized;
            Quaternion rot = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0, 0, 45f);

            for (int i = 0; i < diamonds.Length; i++)
            {
                if (diamonds[i] == null) continue;

                float fraction = (i + 1) * 0.2f;
                Vector3 pos = Vector3.Lerp(start, end, fraction);
                float pulse = 0.024f * intensity;

                SetTransformSafe(diamonds[i], pos, rot, new Vector3(pulse, pulse, 0.05f * intensity));
            }
        }

        private void UpdateTeslaArc(PrimitiveObjectToy arc, Vector3 leftStart, Vector3 rightStart, Vector3 leftEnd, Vector3 rightEnd, float time, float intensity)
        {
            if (arc == null) return;

            float t = 0.4f;
            Vector3 pL = Vector3.Lerp(leftStart, leftEnd, t);
            Vector3 pR = Vector3.Lerp(rightStart, rightEnd, t);

            Vector3 span = pR - pL;
            float length = span.magnitude;
            Vector3 mid = (pL + pR) * 0.5f;

            Quaternion arcRot = Quaternion.FromToRotation(Vector3.up, span.normalized);
            SetTransformSafe(arc, mid, arcRot, new Vector3(0.006f * intensity, length * 0.5f, 0.006f * intensity));
        }

        private void UpdateThermalPlume(PrimitiveObjectToy[] plume, Vector3 hitPoint, Vector3 hitNormal, float time, float intensity)
        {
            for (int i = 0; i < plume.Length; i++)
            {
                if (plume[i] == null) continue;

                float cycle = Mathf.Repeat((time * 1.5f) + (i * 0.5f), 1f);
                float riseDist = cycle * 1.0f;
                float expandScale = (0.06f + (cycle * 0.2f)) * (1f - (cycle * 0.6f)) * intensity;

                Vector3 pos = hitPoint + (hitNormal * 0.05f) + (Vector3.up * riseDist);
                SetTransformSafe(plume[i], pos, Quaternion.identity, Vector3.one * Mathf.Max(0.001f, expandScale));
            }
        }

        private void UpdateRicochetJet(PrimitiveObjectToy jet, Vector3 hitPoint, Vector3 hitNormal, float time, float intensity)
        {
            if (jet == null) return;

            Vector3 incoming = (hitPoint - Owner.PlayerCameraReference.position).normalized;
            Vector3 reflect = Vector3.Reflect(incoming, hitNormal).normalized;
            if (reflect == Vector3.zero) reflect = hitNormal;

            float cycle = Mathf.Repeat(time * 3.5f, 1f);
            float dist = cycle * 0.7f;
            Vector3 pos = hitPoint + (hitNormal * 0.02f) + (reflect * (dist * 0.5f));
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, reflect);

            float length = (0.15f * (1f - cycle)) * intensity;
            SetTransformSafe(jet, pos, rot, new Vector3(0.012f * intensity, length, 0.012f * intensity));
        }

        private void UpdateTargetCocoon(PrimitiveObjectToy[] nodes, PrimitiveObjectToy flash, ReferenceHub target, Vector3 forwardDir, float time, float intensity)
        {
            if (target.PlayerCameraReference == null) return;

            Vector3 chestPos = target.PlayerCameraReference.position - (Vector3.up * 0.35f);

            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i] == null) continue;

                float angle = (time * 8f) + (i * (Mathf.PI * 2f / nodes.Length));
                float heightOffset = Mathf.Sin((time * 6f) + (i * 1.5f)) * 0.35f;

                Vector3 pos = chestPos + (Vector3.up * heightOffset) + (new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 0.4f);
                SetTransformSafe(nodes[i], pos, Quaternion.identity, Vector3.one * (0.035f * intensity));
            }

            Vector3 flashPos = chestPos + (forwardDir * 0.5f);
            SetTransformSafe(flash, flashPos, Quaternion.identity, Vector3.one * (0.32f * intensity));
        }

        private void HideTargetEffects()
        {
            for (int i = 0; i < TargetPlasmaNodes.Length; i++)
            {
                SetTransformSafe(TargetPlasmaNodes[i], Vector3.zero, Quaternion.identity, Vector3.zero);
            }
            SetTransformSafe(PunchThroughFlash, Vector3.zero, Quaternion.identity, Vector3.zero);
        }

        private void HandleEnvironmentDamage(Collider col, Vector3 point)
        {
            EyeLasersConfig cfg = EyeLasersPlugin.Instance.Config;

            if (cfg.BreakWindows)
            {
                var window = col.GetComponentInParent<BreakableWindow>();
                if (window != null && !window.IsBroken)
                {
                    window.Damage(50f, null, point);
                }
            }

            if (cfg.DamageDoors)
            {
                var door = col.GetComponentInParent<Interactables.Interobjects.DoorUtils.DoorVariant>();
                if (door is Interactables.Interobjects.DoorUtils.IDamageableDoor dmgDoor)
                {
                    dmgDoor.ServerDamage(20f, Interactables.Interobjects.DoorUtils.DoorDamageType.ServerCommand);
                }
            }
        }

        private void CheckAndDetonateExplosives(Vector3 origin, Vector3 hitPoint, Collider directHitCol)
        {
            try
            {
                EyeLasersConfig cfg = EyeLasersPlugin.Instance.Config;
                Vector3 beamVec = hitPoint - origin;
                float beamLength = beamVec.magnitude;
                if (beamLength < 0.1f) return;
                Vector3 beamDir = beamVec / beamLength;

                if (cfg.DetonateInHands)
                {
                    foreach (ReferenceHub hub in ReferenceHub.AllHubs)
                    {
                        if (hub == null || hub == Owner || !EyeLasersPlugin.IsAlive(hub) || hub.PlayerCameraReference == null)
                            continue;

                        Vector3 playerPos = hub.PlayerCameraReference.position;
                        bool isDirectHit = directHitCol != null && directHitCol.transform.root.gameObject == hub.gameObject;

                        if (IsPointNearBeam(playerPos, origin, beamDir, beamLength, cfg.DetonationRadius) || isDirectHit)
                        {
                            if (hub.inventory != null && hub.inventory.CurInstance is ThrowableItem throwable)
                            {
                                ItemType grenadeType = throwable.ItemTypeId;
                                if (IsExplosiveType(grenadeType))
                                {
                                    hub.inventory.ServerRemoveItem(throwable.ItemSerial, null);
                                    SpawnWorldExplosion(playerPos, grenadeType, Owner);
                                    EyeLasersPlugin.LogEvent($"[DETONATE-HANDS] Граната {grenadeType} в руках {hub.nicknameSync.MyNick} взорвана лучом {Owner.nicknameSync.MyNick}");
                                    return;
                                }
                            }
                        }
                    }
                }

                if (directHitCol != null && TryDetonateCollider(directHitCol, cfg))
                    return;

                int hitCount = Physics.OverlapSphereNonAlloc(hitPoint, 2.2f, OverlapBuffer, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < hitCount; i++)
                {
                    var col = OverlapBuffer[i];
                    if (col != null && TryDetonateCollider(col, cfg))
                        return;
                }

                Vector3 midBeam = origin + (beamDir * (beamLength * 0.5f));
                int midCount = Physics.OverlapSphereNonAlloc(midBeam, beamLength * 0.55f, OverlapBuffer, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < midCount; i++)
                {
                    var col = OverlapBuffer[i];
                    if (col == null) continue;

                    if (IsPointNearBeam(col.transform.position, origin, beamDir, beamLength, cfg.DetonationRadius))
                    {
                        if (TryDetonateCollider(col, cfg))
                            return;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EyeLasers] Explosives check error: {ex}");
            }
        }

        private bool TryDetonateCollider(Collider col, EyeLasersConfig cfg)
        {
            if (col == null) return false;

            if (cfg.DetonateFlyingProjectiles)
            {
                var proj = col.GetComponentInParent<ThrownProjectile>();
                if (proj != null && proj.gameObject != null)
                {
                    Vector3 pPos = proj.transform.position;
                    ItemType expType = ItemType.GrenadeHE;

                    if (proj.name.IndexOf("Flash", StringComparison.OrdinalIgnoreCase) >= 0) expType = ItemType.GrenadeFlash;
                    else if (proj.name.IndexOf("018", StringComparison.OrdinalIgnoreCase) >= 0) expType = ItemType.SCP018;
                    else if (proj.name.IndexOf("2176", StringComparison.OrdinalIgnoreCase) >= 0) expType = ItemType.SCP2176;

                    NetworkServer.Destroy(proj.gameObject);
                    SpawnWorldExplosion(pPos, expType, Owner);
                    EyeLasersPlugin.LogEvent($"[DETONATE-PROJECTILE] Летящий снаряд {expType} перехвачен лазером {Owner.nicknameSync.MyNick}");
                    return true;
                }
            }

            var pickup = col.GetComponentInParent<ItemPickupBase>();
            if (pickup != null && pickup.gameObject != null)
            {
                ItemType type = pickup.Info.ItemId;
                bool isExp = IsExplosiveType(type);
                bool isAmmo = IsAmmoItem(type);

                if ((isExp && cfg.DetonateFloorPickups) || (isAmmo && cfg.DetonateAmmo))
                {
                    Vector3 pos = pickup.transform.position;
                    ItemType expType = (type == ItemType.GrenadeFlash) ? ItemType.GrenadeFlash : ItemType.GrenadeHE;
                    pickup.DestroySelf();
                    SpawnWorldExplosion(pos, expType, Owner);
                    EyeLasersPlugin.LogEvent($"[DETONATE-PICKUP] Предмет {type} на полу подорван лазером {Owner.nicknameSync.MyNick}");
                    return true;
                }
            }

            return false;
        }

        private static bool IsPointNearBeam(Vector3 point, Vector3 origin, Vector3 dir, float length, float radius)
        {
            Vector3 toPoint = point - origin;
            float t = Vector3.Dot(toPoint, dir);
            if (t < -0.5f || t > length + 1.5f) return false;

            Vector3 projection = origin + (dir * Mathf.Clamp(t, 0f, length));
            return (point - projection).sqrMagnitude <= (radius * radius);
        }

        private static bool IsExplosiveType(ItemType type)
        {
            switch (type)
            {
                case ItemType.GrenadeHE:
                case ItemType.GrenadeFlash:
                case ItemType.SCP018:
                case ItemType.SCP2176:
                    return true;
                default:
                    return false;
            }
        }

        public static void SpawnWorldExplosion(Vector3 position, ItemType grenadeType, ReferenceHub owner)
        {
            if (grenadeType == ItemType.GrenadeHE)
            {
                ExplosionUtils.ServerExplode(position, owner != null ? new Footprint(owner) : default, ExplosionType.Grenade);
                return;
            }

            if (InventoryItemLoader.AvailableItems.TryGetValue(grenadeType, out var itemBase) && itemBase is ThrowableItem throwable)
            {
                var projectile = UnityEngine.Object.Instantiate(throwable.Projectile, position, Quaternion.identity);
                if (projectile == null) return;

                if (owner != null)
                    projectile.PreviousOwner = new Footprint(owner);

                NetworkServer.Spawn(projectile.gameObject);

                if (projectile is TimeGrenade grenade)
                {
                    grenade.ServerFuseEnd();
                }
                else if (projectile is Scp018Projectile scp018)
                {
                    if (scp018.TryGetComponent<Rigidbody>(out var rb))
                    {
                        rb.linearVelocity = UnityEngine.Random.onUnitSphere * 18f;
                    }
                }
                else if (projectile is Scp2176Projectile)
                {
                    var room = RoomIdentifier.AllRoomIdentifiers.FirstOrDefault(r => (r.transform.position - position).sqrMagnitude < 64f);
                    if (room != null && room.TryGetComponent<RoomLightController>(out var rlc))
                    {
                        rlc.ServerFlickerLights(8f);
                    }
                    NetworkServer.Destroy(projectile.gameObject);
                }
            }
        }

        private static bool IsAmmoItem(ItemType type)
        {
            switch (type)
            {
                case ItemType.Ammo9x19:
                case ItemType.Ammo556x45:
                case ItemType.Ammo762x39:
                case ItemType.Ammo12gauge:
                case ItemType.Ammo44cal:
                    return true;
                default:
                    return false;
            }
        }

        private static void SetTransformSafe(PrimitiveObjectToy toy, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            if (toy == null || toy.gameObject == null) return;
            Transform t = toy.transform;
            t.position = pos;
            if (rot != Quaternion.identity) t.rotation = rot;
            t.localScale = scale;
        }

        private void HideBeams()
        {
            SetTransformSafe(LeftBeamAura, Vector3.zero, Quaternion.identity, Vector3.zero);
            SetTransformSafe(LeftBeamMain, Vector3.zero, Quaternion.identity, Vector3.zero);
            SetTransformSafe(LeftBeamCore, Vector3.zero, Quaternion.identity, Vector3.zero);

            SetTransformSafe(RightBeamAura, Vector3.zero, Quaternion.identity, Vector3.zero);
            SetTransformSafe(RightBeamMain, Vector3.zero, Quaternion.identity, Vector3.zero);
            SetTransformSafe(RightBeamCore, Vector3.zero, Quaternion.identity, Vector3.zero);

            for (int i = 0; i < ShockDiamonds.Length; i++) SetTransformSafe(ShockDiamonds[i], Vector3.zero, Quaternion.identity, Vector3.zero);
            SetTransformSafe(TeslaArc, Vector3.zero, Quaternion.identity, Vector3.zero);
            for (int i = 0; i < EnergyRibbons.Length; i++) SetTransformSafe(EnergyRibbons[i], Vector3.zero, Quaternion.identity, Vector3.zero);
        }

        private void HideImpact()
        {
            SetTransformSafe(ImpactBlast, Vector3.zero, Quaternion.identity, Vector3.zero);
            SetTransformSafe(ImpactPlasma, Vector3.zero, Quaternion.identity, Vector3.zero);
            SetTransformSafe(ImpactCore, Vector3.zero, Quaternion.identity, Vector3.zero);
            SetTransformSafe(ImpactDistortionShield, Vector3.zero, Quaternion.identity, Vector3.zero);
            SetTransformSafe(MoltenPoolCore, Vector3.zero, Quaternion.identity, Vector3.zero);
            SetTransformSafe(MoltenPoolRim, Vector3.zero, Quaternion.identity, Vector3.zero);

            for (int i = 0; i < ImpactRings.Length; i++) SetTransformSafe(ImpactRings[i], Vector3.zero, Quaternion.identity, Vector3.zero);
            for (int i = 0; i < Sparks.Length; i++) SetTransformSafe(Sparks[i], Vector3.zero, Quaternion.identity, Vector3.zero);
            for (int i = 0; i < Embers.Length; i++) SetTransformSafe(Embers[i], Vector3.zero, Quaternion.identity, Vector3.zero);
            for (int i = 0; i < ThermalPlume.Length; i++) SetTransformSafe(ThermalPlume[i], Vector3.zero, Quaternion.identity, Vector3.zero);
            SetTransformSafe(RicochetJet, Vector3.zero, Quaternion.identity, Vector3.zero);

            if (ImpactLight != null) ImpactLight.LightIntensity = 0f;
        }

        private void UpdateTripleBeam(PrimitiveObjectToy aura, PrimitiveObjectToy main, PrimitiveObjectToy core, Vector3 start, Vector3 end, float jitter, float alpha)
        {
            Vector3 direction = end - start;
            float distance = direction.magnitude;
            if (distance < 0.05f) return;

            Vector3 midPoint = start + (direction * 0.5f);
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);

            SetTransformSafe(aura, midPoint, rotation, new Vector3(0.045f * jitter, distance * 0.5f, 0.045f * jitter));
            SetTransformSafe(main, midPoint, rotation, new Vector3(0.02f * jitter, distance * 0.5f, 0.02f * jitter));
            SetTransformSafe(core, midPoint, rotation, new Vector3(0.006f * alpha, distance * 0.5f, 0.006f * alpha));
        }

        private void UpdatePlasmaRibbons(PrimitiveObjectToy[] ribbons, Vector3 start, Vector3 end, float time, float alpha)
        {
            Vector3 dir = end - start;
            float dist = dir.magnitude;
            if (dist < 0.1f) return;

            Vector3 forward = dir.normalized;
            Vector3 right = Vector3.Cross(forward, Vector3.up).normalized;
            if (right == Vector3.zero) right = Vector3.right;
            Vector3 up = Vector3.Cross(forward, right).normalized;

            for (int i = 0; i < ribbons.Length; i++)
            {
                if (ribbons[i] == null) continue;

                float t = Mathf.Repeat((time * 2.5f) + (i * (1f / ribbons.Length)), 1f);
                float angle = (time * 8f) + (i * Mathf.PI / 3f);
                float wave = Mathf.Sin((t * 15f) + (time * 12f)) * 0.1f * alpha;

                Vector3 pos = Vector3.Lerp(start, end, t) + (right * Mathf.Cos(angle) * wave) + (up * Mathf.Sin(angle) * wave);
                Quaternion rot = Quaternion.LookRotation(forward, up);

                SetTransformSafe(ribbons[i], pos, rot, new Vector3(0.02f * alpha, 0.08f * alpha, 0.02f * alpha));
            }
        }

        private void UpdateImpactRing(PrimitiveObjectToy ring, Vector3 hitPoint, Vector3 hitNormal, float time, float timeOffset, float maxScale, float speed)
        {
            if (ring == null || maxScale <= 0.001f) return;

            float progress = Mathf.Repeat((time * speed) + timeOffset, 1f);
            float currentRadius = progress * maxScale;

            SetTransformSafe(ring, hitPoint + (hitNormal * (0.012f + (progress * 0.035f))), Quaternion.FromToRotation(Vector3.up, hitNormal), new Vector3(currentRadius, 0.002f, currentRadius));
        }

        private void UpdateSparks(PrimitiveObjectToy[] sparks, Vector3 hitPoint, Vector3 hitNormal, float time, float intensity)
        {
            Vector3 incomingDir = (hitPoint - Owner.PlayerCameraReference.position).normalized;
            Vector3 reflectDir = Vector3.Reflect(incomingDir, hitNormal).normalized;
            if (reflectDir == Vector3.zero) reflectDir = hitNormal;

            Vector3 tangent = Vector3.Cross(reflectDir, Vector3.up).normalized;
            if (tangent == Vector3.zero) tangent = Vector3.right;
            Vector3 binormal = Vector3.Cross(reflectDir, tangent).normalized;

            for (int i = 0; i < sparks.Length; i++)
            {
                if (sparks[i] == null) continue;

                float cycle = Mathf.Repeat(time * (5f + (i * 0.5f)) + (i * 0.1f), 1f);
                float spraySpread = (1f - Mathf.Pow(cycle - 0.5f, 2f)) * (0.25f + (i * 0.02f));
                float distanceAlongReflect = cycle * (0.9f + (i * 0.05f));
                float angle = i * (Mathf.PI * 2f / sparks.Length) + (time * 8f);

                Vector3 sparkPos = hitPoint + (hitNormal * 0.02f)
                                            + (reflectDir * distanceAlongReflect)
                                            + (tangent * Mathf.Cos(angle) * spraySpread)
                                            + (binormal * Mathf.Sin(angle) * spraySpread);

                float sparkScale = (0.022f * (1f - cycle)) * intensity;
                SetTransformSafe(sparks[i], sparkPos, Quaternion.identity, Vector3.one * Mathf.Max(0.001f, sparkScale));
            }
        }

        private void UpdateEmbers(PrimitiveObjectToy[] embers, Vector3 hitPoint, float time, float intensity)
        {
            for (int i = 0; i < embers.Length; i++)
            {
                if (embers[i] == null) continue;

                float cycle = Mathf.Repeat(time * (1.5f + (i * 0.2f)) + (i * 0.3f), 1f);
                float driftX = Mathf.Sin(time * 2f + i) * 0.3f * cycle;
                float driftZ = Mathf.Cos(time * 2.5f + i) * 0.3f * cycle;
                float rise = cycle * 1.2f;

                Vector3 emberPos = hitPoint + new Vector3(driftX, rise, driftZ);
                float scale = (0.03f * (1f - cycle)) * intensity;

                SetTransformSafe(embers[i], emberPos, Quaternion.identity, Vector3.one * Mathf.Max(0.001f, scale));
            }
        }

        public void SetVisible(bool state)
        {
            if (!state)
            {
                HideBeams();
                HideImpact();
                HideTargetEffects();
                SetTransformSafe(LeftEyeCore, Vector3.zero, Quaternion.identity, Vector3.zero);
                SetTransformSafe(LeftEyeCorona, Vector3.zero, Quaternion.identity, Vector3.zero);
                SetTransformSafe(RightEyeCore, Vector3.zero, Quaternion.identity, Vector3.zero);
                SetTransformSafe(RightEyeCorona, Vector3.zero, Quaternion.identity, Vector3.zero);
                SetTransformSafe(LeftAnamorphicFlare, Vector3.zero, Quaternion.identity, Vector3.zero);
                SetTransformSafe(RightAnamorphicFlare, Vector3.zero, Quaternion.identity, Vector3.zero);
                SetTransformSafe(LeftGyroRing, Vector3.zero, Quaternion.identity, Vector3.zero);
                SetTransformSafe(RightGyroRing, Vector3.zero, Quaternion.identity, Vector3.zero);
                SetTransformSafe(HeadIonizationHalo, Vector3.zero, Quaternion.identity, Vector3.zero);

                if (EyesLight != null) EyesLight.LightIntensity = 0f;
            }
        }

        public void Destroy()
        {
            if (IsDestroyed) return;

            IsDestroyed = true;
            State = LaserAnimState.Dead;

            for (int i = 0; i < _spawnedObjects.Count; i++)
            {
                var obj = _spawnedObjects[i];
                if (obj != null)
                {
                    try
                    {
                        NetworkServer.Destroy(obj);
                    }
                    catch (Exception)
                    {
                        try { UnityEngine.Object.Destroy(obj); } catch { }
                    }
                }
            }
            _spawnedObjects.Clear();
            _spawnedPrimitives.Clear();
        }
    }
}