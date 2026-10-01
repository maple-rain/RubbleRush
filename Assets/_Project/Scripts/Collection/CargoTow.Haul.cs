using UnityEngine;

namespace Demo.Collection
{
    /// <summary>
    /// 짐을 <b>실제로 끄는</b> 쪽 — 자리 잡기와 줄 풀기 [분할 2026-10-01].
    ///
    /// <see cref="CargoTow"/>가 330줄이 되어 CLAUDE.md 규칙 3을 넘었다. 축이 둘이라 거기서 갈랐다.
    /// 저쪽은 <b>무엇을 들고 있는가</b>(Add·Detach·DropAll·단위 수)이고, 이쪽은
    /// <b>그것이 매 프레임 어디에 놓이는가</b>(LateUpdate·슬롯·그물·걸림)다.
    /// 들고 있는 것을 고칠 때와 끌리는 모양을 고칠 때가 서로 다른 작업이라, 파일이 갈리는 편이 싸다.
    ///
    /// <b>새 타입을 만들지 않고 partial로 둔 이유</b>: 둘은 같은 컴포넌트의 상태를 공유한다
    /// (towed·net·tension·snag·netLine). 타입을 쪼개면 그 공유분을 또 어딘가로 옮겨야 하고,
    /// 그건 파일 수만 늘릴 뿐 읽는 사람에게 아무것도 더 말해 주지 않는다.
    /// 규칙이 막으려는 것은 <b>한 파일이 커져서 부분 수정이 비싸지는 것</b>이고, partial이면 그게 풀린다.
    /// (에디터 툴 둘을 같은 이유로 쪼갰다 — BalanceCsvIO.Import.cs 참조.)
    /// </summary>
    public sealed partial class CargoTow
    {
        void Stow()
        {
            net.Clear();
            tension.Clear();
            snag.Reset();
            if (netLine != null) netLine.positionCount = 0;
        }

        /// <summary>걸림의 결과를 적용한다 [14번]. 목록을 다 돈 뒤에만 부른다.</summary>
        void ResolveSnag()
        {
            if (!snag.Triggered || snagged.Count == 0) return;

            if (snag.Mode == Hazard.DebrisSnag.Response.Snap)
            {
                // 줄이 끊어진다 — 걸린 조각만이 아니라 전부 놓친다
                int lost = towed.Count;
                DropAll();
                snag.Consume(lost);
                return;
            }

            // DropOff — 걸린 조각만 그 자리에 남는다
            for (int i = 0; i < snagged.Count; i++) Detach(snagged[i]);
            snag.Consume(snagged.Count);
            snagged.Clear();
        }

        void LateUpdate()
        {
            if (towed.Count == 0)
            {
                if (netLine != null && netLine.positionCount != 0) Stow();
                return;
            }

            float dt = Time.deltaTime;
            Vector3 here = transform.position;
            Vector3 back = -transform.forward;

            // ① 매듭이 어디에 있는가
            Vector3 anchor, dir;
            if (UseTension)
            {
                tension.Length = Leash;              // 짐이 줄거나 늘면 줄 길이도 따라간다
                tension.Tick(dt, here, back, TotalUnits);
                anchor = tension.Position;
                dir = tension.Direction;
            }
            else
            {
                anchor = here + back * Leash;
                dir = back;
            }
            anchor.y = here.y;

            // ② 잔해를 그 둘레에 깐다. 실제로 얼마나 퍼졌는지를 돌려받는다
            snag.Begin();
            snagged.Clear();
            float spread = PlaceSlots(anchor, dir, dt);
            snag.End(dt);
            ResolveSnag();

            // ③ 줄을 푼다
            if (UseRope)
            {
                // 그물을 쥔 손은 플레이어 쪽 — 줄이 거기서 뻗어 나가야 끌려오는 것으로 보인다
                Vector3 hand = here + dir * 0.4f;
                hand.y = here.y;

                // <b>공칭 반지름이 아니라 실제 퍼진 만큼을 감싼다</b> [고침 2026-10-01].
                // DiscRadius(4.02m)로 감싸면 끌리면서 뒤처진 바깥 잔해(실측 5.56m)가
                // 그물 밖으로 삐져나온다 — 그물이 짐을 싼 것으로 안 보인다.
                net.Tick(dt, anchor, spread, hand);
                net.Draw(netLine);
            }
            else if (netLine != null && netLine.positionCount != 0) netLine.positionCount = 0;
        }

