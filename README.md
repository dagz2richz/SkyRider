# DragonRider Survivors

A medieval battlemage survivors roguelike where your dragon is a partner that grows up with you, built in
**Unity 6 (6000.6.5f1, URP)**. (The code namespace and folders are still called `Skyfall`, from the Godot original.)

Cross three lands. Each stage is eight minutes against growing waves of skeletons, renegade knights and ogres: a
champion arrives at 4:00, and at 0:00 the field clears and the stage's warlord waits (on a giant floating island in worlds 2 and 3). Slay
him and a portal carries you to the next land. Everything attacks on its own: you move, fly your dragon and choose
upgrades.

## Play

1. Open the project in Unity Hub with Unity **6000.6.5f1**. Unity downloads the packages in `Packages/manifest.json`
   by itself.
2. Optional but recommended: import the Asset Store packs listed below, then run **Skyfall > Set Up Asset Packs**.
3. Open `Assets/Skyfall/Scenes/Skyfall.unity` and press **Play**.
4. Click inside the Game view so it captures the mouse. With a gamepad plugged in you do not need the mouse at all.

## Asset Store packs (not included)

The game runs on its own with built-in procedural models and synthesized sound, but it looks and sounds far better
with the Asset Store packs below. They are **not** in this repository: the Asset Store license lets you use them in
a game, not redistribute their files (they are listed in `.gitignore`).

To use them:

1. Add each pack to your Unity account on the Asset Store (all are free).
2. In Unity, open **Window > Package Manager > My Assets**, then download and import each one.
3. Run **Skyfall > Set Up Asset Packs** from the menu bar.

The setup converts materials to URP, fixes a few import settings (the Toon RTS knight's rig becomes humanoid, its
models get their textured materials and its idle and walk clips loop), builds the animators, renders the dragon
portraits and recolors the UI icons (into the ignored `Assets/Skyfall/Generated`). It also links everything into the
game through `Assets/Skyfall/Resources/Skyfall/SkinLibrary.asset`.

Run it again whenever you re-import or update a pack, or after cloning onto a new machine. It is safe to run any
time. Anything missing falls back to the built-in look.

### Characters and animation

| Pack | Imports to `Assets/…` | Used for |
| --- | --- | --- |
| RPG Hero PBR HP Polyart | `RPGHero` | Dragonguard Knight |
| Battle Wizard Poly Art | `WizardPolyArt` | Ember Mage |
| Human Animations (Kevin Iglesias: Basic Motions, Melee, Spellcasting) | `Kevin Iglesias` | Blood Huntress model and animations; extra run, attack, hit, death and jump clips for the knights, skeletons and Dragonguard |
| Dragon for Boss Monster: PBR, and Dragon the Soul Eater and Dragon Boar | `FourEvilDragonsPBR` | All three dragons (the Usurper model) and their portraits |
| RPG - Ogre Pack - PBR/Mobile | `Ogre` | Ogre brutes, hulks, harpooners and boss ogres |
| Mini Legion Grunt PBR HP Polyart | `Mini Legion Grunt PBR HP Polyart` | Grunt alchemists and shamans; animations for the ogres |
| Mini Legion Rock Golem PBR HP Polyart | `Mini Legion Rock Golem PBR HP Polyart` | Champion and warlord golems |
| Toon RTS Units, undead demo | `Toon_RTS` | Skeletons |
| Toon RTS Units, knight demo | `Toon_RTS_demo` | Renegade knights |

### Scenery and sky

| Pack | Imports to `Assets/…` | Used for |
| --- | --- | --- |
| Low Poly Environment - Nature Free (Polytope Studio) | `Polytope Studio` | Stage 1 trees, rocks, shrubs and grass |
| Ruins Creation Kit | `Ruins_creation_kit` | Stage 1 ruins |
| Low Poly Tree Pack (BrokenVector) | `BrokenVector` | Stage 2 forests |
| Slavic World Free (EmaceArt) | `EmaceArt` | Stage 2 villages and props |
| Elementary Dungeon Pack Lite (Gridness Studios) | `Gridness Studios` | Stage 3 crypts |
| Fantasy Skybox FREE | `Fantasy Skybox FREE` | Skies for all three stages |

