using Demo.Core;
using Demo.Data;
using UnityEngine;

namespace Demo.Collection
{
    /// <summary>
    /// 잔해를 삼키는 구멍. <b>구역에 하나뿐이다</b> [확정 2026-09-22].
    ///
    /// 성장 규칙은 하나다 — <b>사물의 크기 &lt; 구멍의 크기</b>여야 들어간다.
    /// 초반에는 구멍이 작아 부서진 잔해만 들어가고, 단계가 오르면 더 큰 것이 들어간다.
    /// "부서진 것만 들어간다"는 별도 규칙이 아니라 구멍이 작아서 생기는 결과다.
    ///
    /// 경험치는 화면에 보이지 않는다. 플레이어는 숫자가 아니라 구멍이 커지는 것으로 진행을 읽는다.
    /// (가시화 여부는 추후 번복될 수 있어 DebugHotkeys에 표시 토글을 달아 뒀다.)
    ///
    /// 플레이어는 빠지지 않는다. 무한궤도로 걸쳐 지나간다.
    /// </summary>
    public sealed class Hole : MonoBehaviour
    {
        /// <summary>현재 단계 인덱스. GameConfig.holeLevels의 첨자.</summary>
        public int Level { get; private set; }

        /// <summary>현재 반경(m). 이보다 작은 것만 들어간다.</summary>
        public float Radius { get; private set; }

        /// <summary>누적 경험치. 단계가 오르면 0부터 다시 쌓인다.</summary>
        public int Xp { get; private set; }

        /// <summary>다음 단계까지 필요한 경험치. 최종 단계면 0.</summary>
        public int XpToNext { get; private set; }

        /// <summary>이번 단계 진행률 0~1. 최종 단계면 1.</summary>
        public float LevelProgress01 => XpToNext > 0 ? Mathf.Clamp01((float)Xp / XpToNext) : 1f;

        /// <summary>최종 단계인가. "부수지 않고 통째로 밀어넣는" 후반부가 여기서 열린다.</summary>
        public bool IsMaxLevel => XpToNext <= 0;

        /// <summary>지금까지 삼킨 총 단위.</summary>
        public int SwallowedUnits { get; private set; }

        [SerializeField, Tooltip("잔해가 구멍으로 빨려 들어가는 데 걸리는 시간(초)")]
        float fallSeconds = 0.45f;

        Transform visual;
        Vector3 origin, driftTarget;

        void Awake()
        {
            // 스케일은 자식에만 준다. 루트를 늘이면 자식 연출까지 같이 늘어난다.
            visual = transform.childCount > 0 ? transform.GetChild(0) : null;
        }

        /// <summary>ZoneBuilder가 구역을 지을 때 부른다.</summary>
        public void Init()
        {
            Level = 0;
            Xp = 0;
            xpCarry = 0f;
            SwallowedUnits = 0;
            origin = transform.position;
            driftTarget = origin;
            ApplyLevel();
        }

        /// <summary>
        /// 최종 단계에서만 스스로 떠돈다 [확정 2026-09-22].
        ///
        /// 플레이어가 조작하지 않는다. 다 자란 구멍이 천천히 배회하며 닿는 것을 삼키는 것이
        /// 후반부의 그림이고, 조작을 늘리지 않으면서 긴장감을 만든다.
        /// 최초 위치에서 holeDriftRadius 안으로 제한해 구역 밖으로 새지 않게 한다.
        /// </summary>
        void Drift(GameConfig cfg)
        {
            if (cfg.holeDriftSpeed <= 0f) return;

            // "따라오는 구멍"을 고르면 최종 단계가 아니어도 움직인다 [지시 2026-10-13].
            bool follows = Core.Upgrades.Has(Core.Upgrades.HoleFollows);
            if (!follows && !IsMaxLevel) return;

            Vector3 here = transform.position;

            if (follows)
            {
                Transform p = Player();
                if (p != null)
                {
                    // 쫓아가되 구역 밖으로는 안 나간다 — 최초 위치 반경 제한은 그대로다.
                    Vector3 want = new Vector3(p.position.x, here.y, p.position.z);
                    Vector3 off = want - origin;
                    off.y = 0f;
                    float lim = cfg.holeDriftRadius;
                    if (off.sqrMagnitude > lim * lim) off = off.normalized * lim;
                    driftTarget = new Vector3(origin.x + off.x, here.y, origin.z + off.z);
                }
            }
            else if ((driftTarget - here).sqrMagnitude < 0.25f)
            {
                Vector2 r = Random.insideUnitCircle * cfg.holeDriftRadius;
                driftTarget = origin + new Vector3(r.x, 0f, r.y);
            }

            transform.position = Vector3.MoveTowards(here, driftTarget,
                                                     cfg.holeDriftSpeed * Time.deltaTime);
        }

