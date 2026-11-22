using UnityEngine;
namespace MiningSimulator.Ores
{
    public sealed partial class MushroomMonster
    {
        public MonsterSpawnZone.SavedMonster CaptureWorld(string prefab)
        {
            var s=new MonsterSpawnZone.SavedMonster{kind=1,prefab=prefab,level=Level,boss=IsBoss,summon=IsGodSummon,damage=scaledDamage,burnDamage=scaledBurnDamage,directorVariant=DirectorVariant,ghostTier=ghostTier,cooldown=IsOvertimeGhost?Mathf.Max(0f,nextAttack-Time.time):0f,hasLifetime=encounterVisuals!=null,lifetime=CombatTimeRemaining};s.CaptureHealth(health);return s;
        }
        public void RestoreWorld(MonsterSpawnZone.SavedMonster s)
        {
            Level=Mathf.Max(1,s.level);scaledDamage=s.damage;scaledBurnDamage=s.burnDamage;s.RestoreHealth(health);
            if (IsOvertimeGhost) SetGhostTier(s.ghostTier);
            if (s.kind == 0 || IsOvertimeGhost) nextAttack = Time.time + Mathf.Max(0f, s.cooldown);
            if (FollowsStageDirector) encounterVisuals?.RestoreExpiry(0);
            else if (s.kind == 0 || s.hasLifetime) encounterVisuals?.RestoreExpiry(s.lifetime);
            SetDirectorVariant(s.directorVariant);
            // Old normal Bread enemies can have damage rolled with the old shared stat multiplier.
            if (!FollowsStageDirector && !s.boss && !s.summon && SpeciesData is BreadMonsterData bread)
            {
                float maxRoll = Mathf.Max(.01f, bread.randomStatMultiplier.x, bread.randomStatMultiplier.y);
                scaledDamage = Mathf.Min(scaledDamage, bread.AttackDamageAtLevel(Level) * maxRoll);
            }
            var p=transform.parent?transform.parent.lossyScale:Vector3.one;transform.localScale=new Vector3(s.scale.x/p.x,s.scale.y/p.y,s.scale.z/p.z);transform.SetPositionAndRotation(s.position,s.rotation);
        }
    }
}
