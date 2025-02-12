using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

public class HeroSpawner : MonoBehaviour
{
    [SerializeField] private Button heroSummonButton;

    [SerializeField] private GameObject heroPrefab;
    [SerializeField] private GameObject heroSpawnPointInCellPrefab;

    [SerializeField] private Tilemap heroSpawnTilemap;

    [SerializeField] private InGameUIManager inGameUIManager;

    [SerializeField] private InGameResourceManager inGameResourceManager;
    
    private IObjectPool<Hero> HeroPool { get; set; }

    private int HeroSummonProbabilityIndex { get; set; }

    private readonly List<Vector3Int> heroSpawnPositionList = new();
    private List<HeroSpawnPointInCell> HeroSpawnPointInCellList { get; } = new();

    private RectTransform heroSummonButtonRectTr;

    public Dictionary<Collider2D, HeroSpawnPointInCell> CurrCellsDict { get; } = new();

    private int currHeroCount;
    public int MaxHeroCount { get; private set; } = 20;

    private StringBuilder stringBuilder = new();

    public HeroSummonProbabilityData CurrentHeroSummonProbabilityData
    {
        get;
        private set;
    }

    public int CurrProbabilityLevel => HeroSummonProbabilityIndex + 1;
    public int MaxProbabilityLevel => heroSummonProbabilityDataLists.Count;
    
    [SerializeField] private Button probabilityEnforceButton;

    public float RareSummonOnlyProbability => 60f;
    public float HeroicSummonOnlyProbability => 20f;
    public float LegendarySummonOnlyProbability => 10f;

    public Dictionary<int, List<HeroSpawnPointInCell>> CellsByOccupyHeroIdDict { get; } = new();
    
    private const int DefaultHeroSortingOrderOffset = 5;

    private void Awake()
    {
        HeroPool = new ObjectPool<Hero>(OnCreateHero, OnGetHero, OnReleaseHero, OnDestroyHero);
            
        HeroSummonProbabilityIndex = 0;

        foreach (var pair in CurrCellsDict)
            Destroy(pair.Value.gameObject);

        CurrCellsDict.Clear();

        currHeroCount = 0;
        
        CurrentHeroSummonProbabilityData = heroSummonProbabilityDataLists[HeroSummonProbabilityIndex];
        
        CellsByOccupyHeroIdDict.Clear();
    }

    private void Start()
    {
        heroSummonButton.TryGetComponent(out heroSummonButtonRectTr);
        RectTransformUtility.ScreenPointToWorldPointInRectangle(heroSummonButtonRectTr, heroSummonButtonRectTr.position,
            Camera.main, out Vector3 position);
        transform.position = position;

        heroSpawnPositionList.Clear();

        foreach (var cell in HeroSpawnPointInCellList)
            Destroy(cell.gameObject);
        HeroSpawnPointInCellList.Clear();

        BoundsInt bounds = heroSpawnTilemap.cellBounds;
        foreach (Vector3Int cellPos in bounds.allPositionsWithin)
        {
            if (heroSpawnTilemap.HasTile(cellPos))
                heroSpawnPositionList.Add(cellPos);
        }

        heroSpawnPositionList.Sort((a, b) =>
        {
            if (a.y != b.y)
                return b.y.CompareTo(a.y);

            return a.x.CompareTo(b.x);
        });

        foreach (var pos in heroSpawnPositionList)
        {
            Instantiate(heroSpawnPointInCellPrefab).TryGetComponent(out HeroSpawnPointInCell cell);
            HeroSpawnPointInCellList.Add(cell);
            CurrCellsDict.Add(cell.Coll2D, cell);

            cell.transform.position = pos;
        }
        
        inGameUIManager.SetHeroCountText(currHeroCount, MaxHeroCount);
        inGameUIManager.SetProbabilityTexts();
        inGameUIManager.SetLuckySummonGemCostTexts();
    }
    
