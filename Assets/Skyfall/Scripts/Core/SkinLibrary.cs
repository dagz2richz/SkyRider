using System;
using System.Collections.Generic;
using UnityEngine;

namespace Skyfall
{
    /// References to the Asset Store models, effects, sounds and sky the game uses in place of its built-in
    /// procedural look. Lives at Resources/Skyfall/SkinLibrary.asset and is filled by Skyfall > Set Up Asset Packs.
    /// Any empty slot falls back to the procedural version, so the game always runs.
    [CreateAssetMenu(menuName = "Skyfall/Skin Library", fileName = "SkinLibrary")]
    public class SkinLibrary : ScriptableObject
    {
        [Header("Heroes")]
        public GameObject knight;
        public GameObject pyromancer;
        public GameObject huntress;
        /// Animator for the Blood Huntress model (built from the Kevin Iglesias female clips by the setup).
        public RuntimeAnimatorController huntressController;
        /// The RPG Hero's own sword-and-shield clips plus a fall and a leap attack (built by the setup).
        public RuntimeAnimatorController knightController;

        [Header("Dragons (all the Usurper model; its color variants)")]
        public GameObject dragonBlue;
        public GameObject dragonRed;
        public GameObject dragonBlack;
        public GameObject dragonAlbino;

        [Header("Ogres")]
        public GameObject[] ogres;
        public GameObject[] bossOgres;
        public GameObject ogreWeapon;
        public RuntimeAnimatorController ogreController;
        public GameObject archer;
        public GameObject shaman;
        public GameObject golem;
        public GameObject golemKing;
        /// Final boss models of stages 2 and 3: a warpainted ogre (club in hand, the Grunt's clips) and a Soul Eater dragon
        /// (its own animator: Idle, Walk, Run, Basic Attack, Tail Attack, Scream, Get Hit, Die).
        public GameObject forestBoss;
        public GameObject cryptBoss;

        [Header("Swarm (skeletons and renegade knights)")]
        public GameObject skeleton;
        public RuntimeAnimatorController skeletonController;
        public GameObject humanSoldier;
        /// Built by the setup from the Toon RTS knight's own (generic-rig) clips.
        public RuntimeAnimatorController humanController;

        [Header("Scenery")]
        public GameObject[] deadTrees;
        public GameObject[] stumps;
        public GameObject[] rocks;
        public GameObject[] standingStones;
        public GameObject[] shrubs;
        public GameObject[] grass;
        /// Point Grass Renderer blade material (the package's demo blade); the world copies it per stage. Empty = no ground grass.
        public Material grassMaterial;
        /// The Ruins Creation Kit model; its child objects are the individual ruin pieces.
        public GameObject ruinsKit;

        [Header("Stage 2: The Verdant Marches")]
        public GameObject[] forestTrees;
        public GameObject[] forestBushes;
        public GameObject[] forestRocks;
        public GameObject[] villageHouses;
        public GameObject[] villageProps;

        [Header("Stage 3: The Sunken Crypts")]
        public GameObject[] cryptPieces;
        public GameObject[] cryptProps;
        public GameObject cryptTorch;

        [Header("Sky")]
        public Material skybox;
        /// Skies for stages 2 and 3 (day forest, crypt night); stage 1 uses skybox.
        public Material skyStage2, skyStage3;

        public Material SkyFor(int stage) => stage == 2 && skyStage2 ? skyStage2 : stage == 3 && skyStage3 ? skyStage3 : skybox;

        [Serializable]
        public class Named
        {
            public string name;
            public GameObject prefab;
        }

        [Serializable]
        public class SoundSet
        {
            public string name;
            public AudioClip[] clips;
        }

        [Serializable]
        public class NamedIcon
        {
            public string name;
            public Texture2D icon;
        }

        [Header("Effects, sounds and UI icons")]
        public List<Named> vfx = new List<Named>();
        public List<SoundSet> sounds = new List<SoundSet>();
        public List<NamedIcon> icons = new List<NamedIcon>();

        Dictionary<string, Texture2D> iconMap;

        /// A UI icon by game id (spell, passive, weapon, bucket, armor slot, talent, power-up, hero), or null.
        public Texture2D Icon(string name)
        {
            if (iconMap == null)
            {
                iconMap = new Dictionary<string, Texture2D>();
                foreach (var i in icons) if (i.icon) iconMap[i.name] = i.icon;
            }
            iconMap.TryGetValue(name, out var t);
            return t;
        }

        Dictionary<string, GameObject> vfxMap;
        Dictionary<string, AudioClip[]> soundMap;

        public GameObject Vfx(string name)
        {
            if (vfxMap == null)
            {
                vfxMap = new Dictionary<string, GameObject>();
                foreach (var v in vfx) if (v.prefab) vfxMap[v.name] = v.prefab;
            }
            vfxMap.TryGetValue(name, out var p);
            return p;
        }

        public AudioClip[] Sound(string name)
        {
            if (soundMap == null)
            {
                soundMap = new Dictionary<string, AudioClip[]>();
                foreach (var s in sounds) if (s.clips != null && s.clips.Length > 0) soundMap[s.name] = s.clips;
            }
            soundMap.TryGetValue(name, out var c);
            return c;
        }

        public GameObject Hero(string id) => id == "knight" ? knight : id == "pyromancer" ? pyromancer : id == "huntress" ? huntress : null;
        public RuntimeAnimatorController HeroController(string id) => id == "huntress" ? huntressController : id == "knight" ? knightController : null;
        /// A color variant of the dragon model: blue, red, black or albino.
        public GameObject Dragon(string skin) => skin == "blue" ? dragonBlue : skin == "red" ? dragonRed : skin == "black" ? dragonBlack : skin == "albino" ? dragonAlbino : null;
    }
}
