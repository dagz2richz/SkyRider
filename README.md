# DragonFall SkyRiders

A medieval battlemage survivors roguelike with a dragon you can ride, built in **Unity 6 (6000.6.5f1, URP)**.
Ported from the Godot game *Skyfall: Dragon Riders*.

Survive eight minutes against growing waves of ogres. A mini boss arrives at 4:00; at 0:00 the field clears and the Mountain King waits on a floating island, guarding a dragon egg.
Everything attacks on its own: you move, fly your dragon and pick upgrades.

## Play

1. Open the project in Unity Hub with Unity **6000.6.5f1**.
2. Open `Assets/Skyfall/Scenes/Skyfall.unity` and press **Play**.
3. Click inside the Game view so it captures the mouse.

## Controls

| Key | Action |
| --- | --- |
| WASD | Move (relative to the camera) |
| Mouse | Look around |
| Space | Jump, double jump; hold to climb while flying |
| Q | Call your dragon and take flight (Q again to drop off) |
| E | Dragon signature ability (from the air you also leap down and slam) |
| Shift / Ctrl | Dive while flying |
| Scroll | Zoom |
| Tab | Stats screen |
| Esc / P | Pause |
| 1 / 2 / 3 | Pick a level-up card |

## Content

- **Heroes:** Dragonguard Knight, Blood Huntress, Ember Mage
- **Dragons:** Azure Wyrm (fire), Crimson Drake (explosive), Night Wyrm (blood)
- **Spells:** Gravity Well, Frost Bolt, Chain Lightning, Meteor Storm
- **Passives:** Blood Aura, Lightning Storm, Bloodthirst, Spellblade, Windwalker

## Asset Store packs (not included)

The game runs on its own with built-in procedural models and synthesized sound. It looks and sounds much better
with these free Unity Asset Store packs, which are **not** in this repository because their license doesn't allow
redistributing them:

Dragon for Boss Monster: PBR · Dragon the Soul Eater and Dragon Boar · RPG Hero PBR HP Polyart · Battle Wizard Poly Art ·
Mini Legion Grunt PBR HP Polyart · Mini Legion Rock Golem PBR HP Polyart · RPG - Ogre Pack - PBR/Mobile ·
Low Poly Environment - Nature Free (Polytope Studio) · Fantasy Skybox FREE · Magic Effects FREE (Hovl Studio) ·
Hit Effects FREE (Matthew Guz) · Free Sound Effects Pack Starter · Middle Age - Medieval Action Sound FX Pack ·
RPG Essentials Sound Effects - FREE! · Demo Ancient Magic Pack FREE · Free Frost Mage SFX · Monster Roars - Audio Pack

To use them: add them to your account on the Asset Store, import them in **Window > Package Manager > My Assets**,
then run **Skyfall > Set Up Asset Packs** in the Unity menu bar. That converts their materials to URP and links them
into the game (see `Assets/Skyfall/Resources/Skyfall/SkinLibrary.asset`). Anything missing falls back to the built-in look.

## Code map (`Assets/Skyfall/Scripts`)

| File | What it does |
| --- | --- |
| `Core/Game.cs` | Run manager: timer, waves, spawning, bosses, XP, level-ups, sky |
| `Core/Data.cs` | All tunable numbers: heroes, weapons, spells, dragons, ogres, bosses, waves |
| `Actors/Player.cs` | Hero movement, flight, camera, stats, auto attacks, defenses |
| `Actors/DragonPet.cs` | Dragon companion: breath, dive-bombs, riding, signature abilities |
| `Actors/Enemy.cs` | Ogres, archers, shamans and bosses |
| `Actors/Spells.cs` | Auto-cast spells and passives |
| `World/WorldTerrain.cs`, `World/Props.cs` | Heightmap world and scenery |
| `UI/Hud.cs`, `UI/UiKit.cs` | Menus, HUD, level-up cards, pause and end screens |
| `Core/FX.cs`, `Core/Sfx.cs` | Visual effects and sound |
| `Core/SkinLibrary.cs`, `Core/Skins.cs` | Swapping in the Asset Store models, effects and sounds |

Editor tools live in `Assets/Skyfall/Editor`: scene setup, asset pack setup, and a headless test runner.
