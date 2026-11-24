# Capsule Bonk menu

`Assets/Scenes/SampleScene.unity` contains the menu scene and its uGUI controls. Use **Survivor → Menu → Preview Main Menu in Scene** or **Preview Skins in Scene** in Edit Mode. The preview uses the terrain currently open in the scene, aligns Scene View with the Game View shot and temporarily hides the gameplay HUD. Use the **Skin** list and **Rotation** slider to preview a finish. **Stop Preview** restores the scene's camera, player pose and HUD.

- **Main Menu → Capsule Bonk UI** contains the menu controls. Edit their RectTransforms, colors and labels in the Hierarchy.
- **MenuDiorama** contains the props, lights, camera shots and Volume. Move the root to change the preferred location. **Find Mining Wall** locates suitable terrain nearby when each seed is generated. Turn that setting off to use the authored horizontal position and camera heading.
- Edit CapsuleBoy under **PlayerArmature**. In Play Mode the menu uses those same renderers and bones at **MenuDiorama → Turntable**, then returns them to PlayerArmature on Play or Resume.
- **MenuDiorama → CamPos_MainMenu** and **CamPos_SkinSelect** set shot offset and field of view. Select **Menu Volume** to open the menu-specific profile.

## Create or edit skins

Select `Assets/GameData/Player/CapsuleBoy Skin Catalog.asset` to edit the skin names, body colors and Gold or Gem prices together. **Edit Skin Catalog** in the preview window opens the same asset.

1. Run **Survivor → Menu → Create Default Skins**. The tool creates missing entries in `Assets/GameData/Survivor/UI/MainMenu/Skins`. Existing entries retain their edits.
2. Create extra entries with **Create → Survivor → Character Skin**. Use a unique ID and fill in the name, description, tint and costs. Assign a sprite if you have one. The menu generates a pixel portrait for entries without a sprite.
3. Add new entries to the skin catalog and add their card view. Keep at least one entry unlocked by default. The default setup uses Classic White.
4. `Body Tint` changes the color of the existing Main slot. The original face texture and the other material slots stay in place.

## Bind another compatible scene

1. Place the existing `MainMenu` prefab under a **Screen Space – Overlay** Canvas with a **Graphic Raycaster** and Canvas Scaler. Use **Scale With Screen Size**, 1920 × 1080, match 0.5, and the existing Input System EventSystem.
2. Configure **Game Main Menu** with its menu data and shared settings panel. The presentation creates the navigation buttons and calls the existing Play, Shop, Quest, Settings and Quit actions.
3. Run **Survivor → Menu → Auto-Bind Scene References**. It connects the existing game systems and creates the diorama and UI if they are missing. Check the Console and save the scene. Run this step before adding custom skins to the list.
4. The shot cameras start at local offsets `(0.15, 1.03, 5.8)` and `(1.12, 0.95, 5.30)` with a 28° field of view. The camera centers CapsuleBoy in Skins and frames him on the right on the main page.
5. `Resources/Capsule Bonk UI Style.asset` provides the default palette and fonts. The Upgrade screen reads that same asset.

Unlocks debit the active `PlayerWallet` and persist in the existing `GameSave` profile. A locked skin can be previewed. Confirming a purchase unlocks and equips it, and Play applies the equipped material to the character in the run.

## Existing files updated

- `Assets/Scenes/SampleScene.unity` contains the authored menu UI, character and diorama.
- `Assets/GameData/Survivor/UI/MainMenu/MainMenu.prefab` hosts the menu controller.
- `Assets/GameData/UI/Runtime/Scripts/GameMainMenu.cs` bridges menu activation, Escape, and return focus from Shop and Quest.
- `Assets/GameData/Cards/Scripts/RustUpgradeLayout.cs` reads the shared color and font settings.