    /// <param name="isLuckySummon">If this value is true, do a lucky summon; if it is false(default), do a random summon.</param>
    /// <param name="probability">Assign this value only when isLuckySummon is true.</param>
    /// <param name="heroGrade">Assign this value only when isLuckySummon is true.</param>
    /// <param name="isSummonHeroInCell">Assign this value only if isLuckySummon is true. If it is false, just return the hero; do not summon it in the cell.</param>
    /// <param name="useInGameResource">Assign this value as false only when isLuckySummon is true. If this value is false, Does not consume InGameResources.</param>
    /// <param name="useCoin">Assign this value as false only when isLuckySummon and useInGameResource is true. If this value is false, use the gem instead.</param>
    public Hero OnClickCreateHero(bool isLuckySummon = false, float? probability = null, HeroGrade? heroGrade = null,bool isSummonHeroInCell = true ,bool useInGameResource = true, bool useCoin = true)
    {
        
        if (currHeroCount >= MaxHeroCount)
        {
            inGameUIManager.SetLogText("Hero count is full");
            
            SoundManager.Instance.PlaySfx(SfxClipId.FailedSfxSoundId);

            return null;
        }
        
        if (useInGameResource)
        {
            if (useCoin)
            {
                bool canUseCoin = inGameResourceManager.TryUseCoin(inGameResourceManager.CurrentHeroSummonCoinCost);
                if (!canUseCoin)
                {
                    inGameUIManager.SetLogText("Not enough coins to summon a hero.");
                    
                    SoundManager.Instance.PlaySfx(SfxClipId.FailedSfxSoundId);

                    return null;
                }
            }
            else
            {
                int gemCost;
                switch (heroGrade)
                {
                    case HeroGrade.Rare:
                        gemCost = inGameResourceManager.InitialRareSummonGemCost;
                        break;
                    case HeroGrade.Heroic:
                        gemCost = inGameResourceManager.InitialHeroicSummonGemCost;
                        break;
                    case HeroGrade.Legendary:
                        gemCost = inGameResourceManager.InitialLegendarySummonGemCost;
                        break;
                    default:
                        Debug.Assert(false, "Invalid Hero Grade for luckySummon.");
                        return null;
                }
            
                bool canUseGem = inGameResourceManager.TryUseGem(gemCost);
                if (!canUseGem)
                {
                    inGameUIManager.SetLogText("Not enough gems to summon a hero.");

                    SoundManager.Instance.PlaySfx(SfxClipId.FailedSfxSoundId);
                    
                    return null;
                }
            }
        }

        Hero hero = HeroPool.Get();

        bool? success;
        if (isLuckySummon)
            success = SetHeroDataByLuckySummon(hero, probability, heroGrade);
        else
            success = SetHeroDataByRandData(hero);

        if (success is false)
        {
            inGameUIManager.SetLogText("Lucky Summon failed.....");
            
            SoundManager.Instance.PlaySfx(SfxClipId.FailedSfxSoundId);
            
            HeroPool.Release(hero);
            
            return null;
        }
        
        if (success is null)
        {
            HeroPool.Release(hero);
            
            Debug.Assert(false, "SetHeroData Failed");
            
            return null;
        }
        
        if(!isSummonHeroInCell)
            return hero;
        
        if(!isLuckySummon)
            inGameResourceManager.AddHeroSummonCoinCost();

        if (CellsByOccupyHeroIdDict.ContainsKey(hero.HeroId))
        {
            foreach (var cell in CellsByOccupyHeroIdDict[hero.HeroId])
            {
                bool canSpawnHeroInCell = CanSpawnHeroInCell(cell, hero);
                if (canSpawnHeroInCell)
                {
                    SortHeroesInCellDrawOrder();

                    SetLogTextInHeroSummonSuccess(isLuckySummon, hero);
                    
                    return hero;
                }
            }
        }
        
        foreach (var cell in HeroSpawnPointInCellList)
        {
            bool canSpawnHeroInCell = CanSpawnHeroInCell(cell, hero);
            if (canSpawnHeroInCell)
            {
                SortHeroesInCellDrawOrder();

                SetLogTextInHeroSummonSuccess(isLuckySummon, hero);
                    
                return hero;
            }
        }
        
        inGameUIManager.SetLogText("There is no cell available to spawn a hero.");
        
        SoundManager.Instance.PlaySfx(SfxClipId.FailedSfxSoundId);
        
        HeroPool.Release(hero);

        return null;
    }

