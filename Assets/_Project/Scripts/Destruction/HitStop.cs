using System.Collections;
using Demo.Data;
using UnityEngine;

namespace Demo.Destruction
{
    /// <summary>
    /// 시간을 아주 잠깐 멈춘다 — 격투 게임의 히트스톱.
    /// 때린 것이 <b>단단했다</b>는 신호다. 부드럽게 이어지면 손이 허공을 지난 것처럼 느껴진다.
    ///
    /// <b>왜 따로 떼어 냈나</b> [2026-09-30]. <see cref="DestructionFX"/>가 315줄이 되어
    /// CLAUDE.md 규칙 3(300줄)을 넘었다. 이 부분은 연출 박자를 하나도 모르고
    /// "얼마나 멈출 것인가"만 알면 되므로 경계가 가장 깨끗하다.
    ///
    /// <b>겹치면 시간만 늘어난다.</b> 두 번째 호출이 첫 코루틴을 끊는데, 그 시점에는 이미
    /// 배율이 0이라 코루틴 안에서 "되돌릴 값"을 읽으면 게임이 영영 멈춘다. 그래서
    /// 되돌릴 배율은 <b>멈추기 전에</b> 한 번만 기억한다.
    ///
    /// <b>정지 중에 화면이 흔들리면 안 된다.</b> 히트스톱은 한 장면을 붙드는 것이다.
    /// <see cref="Core.FollowCamera.TickShake"/>가 timeScale이 0이면 흔들림을 붙들어 둔다 —
    /// 그 처리가 없던 동안에는 세상만 멈추고 카메라가 최대 세기로 요동쳤다
    /// (실측: 정지 113ms 동안 변위가 프레임마다 0.068m씩 바뀜) [지적 2026-09-30].
    /// </summary>
    public sealed class HitStop : MonoBehaviour
    {
        Coroutine freeze;
        float restoreScale = 1f;      // 히트스톱이 끝나면 되돌릴 배율
        bool frozen;

        /// <summary>지금 멈춰 있는가.</summary>
        public bool Frozen => frozen;

        /// <summary>
        /// <see cref="GameConfig.hitStopSeconds"/>에 <paramref name="scale"/>을 곱한 만큼 멈춘다.
        /// 박자마다 배수를 다르게 주면 "두드림 / 층 붕괴 / 완파"가 길이로 구분된다.
        /// </summary>
        public void Hold(float scale)
        {
            GameConfig cfg = Core.GameConfigRef.Current;
            HoldSeconds((cfg != null ? cfg.hitStopSeconds : 0f) * scale);
        }

        /// <summary>초를 직접 준다.</summary>
        public void HoldSeconds(float seconds)
        {
            if (seconds <= 0f || !isActiveAndEnabled) return;

            if (!frozen)
            {
                if (Time.timeScale <= 0f) return;   // 남이 멈춰 둔 것이다 — 건드리지 않는다
                restoreScale = Time.timeScale;
                frozen = true;
            }

            if (freeze != null) StopCoroutine(freeze);
            freeze = StartCoroutine(Freeze(seconds));
        }

        /// <summary>
        /// 대기는 반드시 <b>실제 시간</b>으로 잰다. 배율이 0인 동안에는 게임 시간이
        /// 한 톨도 안 흐르므로, 보통 대기를 쓰면 영원히 안 깨어난다.
        /// </summary>
        IEnumerator Freeze(float seconds)
        {
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(seconds);
            Restore();
        }

        void Restore()
        {
            if (!frozen) return;
            Time.timeScale = restoreScale;
            frozen = false;
            freeze = null;
        }

        /// <summary>꺼져도 시간이 멈춘 채로 남지 않게 한다.</summary>
        void OnDisable()
        {
            if (freeze != null) StopCoroutine(freeze);
            freeze = null;
            Restore();
        }
    }
}