        /// <summary>
        /// 쫓아갈 대상. <b>한 번만 찾는다</b> — Update에서 Find를 도는 것은 금지다
        /// (CLAUDE.md 규칙 5). 구멍은 구역이 지어질 때 생기고 플레이어는 그 전에
        /// 있으므로 처음 필요해질 때 한 번이면 충분하다.
        /// </summary>
        Transform Player()
        {
            if (lookedForPlayer) return playerT;
            lookedForPlayer = true;
            var pc = FindFirstObjectByType<Demo.Player.PlayerController>();
            playerT = pc != null ? pc.transform : null;
            return playerT;
        }

        Transform playerT;
        bool lookedForPlayer;

        /// <summary>
        /// 지금 어디로 가고 있는가. 화면에 "저기로 간다"를 보여 주는 데 쓴다
        /// [지시 2026-10-14]. 안 움직이고 있으면 <see cref="IsDrifting"/>가 false다.
        /// </summary>
        public Vector3 DriftTarget => driftTarget;

        /// <summary>
        /// 지금 스스로 움직이는 중인가.
        ///
        /// 따라오는 카드를 안 고르면 구멍은 <b>무작위로</b> 떠돈다 — 플레이어가 어디로 갈지
        /// 전혀 못 읽는다. 그래서 목적지를 보여 줄 필요가 생겼다.
        /// </summary>
        public bool IsDrifting
        {
            get
            {
                GameConfig cfg = GameConfigRef.Current;
                if (cfg == null || cfg.holeDriftSpeed <= 0f) return false;
                return IsMaxLevel || Core.Upgrades.Has(Core.Upgrades.HoleFollows);
            }
        }

        void ApplyLevel()
        {
            GameConfig cfg = GameConfigRef.Current;
            if (cfg == null || cfg.holeLevels == null || cfg.holeLevels.Length == 0)
            {
                Debug.LogError("[Hole] GameConfig.holeLevels가 비어 있다. 구멍이 성장할 수 없다.", this);
                return;
            }

            Level = Mathf.Clamp(Level, 0, cfg.holeLevels.Length - 1);
            GameConfig.HoleLevel lv = cfg.holeLevels[Level];

            Radius = lv.radius;
            XpToNext = Level >= cfg.holeLevels.Length - 1 ? 0 : lv.xpToNext;

            if (visual != null) visual.localScale = new Vector3(Radius * 2f, visual.localScale.y, Radius * 2f);
        }

        /// <summary>경험치를 넣는다. 단계가 올랐으면 true.</summary>
        public bool AddXp(int amount)
        {
            if (amount <= 0 || IsMaxLevel) return false;

            Xp += amount;
            bool grew = false;

            // 한 번에 여러 단계가 오를 수 있다 — 큰 것을 한꺼번에 넣었을 때.
            while (!IsMaxLevel && Xp >= XpToNext)
            {
                Xp -= XpToNext;
                Level++;
                ApplyLevel();
                grew = true;
            }

            // 평면(2D)으로 낸다 — 단계 상승은 화면 전체의 사건이고, 3D로 두면
            // 플레이어가 구멍에서 멀 때 가장 중요한 소리가 안 들린다.
            if (grew) Utils.GameAudio.Play(Data.Sfx.HoleLevelUp, transform.position);

            if (grew)
                Debug.Log($"[Hole] 구멍이 커졌다 — 단계 {Level} · 반경 {Radius:0.0}m" +
                          (IsMaxLevel ? " (최종)" : $" · 다음까지 {XpToNext - Xp}"));

            if (grew) OfferUpgrade();
            return grew;
        }

        /// <summary>
        /// 단계가 오르면 업그레이드 카드를 내민다 [지시 2026-10-13].
        /// 한 번에 두 칸이 올라도 카드는 한 번만 뜬다 — 후보는 어차피 누적이다.
        /// </summary>
        void OfferUpgrade()
        {
            if (!lookedForPicker) { lookedForPicker = true; picker = FindFirstObjectByType<UI.UpgradeScreen>(); }
            if (picker != null) picker.Offer(Level + 1);   // 화면에 보이는 단계 숫자
        }

        UI.UpgradeScreen picker;
        bool lookedForPicker;

        /// <summary>
        /// 경험치에 배율을 먹인다. <b>소수점을 모아 둔다</b> —
        /// 잔해 하나가 주는 경험치가 1이라, 1.5배를 넣을 때마다 반올림하면
        /// 매번 2가 되어 실제로는 2배가 된다 [실측 2026-10-13].
        /// </summary>
        void AddXpScaled(int raw)
        {
            if (raw <= 0) return;
            xpCarry += raw * Core.Upgrades.Mul(Core.Upgrades.MoreXp);
            int whole = Mathf.FloorToInt(xpCarry);
            if (whole <= 0) return;
            xpCarry -= whole;
            AddXp(whole);
        }

