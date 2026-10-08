using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Demo.Collection;
using Demo.Data;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 주머니로 끄는 짐이 화면에 들어오는가, 안 들어오면 무엇을 얼마나 바꿔야 하는가 [2026-10-08].
    ///
    /// <b>왜 필요한가.</b> 주머니 모형이 확정됐는데(2026-10-08), 실측상 짐이 플레이어 뒤
    /// 10.6m까지 간다. 카메라는 고정 쿼터뷰(yaw 45 · pitch 50)라 <b>뒤쪽 시야가 진행 방향에
    /// 따라 달라진다</b> — 어떤 각도에서는 짐이 통째로 화면 밖이다. 짐이 안 보이면
    /// "끌고 가는 느낌"도 없다. 고를 수 있는 손잡이는 둘이다.
    ///
    /// <b>A. 줌아웃</b> (<c>cameraZoomPerUnit</c>) — 멀어지면 다 보이지만 <b>전부 작아진다</b>.
    /// <b>B. 시선 뒤로</b> (새 값) — 카메라가 플레이어가 아니라 플레이어와 짐 사이를 본다.
    /// 크기는 그대로지만 <b>플레이어가 화면 아래로 밀린다</b> — 그래서 플레이어가 보이는지도 같이 본다.
    ///
    /// <b>무엇을 재는가.</b> 지어낸 상자가 아니라 <see cref="PocketCompare"/>가 방금 돌린
    /// 시뮬레이션의 <b>실제 잔해 좌표</b>를 그대로 쓴다. 카메라도 씬의 실제 값(화각·종횡비·
    /// 각도·거리)으로 세운다. 36개 진행 방향을 전부 돌려 <b>최악 각도</b>를 찾는다.
    ///
    /// <b>이 도구는 값을 정하지 않는다.</b> 각 손잡이를 얼마나 돌리면 들어오는지만 적는다.
    /// "작아지는 것"과 "플레이어가 아래로 밀리는 것" 중 무엇이 나은지는 기획자가 보고 정한다.
    /// </summary>
    public static class RearViewFit
    {
        const float Margin = 0.92f;   // 화면 가장자리에 딱 붙는 것은 '보인다'고 치지 않는다

        // 아래 넷은 <b>[제안·미승인]</b>이다. 에셋에 쓰지 않는다 — 여기서는 "이 값이면 되는가"를
        // 같은 자로 확인할 뿐이고, 정하는 것은 기획자다 (CLAUDE.md 역할 분담).
        const float PropFit = 2.07f, PropZoomMax = 16f;
        const float PropShare = 0.35f, PropBackMax = 8f;

        struct Cam
        {
            public float Pitch, Yaw, Fov, Aspect, Distance, Bias;
        }

        [MenuItem("Demo/15c. 짐이 화면에 들어오는가", priority = 69)]
        public static void Run()
        {
            var fc = Object.FindFirstObjectByType<Core.FollowCamera>();
            if (fc == null) { Debug.LogError("[시야] 씬에 FollowCamera가 없다."); return; }
            var cfg = Load();
            if (cfg == null) { Debug.LogError("[시야] GameConfig를 못 찾았다."); return; }

            var so = new SerializedObject(fc);
            var cam = fc.GetComponent<Camera>();
            var baseCam = new Cam
            {
                Pitch = so.FindProperty("pitch").floatValue,
                Yaw = so.FindProperty("yaw").floatValue,
                Fov = cam != null ? cam.fieldOfView : 60f,
                Aspect = 16f / 9f,     // 세로 기준 폰 가로모드. 실기 종횡비는 미측정
                Distance = cfg.cameraDistance,
                Bias = 0f,
            };

            var sb = new StringBuilder();
            var csv = new StringBuilder();
            csv.AppendLine("# 주머니 짐이 화면에 들어오는가 [2026-10-08]");
            csv.AppendLine($"# 카메라: pitch {baseCam.Pitch} · yaw {baseCam.Yaw} · fov {baseCam.Fov} · " +
                           $"종횡비 16:9 · 기본 거리 {baseCam.Distance}m · 여백 {Margin:P0}");
            csv.AppendLine("# 좌표는 Demo/15가 돌린 실제 시뮬레이션의 잔해 위치다");
            csv.AppendLine("shape,type,units,load_back_m,now_visible_pct,proposed_visible_pct,proposed_player_ok,need_distance_m,need_zoom_per_unit,need_bias_m,bias_player_ok");

            sb.AppendLine("주머니 짐이 화면에 들어오는가 — 진행 방향 36가지 중 최악 [2026-10-08]");
            sb.AppendLine($"카메라 pitch {baseCam.Pitch} · yaw {baseCam.Yaw} · fov {baseCam.Fov} · " +
                          $"거리 {baseCam.Distance}m · 줌 {cfg.cameraZoomPerUnit}/단위 (상한 {cfg.cameraZoomMax})");
            sb.AppendLine();
            sb.AppendLine($"제안값(미승인): fitPerMeter {PropFit} · zoomMax {PropZoomMax} · " +
                          $"lookBackShare {PropShare} · lookBackMax {PropBackMax}   (*= 플레이어가 밀려남)");
            sb.AppendLine();
            sb.AppendLine(Row("모형·종류", "단위", "짐 뒤끝", "지금/제안", "필요 거리", "→줌/단위", "필요 시선", "플레이어"));

            // <b>두 모형을 다 잰다</b> [2026-10-08]. 주머니만 재면 "지금 안 보인다"가
            // 주머니 탓으로 읽히는데, 기존 방식도 짐이 뒤로 뻗는다. 대조군 없이는
            // 새로 생긴 결함인지 원래 있던 결함인지 못 가른다.
            foreach (CargoTow.HaulShape shape in new[] { CargoTow.HaulShape.Slots,
                                                         CargoTow.HaulShape.Pocket })
            foreach (var cs in PocketCompare.Cases)
            {
                PocketCompare.Drive(shape, cs, out _, out _);
                var load = new List<(float side, float back, float r)>(PocketCompare.LastLoad);
                if (load.Count == 0) continue;

                float backMax = 0f;
                foreach (var p in load) if (p.back + p.r > backMax) backMax = p.back + p.r;

                int units = cs.count;   // debrisSizeClass가 전부 1이라 조각 수 = 단위 수
                float zoomNow = Mathf.Min(units * cfg.cameraZoomPerUnit, cfg.cameraZoomMax);

                // '지금'은 GameConfig에 실제로 들어 있는 값이다 — 기획자가 값을 고치고 다시
                // 돌리면 이 칸이 곧 검증이 된다.
                Cam now = baseCam;
                now.Distance = cfg.cameraDistance + zoomNow;
                now.Bias = Mathf.Min(backMax * cfg.cameraLookBackShare, cfg.cameraLookBackMax);
                if (cfg.cameraFitPerMeter > 0f)
                    now.Distance = cfg.cameraDistance + Mathf.Min(cfg.cameraZoomMax,
                        Mathf.Max(zoomNow, cfg.cameraFitPerMeter * (backMax - now.Bias) - cfg.cameraDistance));
                float seenNow = VisibleShare(now, load);

                // 제안값으로는 되는가. 에셋에 쓰지 않는다 — 값은 기획자가 정한다.
                // FollowCamera가 쓰는 식 그대로다: 시선은 짐 뒤끝의 몇 할, 거리는 남은 만큼의 배수.
                Cam prop = baseCam;
                prop.Bias = Mathf.Min(backMax * PropShare, PropBackMax);
                prop.Distance = cfg.cameraDistance + Mathf.Min(PropZoomMax,
                    Mathf.Max(zoomNow, PropFit * (backMax - prop.Bias) - cfg.cameraDistance));
                float seenProp = VisibleShare(prop, load);
                bool propPlayer = PlayerVisible(prop);

                float needDist = Solve(baseCam, load, true);
                float needBias = Solve(baseCam, load, false);
                bool playerOk = needBias > 0f && PlayerVisible(Bias(baseCam, needBias));
                float perUnit = needDist > 0f && units > 0
                              ? (needDist - cfg.cameraDistance) / units : -1f;

                sb.AppendLine(Row((shape == CargoTow.HaulShape.Slots ? "슬롯 " : "주머니 ") + cs.name,
                    units.ToString(), F(backMax), $"{seenNow:P0} / {seenProp:P0}{(propPlayer ? "" : "*")}",
                    needDist > 0 ? F(needDist) : "안 됨",
                    perUnit >= 0 ? F(perUnit) : "-",
                    needBias > 0 ? F(needBias) : "안 됨",
                    needBias > 0 ? (playerOk ? "보임" : "밀려남") : "-"));

                csv.AppendLine(string.Join(",", shape, cs.name, units, F(backMax), F(seenNow), F(seenProp), propPlayer ? 1 : 0,
                    F(needDist), F(perUnit), F(needBias), playerOk ? 1 : 0));
            }

            Combined(sb, csv, baseCam, cfg);

            sb.AppendLine();
            sb.AppendLine("'필요 거리'는 짐이 다 들어오는 최소 카메라 거리, '→줌/단위'는 그걸 " +
                          "cameraZoomPerUnit으로 옮긴 값이다 (지금 " + cfg.cameraZoomPerUnit + ").");
            sb.AppendLine("'필요 시선'은 거리를 안 늘리고 시선만 뒤로 옮겼을 때의 거리(m)다. " +
                          "'플레이어'가 '밀려남'이면 그 값에서는 플레이어가 화면 밖이다.");
            sb.AppendLine("어느 쪽도 확정이 아니다 [제안·미승인]. 표: Tools/balance/rear_view.csv");

            Directory.CreateDirectory("Tools/balance");
            File.WriteAllText("Tools/balance/rear_view.csv", csv.ToString(), new UTF8Encoding(true));
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// 시선 비율을 쓸어 본다 [2026-10-08]. 비율이 크면 거리를 적게 물러나도 짐이 들어오지만
        /// 플레이어가 화면 위로 밀린다. 둘을 동시에 만족하는 구간이 있는지, 있다면 어디인지만 적는다.
        ///
        /// 판정은 <b>열 경우(두 모형 × 다섯 종류) 전부</b>를 걸어야 통과다 — 한 종류만 되는 값은
        /// 게임에서 못 쓴다.
        /// </summary>
        static void Combined(StringBuilder sb, StringBuilder csv, Cam baseCam, GameConfig cfg)
        {
            sb.AppendLine();
            sb.AppendLine("[시선 비율 쓸기] 열 경우 전부에서 짐이 다 보이고 플레이어도 남는가");
            sb.AppendLine(Row("시선비율", "최소 보임", "플레이어 밀림", "최대 거리", "", "", "", ""));
            csv.AppendLine("# [시선 비율 쓸기] fitPerMeter " + PropFit + ", zoomMax " + PropZoomMax);
            csv.AppendLine("share,worst_visible,player_lost_cases,max_distance_m");

            foreach (float share in new[] { 0.15f, 0.25f, 0.35f, 0.45f, 0.55f, 0.65f })
            {
                float worst = 1f, maxD = 0f;
                int lost = 0;
                foreach (CargoTow.HaulShape shape in new[] { CargoTow.HaulShape.Slots,
                                                             CargoTow.HaulShape.Pocket })
                foreach (var cs in PocketCompare.Cases)
                {
                    PocketCompare.Drive(shape, cs, out _, out _);
                    var load = new List<(float side, float back, float r)>(PocketCompare.LastLoad);
                    if (load.Count == 0) continue;

                    float back = 0f;
                    foreach (var q in load) if (q.back + q.r > back) back = q.back + q.r;

                    Cam c = baseCam;
                    c.Bias = Mathf.Min(back * share, PropBackMax);
                    c.Distance = cfg.cameraDistance + Mathf.Min(PropZoomMax,
                        Mathf.Max(0f, PropFit * (back - c.Bias) - cfg.cameraDistance));

                    float seen = VisibleShare(c, load);
                    if (seen < worst) worst = seen;
                    if (!PlayerVisible(c)) lost++;
                    if (c.Distance > maxD) maxD = c.Distance;
                }

                sb.AppendLine(Row(F(share), $"{worst:P0}", lost == 0 ? "없음" : lost + "경우",
                                  F(maxD) + "m", "", "", "", ""));
                csv.AppendLine(string.Join(",", F(share), F(worst), lost, F(maxD)));
            }
        }

        /// <summary>
        /// 손잡이 하나를 키워 가며 짐이 다 들어오는 최솟값을 찾는다. 못 찾으면 −1.
        ///
        /// <b>거리는 이분 탐색, 시선은 훑기</b> [고침 2026-10-08]. 거리는 멀수록 더 보이므로
        /// 단조라 이분으로 되지만, <b>시선은 단조가 아니다</b> — 너무 뒤로 보내면 짐이 반대쪽으로
        /// 빠진다. 처음에 둘 다 이분으로 풀었다가 "시선으로는 아예 안 된다(−1)"는 틀린 답을
        /// 받았다. 양 끝만 찍어 보고 가운데를 건너뛴 것이다.
        /// </summary>
        static float Solve(Cam c, List<(float side, float back, float r)> load, bool byDistance)
        {
            if (!byDistance)
            {
                for (float b = 0f; b <= 30f; b += 0.1f)
                    if (VisibleShare(Bias(c, b), load) >= 1f) return b;
                return -1f;
            }

            float lo = c.Distance, hi = 60f;
            if (VisibleShare(Dist(c, lo), load) >= 1f) return lo;
            if (VisibleShare(Dist(c, hi), load) < 1f) return -1f;
            for (int i = 0; i < 40; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (VisibleShare(Dist(c, mid), load) >= 1f) hi = mid; else lo = mid;
            }
            return hi;
        }

        static Cam Dist(Cam c, float d) { c.Distance = d; return c; }
        static Cam Bias(Cam c, float b) { c.Bias = b; return c; }

        /// <summary>진행 방향 36가지 중 <b>가장 나쁜</b> 각도에서 짐의 몇 할이 화면에 드는가.</summary>
        static float VisibleShare(Cam c, List<(float side, float back, float r)> load)
        {
            float worst = 1f;
            for (int h = 0; h < 36; h++)
            {
                Vector3 fwd = Quaternion.Euler(0f, h * 10f, 0f) * Vector3.forward;
                Vector3 back = -fwd;
                Vector3 side = new Vector3(back.z, 0f, -back.x);

                int seen = 0;
                for (int i = 0; i < load.Count; i++)
                {
                    Vector3 w = side * load[i].side + back * load[i].back;
                    if (Inside(c, fwd, w, load[i].r)) seen++;
                }
                float share = seen / (float)load.Count;
                if (share < worst) worst = share;
            }
            return worst;
        }

        static bool PlayerVisible(Cam c)
        {
            for (int h = 0; h < 36; h++)
            {
                Vector3 fwd = Quaternion.Euler(0f, h * 10f, 0f) * Vector3.forward;
                if (!Inside(c, fwd, Vector3.zero, 1f)) return false;   // 로봇 반경 ~1m
            }
            return true;
        }

        /// <summary>
        /// 플레이어를 원점에 둔 국소 좌표의 점 하나가 화면 안인가.
        /// FollowCamera가 쓰는 식을 그대로 쓴다 — 시선은 <paramref name="c"/>.Bias만큼 뒤로 민다.
        /// </summary>
        static bool Inside(Cam c, Vector3 playerFwd, Vector3 p, float radius)
        {
            Quaternion rot = Quaternion.Euler(c.Pitch, c.Yaw, 0f);
            Vector3 focus = -playerFwd * c.Bias;
            Vector3 camPos = focus - rot * Vector3.forward * c.Distance;

            Vector3 v = Quaternion.Inverse(rot) * (p - camPos);
            if (v.z <= 0.05f) return false;

            float halfH = Mathf.Tan(c.Fov * 0.5f * Mathf.Deg2Rad) * v.z * Margin;
            float halfW = halfH * c.Aspect;
            return Mathf.Abs(v.x) + radius <= halfW && Mathf.Abs(v.y) + radius <= halfH;
        }

        static GameConfig Load()
        {
            string[] g = AssetDatabase.FindAssets("t:GameConfig");
            return g.Length > 0 ? AssetDatabase.LoadAssetAtPath<GameConfig>(
                AssetDatabase.GUIDToAssetPath(g[0])) : null;
        }

        static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);

        static string Row(string a, string b, string c, string d, string e, string f, string g, string h) =>
            string.Format("  {0,-18}{1,-6}{2,10}{3,11}{4,11}{5,11}{6,11}{7,10}", a, b, c, d, e, f, g, h);
    }
}
