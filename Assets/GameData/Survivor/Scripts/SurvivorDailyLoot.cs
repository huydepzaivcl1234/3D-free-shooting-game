using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using MiningSimulator.Ores;
namespace GameData.Player.Script
{
    [DisallowMultipleComponent]
    public sealed partial class SurvivorDailyLoot : MonoBehaviour
    {
        public SurvivorLootData data;
        private DayNightSystem clock;
        private MiningCardSystem cards;
        private PlayerWallet wallet;
        private bool opening;
        private int interactionFrame = -1;
        private MapFeaturePopulator mapFeatures;
        private float retryAt;
        private bool saveBlocked;
        private const string Key="Survivor.DailyLoot.v1";
        [Serializable] private sealed class BoxProgress {public bool shiny,collected,bonus;public SurvivorLootBox.SavedBox runtime;}
        [Serializable] private sealed class Progress {public int version=5,day=-1,rerollsUsed,paidOpenCount;public bool shiny,collected,exhaustionRolled;public Vector3 origin;public float layoutAngle;public List<BoxProgress> boxes=new();}
        private Progress progress=new();
        private readonly RaycastHit[] groundHits=new RaycastHit[32];
        private readonly Collider[] obstacles=new Collider[32];
        private readonly List<Vector3> placementPath = new();
        internal int RerollsRemaining => data && !saveBlocked ? Mathf.Max(0, data.rerollsPerRun - progress.rerollsUsed) : 0;
        internal bool SpendReroll()
        {
            if (RerollsRemaining == 0) return false;
            progress.rerollsUsed++;
            Save();
            return true;
        }
        private readonly List<SurvivorLootBox> boxes=new();
        public IReadOnlyList<SurvivorLootBox> CurrentBoxes=>boxes;
        internal int TotalBoxCount => progress.boxes.Count;
        internal int CollectedBoxCount
        {
            get
            {
                int count = 0;
                foreach (var box in progress.boxes) if (box.collected) count++;
                return count;
            }
        }
        public SurvivorLootBox CurrentBox {get {foreach(var box in boxes)if(box)return box;return null;}}
        public int SpawnedDay=>progress.day;
        public bool CollectedToday {get {if(progress.day<0)return false;foreach(var box in progress.boxes)if(!box.collected)return false;return true;}}
        public int BoxesSpawned{get;private set;}
        private void Awake(){clock=GetComponent<DayNightSystem>();if(!clock)clock=FindAnyObjectByType<DayNightSystem>();cards=FindAnyObjectByType<MiningCardSystem>();Load();}
        private void Start()
        {
            mapFeatures = FindAnyObjectByType<MapFeaturePopulator>();
            wallet = cards && cards.Player ? cards.Player.GetComponent<PlayerWallet>() : null;
            if (!wallet) wallet = FindAnyObjectByType<PlayerWallet>();
            SurvivorAbilityLoot.Attach(this);
            RefreshMorning();
        }
        private void Update()
        {
            if (!saveBlocked && Time.time >= retryAt &&
                (progress.version < 4 || !CollectedToday || !progress.exhaustionRolled)) RefreshMorning();

            if (UnityEngine.InputSystem.Keyboard.current?.eKey.wasPressedThisFrame != true) return;
            var target = NearbyChest();
            if (!target) return;
            interactionFrame = Time.frameCount;
            SurvivorChestRewardPanel.Show(this, target);
        }

        internal float NextGoldPrice => (float)Math.Round(
            5d * Math.Pow(1.0125d, Mathf.Clamp(progress.paidOpenCount, 0, 51)), 2, MidpointRounding.AwayFromZero);

        internal string PriceLabel => NextGoldPrice.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        internal bool CanAffordNext => wallet && wallet.CurrentMoney >= NextGoldPrice;
        internal bool ClaimsInteract => interactionFrame == Time.frameCount || NearbyChest();

