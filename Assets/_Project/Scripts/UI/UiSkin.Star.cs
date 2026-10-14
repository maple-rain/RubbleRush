using UnityEngine;

namespace Demo.UI
{
    /// <summary>
    /// 별 등급 표시 [지시 2026-10-13].
    ///
    /// <b>왜 글자 ★가 아닌가.</b> 글꼴에 그 글리프가 없으면 네모가 뜬다. 배민 도현체에
    /// 들어 있는지 보장할 수 없고, 있어도 글꼴마다 크기와 위치가 달라 가운데 맞추기가 어렵다.
    /// 이 프로젝트는 이미 원형 아이콘을 코드로 그려 쓰고 있으니 같은 길로 간다.
    ///
    /// <b>한 번만 굽는다.</b> 별 한 장을 알파로 만들어 두고, 채운 별과 빈 별은
    /// <b>같은 그림을 색만 바꿔</b> 그린다. 텍스처는 하나뿐이다.
    /// </summary>
    public sealed partial class UiSkin
    {
        static Texture2D s_star;

        /// <summary>별 모양 알파 텍스처. 흰색에 알파로 모양이 들어 있다.</summary>
        public static Texture2D Star
        {
            get
            {
                if (s_star != null) return s_star;

                const int N = 64;
                var pts = new Vector2[10];
                for (int i = 0; i < 10; i++)
                {
                    // 위 꼭짓점에서 시작해 바깥·안쪽 반지름을 번갈아 찍는다.
                    float a = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                    float rad = (i % 2 == 0) ? 0.47f : 0.19f;
                    pts[i] = new Vector2(0.5f + Mathf.Cos(a) * rad, 0.5f - Mathf.Sin(a) * rad);
                }

                var px = new Color32[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        // 3x3로 더 잘게 재서 가장자리를 부드럽게 한다 — 안 하면 톱니가 보인다.
                        int hit = 0;
                        for (int sy = 0; sy < 3; sy++)
                            for (int sx = 0; sx < 3; sx++)
                            {
                                var p = new Vector2((x + (sx + 0.5f) / 3f) / N,
                                                    (y + (sy + 0.5f) / 3f) / N);
                                if (InPolygon(pts, p)) hit++;
                            }
                        px[y * N + x] = new Color32(255, 255, 255, (byte)(hit * 255 / 9));
                    }

                s_star = new Texture2D(N, N, TextureFormat.RGBA32, false);
                s_star.SetPixels32(px);
                s_star.Apply();
                s_star.wrapMode = TextureWrapMode.Clamp;
                s_star.hideFlags = HideFlags.HideAndDontSave;
                return s_star;
            }
        }

        /// <summary>수평 반직선 교차 홀짝. 오목 다각형에서도 맞는다.</summary>
        static bool InPolygon(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > p.y) == (poly[j].y > p.y)) continue;
                float x = (poly[j].x - poly[i].x) * (p.y - poly[i].y)
                        / (poly[j].y - poly[i].y) + poly[i].x;
                if (p.x < x) inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// 별을 가로로 가운데 맞춰 늘어놓는다. 채운 것은 호박색, 빈 것은 흐리게.
        ///
        /// 뒤에 검은 별을 한 겹 크게 깔아 밝은 배경에서도 윤곽이 산다.
        /// </summary>
        public static void Stars(Rect r, int filled, int total)
        {
            if (total <= 0 || r.height <= 0f) return;

            float gap = r.height * 0.18f;
            float side = Mathf.Min(r.height, (r.width - gap * (total - 1)) / total);
            if (side <= 1f) return;

            float used = side * total + gap * (total - 1);
            float x = r.x + (r.width - used) * 0.5f;
            float y = r.y + (r.height - side) * 0.5f;
            float lip = Mathf.Max(2f, side * 0.05f);

            Color keep = GUI.color;
            Texture2D tex = Star;

            for (int i = 0; i < total; i++)
            {
                var cell = new Rect(x + (side + gap) * i, y, side, side);

                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(cell.x - lip, cell.y - lip,
                                         cell.width + lip * 2f, cell.height + lip * 2f), tex);

                GUI.color = i < filled ? Accent : new Color(1f, 1f, 1f, 0.16f);
                GUI.DrawTexture(cell, tex);
            }

            GUI.color = keep;
        }
    }
}
