using UnityEngine;

namespace Demo.Collection
{
    /// <summary>
    /// 줄의 <b>모양</b> 쪽 [분할 2026-10-08] — 구속 다시 맞추기, 밖에서 미는 입구,
    /// 한 프레임 모양 상한, 그리고 그리기.
    /// 저쪽(<see cref="CableRope"/>)은 한 프레임을 전진시키는 일만 한다.
    ///
    /// 300줄 규칙(CLAUDE.md 3)을 넘겨 갈랐고, 축은 작업이 정해 줬다 —
    /// "케이블이 떨린다"를 고치는 동안 건드린 것은 전부 이쪽이었고 적분은 한 줄도 안 바뀌었다.
    /// </summary>
    public sealed partial class CableRope
    {
        /// <summary>
        /// 길이·바닥 구속을 다시 맞춘다. 밖에서 <see cref="Push"/>로 테두리를 밀었으면
        /// 이웃 간 길이가 어긋나 있으므로 <b>민 쪽이 직접 불러야 한다</b>.
        /// 베를레의 성질이다 — 위치를 억지로 옮겨도 반복만 돌리면 속도까지 맞아 들어간다.
        /// </summary>
        public void Relax()
        {
            if (!ready) return;
            int n = p.Length;
            for (int k = 0; k < iterations; k++)
            {
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    Vector3 d = p[j] - p[i];
                    float len = d.magnitude;
                    if (len < 1e-5f) continue;
                    Vector3 fix = d * ((len - restLength) / len * 0.5f);
                    p[i] += fix; p[j] -= fix;
                }
                for (int i = 0; i < n; i++) if (p[i].y < floorY) p[i].y = floorY;
            }
        }

        /// <summary>
        /// 테두리 점 하나를 민다 — <see cref="NetPocket"/>의 반작용 입구다.
        /// 이게 없으면 그물은 쇠틀이고, 잔해가 아무리 밀어도 모양이 안 바뀐다.
        ///
        /// <b>직전 위치도 같이 민다</b> [고침 2026-10-08]. 베를레는 속도를 <c>(지금 − 직전)</c>으로
        /// 들고 있어서, 위치만 옮기면 <b>잔해가 민 거리가 그대로 줄의 속도</b>가 된다.
        /// 한 프레임에 잔해 서른 개가 두 번씩 미니 줄이 떨었다 — 실측: 테두리 점의 55%가
        /// 매 프레임 이동 방향을 뒤집었고, 모양 변화 속도가 8.8m/s로 플레이어보다 빨랐다.
        /// 잔해 쪽에서 똑같은 것을 이미 한 번 고쳤다 (<see cref="NetPocket"/>).
        ///
        /// <see cref="pushResponse"/>가 0이면 밀린 만큼 <b>모양만</b> 바뀌고 관성이 안 남는다.
        /// </summary>
        public void Push(int i, Vector3 delta)
        {
            if (!ready || i < 0 || i >= p.Length) return;
            delta.y = 0f;
            p[i] += delta;
            prev[i] += delta * (1f - Mathf.Clamp01(pushResponse));
        }

        /// <summary>테두리의 무게중심. 안팎을 가를 때 쓴다 — 볼록하지 않아도 대체로 맞는다.</summary>
        public Vector3 Centroid
        {
            get
            {
                if (!ready) return Vector3.zero;
                Vector3 s = Vector3.zero;
                for (int i = 0; i < p.Length; i++) s += p[i];
                return s / p.Length;
            }
        }

        /// <summary>
        /// 이 프레임에 <b>모양이</b> 바뀐 양을 묶는다 [2026-10-08]. 한 프레임의 끝에서 부른다.
        ///
        /// <b>끌려가는 몫은 안 묶는다.</b> 테두리가 통째로 움직인 평균을 빼고, 거기서 벗어난
        /// 만큼만 상한에 건다 — 그물이 플레이어를 따라가는 것은 떨림이 아니다.
        ///
        /// 잔해 쪽에서 이미 같은 약을 썼다 (<see cref="NetPocket"/>의 한 프레임 이동 상한).
        /// 거기서도 "보정이 속도로 남는 정도"를 양 끝까지 쓸어 봤지만 총량이 안 줄었고,
        /// 합계를 묶고 나서야 잡혔다. 줄도 같은 병이다 — 실측에서 모양 변화가 6.8~9.6m/s로
        /// 플레이어(6m/s)보다 빨랐다.
        /// </summary>
        public void Settle()
        {
            if (!ready || shapeStep <= 0f) return;
            int n = p.Length;

            Vector3 drift = Vector3.zero;
            for (int i = 0; i < n; i++) { Vector3 d = p[i] - frameStart[i]; d.y = 0f; drift += d; }
            drift /= n;

            for (int i = 0; i < n; i++)
            {
                Vector3 shape = p[i] - frameStart[i] - drift; shape.y = 0f;
                float len = shape.magnitude;
                if (len <= shapeStep) continue;

                Vector3 cut = shape * (1f - shapeStep / len);
                p[i] -= cut;
                prev[i] -= cut;
            }
        }

        /// <summary>지금 둘레 길이(m). 자유 사슬이면 처음 둘러싼 고리의 길이 그대로다.</summary>
        public float Perimeter => ready ? restLength * p.Length : 0f;

        /// <summary>
        /// 손에서 뻗어 나가 테두리를 한 바퀴 도는 한 줄로 그린다.
        ///
        /// 선 하나로 그리는 이유는 LineRenderer가 끊어진 구간을 못 그리기 때문이다 —
        /// 손과 고리를 따로 그리려면 렌더러가 둘 필요하고, 그건 드로우콜 하나를 더 쓴다
        /// (예산 100 중 잔해 0개인 씬이 이미 82~84를 쓴다).
        /// 손에서 <see cref="Near"/>로 들어가 고리를 돌아 제자리로 돌아온다.
        /// </summary>
        public void Draw(LineRenderer line)
        {
            if (!ready || line == null) { if (line != null) line.positionCount = 0; return; }
            int n = p.Length;
            if (line.positionCount != n + 2) line.positionCount = n + 2;
            line.SetPosition(0, Hand);
            for (int i = 0; i <= n; i++) line.SetPosition(i + 1, p[(Near + i) % n]);
        }

    }
}
