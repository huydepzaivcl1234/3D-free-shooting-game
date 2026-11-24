using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiningSimulator.Ores
{
    internal sealed class CharacterSkinManager
    {
        private const string SaveKey = "Survivor.CapsuleBoy.Skins.v1";
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private readonly PlayerWallet wallet;
        private readonly CharacterSkinData[] skins;
        private readonly Material original;
        private readonly MaterialPropertyBlock properties = new();
        private readonly Profile profile;
        internal CharacterSkinData Equipped { get; private set; }
        internal event Action<CharacterSkinData> OnEquippedChanged, OnUnlocked;

        [Serializable]
        private sealed class Profile
        {
            public string equipped;
            public List<string> unlocked = new();
        }

        internal CharacterSkinManager(CharacterSkinData[] skins, PlayerWallet wallet, Material original)
        {
            this.skins = skins;
            this.wallet = wallet;
            this.original = original;
            try { profile = JsonUtility.FromJson<Profile>(GameSave.GetString(SaveKey)); }
            catch (ArgumentException) { }
            profile ??= new Profile();
            profile.unlocked ??= new List<string>();
            foreach (var skin in skins)
                if (IsOwned(skin) && (Equipped == null || skin.skinId == profile.equipped))
                    Equipped = skin;
        }

        internal bool IsOwned(CharacterSkinData skin) =>
            skin && (skin.unlockedByDefault || profile.unlocked.Contains(skin.skinId));

        internal bool CanBuy(CharacterSkinData skin) => skin && wallet &&
            wallet.CurrentMoney >= skin.unlockCostGold && wallet.CurrentGems >= skin.unlockCostGems;

        internal bool Equip(CharacterSkinData skin, bool purchase)
        {
            if (!skin || Array.IndexOf(skins, skin) < 0) return false;
            bool unlock = !IsOwned(skin);
            if (unlock)
            {
                if (!purchase || !CanBuy(skin) || !wallet.TrySpend(skin.unlockCostGold)) return false;
                if (!wallet.TrySpendGems(skin.unlockCostGems))
                {
                    wallet.AddMoney(skin.unlockCostGold);
                    return false;
                }
                profile.unlocked.Add(skin.skinId);
            }
            Equipped = skin;
            profile.equipped = skin.skinId;
            GameSave.SetString(SaveKey, JsonUtility.ToJson(profile));
            GameSave.Save();
            if (unlock) OnUnlocked?.Invoke(skin);
            OnEquippedChanged?.Invoke(skin);
            return true;
        }

        internal void ApplySkin(CharacterSkinData skin, Renderer[] renderers)
        {
            if (!skin || !original) return;
            foreach (var renderer in renderers)
            {
                if (!renderer) continue;
                var slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] != original) continue;
                    renderer.GetPropertyBlock(properties, i);
                    properties.SetColor(BaseColor, skin.bodyTint);
                    renderer.SetPropertyBlock(properties, i);
                }
            }
        }

    }
}
