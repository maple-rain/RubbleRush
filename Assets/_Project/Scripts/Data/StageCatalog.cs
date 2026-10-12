using UnityEngine;

namespace Demo.Data
{
    /// <summary>
    /// 고를 수 있는 스테이지 목록 [2026-10-12].
    ///
    /// <b>스테이지는 씬이 아니라 데이터다.</b> 시작 메뉴를 씬으로 뗐다고 해서 스테이지까지
    /// 씬으로 만들면, CLAUDE.md 규칙 2가 막으려던 것이 그대로 돌아온다 —
    /// "구역은 <see cref="ZoneData"/>로 정의하고 런타임에 생성한다. 그래야 레벨 구성까지
    /// 코드가 소유할 수 있다". 스테이지를 늘리는 일은 <b>이 목록에 한 줄</b>이어야 하고,
    /// 씬을 복제하고 빌드 목록에 넣는 일이 되어서는 안 된다.
    ///
    /// 비어 있으면 시작 메뉴가 스테이지 고르기를 건너뛰고 바로 들어간다 —
    /// 지금처럼 구역이 하나뿐일 때 쓸데없는 화면을 한 겹 더 보여 주지 않기 위해서다.
    /// </summary>
    [CreateAssetMenu(menuName = "Demo/Stage Catalog", fileName = "StageCatalog")]
    public sealed class StageCatalog : ScriptableObject
    {
        [System.Serializable]
        public sealed class Entry
        {
            [Tooltip("메뉴에 보일 이름. 비면 ZoneData의 이름을 쓴다")]
            public string displayName;

            [Tooltip("이 스테이지가 지을 구역")]
            public ZoneData zone;

            [Tooltip("켜면 목록에 보이되 못 고른다. 아직 안 만든 스테이지 자리")]
            public bool locked;

            public string Label =>
                !string.IsNullOrWhiteSpace(displayName) ? displayName
                : zone != null && !string.IsNullOrWhiteSpace(zone.displayName) ? zone.displayName
                : zone != null ? zone.name : "(빈 칸)";
        }

        [SerializeField] Entry[] stages = new Entry[0];

        public Entry[] Stages => stages;

        /// <summary>
        /// 고르는 화면을 띄울 가치가 있는가.
        ///
        /// <b>잠긴 칸도 센다</b> [고침 2026-10-12]. 풀린 것만 세면 스테이지가 하나뿐인 지금
        /// 화면이 아예 안 뜨는데, 기획자가 원한 것은 <b>그 구조가 보이는 것</b>이다 —
        /// "튜토리얼 다음에 스테이지 1, 2가 온다"가 첫 화면에서 읽혀야 한다.
        /// 칸이 하나뿐일 때만 건너뛴다. 그때는 고를 것이 없으니 화면이 거짓말이 된다.
        /// </summary>
        public bool WorthShowing
        {
            get
            {
                int n = 0;
                foreach (Entry e in stages) if (e != null) n++;
                return n > 1;
            }
        }

        /// <summary>첫 번째로 고를 수 있는 것. 없으면 null.</summary>
        public ZoneData First()
        {
            foreach (Entry e in stages)
                if (e != null && e.zone != null && !e.locked) return e.zone;
            return null;
        }
    }

    /// <summary>
    /// 고른 스테이지를 다음 씬으로 넘긴다 [2026-10-12].
    ///
    /// <b>static으로 둔다.</b> 씬을 넘어가도 살아남아야 하는데, 값 하나를 위해
    /// DontDestroyOnLoad 오브젝트를 만드는 것은 과하다. 같은 실행 안에서만 살면 된다.
    ///
    /// <b>비어 있으면 아무 일도 안 한다.</b> 에디터에서 Main.unity를 직접 열어 돌리는 일이
    /// 개발 중에 제일 잦은데, 그때는 <see cref="Core.GameLoop"/>에 물려 있는 구역이 그대로
    /// 쓰인다 — 시작 씬을 거치지 않았다고 해서 안 돌아가면 안 된다.
    /// </summary>
    public static class StagePick
    {
        public static ZoneData Chosen { get; set; }

        /// <summary>시작 씬이 고른 것이 있으면 그것을, 없으면 <paramref name="fallback"/>을.</summary>
        public static ZoneData Or(ZoneData fallback) => Chosen != null ? Chosen : fallback;
    }
}