        /// <summary>
        /// 그물에 싸여 끌려오는 덩어리다 [지시 2026-09-22].
        ///
        /// 예전에는 뒤로 줄을 세웠는데, 그러면 꼬리처럼 보이고 개수가 늘수록 화면 밖으로 나갔다.
        /// 대신 <b>해바라기 배열(피보나치 나선)</b>로 원판을 채운다 — 개수가 늘어도 반지름이
        /// sqrt로만 자라고 간격이 고르다. 덩어리가 구멍 위를 지나가면 바깥쪽부터 빠진다.
        ///
        /// <b>바깥쪽일수록 늦게 따라온다</b> [지시 13번, 2026-10-01]. 전부 같은 속도로 따라오면
        /// 뭉치가 한 장의 판때기로 움직여서, 그물이 아니라 썰매처럼 보인다.
        /// </summary>
        /// <returns>가장 바깥 잔해까지의 거리(m). 그물이 이만큼을 감싼다</returns>
        float PlaceSlots(Vector3 anchor, Vector3 dir, float dt)
        {
            const float Golden = 2.39996323f;                 // 황금각 ≈ 137.5°
            Vector3 right = new Vector3(dir.z, 0f, -dir.x);   // 뭉치를 줄 방향에 맞춰 돌린다
            float inv = 1f / Mathf.Sqrt(Mathf.Max(1, towed.Count));
            float spreadSqr = 0f, biggest = 0f;

            for (int i = 0; i < towed.Count; i++)
            {
                Debris d = towed[i];
                if (d == null) continue;

                // 구멍이 당기는 중이면 놓아준다. 두 힘이 맞서면 잔해가 그 사이에서 떤다.
                if (d.IsBeingSucked) continue;

                float r = slotSpacing * Mathf.Sqrt(i + 0.5f);
                float a = i * Golden;
                Vector3 slot = anchor + (right * Mathf.Cos(a) + dir * Mathf.Sin(a)) * r;
                slot.y = d.transform.position.y;

                // 바깥쪽(i가 클수록)이 더 느리다. sqrt로 재면 0~1로 고르게 퍼진다.
                float outer = Mathf.Sqrt(i + 0.5f) * inv;
                float rate = followLerp * (1f - outerLag * outer);
                d.transform.position = Vector3.Lerp(d.transform.position, slot,
                                                    1f - Mathf.Exp(-rate * dt));

                // 건물·토대에 걸렸는가 [14번]. 걸리면 자리를 고쳐 주고 목록에 담는다
                Vector3 at = d.transform.position;
                if (snag.Check(ref at, d.SizeRadius))
                {
                    d.transform.position = at;
                    snagged.Add(d);
                }

                // 제곱으로 비교한다 — 잔해 500개에 Sqrt를 500번 부를 이유가 없다.
                // 가장 큰 조각도 여기서 같이 본다. LargestRadius를 부르면 500개를 또 훑는다.
                Vector3 off = d.transform.position - anchor; off.y = 0f;
                if (off.sqrMagnitude > spreadSqr) spreadSqr = off.sqrMagnitude;
                if (d.SizeRadius > biggest) biggest = d.SizeRadius;
            }

            // 조각 반경만큼 더 싸야 그물이 잔해를 스치지 않고 감싼다
            return Mathf.Max(DiscRadius, Mathf.Sqrt(spreadSqr) + biggest);
        }
    }
}
