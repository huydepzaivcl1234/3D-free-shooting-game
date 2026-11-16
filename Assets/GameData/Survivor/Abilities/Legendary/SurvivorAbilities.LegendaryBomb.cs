using System.Collections.Generic;
using MiningSimulator.Ores;
using UnityEngine;

namespace GameData.Player.Script
{
    public sealed partial class SurvivorAbilities
    {
        private int bombHits, bombRepeatLevel;
        private float bombCooldown, bombFlight, regenFxTime;
        private Vector3 bombAim, bombOrigin, bombLanding;
        private bool bombInFlight, bombApplyingDamage;
        private GameObject bombModel, repeatBombModel, bombSmoke, regenerationRing;
        private MiningAudioManager bombAudio;
        private readonly List<CharacterHealth> bombVictims = new();

        private void RecordBombHit(CharacterHealth victim)
        {
            if (bombApplyingDamage || !health || health.Health <= 0 || Level(SurvivorUpgradeId.LegendaryBomb) <= 0)
                return;

            int required = Mathf.Max(1, Settings.legendary.giantBomb.requiredHits);
            if (bombHits < required)
                bombHits++;

            bombAim = victim.transform.position;
        }

        private void TickBomb(float dt)
        {
            bombCooldown = Mathf.Max(0, bombCooldown - dt);
            int level = Level(SurvivorUpgradeId.LegendaryBomb);
            if (level <= 0)
                return;

            var rule = Settings.legendary.giantBomb;
            if (!bombInFlight)
            {
                int required = Mathf.Max(1, rule.requiredHits);
                if (bombHits < required || bombCooldown > 0)
                    return;

                bombHits -= required;
                bombOrigin = transform.position + Vector3.up * rule.launchHeight;
                Vector3 offset = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(bombAim - transform.position, Vector3.up), rule.maxThrowDistance);
                bombLanding = transform.position + offset;
                if (Physics.Raycast(bombLanding + Vector3.up * 30, Vector3.down, out var ground, 90, rule.groundLayers, QueryTriggerInteraction.Ignore))
                    bombLanding = ground.point;

                bombInFlight = true;
                bombFlight = 0;
                bombRepeatLevel = RollAbilityRepeat();
                PlayBombSound(rule.launchSound, bombOrigin, rule.launchVolume);
                RenderBomb();
                return;
            }

            bombFlight += dt;
            if (bombFlight < Mathf.Max(.01f, rule.riseSeconds) + Mathf.Max(.01f, rule.fallSeconds))
            {
                RenderBomb();
                return;
            }

            bombInFlight = false;
            bombCooldown = Mathf.Max(0, rule.cooldown);
            if (bombModel)
                bombModel.SetActive(false);
            if (repeatBombModel)
                repeatBombModel.SetActive(false);

            int repeat = bombRepeatLevel;
            bombRepeatLevel = 0;

            bombImpactPosition = bombLanding;
            PlaySurvivorEffect(ref bombSmoke, rule.smokePrefab, bombLanding, rule.smokeScale);
            PlayBombSound(rule.explosionSound, bombLanding, rule.explosionVolume);
            if (TryGetComponent<TopDownSurvivor>(out var camera))
                camera.ShakeSideways(rule.shakeDistance, rule.shakeSeconds);
            bombImpactTime = Mathf.Max(.01f, rule.effectSeconds);

            Targets(bombVictims, bombLanding, Mathf.Max(.01f, rule.radius));
            bombApplyingDamage = true;
            try
            {
                foreach (var victim in bombVictims)
                {
                    if (!victim || !victim.IsTargetable)
                        continue;

                    float damage = rule.damage.At(level) + victim.MaxHealth * rule.enemyMaxHealthPercent * .01f;
                    victim.DealDamageWithTrueBonus(damage, rule.damageType, gameObject, 0, abilityProc: true);
                    if (repeat > 0 && victim.IsTargetable)
                        victim.DealDamageWithTrueBonus(RepeatedAbilityDamage(damage, repeat), rule.damageType,
                            gameObject, 0, abilityProc: true);
                }
            }
            finally
            {
                bombApplyingDamage = false;
                bombVictims.Clear();
            }
        }

        private void PlayBombSound(AudioClip clip, Vector3 position, float volume)
        {
            if (!clip || volume <= 0)
                return;

            if (!bombAudio)
                bombAudio = FindAnyObjectByType<MiningAudioManager>();
            if (bombAudio)
                bombAudio.PlayWorldSfx(clip, position, volume);
        }