### Effects, sound and UI

| Pack | Imports to `Assets/…` | Used for |
| --- | --- | --- |
| Magic Effects FREE (Hovl Studio) | `Hovl Studio` | Explosions, auras, portals, magic circles |
| Hit Effects FREE (Matthew Guz) | `Matthew Guz` | Hit sparks |
| Polygonal Particles | `Polygonal Particles` | Particle materials |
| Free Sound Effects Pack Starter | `Free Pack` | Impacts and whooshes |
| Middle Age - Medieval Action Sound FX Pack | `Medieval Action - FX Pack 2.0` | Swords, blocks, bows |
| RPG Essentials Sound Effects - FREE! (Leohpaz) | `Leohpaz` | Hits, heals, hurt and death sounds |
| Demo Ancient Magic Pack FREE | `A.M.P. DEMO` | Spell casts, buffs, fire and lightning |
| Free Frost Mage SFX | `Free Frost Mage - SFX` | Frost spells |
| Monster Roars - Audio Pack | `Monster Roars` | Dragon and boss roars |
| Basic RPG Icons | `Basic_RPG_Icons` | Every UI icon (many are recolored by the setup) |

Also present in the project but not used by the game: `DoubleL`, `GabrielAguiarProductions` and `LightningBolt`.

### Unity packages (downloaded automatically)

URP, Input System and the other `com.unity.*` packages, plus the editor-only AI tools: AI Game Developer
(`com.ivanmurzak.unity.mcp` with its Animation and ParticleSystem extensions) and Unity AI Assistant and Inference.
The game itself does not need the AI tools.

## Controls

| Key | Action |
| --- | --- |
| WASD | Move (relative to the camera) |
| Mouse | Look around |
| Space | Jump, double jump; hold to climb while flying |
| Q | Call your dragon and take flight (Q again to drop off); after you leave it, Q is ready 1 s after you touch the ground |
| E | Dragon signature ability (from the air you leap down and slam); locks Q for 5 s |
| F | Capstone form (Dragon Form / Crimson Specter / Phoenix Form) |
| Shift / C | Dive while flying (not Ctrl: in the editor Ctrl+S would save the scene) |
| R | Reroll a level-up (5 per world) |
| Scroll | Zoom |
| Tab | Stats screen |
| Esc / P | Pause |
| 1 / 2 / 3 | Pick a level-up card |

### Gamepad

Any Xbox, PlayStation or generic controller works alongside the keyboard and mouse.

| Control | Action |
| --- | --- |
| Left stick | Move |
| Right stick | Look around; click it to reset the zoom |
| A / Cross | Jump, double jump; hold to climb while flying |
| Y / Triangle | Call your dragon (and reroll on the level-up screen) |
| RB / R1 or X / Square | Dragon signature ability (E) |
| LB / L1 | Capstone form (F) |
| LT / L2 or B / Circle (held) | Dive while flying |
| D-pad up / down | Zoom in / out |
| Start / Options | Pause |
| Select / View / Share | Stats screen |
| D-pad or left stick + A | Menus and level-up cards; B / Circle goes back |

## The dragon

- **It grows each world:** a Hatchling in world 1 walks beside you and only breathes fire (no flight, no floating
  islands). A Young Dragon in world 2 can carry you. An Adult in world 3 adds **Guardian Wings**: below 30% health it
  shields you for 3 s and heals you for half your health (60 s cooldown).
- **Dragons:** Azure Wyrm (fire), Crimson Drake (explosive), Night Wyrm (blood). Each has its own signature (E):
  Inferno Wave, Cataclysm, Blood Rain.
- **Dragon card:** every level-up offers one dragon upgrade alongside two hero cards: breath, signature, size, cone,
  haste, and guardian instincts (rescue swoop, sheltering wing, wrath).
