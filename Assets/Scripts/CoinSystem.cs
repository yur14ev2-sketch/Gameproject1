using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class CoinSystem : MonoBehaviour
{
    [Header("Coin Pop Animation")]
    [SerializeField, Min(0f)] private float popHeight = 3f;
    [SerializeField, Min(0.01f)] private float riseDuration = 0.35f;
    [SerializeField, Min(0.01f)] private float fallDuration = 0.3f;

    private sealed class CoinEntry
    {
        public Transform Coin;
        public Renderer[] Renderers;
        public Collider[] Colliders;
        public Collider QuestionBlock;
        public Vector3 HiddenPosition;
        public float BottomOffset;
        public bool HasPopped;
        public bool IsCollectible;
        public bool IsCollected;
    }

    private readonly List<CoinEntry> coins = new List<CoinEntry>();
    private readonly Dictionary<Transform, CoinEntry> coinsByBlock =
        new Dictionary<Transform, CoinEntry>();
    private Collider playerCollider;
    private CoinCounterUI coinCounterUi;
    private int collectedCoinCount;

    private void Awake()
    {
        if (transform.childCount == 0)
        {
            Debug.LogError("Coin System needs a Coin child object.", this);
            enabled = false;
            return;
        }

        Transform coinTemplate = transform.GetChild(0);
        List<BoxCollider> questionBlocks = FindQuestionBlocks();

        if (questionBlocks.Count == 0)
        {
            Debug.LogError("Coin System could not find a Question Block.", this);
            enabled = false;
            return;
        }

        for (int i = 0; i < questionBlocks.Count; i++)
        {
            Transform coin = i == 0
                ? coinTemplate
                : Instantiate(coinTemplate, transform);

            coin.name = $"Coin ({i + 1})";
            CreateCoinEntry(coin, questionBlocks[i]);
        }

        PlayerMovement player = FindObjectOfType<PlayerMovement>();

        if (player != null)
            playerCollider = player.GetComponent<Collider>();

        coinCounterUi = GetComponent<CoinCounterUI>();
        coinCounterUi?.SetCount(0);
    }

    public bool TryPopCoin(Collider questionBlockCollider)
    {
        CoinEntry entry = FindEntry(questionBlockCollider.transform);

        if (entry == null || entry.HasPopped)
            return false;

        entry.HasPopped = true;
        StartCoroutine(PopCoin(entry));
        return true;
    }

    private void FixedUpdate()
    {
        if (playerCollider == null)
            return;

        for (int i = 0; i < coins.Count; i++)
        {
            CoinEntry entry = coins[i];

            if (!entry.IsCollectible || entry.IsCollected)
                continue;

            for (int j = 0; j < entry.Colliders.Length; j++)
            {
                if (!entry.Colliders[j].bounds.Intersects(playerCollider.bounds))
                    continue;

                CollectCoin(entry);
                break;
            }
        }
    }

    private void CreateCoinEntry(Transform coin, Collider questionBlock)
    {
        Vector3 hiddenPosition = new Vector3(
            questionBlock.bounds.center.x,
            questionBlock.bounds.center.y,
            coin.position.z);
        coin.position = hiddenPosition;

        CoinEntry entry = new CoinEntry
        {
            Coin = coin,
            Renderers = coin.GetComponentsInChildren<Renderer>(true),
            Colliders = coin.GetComponentsInChildren<Collider>(true),
            QuestionBlock = questionBlock,
            HiddenPosition = hiddenPosition
        };

        entry.BottomOffset = CalculateCoinBottomOffset(entry);

        for (int i = 0; i < entry.Colliders.Length; i++)
            entry.Colliders[i].isTrigger = true;

        SetCoinVisible(entry, false);
        SetCoinCollidersEnabled(entry, false);
        coins.Add(entry);
        coinsByBlock[questionBlock.transform] = entry;
    }

    private IEnumerator PopCoin(CoinEntry entry)
    {
        Vector3 landingPosition = entry.HiddenPosition;
        landingPosition.y = entry.QuestionBlock.bounds.max.y + entry.BottomOffset;

        Vector3 apexPosition = landingPosition + Vector3.up * popHeight;

        yield return MoveCoinUpAndReveal(
            entry,
            entry.HiddenPosition,
            apexPosition,
            landingPosition.y,
            riseDuration);
        yield return MoveCoin(entry, apexPosition, landingPosition, fallDuration);

        entry.Coin.position = landingPosition;
        SetCoinCollidersEnabled(entry, true);
        entry.IsCollectible = true;
    }

    private void CollectCoin(CoinEntry entry)
    {
        entry.IsCollectible = false;
        entry.IsCollected = true;
        SetCoinVisible(entry, false);
        SetCoinCollidersEnabled(entry, false);

        collectedCoinCount++;
        coinCounterUi?.SetCount(collectedCoinCount);
    }

    private CoinEntry FindEntry(Transform blockTransform)
    {
        Transform current = blockTransform;

        while (current != null)
        {
            if (coinsByBlock.TryGetValue(current, out CoinEntry entry))
                return entry;

            current = current.parent;
        }

        return null;
    }

    private IEnumerator MoveCoinUpAndReveal(
        CoinEntry entry,
        Vector3 start,
        Vector3 end,
        float revealCenterY,
        float duration)
    {
        float elapsed = 0f;
        bool isVisible = false;

        SetCoinVisible(entry, false);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            entry.Coin.position = Vector3.LerpUnclamped(
                start,
                end,
                Mathf.SmoothStep(0f, 1f, t));

            if (!isVisible && entry.Coin.position.y >= revealCenterY)
            {
                isVisible = true;
                SetCoinVisible(entry, true);
            }

            yield return null;
        }

        entry.Coin.position = end;
        SetCoinVisible(entry, true);
    }

    private IEnumerator MoveCoin(
        CoinEntry entry,
        Vector3 start,
        Vector3 end,
        float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            entry.Coin.position = Vector3.LerpUnclamped(
                start,
                end,
                Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }

        entry.Coin.position = end;
    }

    private float CalculateCoinBottomOffset(CoinEntry entry)
    {
        if (entry.Renderers.Length == 0)
            return 0f;

        Bounds bounds = entry.Renderers[0].bounds;

        for (int i = 1; i < entry.Renderers.Length; i++)
            bounds.Encapsulate(entry.Renderers[i].bounds);

        return entry.Coin.position.y - bounds.min.y;
    }

    private List<BoxCollider> FindQuestionBlocks()
    {
        BoxCollider[] candidates = FindObjectsOfType<BoxCollider>();
        List<BoxCollider> questionBlocks = new List<BoxCollider>();

        for (int i = 0; i < candidates.Length; i++)
        {
            BoxCollider candidate = candidates[i];

            if (candidate.transform.IsChildOf(transform))
                continue;

            string normalizedName = candidate.name.Replace(" ", "").ToLowerInvariant();

            if (!normalizedName.StartsWith("questionblock"))
                continue;

            questionBlocks.Add(candidate);
        }

        questionBlocks.Sort((a, b) =>
        {
            int horizontalOrder = a.bounds.center.x.CompareTo(b.bounds.center.x);
            return horizontalOrder != 0
                ? horizontalOrder
                : a.bounds.center.y.CompareTo(b.bounds.center.y);
        });

        return questionBlocks;
    }

    private void SetCoinVisible(CoinEntry entry, bool visible)
    {
        for (int i = 0; i < entry.Renderers.Length; i++)
            entry.Renderers[i].enabled = visible;
    }

    private void SetCoinCollidersEnabled(CoinEntry entry, bool enabledState)
    {
        for (int i = 0; i < entry.Colliders.Length; i++)
            entry.Colliders[i].enabled = enabledState;
    }

}