    private void SetLogTextInHeroSummonSuccess(bool isLuckySummon, Hero hero)
    {
        stringBuilder.Clear();
        
        if (isLuckySummon)
        {
            switch (hero.HeroGrade)
            {
                case HeroGrade.Rare:
                {
                    stringBuilder.AppendFormat($"Lucky Summon Success! You've summoned a <color=#0000FF>{hero.HeroGrade.ToString()}</color> hero!");
                }
                    break;
                case HeroGrade.Heroic:
                {
                    stringBuilder.AppendFormat($"Lucky Summon Success! You've summoned a <color=#A652EB>{hero.HeroGrade.ToString()}</color> hero!");
                }
                    break;
                case HeroGrade.Legendary:
                {
                    stringBuilder.AppendFormat($"Lucky Summon Success! You've summoned a <color=#FFEB04>{hero.HeroGrade.ToString()}</color> hero!");
                }
                    break;
                default:
                    return;
            }
        }
        else
        {
            switch (hero.HeroGrade)
            {
                case HeroGrade.Rare:
                {
                    stringBuilder.AppendFormat($"With a {heroSummonProbabilityDataLists[HeroSummonProbabilityIndex].RareProbability}% chance, summon the <color=#0000FF>{hero.HeroGrade.ToString()}</color> hero!");
                }
                    break;
                case HeroGrade.Heroic:
                {
                    stringBuilder.AppendFormat($"With a {heroSummonProbabilityDataLists[HeroSummonProbabilityIndex].HeroicProbability}% chance, summon the <color=#A652EB>{hero.HeroGrade.ToString()}</color> hero!");
                }
                    break;
                case HeroGrade.Legendary:
                {
                    stringBuilder.AppendFormat($"With a {heroSummonProbabilityDataLists[HeroSummonProbabilityIndex].LegendaryProbability}% chance, summon the <color=#FFEB04>{hero.HeroGrade.ToString()}</color> hero!");
                }
                    break;
                default:
                    return;
            }
        }
        
        inGameUIManager.SetLogText(stringBuilder.ToString());
    }

    private bool CanSpawnHeroInCell(HeroSpawnPointInCell cell, Hero hero)
    {
        if (cell.CanSpawnHero(hero))
        {
            SpawnHeroInit(hero);
            
            return true;
        }
        
        return false;
    }

    public void SpawnHeroInit(Hero hero)
    {
        hero.Initialize();

        inGameUIManager.SetHeroCountText(++currHeroCount, MaxHeroCount);
    }

    public void OnClickEnforceProbability()
    {
        bool canUseCoin = inGameResourceManager.TryUseCoin(CurrentHeroSummonProbabilityData.EnforceCost);
        if (!canUseCoin)
        {
            inGameUIManager.SetLogText("Not enough coins to enforce a probability.");
            
            SoundManager.Instance.PlaySfx(SfxClipId.FailedSfxSoundId);
            
            return;
        }

        ++HeroSummonProbabilityIndex;
        
        CurrentHeroSummonProbabilityData = heroSummonProbabilityDataLists[HeroSummonProbabilityIndex];
        
        if (CurrProbabilityLevel == MaxProbabilityLevel)
        {
            probabilityEnforceButton.interactable = false;
        }

        inGameUIManager.SetProbabilityTexts();
    }