        private void RenderBomb()
        {
            var rule = Settings.legendary.giantBomb;
            if (!rule.model)
                return;

            if (!bombModel)
                bombModel = Instantiate(rule.model);

            float rise = Mathf.Max(.01f, rule.riseSeconds);
            float fall = Mathf.Max(.01f, rule.fallSeconds);
            Vector3 landingCenter = bombLanding + Vector3.up * (rule.modelSize * .5f);
            Vector3 apex = Vector3.Lerp(bombOrigin, landingCenter, .45f);
            apex.y = Mathf.Max(bombOrigin.y, landingCenter.y) + rule.arcHeight;
            float ascent = Mathf.Clamp01(bombFlight / rise);
            float descent = Mathf.Clamp01((bombFlight - rise) / fall);
            Vector3 position = bombFlight < rise
                ? Vector3.Lerp(bombOrigin, apex, 1 - (1 - ascent) * (1 - ascent))
                : Vector3.Lerp(apex, landingCenter, descent * descent);

            bombModel.transform.SetPositionAndRotation(position, Quaternion.AngleAxis(bombFlight * rule.tumbleDegreesPerSecond, new Vector3(1, .35f, .5f)));
            bombModel.transform.localScale = Vector3.one * rule.modelSize;
            bombModel.SetActive(true);
            if (bombRepeatLevel > 0)
            {
                if (!repeatBombModel) repeatBombModel = Instantiate(rule.model);
                Vector3 offset = Vector3.Cross(Vector3.up, (bombLanding - bombOrigin).normalized);
                if (offset.sqrMagnitude < .01f) offset = transform.right;
                float spread = rule.modelSize * .7f * (1f - descent);
                repeatBombModel.transform.SetPositionAndRotation(position + offset.normalized * spread, bombModel.transform.rotation);
                repeatBombModel.transform.localScale = bombModel.transform.localScale;
                repeatBombModel.SetActive(true);
            }
        }

        private float bombImpactTime;
        private Vector3 regenFxPosition;

        internal void ShowRegeneration()
        {
            if (!isActiveAndEnabled)
                return;

            var visual = Settings.regenerationVisual;
            regenFxPosition = transform.position + Vector3.up * visual.footOffset;
            PlaySurvivorEffect(ref regenerationRing, visual.prefab, regenFxPosition, visual.scale);
            regenFxTime = Mathf.Max(.01f, visual.seconds);
        }

        private static void PlaySurvivorEffect(ref GameObject instance, GameObject prefab, Vector3 position, float scale, float elapsed = 0)
        {
            if (!prefab)
                return;

            if (!instance)
                instance = Instantiate(prefab);

            instance.SetActive(false);
            instance.transform.SetPositionAndRotation(position, Quaternion.identity);
            instance.transform.localScale = prefab.transform.localScale * scale;
            instance.SetActive(true);
            var particles = instance.GetComponentsInChildren<ParticleSystem>();
            foreach (var particle in particles)
            {
                var main = particle.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.loop = false;
                particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (elapsed > 0)
                    particle.Simulate(elapsed, false, true);
                particle.Play(false);
            }
        }

        private void TickBombEffects(float dt)
        {
            if (regenFxTime > 0 && (regenFxTime -= dt) <= 0 && regenerationRing)
                regenerationRing.SetActive(false);

            if (bombImpactTime <= 0 || (bombImpactTime -= dt) > 0)
                return;

            if (bombSmoke) bombSmoke.SetActive(false);
        }

        private Vector3 bombImpactPosition;

        private void RestoreBombVisuals()
        {
            if (bombInFlight)
                RenderBomb();

            var rule = Settings.legendary.giantBomb;
            if (bombImpactTime > 0)
            {
                float elapsed = Mathf.Max(0, rule.effectSeconds - bombImpactTime);
                PlaySurvivorEffect(ref bombSmoke, rule.smokePrefab, bombImpactPosition, rule.smokeScale, elapsed);
            }

            var visual = Settings.regenerationVisual;
            if (regenFxTime > 0)
                PlaySurvivorEffect(ref regenerationRing, visual.prefab, regenFxPosition, visual.scale, Mathf.Max(0, visual.seconds - regenFxTime));
        }

        private void ResetBomb()
        {
            bombHits = bombRepeatLevel = 0;
            bombCooldown = bombFlight = bombImpactTime = regenFxTime = 0;
            bombInFlight = false;
            if (bombModel) bombModel.SetActive(false);
            if (repeatBombModel) repeatBombModel.SetActive(false);
            if (bombSmoke) bombSmoke.SetActive(false);
            if (regenerationRing) regenerationRing.SetActive(false);
        }

        private void DestroyBomb()
        {
            if (bombModel) Destroy(bombModel);
            if (repeatBombModel) Destroy(repeatBombModel);
            if (bombSmoke) Destroy(bombSmoke);
            if (regenerationRing) Destroy(regenerationRing);
        }
    }
}
