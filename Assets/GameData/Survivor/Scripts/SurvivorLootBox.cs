using UnityEngine;
using MiningSimulator.Ores;
namespace GameData.Player.Script
{
    [DisallowMultipleComponent]
    public sealed partial class SurvivorLootBox : MonoBehaviour
    {
        public GameObject shinyEffects;
        private SurvivorDailyLoot owner;
        private MiningCardSystem cards;
        private float readyAt;
        private GameObject animeStars;
        public bool Shiny{get;private set;}
        public void Configure(SurvivorDailyLoot system,MiningCardSystem cardSystem,bool shiny){owner=system;cards=cardSystem;Shiny=shiny;if(shiny&&system.data.shinyVfx&&!animeStars){if(shinyEffects)shinyEffects.SetActive(false);animeStars=Instantiate(system.data.shinyVfx,transform);animeStars.name="Anime star sparkles";animeStars.transform.localPosition=system.data.shinyVfxOffset;animeStars.transform.localScale=Vector3.one*system.data.shinyVfxScale;shinyEffects=animeStars;}readyAt=Time.time+owner.data.pickupDelay;if(shinyEffects)shinyEffects.SetActive(shiny);if(shiny){var properties=new MaterialPropertyBlock();foreach(var renderer in GetComponentsInChildren<MeshRenderer>()){if(!renderer.sharedMaterial||!renderer.sharedMaterial.HasProperty("_EmissionColor"))continue;properties.SetColor("_EmissionColor",renderer.sharedMaterial.GetColor("_EmissionColor")*3);renderer.SetPropertyBlock(properties);}}}
        private void Update(){if(!owner||!cards||Time.time<readyAt||WorldActorVisibility.ShouldPause(this)||!cards.CanCollect)return;owner.TryOpen(this);}
    }
}