- **Flight:** unlimited in worlds 2 and 3 (none in world 1). Harpooners hunt riders: one harpoon slows you, a second
  while you are still hooked drags you to the ground. Using E locks Q for 5 s (shown on the flight bar).
- **Dragonguard Knight, mounted:** his sword swings give way to piercing Skyblades hurled at the nearest ogres.

## Leveling

- **Abilities:** weapon, defense and passive go to level 5. Spells and the dragon's breath and signature go to
  **level 10**. Spells evolve at level 5 (*Event Horizon*, *Glacial Comet*, *Storm Lord*, *Starfall*) and gain a
  unique upgrade at level 10.
- **Every level:** +2% damage (+5% for the Ember Mage), +1% health, +0.5% parry.
- **Talents:** two in-run talent rows open at levels 3 and 7. Between runs, permanent trees (6 rows of 3) are bought
  with **hero XP** (per hero) and **bond XP** (per dragon: riding time, dragon kills, signatures, rescues). The hero
  capstones are the F forms.
- **Armor:** every 5 levels you choose one of three pieces for the next slot (helm first), all of one rarity:
  Common (3 stats), Superior (4), Mythic (6, rare).
- **Rerolls:** 5 per world.

## Stages

| Stage | Land | Warlord |
| --- | --- | --- |
| 1 | The Burning Fields: ruins and battlefields | Gorrak, the Mountain King |
| 2 | The Verdant Marches: forest villages, daylight | Ulgrim, the Forest Tyrant |
| 3 | The Sunken Crypts: torch-lit crypt halls, night | Skarnath, the Crypt Warlord |

Beating stage 3 is a victory. A warlord may hold a dragon you don't own captive; walk up to it to break its chains. The first rescue is
guaranteed, after that it is a 50% chance. Bosses drop no XP.

## Saving

Three save slots hold your talent trees, owned dragons and records; settings are shared. The run in progress is
saved when each stage begins and from **Save & Main Menu** in the pause menu. Saves live in
`%USERPROFILE%/AppData/LocalLow/DefaultCompany/DragonRider Survivors/skyfall_save.json`.

## Dev menu

The title screen has a **Dev** menu for testing (free talents, 10x XP, unlock everything). It is temporary: delete
`Assets/Skyfall/Scripts/UI/DevMenu.cs` and every line marked `// DEV` to remove it.

## Code map (`Assets/Skyfall/Scripts`)

| File | What it does |
| --- | --- |
| `Core/Game.cs` | Run manager: stages and portals, waves, spawning, bosses, dragon rescues, XP, level-ups, rerolls, run save, sky |
| `Core/Data.cs` | All tunable numbers: heroes, weapons, spells, dragons, stat buckets, armor, talents, enemies, bosses, stages |
| `Core/Save.cs` | Save slots, talent trees, settings, owned dragons and the run in progress (JSON) |
| `Actors/Player.cs` | Hero movement, flight, camera, stats, armor, auto attacks, defenses, harpoon hits |
| `Actors/PlayerTalents.cs` | Talent effects, capstone forms and the dragon's guardian moves |
| `Actors/DragonPet.cs` | Dragon partner: breath, dive-bombs, riding, signatures, guardian instincts |
| `Actors/Enemy.cs` | Skeletons, knights, ogres, alchemists (potions), shamans, harpooners, bosses, corpses |
| `Actors/Spells.cs` | Auto-cast spells, their evolved forms, and passives |
| `World/*` | Heightmap world, floating islands, solid scenery and each stage's props |
| `UI/Hud.cs`, `UI/UiKit.cs`, `UI/UiNav.cs` | Menus, HUD, talent trees, level-up cards, gamepad navigation |
| `UI/DevMenu.cs` | The removable dev menu |
| `Core/FX.cs`, `Core/Sfx.cs` | Visual effects and sound |
| `Core/SkinLibrary.cs`, `Core/Skins.cs` | Swapping in the Asset Store models, animations, effects, sounds and icons |

Editor tools live in `Assets/Skyfall/Editor`: scene setup, asset pack setup (`SkyfallAssetSetup.cs`) and a headless
test runner.