        internal SurvivorLootBox NearbyChest()
        {
            if (Time.timeScale <= 0f || saveBlocked || !data || !cards || !cards.Player || !cards.CanCollect || RunCheckpoint.IsRestoring) return null;
            Vector3 point = cards.Player.transform.position;
            float best = data.pickupRadius * data.pickupRadius;
            SurvivorLootBox nearest = null;
            foreach (var box in boxes)
            {
                if (!box || !box.ReadyToOpen) continue;
                float distance = (point - box.transform.position).sqrMagnitude;
                if (distance > best) continue;
                nearest = box;
                best = distance;
            }
            return nearest;
        }

        internal string ChestPrompt()
        {
            if (!NearbyChest()) return null;
            var upgrades = PlayerSupportUpgrades.For(cards.Player);
            if (!upgrades || !upgrades.HasEligibleUpgrade)
                return MiningLocalization.Text("Support upgrades maxed", "Nâng cấp bổ trợ đã đạt cấp tối đa");
            return (CanAffordNext
                ? MiningLocalization.TextKey("SURVIVOR_CHEST_VIEW_PROMPT", "[E]  View chest")
                : MiningLocalization.Text("Not enough gold", "Không đủ vàng"))
                + "  ·  " + PriceLabel + " " + MiningLocalization.Text("gold", "vàng");
        }
        private void Load()
        {
            string json=GameSave.GetString(Key,"");if(string.IsNullOrEmpty(json))return;
            try{var p=JsonUtility.FromJson<Progress>(json);if(p==null||p.version<1||p.version>5||p.day < -1 || (p.version==1 && p.day<0)){saveBlocked=true;return;}if(p.version==1){p.boxes=new List<BoxProgress>{new(){shiny=p.shiny,collected=p.collected}};p.version=2;}
                if(p.boxes==null||p.boxes.Count>51||p.boxes.Exists(b=>b==null)||
                    (p.boxes.Count==51&&(!p.exhaustionRolled||!p.boxes[50].bonus))){saveBlocked=true;return;}progress=p;}
            catch(ArgumentException){saveBlocked=true;}
        }
        private void Save(){GameSave.SetString(Key,JsonUtility.ToJson(progress));GameSave.Save();}
        private void ClearBoxes(){foreach(var box in boxes)if(box){box.gameObject.SetActive(false);Destroy(box.gameObject);}boxes.Clear();}
        public void ResetProgress(){GameSave.DeleteKey(Key);progress=new();saveBlocked=false;ClearBoxes();retryAt=0;}
        public bool RefreshMorning()
        {
            if (RunCheckpoint.IsRestoring || (progress.day < 0 && Time.timeScale <= 0)) return false;
            retryAt = Time.time + 2;
            if (saveBlocked || !data || !data.prefab || !clock || !cards || !cards.Player) return false;

            var rule = mapFeatures ? mapFeatures.LootRule : null;
            if (rule && !mapFeatures.PopulationReady) return false;
            bool initializing = progress.day < 0 || progress.version < 3;
            if (initializing)
            {
                if (progress.day < 0) progress.day = clock.DayNumber;
                int count = Mathf.Clamp(rule ? rule.spawnCount : data.startingBoxCount, 0, 50);
                while (progress.boxes.Count < count)
                    progress.boxes.Add(new BoxProgress { shiny = UnityEngine.Random.value < data.shinyChancePercent * .01f });
                progress.version = 3;
            }

            bool assignLayout = initializing || progress.version < 4;
            if (assignLayout)
            {
                progress.origin = cards.Player.transform.position;
                progress.layoutAngle = UnityEngine.Random.Range(0f, 360f);
                progress.version = 4;
            }

            int desiredCount = Mathf.Clamp(rule ? rule.spawnCount : data.startingBoxCount, 0, 50);
            bool hasBonusBox = progress.boxes.Count > 0 && progress.boxes[^1].bonus;
            int baseCount = progress.boxes.Count - (hasBonusBox ? 1 : 0);
            bool countUpdated = false;
            while (baseCount < desiredCount)
            {
                int insertAt = hasBonusBox ? progress.boxes.Count - 1 : progress.boxes.Count;
                progress.boxes.Insert(insertAt, new BoxProgress
                {
                    shiny = UnityEngine.Random.value < data.shinyChancePercent * .01f
                });
                baseCount++;
                countUpdated = true;
            }
            bool exhaustionUpdated = RollExhaustionBonus();
            bool versionUpdated = progress.version < 5;
            progress.version = 5;
            bool saveNeeded = assignLayout || countUpdated || exhaustionUpdated || versionUpdated;
            while (boxes.Count < progress.boxes.Count) boxes.Add(null);
            bool spawned = false;
            var terrain = rule ? mapFeatures.Arena : null;
            for (int i = 0; i < progress.boxes.Count; i++)
            {
                var record = progress.boxes[i];
                if (record.collected || boxes[i]) continue;
                Vector3 point;
                bool correctSavedPosition = false;
                if (record.runtime != null)
                {
                    point = record.runtime.falling ? record.runtime.landing : record.runtime.position;
                    Vector3 savedPosition = point;
                    bool overDensityLimit = MapFeaturePopulator.SpawnDensityLimitReached(point, progress.origin,
                        MapFeaturePopulator.CountSpawnDensityItems(progress.origin));
                    if (terrain != null)
                    {
                        var grid = WorldNavigationGrid.Instance;
                        if ((overDensityLimit || !terrain.TryGetFlatSupport(point, rule.clearance, out point, out _) ||
                            (terrain is TerrainGenerator && grid && !grid.TryFindPath(terrain.Center, point, placementPath))) &&
                            !TryFindPosition(i, out point)) continue;
                    }
                    else if (overDensityLimit && !TryFindPosition(i, out point)) continue;
                    correctSavedPosition = record.runtime.falling || (savedPosition - point).sqrMagnitude > .000001f;
                }
                else if (Time.timeScale <= 0 || !TryFindPosition(i, out point))
                    continue;

                SpawnBox(i, point);
                if (terrain != null)
                    boxes[i].transform.position = point;
                if (record.runtime == null || correctSavedPosition)
                {
                    record.runtime = boxes[i].CaptureRuntime();
                    saveNeeded = true;
                }
                spawned = true;
            }
            if (saveNeeded) Save();
            return spawned;
        }