        float xpCarry;

        /// <summary>디버그·시연용. 한 단계 즉시 올린다.</summary>
        public bool ForceLevelUp()
        {
            if (IsMaxLevel) return false;
            Level++;
            Xp = 0;
            ApplyLevel();
            Debug.Log($"[Hole] (디버그) 단계 {Level} · 반경 {Radius:0.0}m");
            return true;
        }

        /// <summary>
        /// 삼킴 판정은 <b>플레이어가 아니라 잔해</b>가 기준이다 [지시 2026-09-22].
        ///
        /// 예전에는 플레이어가 구멍 근처에 가면 화물이 통째로 사라졌다. 지금은 그물을 구멍 위로
        /// 끌고 지나가면서 조각이 하나씩 빠진다 — 무엇이 들어가고 무엇이 남는지가 화면에서 읽히고,
        /// 끌지 않는 잔해가 굴러 들어가는 것도 자연스럽게 처리된다.
        ///
        /// 플레이어는 빠지지 않는다. 여기서 플레이어를 아예 보지 않기 때문이다.
        /// </summary>
        void Update()
        {
            GameLoop loop = GameLoop.Instance;
            if (loop == null || !loop.IsRunning) return;

            GameConfig cfg = GameConfigRef.Current;
            if (cfg == null) return;

            Drift(cfg);

            Vector2 center = new Vector2(transform.position.x, transform.position.z);
            float rSqr = Radius * Radius;

            // 흡인 범위 — 여기 들어오면 알아서 빨려든다. 조준할 필요가 없다.
            // 판정 주체는 여전히 잔해지만, 구멍 위에 정확히 올려놓을 것을 요구하지는 않는다.
            float margin = cfg.holeSuctionMargin * Core.Upgrades.Mul(Core.Upgrades.WiderSuction);
            float reach = Radius + margin;
            float reachSqr = reach * reach;

            // 뒤에서부터 훑는다 — 낙하가 시작되면 목록이 바뀔 수 있다.
            var active = Debris.Active;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Debris d = active[i];
                if (d == null || d.IsFalling) continue;
                if (!d.FitsInHole(Radius)) continue;                 // 크기 게이트

                float sqr = (d.FlatPosition - center).sqrMagnitude;

                if (sqr <= rSqr) { Swallow(d, cfg, loop); continue; }   // 구멍 안 — 삼킨다
                if (sqr > reachSqr) { d.IsBeingSucked = false; continue; }

                Pull(d, cfg, margin);   // 범위 안 — 끌어당긴다
            }
        }

        /// <summary>
        /// 흡인 범위 안의 잔해를 구멍 쪽으로 당긴다.
        ///
        /// 가까울수록 세게 당긴다 — 범위 가장자리에서는 살짝, 구멍 바로 옆에서는 확 빨려든다.
        /// 균일하게 당기면 멀리서부터 질질 끌려와 "빨려드는" 느낌이 안 난다.
        /// </summary>
        void Pull(Debris d, GameConfig cfg, float margin)
        {
            d.IsBeingSucked = true;

            Vector3 here = d.transform.position;
            Vector3 to = new Vector3(transform.position.x, here.y, transform.position.z);

            float dist = Vector3.Distance(here, to);
            float closeness = 1f - Mathf.Clamp01((dist - Radius) / Mathf.Max(0.01f, margin));

            float speed = cfg.holeSuctionSpeed * Mathf.Lerp(0.25f, 1f, closeness);
            d.transform.position = Vector3.MoveTowards(here, to, speed * Time.deltaTime);
        }

        void Swallow(Debris d, GameConfig cfg, GameLoop loop)
        {
            int units = d.SizeClass;

            CargoTow cargo = loop.Cargo;
            if (cargo != null) cargo.Detach(d);   // 끌고 있던 것이면 화물에서 뺀다

            d.BeginFall(transform.position, fallSeconds, OnFallFinished);

            // 큰 조각일수록 묵직하게. 한 번에 여러 개가 들어가면 SoundBank의 minGap이 솎는다.
            Utils.GameAudio.Play(Data.Sfx.Swallow, d.transform.position,
                                 Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(units / 4f)));

            SwallowedUnits += units;
            loop.AddCollected(units);
            AddXpScaled(units * cfg.xpPerDebrisUnit);
        }

        void OnFallFinished(Debris d)
        {
            if (PoolManager.Instance != null) PoolManager.Instance.Release(d.gameObject);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, Radius);

            GameConfig cfg = GameConfigRef.Current;
            if (cfg == null) return;
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, Radius + cfg.holeSuctionMargin);
        }
    }
}
