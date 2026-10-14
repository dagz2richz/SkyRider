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

        [Header("Dragons")]
        public GameObject dragonBlue;
        public GameObject dragonRed;
        public GameObject dragonBlack;

        [Header("Ogres")]
        public GameObject[] ogres;
        public GameObject[] bossOgres;
        public GameObject ogreWeapon;
        public RuntimeAnimatorController ogreController;
        public GameObject archer;
        public GameObject shaman;
        public GameObject golem;
        public GameObject golemKing;

        [Header("Scenery")]
        public GameObject[] deadTrees;
        public GameObject[] stumps;
        public GameObject[] rocks;
        public GameObject[] standingStones;
        public GameObject[] shrubs;
        public GameObject[] grass;
        /// The Ruins Creation Kit model; its child objects are the individual ruin pieces.
        public GameObject ruinsKit;

        [Header("Sky")]
        public Material skybox;

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

        [Header("Effects and sounds")]
        public List<Named> vfx = new List<Named>();
        public List<SoundSet> sounds = new List<SoundSet>();

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
        public GameObject Dragon(string id) => id == "blue" ? dragonBlue : id == "red" ? dragonRed : id == "black" ? dragonBlack : null;
    }
}