        private void SpawnBox(int index, Vector3 point)
        {
            var record = progress.boxes[index];
            var rule = mapFeatures ? mapFeatures.LootRule : null;
            var go = Instantiate(rule ? rule.prefab : data.prefab, point,
                Quaternion.Euler(0, UnityEngine.Random.Range(0, 360), 0), transform);
            if (!go.TryGetComponent(out SurvivorLootBox box)) box = go.AddComponent<SurvivorLootBox>();
            boxes[index] = box;

            box.Configure(this, record.shiny);
            if (record.runtime != null) box.RestoreRuntime(record.runtime);
            WorldActorVisibility.Attach(go);
            BoxesSpawned++;
        }

        private bool TryFindPosition(int index, out Vector3 position)
        {
            position = default;
            bool bonus = progress.boxes[index].bonus;
            int boxCount = progress.boxes.Count - (progress.boxes.Count != 0 && progress.boxes[^1].bonus ? 1 : 0);
            if (bonus) boxCount = progress.boxes.Count;
            Vector3 origin = progress.origin;
            var grid = WorldNavigationGrid.Instance;
            if (grid && !grid.HasBaked) return false;
            int densityCount = MapFeaturePopulator.CountSpawnDensityItems(origin);
            int near = Mathf.Clamp(data.nearBoxCount, 0, boxCount);
            int mid = Mathf.Clamp(data.midBoxCount, 0, boxCount - near);
            bool isNear = index < near;
            bool isFar = index >= near + mid;
            int sector = isNear ? index : isFar ? index - near - mid : index - near;
            int sectors = isNear ? near : isFar ? boxCount - near - mid : mid;
            float min = Mathf.Max(1, isNear ? data.minimumDistance : isFar ? data.farMinimumDistance : data.midMinimumDistance);
            float max = Mathf.Max(min, isNear ? data.maximumDistance : isFar ? data.farMaximumDistance : data.midMaximumDistance);
            var rule = mapFeatures ? mapFeatures.LootRule : null;
            if (rule)
            {
                var terrain = mapFeatures.Arena;
                int tier = rule.minTier + (rule.maxTier - rule.minTier + 1) * index / boxCount;
                Vector3 reachableFrom = bonus ? cards.Player.transform.position : origin;
                int pathAttempts = 0;
                bool Fits(Vector3 p)
                {
                    float distance = Vector3.ProjectOnPlane(p - origin, Vector3.up).magnitude;
                    if (!bonus && (distance < min || distance > max)) return false;
                    if (MapFeaturePopulator.SpawnDensityLimitReached(p, origin, densityCount)) return false;
                    foreach (var box in boxes)
                        if (box && (box.transform.position - p).sqrMagnitude < rule.spacing * rule.spacing)
                            return false;
                    return !grid || (!grid.IsPortalSpawnExcluded(p, rule.clearance) &&
                        grid.IsCapsulePlacementClear(p, rule.clearance, 1.25f) &&
                        (!bonus || pathAttempts++ < 24) && grid.TryFindPath(reachableFrom, p, placementPath));
                }

                if (!bonus && terrain is TerrainGenerator native && native.TryGetLootSpawn(index, out var authoredPoint) &&
                    Fits(authoredPoint))
                {
                    position = authoredPoint;
                    return true;
                }
                if (bonus)
                    return terrain.TryGetFlatSpot(0, terrain.MaxTier, rule.clearance, rule.spacing, Fits, out position, out _);
                if (terrain.TryGetFlatSpot(tier, tier, rule.clearance, rule.spacing, Fits, out position, out _) ||
                    terrain.TryGetFlatSpot(rule.minTier, rule.maxTier, rule.clearance, rule.spacing, Fits, out position, out _))
                    return true;
                return false;
            }

            for (int attempt = 0; attempt < 48; attempt++)
            {
                float sectorOffset = isNear ? 0f : isFar ? .25f : .5f;
                float angle = (progress.layoutAngle + 360f * (sector + sectorOffset +
                    UnityEngine.Random.Range(-.42f, .42f)) / sectors) * Mathf.Deg2Rad;
                Vector3 direction = new(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                float radius = Mathf.Sqrt(UnityEngine.Random.Range(min * min, max * max));
                if (isFar && grid && attempt < 24)
                {
                    Bounds bounds = grid.AreaBounds;
                    float edgeX = ((direction.x > 0 ? bounds.max.x : bounds.min.x) - origin.x) / direction.x;
                    float edgeZ = ((direction.z > 0 ? bounds.max.z : bounds.min.z) - origin.z) / direction.z;
                    float edge = Mathf.Min(edgeX, edgeZ);
                    float preferred = edge * Mathf.Min(.97f, Mathf.Clamp01(data.farEdgeFraction) + UnityEngine.Random.Range(-.05f, .05f));
                    radius = Mathf.Clamp(preferred, min, max);
                }
                Vector3 candidate = origin + direction * radius;
                if (grid && !grid.TryGetGroundPoint(candidate, out candidate)) continue;
                bool nav=NavMesh.SamplePosition(candidate,out var walkable,1.25f,NavMesh.AllAreas);if(nav)candidate=walkable.position;
                float distance=Vector3.ProjectOnPlane(candidate-origin,Vector3.up).magnitude;if(distance<min||distance>max)continue;
                if (MapFeaturePopulator.SpawnDensityLimitReached(candidate, origin, densityCount)) continue;
                int count=Physics.RaycastNonAlloc(candidate+Vector3.up*8,Vector3.down,groundHits,40,data.groundLayers,QueryTriggerInteraction.Ignore);
                bool found=false;float top=float.NegativeInfinity;
                for(int i=0;i<count;i++){var hit=groundHits[i];if(hit.normal.y<.7f||hit.collider.GetComponentInParent<CharacterHealth>()||hit.collider.GetComponentInParent<SurvivorLootBox>())continue;if(nav&&Mathf.Abs(hit.point.y-candidate.y)>1.5f)continue;if(hit.point.y>top){top=hit.point.y;position=hit.point;found=true;}}
                if(!found)continue;
                int occupied=Physics.OverlapBoxNonAlloc(position+Vector3.up*.48f,new Vector3(.58f,.40f,.58f),obstacles,Quaternion.identity,~0,QueryTriggerInteraction.Ignore);
                bool blocked=occupied>=obstacles.Length;
                for(int i=0;i<occupied&&!blocked;i++)if(obstacles[i]&&obstacles[i].bounds.max.y>position.y+.12f)blocked=true;
                foreach(var box in boxes)if(box&&(box.transform.position-position).sqrMagnitude<2.25f)blocked=true;
                if (blocked) continue;
                if (grid && (Mathf.Abs(position.y - candidate.y) > .6f ||
                    !grid.TryFindPath(origin, position, placementPath))) continue;
                position += Vector3.up * .04f;
                return true;
            }
            return false;
        }

        private bool RollExhaustionBonus()
        {
            if (progress.exhaustionRolled || !CollectedToday) return false;
            progress.exhaustionRolled = true;
            if (progress.boxes.Count == 0) return true;
            int level = PlayerSupportUpgrades.For(cards.Player)?.Level(SupportUpgradeId.ExtraLootBox) ?? 0;
            if (level > 0 && UnityEngine.Random.value < (.05f + .00025f * (level - 1)))
                progress.boxes.Add(new BoxProgress { bonus = true, shiny = UnityEngine.Random.value < data.shinyChancePercent * .01f });
            return true;
        }

        public bool TryOpen(SurvivorLootBox box) =>
            Time.timeScale > 0f && cards && cards.CanCollect && TryPurchase(box, out _);

        internal bool TryOpenFromPanel(SurvivorLootBox box, RectTransform panel, out SupportUpgradeId reward)
        {
            reward = default;
            var coordinator = FindFirstObjectByType<UiPanelCoordinator>();
            return coordinator && coordinator.isActiveAndEnabled && !coordinator.IsMainMenuOpen &&
                coordinator.IsOpenPanel(panel) && TryPurchase(box, out reward);
        }

        private bool TryPurchase(SurvivorLootBox box, out SupportUpgradeId reward)
        {
            reward = default;
            int index = boxes.IndexOf(box);
            if (opening || !box || !box.ReadyToOpen || index < 0 || index >= progress.boxes.Count ||
                progress.boxes[index].collected || !cards || !cards.Player || !data ||
                !cards.Player.TryGetComponent<CharacterHealth>(out var health) || health.Health <= 0 ||
                !wallet || saveBlocked || GameSave.SuppressWrites || !GameSave.Store.Writable || RunCheckpoint.IsRestoring)
                return false;
            if ((cards.Player.transform.position - box.transform.position).sqrMagnitude > data.pickupRadius * data.pickupRadius)
                return false;

            opening = true;
            try
            {
                float boost = box.Shiny ? 1f + Mathf.Max(0, data.shinyRarityBonusPercent) * .01f : 1f;
                var upgrades = PlayerSupportUpgrades.For(cards.Player);
                if (!upgrades || !upgrades.TryBuyRandom(wallet, NextGoldPrice, boost, out reward)) return false;
                progress.boxes[index].collected = true;
                progress.paidOpenCount++;
                boxes[index] = null;
                if (box.TryGetComponent<SurvivorChestVisual>(out var visual)) visual.PlayOpening();
                box.gameObject.SetActive(false);
                Destroy(box.gameObject);
                RollExhaustionBonus();
                Save();
                MonsterSpawnZone.CaptureWorldsIfDue(force: true);
                return true;
            }
            finally { opening = false; }
        }
    }
}
