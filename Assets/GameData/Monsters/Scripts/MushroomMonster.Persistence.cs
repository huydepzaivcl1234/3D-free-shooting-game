using UnityEngine;
namespace MiningSimulator.Ores
{
    public sealed partial class MushroomMonster
    {
        public MonsterSpawnZone.SavedMonster CaptureWorld(string prefab)
        {
            var s=new MonsterSpawnZone.SavedMonster{kind=1,prefab=prefab,level=Level,boss=IsBoss,summon=IsGodSummon,damage=scaledDamage,burnDamage=scaledBurnDamage};s.CaptureHealth(health);return s;
        }
        public void RestoreWorld(MonsterSpawnZone.SavedMonster s)
        {
            Level=Mathf.Max(1,s.level);scaledDamage=s.damage;scaledBurnDamage=s.burnDamage;s.RestoreHealth(health);
            // Old normal Bread enemies can have damage rolled with the old shared stat multiplier.
            if (!s.boss && !s.summon && SpeciesData is BreadMonsterData bread)
            {
                float maxRoll = Mathf.Max(.01f, bread.randomStatMultiplier.x, bread.randomStatMultiplier.y);
                scaledDamage = Mathf.Min(scaledDamage, bread.AttackDamageAtLevel(Level) * maxRoll);
            }
            var p=transform.parent?transform.parent.lossyScale:Vector3.one;transform.localScale=new Vector3(s.scale.x/p.x,s.scale.y/p.y,s.scale.z/p.z);transform.SetPositionAndRotation(s.position,s.rotation);
        }
    }
}
