using System.Collections.Generic;
using UnityEngine;

public class SimpleModularHumanRandomizer : MonoBehaviour
{
    [Header("Assign 'human_mesh'")]
    public Transform humanMesh;

    [Header("Run")]
    public bool randomizeOnAwake  = true;
    public bool randomizeOnEnable = false;
    public int  seed = 0; // 0=random every run, >0 reproducible

    [Header("Gender & Suits")]
    [Range(0,1)] public float maleProbability     = 0.5f;
    public bool  allowCompleteSuits               = true;
    [Range(0,1)] public float completeSuitChance  = 0.30f; // one child under complet_suits

    [Header("Hair & Face")]
    [Range(0,1)] public float hairNoneChance      = 0.05f; // bald
    [Range(0,1)] public float beardChanceMale     = 0.45f; // beard_a..d (male only)
    public bool  hideHairWhenCap                  = true;

    [Header("Accessories")]
    [Range(0,1)] public float capChance           = 0.35f; // cap/cap2/cap3
    [Range(0,1)] public float chainChance         = 0.25f; // chain1..3
    [Range(0,1)] public float glassesChance       = 0.20f; // glasses

    [Header("Outerwear (separate from tops)")]
    [Range(0,1)] public float outerwearChance     = 0.35f; // choose one of jacket/pullover
    [Range(0,1)] public float scarfChance         = 0.10f; // scarf

    // Roots
    Transform tBeard, tCloth, tHair;

    // Hair (exact)
    GameObject hair_a, hair_b, hair_c, hair_d, hair_e;

    // Beard (exact)
    GameObject beard_a, beard_b, beard_c, beard_d;

    // Cloth
    Transform  suitsRoot;               // cloth/complet_suits
    GameObject cap, cap2, cap3;
    GameObject chain1, chain2, chain3;
    GameObject glasses, jacket, pullover, scarf;

    // Tops (only these three)
    GameObject shirt, t_shirt, tank_top;

    // Bottoms & shoes
    GameObject trousers, shortpants;
    GameObject shoes1, shoes2, shoes3;

    System.Random rng;

    void Reset()
    {
        if (!humanMesh)
        {
            var guess = transform.Find("human_mesh");
            if (guess) humanMesh = guess;
        }
    }

    void Awake()
    {
        if (!humanMesh)
        {
            var guess = transform.Find("human_mesh");
            if (guess) humanMesh = guess;
        }

        if (!FindRoots()) return;
        CacheExactItems();

        if (randomizeOnAwake) RandomizeNow();
    }

    void OnEnable()
    {
        if (randomizeOnEnable) RandomizeNow();
    }

    [ContextMenu("Randomize Now")]
    public void RandomizeNow()
    {
        rng = (seed == 0) ? new System.Random() : new System.Random(seed);

        // Turn EVERYTHING off under these roots (not the roots themselves)
        DeactivateAllChildren(tBeard);
        DeactivateAllChildren(tCloth);
        DeactivateAllChildren(tHair);

        // Gender
        bool isMale = rng.NextDouble() < maleProbability;

        // Hair
        GameObject chosenHair = null;
        if (rng.NextDouble() >= hairNoneChance)
        {
            if (isMale)
            {
                // male: a, b, e (+ c unisex)
                chosenHair = ActivateRandomPresent(hair_a, hair_b, hair_e, hair_c);
            }
            else
            {
                // female: d (+ c unisex)
                chosenHair = ActivateRandomPresent(hair_d, hair_c);
            }
        }

        // Beard (male only)
        if (isMale && rng.NextDouble() < beardChanceMale)
            ActivateRandomPresent(beard_a, beard_b, beard_c, beard_d);

        // Outfit
        bool usedSuit = false;
        if (allowCompleteSuits && suitsRoot && suitsRoot.childCount > 0 && rng.NextDouble() < completeSuitChance)
        {
            // IMPORTANT: reactivate the suits parent before enabling a child
            suitsRoot.gameObject.SetActive(true);

            int pick = rng.Next(0, suitsRoot.childCount);
            for (int i = 0; i < suitsRoot.childCount; i++)
                SafeSetActive(suitsRoot.GetChild(i).gameObject, i == pick);

            // With suits, skip extra layers to avoid clipping; glasses/chains optional if you want:
            MaybeActivate(glassesChance, glasses);
            MaybeActivate(chainChance,  chain1, chain2, chain3);

            usedSuit = true;
        }

        if (!usedSuit)
        {
            // TOPS (pick exactly one)
            var top = ActivateRandomPresent(shirt, t_shirt, tank_top);
            if (!top)
            {
                // fallback if something is missing in the prefab
                top = FirstPresent(shirt, t_shirt, tank_top);
                if (top) top.SetActive(true);
                else Debug.LogWarning($"{name}: No tops found (shirt/t-shirt/tank_top).");
            }

            // OUTERWEAR (jacket or pullover)
            if (rng.NextDouble() < outerwearChance)
            {
                ActivateRandomPresent(jacket, pullover);
            }

            // SCARF (independent)
            MaybeActivate(scarfChance, scarf);

            // GLASSES
            MaybeActivate(glassesChance, glasses);

            // BOTTOMS (pick exactly one)
            var bottom = ActivateRandomPresent(trousers, shortpants);
            if (!bottom)
            {
                bottom = FirstPresent(trousers, shortpants);
                if (bottom) bottom.SetActive(true);
                else Debug.LogWarning($"{name}: No bottoms found (trousers/shortpants).");
            }

            // SHOES (pick exactly one)
            var pickedShoes = ActivateRandomPresent(shoes1, shoes2, shoes3);
            if (!pickedShoes)
            {
                pickedShoes = FirstPresent(shoes1, shoes2, shoes3);
                if (pickedShoes) pickedShoes.SetActive(true);
                else Debug.LogWarning($"{name}: No shoes found (shoes1/2/3).");
            }

            // CAPS (can hide hair)
            bool capOn = MaybeActivate(capChance, cap, cap2, cap3);
            if (capOn && hideHairWhenCap && chosenHair) chosenHair.SetActive(false);

            // CHAINS
            MaybeActivate(chainChance, chain1, chain2, chain3);
        }
    }

