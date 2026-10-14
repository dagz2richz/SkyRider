# DragonFall SkyRiders

A medieval battlemage survivors roguelike with a dragon you can ride, built in **Unity 6 (6000.6.5f1, URP)**.
Ported from the Godot game *Skyfall: Dragon Riders*.

Survive eight minutes against growing waves of ogres. A mini boss arrives at 4:00; at 0:00 the field clears and the Mountain King waits on a floating island, guarding a dragon egg.
Everything attacks on its own: you move, fly your dragon and pick upgrades.

Ogres drop gold. Gold is banked when a run ends (death or victory) and spent in the **Shop** (main menu, and on the loadout screen before you ride) on weapons and armor.

## Play

1. Open the project in Unity Hub with Unity **6000.6.5f1**.
2. Open `Assets/Skyfall/Scenes/Skyfall.unity` and press **Play**.
3. Click inside the Game view so it captures the mouse. With a gamepad plugged in you do not need the mouse at all.

## Controls

| Key | Action |
| --- | --- |
| WASD | Move (relative to the camera) |
| Mouse | Look around |
| Space | Jump, double jump; hold to climb while flying |
| Q | Call your dragon and take flight (Q again to drop off); unavailable for 3 s after an E leap-slam |
| E | Dragon signature ability (from the air you also leap down and slam) |
| Shift / Ctrl | Dive while flying |
| Scroll | Zoom |
| Tab | Stats screen |
| Esc / P | Pause |
| 1 / 2 / 3 | Pick a level-up card |

### Gamepad

Any Xbox, PlayStation or generic controller works alongside the keyboard and mouse.

| Control | Action |
| --- | --- |
| Left stick | Move |
| Right stick | Look around (scaled by the look-sensitivity setting); click it to reset the zoom |
| A / Cross | Jump, double jump; hold to climb while flying |
| Y / Triangle | Call your dragon and take flight (same 3 s lockout after a leap-slam as Q) |
| RB / R1 or X / Square | Dragon signature ability (E) |
| LT / L2 or B / Circle (held) | Dive while flying |
| D-pad up / down | Zoom in / out |
| Start / Options | Pause |
| Select / View / Share | Stats screen |
| D-pad or left stick + A | Menus: move the gold focus frame and choose; level-up cards too |
| B / Circle | Menus: Back (shop, hero select, loadout, settings, controls, pause = resume, stats = close, quit = stay) |
| LB / RB | Shop: switch between the Weapons and Armor tabs |
| D-pad left / right on a slider | Adjust volume, game speed, look sensitivity |

Moving the mouse hides the focus frame; pressing any gamepad button shows it again. Hits and boss slams give a short rumble.

## Content

- **Heroes:** Dragonguard Knight (Sword & Shield), Blood Huntress (Javelin), Ember Mage (Bow & Arrow)
- **Dragons:** Azure Wyrm (fire), Crimson Drake (explosive), Night Wyrm (blood)
- **Spells:** Gravity Well, Frost Bolt, Chain Lightning, Meteor Storm
- **Passives:** Blood Aura, Lightning Storm, Bloodthirst, Spellblade, Windwalker

## Gold, weapons and armor

- **Gold:** every ogre drops spinning coins that fly to you like XP gems (runts 1, ogres 2, brutes 4, hulks 8, archers and shamans 3). The mini boss drops a pile of 150; the Mountain King adds 500 when you already own every dragon. The HUD shows gold earned this run; the end screen shows the total banked.
- **Weapons** (one per hero, level 1 is free, upgrade to level 5 for 200 / 450 / 800 / 1300 gold): **Sword & Shield** (Knight), **Javelin** (Huntress), **Bow & Arrow** (Ember Mage). Each level gives +15% damage and 7% faster attacks; the Sword & Shield also gains reach and block chance, the Javelin and Bow throw +1 projectile at levels 3 and 5, and Bow arrows pierce one extra foe at level 5.
- **Armor** (five slots shared by all heroes, tiers 0 to 5, costing 100 / 250 / 500 / 900 / 1500 per tier): **Helm** +6% max health, **Chest** +2 armor, **Gloves** +5% attack speed and +2% crit, **Legs** +1 defense level (block / dodge / Flame Ward chance and their perks at 3 and 5), **Boots** +5% move speed, per tier.
- Level-ups no longer upgrade weapons or armor: they offer abilities and stat buckets only (the Defense bucket gives +2% block / dodge / ward, +1.5 armor and +2% parry).

## Power-ups

Ogres sometimes drop a glowing power-up that bobs, spins and flies to you like a gem. They last for the run only (never saved) and each lasts **15 s**; picking up the same kind again refreshes its timer, and different kinds stack. Active power-ups are listed with their seconds left under the level and gold at the top left.

| Power-up | Icon | Effect |
| --- | --- | --- |
| **Swiftness** | cyan wings | +40% movement speed |
| **Fury** | red sword | +50% damage from everything you and your dragon deal |
| **Fortune** | gold coin stack with "x2" | gold pickups are worth double |

- Every regular ogre has a **0.1%** chance to drop one (random kind). The mini boss and the Mountain King **always drop two**.
- The Utility stat bucket's fourth stat is **+0.1% power-up drop chance** per rank (rank 5 = 0.6%); the Stats screen shows your current chance.

## Dragon flight rules

- Flight (Q) drains the flight bar while you fly. The bar only starts to refill **3 s after the flight ends** (landing or dropping off); any new flight resets that wait. The bar is dimmed and reads "Rest" while it waits.
- Using the dragon signature (E) while mounted leaps you off and slams; **Q is unavailable for 3 s afterwards** ("Your dragon is circling back..."), shown on the flight bar.

## Colors

Anything an ogre blows up on death or on touch (death puffs, boulders, boss slams and charges) is red. Your dragon's abilities follow its color: the Azure Wyrm's Inferno Wave is blue fire, the Crimson Drake (a deeper, darker crimson) casts a deep red Cataclysm, and the Night Wyrm keeps its blood red and black.

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
| `UI/UiNav.cs` | Gamepad menu focus: registered rects, nearest-in-direction picking, activation queue |
| `Core/FX.cs`, `Core/Sfx.cs` | Visual effects and sound |
| `Core/SkinLibrary.cs`, `Core/Skins.cs` | Swapping in the Asset Store models, effects and sounds |

Editor tools live in `Assets/Skyfall/Editor`: scene setup, asset pack setup, and a headless test runner.
