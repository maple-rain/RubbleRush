using UnityEngine;

namespace Demo.Collection
{
    /// <summary>
    /// 주머니의 <b>자</b> 쪽 [분할 2026-10-08]. 저쪽은 풀고, 이쪽은 푼 결과를 센다.
    ///
    /// 300줄 규칙(CLAUDE.md 3)을 넘겨 갈랐다. 축이 둘이라 거기서 잘랐다 —
    /// 구속을 고칠 때와 "무엇을 숫자로 셀 것인가"를 고칠 때가 서로 다른 작업이다.
    /// 이 세션에서 자를 두 번 고쳐 달았다(속도, 단계별 이동량). 그때마다 솔버는 안 건드렸다.
    /// </summary>
    public sealed partial class NetPocket
    {
        /// <summary>푼 뒤의 속도. 구속이 위치를 옮긴 것까지 다 반영된 값이다.</summary>
        void Speeds(float dt)
        {
            float sum = 0f, max = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector3 v = pos[i] - prev[i]; v.y = 0f;
                float sp = v.magnitude / dt;
                sum += sp;
                if (sp > max) max = sp;
            }
            MeanSpeed = count > 0 ? sum / count : 0f;
            MaxSpeed = max;
        }

        /// <summary>실측용 집계. 주장을 숫자로 돌려준다.</summary>
        void Count_(CableRope rope)
        {
            Escaped = Overlaps = Contacts = 0;
            int n = rope.Count;
            Vector3 cen = rope.Centroid;

            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                {
                    Vector3 d = pos[j] - pos[i]; d.y = 0f;
                    float need = rad[i] + rad[j];
                    if (d.sqrMagnitude < need * need - 1e-4f) Overlaps++;
                }

            for (int k = 0; k < count; k++)
            {
                bool out_ = false, touch = false;
                for (int i = 0; i < n; i++)
                {
                    Vector3 c = ClosestOnSegment(rope.At(i), rope.At((i + 1) % n), pos[k], out _);
                    Vector3 away = pos[k] - c; away.y = 0f;
                    Vector3 inward = cen - c; inward.y = 0f;
                    if (Vector3.Dot(away, inward) < 0f) out_ = true;
                    if (away.magnitude < rad[k] + 0.05f) touch = true;
                }
                if (out_) Escaped++;
                if (touch) Contacts++;
            }
        }

    }
}