    // ---------- roots & cache ----------

    bool FindRoots()
    {
        if (!humanMesh)
        {
            Debug.LogWarning($"{name}: Assign 'humanMesh' (human_mesh).");
            return false;
        }

        tBeard = FindDirectChild(humanMesh, "beard");
        tCloth = FindDirectChild(humanMesh, "cloth");
        tHair  = FindDirectChild(humanMesh, "hair");

        if (!tCloth || !tHair)
        {
            Debug.LogWarning($"{name}: Missing required child 'cloth' or 'hair' under human_mesh.");
            return false;
        }
        return true;
    }

    void CacheExactItems()
    {
        // Hair
        hair_a = FindDeepGO(tHair, "hair_a");
        hair_b = FindDeepGO(tHair, "hair_b");
        hair_c = FindDeepGO(tHair, "hair_c"); // unisex
        hair_d = FindDeepGO(tHair, "hair_d"); // female
        hair_e = FindDeepGO(tHair, "hair_e"); // male

        // Beard (may not exist if asset stripped)
        if (tBeard)
        {
            beard_a = FindDeepGO(tBeard, "beard_a");
            beard_b = FindDeepGO(tBeard, "beard_b");
            beard_c = FindDeepGO(tBeard, "beard_c");
            beard_d = FindDeepGO(tBeard, "beard_d");
        }

        // Cloth
        suitsRoot = FindDirectChild(tCloth, "complet_suits") ?? FindDirectChild(tCloth, "complete_suits");

        cap       = FindDeepGO(tCloth, "cap");
        cap2      = FindDeepGO(tCloth, "cap2");
        cap3      = FindDeepGO(tCloth, "cap3");

        chain1    = FindDeepGO(tCloth, "chain1");
        chain2    = FindDeepGO(tCloth, "chain2");
        chain3    = FindDeepGO(tCloth, "chain3");

        glasses   = FindDeepGO(tCloth, "glasses");
        jacket    = FindDeepGO(tCloth, "jacket");
        pullover  = FindDeepGO(tCloth, "pullover");
        scarf     = FindDeepGO(tCloth, "scarf");

        shirt     = FindDeepGO(tCloth, "shirt");
        t_shirt   = FindDeepGO(tCloth, "t-shirt");
        tank_top  = FindDeepGO(tCloth, "tank_top");

        trousers  = FindDeepGO(tCloth, "trousers");
        shortpants= FindDeepGO(tCloth, "shortpants");

        shoes1    = FindDeepGO(tCloth, "shoes1");
        shoes2    = FindDeepGO(tCloth, "shoes2");
        shoes3    = FindDeepGO(tCloth, "shoes3");
    }

    // ---------- activation helpers ----------

    GameObject ActivateRandomPresent(params GameObject[] candidates)
    {
        var list = new List<GameObject>(candidates.Length);
        for (int i = 0; i < candidates.Length; i++)
            if (candidates[i]) list.Add(candidates[i]);

        if (list.Count == 0) return null;

        int pick = rng.Next(0, list.Count);
        for (int i = 0; i < list.Count; i++)
            SafeSetActive(list[i], i == pick);
        return list[pick];
    }

    bool MaybeActivate(float chance, params GameObject[] options)
    {
        chance = Mathf.Clamp01(chance);
        if (options == null || options.Length == 0) return false;

        if (rng.NextDouble() < chance)
        {
            ActivateRandomPresent(options);
            return true;
        }
        else
        {
            DeactivateAll(options);
            return false;
        }
    }

    GameObject FirstPresent(params GameObject[] gos)
    {
        for (int i = 0; i < gos.Length; i++) if (gos[i]) return gos[i];
        return null;
    }

    void DeactivateAll(params GameObject[] gos)
    {
        if (gos == null) return;
        for (int i = 0; i < gos.Length; i++) SafeSetActive(gos[i], false);
    }

    void DeactivateAllChildren(Transform root)
    {
        if (!root) return;
        foreach (Transform c in root.GetComponentsInChildren<Transform>(true))
            if (c != root) SafeSetActive(c.gameObject, false);
    }

    void SafeSetActive(GameObject go, bool active)
    {
        if (go && go.activeSelf != active) go.SetActive(active);
    }

    // ---------- find helpers ----------

    Transform FindDirectChild(Transform root, string exact)
    {
        if (!root) return null;
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (c.name.Equals(exact, System.StringComparison.OrdinalIgnoreCase))
                return c;
        }
        return null;
    }

    GameObject FindDeepGO(Transform root, string exact)
    {
        if (!root) return null;
        foreach (Transform c in root.GetComponentsInChildren<Transform>(true))
        {
            if (c == root) continue;
            if (c.name.Equals(exact, System.StringComparison.OrdinalIgnoreCase))
                return c.gameObject;
        }
        return null;
    }
}