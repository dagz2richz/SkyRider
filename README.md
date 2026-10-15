# DragonFall SkyRiders

A medieval battlemage survivors roguelike with a dragon you can ride, built in **Unity 6 (6000.6.5f1, URP)**.
Ported from the Godot game *Skyfall: Dragon Riders*.

Cross three lands. Each stage is eight minutes against growing waves of ogres: a champion arrives at 4:00, and at 0:00
the field clears and the stage's warlord waits on a giant floating island. Slay him and a portal carries you to the
next land, keeping everything you have earned. Everything attacks on its own: you move, fly your dragon and choose upgrades.

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
| F | Talent power (the Knight's Dragon Form) |
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
| LB / L1 | Talent power (F) |
| LT / L2 or B / Circle (held) | Dive while flying |
| D-pad up / down | Zoom in / out |
| Start / Options | Pause |
| Select / View / Share | Stats screen |
| D-pad or left stick + A | Menus: move the gold focus frame and choose; level-up cards too |
| B / Circle | Menus: Back |
| LB / RB | Talent trees: switch heroes |
| D-pad left / right on a slider | Adjust volume, game speed, look sensitivity |

## Content

- **Heroes:** Dragonguard Knight (Longsword & Shield), Blood Huntress (Blood Javelin), Ember Mage (Ember Staff)
- **Dragons** (all the same model, colored by their power): Azure Wyrm (fire), Crimson Drake (explosive), Night Wyrm (blood),
  **Venom Wyrm** (plague: poison that spreads from ogre to ogre; signature *Plague Cloud*), **Tempest Drake** (storm: lightning
  breath that arcs on; signature *Eye of the Storm*, a tornado that drags ogres in under falling lightning)
- **Spells:** Gravity Well, Frost Bolt, Chain Lightning, Meteor Storm. On their **third upgrade** each evolves into a stronger,
  visibly different form: *Event Horizon* (golden black hole, double implosion, stuns), *Glacial Comet* (always freezes, leaves
  a freezing ice field), *Storm Lord* (violet lightning that leaves a zapping storm orb), *Starfall* (blue stars, double damage,
  pools of starfire)
- **Passives:** Blood Aura, Lightning Storm, Bloodthirst, Spellblade, Windwalker

## Stages

| Stage | Land | Warlord |
| --- | --- | --- |
| 1 | The Burning Fields: ruins and battlefields | Gorrak, the Mountain King |
| 2 | The Verdant Marches: forest villages, daylight | Ulgrim, the Forest Tyrant |
| 3 | The Sunken Crypts: torch-lit crypt halls, night | Skarnath, the Crypt Warlord |

- Maps are 30% bigger than before, with about a dozen extra floating islands of every size; the warlord's island is twice its old size.
- Each stage starts tougher (health x1 / x1.9 / x3). Beating stage 3 is a victory.
- **Dragon eggs:** a stage warlord drops an egg for a dragon you don't own. The first egg you ever find is guaranteed; after that it is a 50% chance.

## Leveling

- **Abilities and stats:** level-ups alternate between ability upgrades (weapon, spell, passive, dragon breath, signature) and
  stat buckets (Health, Mana, Offense, Defense, Dragon, Utility).
- **Talents:** each hero has a tree of 5 rows x 3 talents. A row opens at levels 3, 7, 11, 14 and 18; you pick one talent per row.
  Row 5 holds the capstones (Knight: Dragon Form, Dragonfire Blade, Undying Vow; Huntress: Blood Moon, Crimson Tempest,
  Wings of Blood; Ember Mage: Supernova, Sunfire Orbs, Pillars of Flame). Browse them all from **Talent Trees** in the menu.
- **Armor:** every 5 levels you choose one of three pieces (helm, breastplate, gauntlets, greaves, boots). Each fills its slot and
  carries stats drawn from the stat buckets: Common (3 stats), Superior (4), Mythic (6, rare), each tier 15% stronger per stat.
- **XP:** half as many ogres drop a gem, but each gem is worth twice as much.

## Saving

Settings, records and owned dragons are saved automatically. The run in progress is saved when each stage begins and when
you choose **Save & Main Menu** from the pause menu; **Continue Run** on the title screen resumes it at the start of that
stage with your hero exactly as saved. A finished run (death or victory) clears it.

## Power-ups

Ogres drop a glowing power-up 1 time in 500 (each Utility rank adds 0.1%); bosses always drop two. Each lasts **15 s**;
picking up the same kind again refreshes it, and different kinds stack.

| Power-up | Effect |
| --- | --- |
| **Swiftness** | +40% movement speed |
| **Fury** | +50% damage from everything you and your dragon deal |
| **Lodestone** | Every gem, orb and power-up on the field flies to you, and your pickup range is huge |

## Dragon flight rules

- Flight lasts **6 s** (50% longer than before), more with the Dragon stat, Windwalker and Sky Sovereign. The bar only starts to
  refill **3 s after the flight ends**; any new flight resets that wait.
- Using the dragon signature (E) while mounted leaps you off and slams; **Q is unavailable for 3 s afterwards**.

## Asset Store packs (not included)

The game runs on its own with built-in procedural models and synthesized sound. It looks and sounds much better
with these free Unity Asset Store packs, which are **not** in this repository because their license doesn't allow
redistributing them:

Dragon for Boss Monster: PBR · Dragon the Soul Eater and Dragon Boar · RPG Hero PBR HP Polyart · Battle Wizard Poly Art ·
Mini Legion Grunt PBR HP Polyart · Mini Legion Rock Golem PBR HP Polyart · RPG - Ogre Pack - PBR/Mobile ·
Low Poly Environment - Nature Free (Polytope Studio) · Fantasy Skybox FREE · Magic Effects FREE (Hovl Studio) ·
Hit Effects FREE (Matthew Guz) · Free Sound Effects Pack Starter · Middle Age - Medieval Action Sound FX Pack ·
RPG Essentials Sound Effects - FREE! · Demo Ancient Magic Pack FREE · Free Frost Mage SFX · Monster Roars - Audio Pack ·
Human Animations (Kevin Iglesias, for the Blood Huntress) · Slavic World Free (EmaceArt) · Low Poly Tree Pack (BrokenVector) ·
Elementary Dungeon Pack Lite (Gridness Studios) · Polygonal Particles · Basic RPG Icons (UI icons)

To use them: add them to your account on the Asset Store, import them in **Window > Package Manager > My Assets**,
then run **Skyfall > Set Up Asset Packs** in the Unity menu bar. That converts their materials to URP, builds the Blood
Huntress's animator and a few recolored icons (in the ignored `Assets/Skyfall/Generated`), and links everything into the
game (see `Assets/Skyfall/Resources/Skyfall/SkinLibrary.asset`). Anything missing falls back to the built-in look.

## Code map (`Assets/Skyfall/Scripts`)

| File | What it does |
| --- | --- |
| `Core/Game.cs` | Run manager: stages and portals, waves, spawning, bosses, eggs, XP, level-ups, run save, sky |
| `Core/Data.cs` | All tunable numbers: heroes, weapons, spells, dragons, stat buckets, armor, talents, ogres, bosses, stages |
| `Core/Save.cs` | Records, settings, owned dragons and the run in progress (JSON) |
| `Actors/Player.cs` | Hero movement, flight, camera, stats, armor, auto attacks, defenses |
| `Actors/PlayerTalents.cs` | Talent effects and the Knight's Dragon Form |
| `Actors/DragonPet.cs` | Dragon companion: breath, dive-bombs, riding, signature abilities |
| `Actors/Enemy.cs` | Ogres, archers, shamans and bosses |
| `Actors/Spells.cs` | Auto-cast spells, their evolved forms, and passives |
| `World/WorldTerrain.cs`, `World/WorldStages.cs`, `World/Props.cs` | Heightmap world and each stage's scenery |
| `World/WorldIslands.cs`, `World/WorldCollision.cs` | Floating islands and solid scenery |
| `UI/Hud.cs`, `UI/UiKit.cs` | Menus, HUD, talent trees, level-up cards, pause and end screens |
| `UI/UiNav.cs` | Gamepad menu focus: registered rects, nearest-in-direction picking, activation queue |
| `Core/FX.cs`, `Core/Sfx.cs` | Visual effects and sound |
| `Core/SkinLibrary.cs`, `Core/Skins.cs` | Swapping in the Asset Store models, effects, sounds and icons |

Editor tools live in `Assets/Skyfall/Editor`: scene setup, asset pack setup, and a headless test runner.
