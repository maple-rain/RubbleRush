using UnityEngine;

namespace Demo.Player
{
    /// <summary>
    /// 연타를 감지해 펀치 배속을 올리는 상태 기계 [지시 2026-09-30, 죠죠 3부 오라 러쉬].
    ///
    /// <b>왜 필요한가.</b> 기본 속도로는 연타하면 <b>주먹이 헛친다</b>. 클립 0.458초를
    /// 상태 speed 3.08로 돌면 한 방이 149ms인데, 60ms 간격으로 치면 정규시간 0.29까지만
    /// 가고 다시 시작한다. 정점이 0.36이므로 <b>벽에 닿기 전에 회수된다</b> —
    /// 빨리 칠수록 안 맞는 구조였다 (실측 8연타 중 정점 도달 0회).
    /// 배속을 올려 한 방이 탭 간격 안에 끝나게 하면 8/8이 된다.
    ///
    /// <b>왜 MonoBehaviour가 아닌가.</b> 코루틴도 생명주기도 필요 없고 상태는 float 둘뿐이다.
    /// 컴포넌트로 만들면 AddComponent 배선과 실행 순서 문제가 따라붙는다 —
    /// <see cref="PlayerAnimator"/>가 읽는 값이라 그보다 먼저 갱신돼야 하기 때문이다.
    /// 지금은 주인이 <see cref="Decay"/>를 부르는 시점이 곧 갱신 시점이라 모호함이 없다.
    ///
    /// 수치는 전부 [제안·미승인]이다.
    /// </summary>
    [System.Serializable]
    public sealed class PunchRush
    {
        [SerializeField, Range(0.03f, 0.3f),
         Tooltip("탭 간격이 이보다 짧으면 러쉬가 최대다(초)")]
        float fastGap = 0.10f;

        [SerializeField, Range(0.1f, 1f),
         Tooltip("탭 간격이 이보다 길면 러쉬가 아니다(초). 그 사이는 비례해서 섞인다")]
        float slowGap = 0.30f;

        [SerializeField, Range(1f, 6f),
         Tooltip("러쉬가 최대일 때의 재생 배율. <b>이게 러쉬의 핵심이다</b> — " +
                 "한 방이 탭 간격 안에 끝나야 주먹이 벽까지 간다.\n" +
                 "2.5면 149ms짜리 한 방이 60ms가 되어 제때 도착한다")]
        float punchRate = 2.5f;

        [SerializeField, Range(0.5f, 10f),
         Tooltip("안 때리면 러쉬가 빠지는 속도(초당). 2.5면 0.4초에 0으로")]
        float fade = 2.5f;

        float level;
        float lastAt = -99f;

        /// <summary>0이면 단발, 1이면 러쉬. 연출이 읽는다.</summary>
        public float Level01 => level;

        /// <summary>
        /// 지금 펀치가 <b>몇 배로</b> 돌고 있는가. 연출이 타격 시점을 앞당기는 데 쓴다 —
        /// 배속이 2.5면 주먹도 2.5배 빨리 도착하므로, 연출을 그대로 두면
        /// 이미 지나간 자리에 터진다.
        /// </summary>
        public float SpeedScale => Mathf.Lerp(1f, punchRate, level);

        /// <summary>
        /// 한 방 쳤다고 알린다. 간격이 짧을수록 러쉬가 오른다.
        ///
        /// <b>실제 시간</b>으로 잰다. 히트스톱이 걸린 동안 게임 시간은 안 흐르는데
        /// 플레이어의 손은 계속 움직이므로, 게임 시간으로 재면 간격이 0으로 보인다.
        /// </summary>
        public void Note()
        {
            float gap = Time.unscaledTime - lastAt;
            lastAt = Time.unscaledTime;
            level = Mathf.Max(level, Mathf.InverseLerp(slowGap, fastGap, gap));
        }

        /// <summary>러쉬를 즉시 해제한다. 마무리 한 방을 정상 속도로 내보낼 때 쓴다.</summary>
        public void Clear() => level = 0f;

        /// <summary>
        /// 시간이 지난 만큼 러쉬를 떨어뜨린다. 주인이 매 프레임 부른다.
        /// 오를 때는 <see cref="Note"/>가 즉시 올리고, 내릴 때만 이 속도를 따른다.
        /// </summary>
        public void Decay() => level = Mathf.MoveTowards(level, 0f, fade * Time.unscaledDeltaTime);
    }
}