    private bool? SetHeroDataByRandData(Hero hero)
    {
        if (HeroSummonProbabilityIndex < 0 || HeroSummonProbabilityIndex >= heroSummonProbabilityDataLists.Count)
            return null;

        HeroSummonProbabilityData pData = heroSummonProbabilityDataLists[HeroSummonProbabilityIndex];

        var properties = typeof(HeroSummonProbabilityData).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        List<float> probabilities = new(properties.Length);
        foreach (var property in properties)
        {
            if (property.PropertyType == typeof(float) && property.Name.EndsWith("Probability"))
            {
                float value = (float)property.GetValue(pData);
                probabilities.Add(value);
            }
        }

        float probability = Random.value * 100f;
        float pSum = 0f;
        for (int i = 0; i < Utility.HeroGradeCount; ++i)
        {
            pSum += probabilities[i];

            if (probability <= pSum)
            {
                return SetHeroData(hero, i);
            }
        }

        return null;
    }

    private bool? SetHeroData(Hero hero, int listIndex)
    {
        if (listIndex < 0 || listIndex >= heroDataRarityLists.Count)
            return null;

        var dataList = heroDataRarityLists[listIndex].dataList;
        hero.SetHeroData(dataList[Random.Range(0, dataList.Count)]);

        return true;
    }

    private Hero OnCreateHero()
    {
        Instantiate(heroPrefab).TryGetComponent(out Hero hero);

        hero.SetPool(HeroPool);

        return hero;
    }

    private void OnGetHero(Hero hero)
    {
        hero.gameObject.SetActive(true);
    }

    private void OnReleaseHero(Hero hero)
    {
        hero.gameObject.SetActive(false);
    }

    private void OnDestroyHero(Hero hero)
    {
        Destroy(hero.gameObject);
    }

    public void SortHeroSpawnPointInCellList()
    {
        HeroSpawnPointInCellList.Sort(CellPositionCmp);

        SortHeroesInCellDrawOrder();
    }

    public void SortHeroesInCellDrawOrder()
    {
        int order = 0;
        
        foreach (var cell in HeroSpawnPointInCellList)
        {
            cell.SortHeroesDrawOrder(DefaultHeroSortingOrderOffset + order);

            order += 3;
        }
    }

    private int CellPositionCmp(HeroSpawnPointInCell cell1, HeroSpawnPointInCell cell2)
    {
        Vector3 cell1Pos = cell1.transform.position;
        Vector3 cell2Pos = cell2.transform.position;

        if (System.Math.Abs(cell1Pos.y - cell2Pos.y) > 0.0001f)
            return cell2Pos.y.CompareTo(cell1Pos.y);

        return cell1Pos.x.CompareTo(cell2Pos.x);
    }
    
    private bool? SetHeroDataByLuckySummon(Hero hero, float? probability, HeroGrade? heroGrade)
    {
        if (probability is null || heroGrade is null)
            return null;

        float randProbability = Random.value * 100f;

        if (probability < randProbability)
        {
            return false;
        }
        
        return SetHeroData(hero, (int)heroGrade - 1);
    }

    public void RemoveCurrHeroCount(int countAmount)
    {
        currHeroCount -= countAmount;
        
        inGameUIManager.SetHeroCountText(currHeroCount, MaxHeroCount);
    }
    
    [System.Serializable]
    public class HeroDataList
    {
        public List<HeroData> dataList = new();
    }

    [HideInInspector] public List<HeroDataList> heroDataRarityLists = new();

    public List<HeroSummonProbabilityData> heroSummonProbabilityDataLists = new();

    private void OnValidate()
    {
        int targetCount = Utility.HeroGradeCount;

        while (heroDataRarityLists.Count < targetCount)
        {
            heroDataRarityLists.Add(new HeroDataList());
        }

        while (heroDataRarityLists.Count > targetCount)
        {
            heroDataRarityLists.RemoveAt(heroDataRarityLists.Count - 1);
        }
    }
}