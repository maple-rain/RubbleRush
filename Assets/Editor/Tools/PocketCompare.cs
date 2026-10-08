using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Demo.Collection;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 두 견인 모형을 같은 경로·같은 dt로 돌려 비교한다 [지시 2026-10-08].
    ///
    /// <b>Slots</b>: 잔해 자리를 해바라기 나선으로 계산해서 꽂는다 (기존).
    /// <b>Pocket</b>: 그물이 경계를 만들고 잔해가 그 안에서 밀린다 (투망).
    ///
    /// <b>무엇을 재는가.</b> 기획 의도가 주장하는 것들을 그대로 숫자로 옮긴다 —
    /// ① 끌면 타원이 되는가(가로세로비) ② 끝쪽에 모이는가(무게중심 치우침)
    /// ③ 케이블과 잔해가 부딪히는가(접촉 수) ④ 짐이 그물 밖으로 새는가
    /// ⑤ 잔해끼리 겹치는가 ⑥ 개수가 늘면 짐이 화면 밖으로 나가는가(최대 반경) ⑦ 비용.
    ///
    /// <b>실제로 게임에서 도는 코드를 돌린다.</b> 측정용으로 배치 규칙을 베껴 쓰지 않는다 —
    /// 그렇게 하면 "시뮬레이션 결과를 게임 이야기인 것처럼" 보고하게 된다 [2026-10-02에 한 실수].
    /// 진짜 <see cref="CargoTow"/>에 진짜 <see cref="Debris"/>를 달고 <see cref="CargoTow.Step"/>을
    /// 고정 dt로 부른다. 플레이 모드가 아니라서 프레임 타이밍에 흔들리지 않고 재현된다.
    ///
    /// <b>이 도구가 못 재는 것</b>: 재미·읽히는가·손맛. 모양이 "투망 같은가"는 사람이 봐야 한다.
    /// 그리고 여기에는 건물·토대가 없다 — 걸림(14번)은 일부러 뺐다. 섞으면 어느 쪽 효과인지 못 가른다.
    /// </summary>
    public static partial class PocketCompare
    {
        /// <summary>
        /// 튜닝 도구가 쓸어 보려고 끼우는 값. null이면 인스펙터 기본값을 쓴다 [2026-10-08].
        /// 수렴하는지는 재 봐야 알지 추론할 일이 아니다.
        /// </summary>
        internal static (int iters, float give, float drag, float bounce, float step)? Override;

        /// <summary>
        /// 켜면 직진 중 한 순간의 <b>기하</b>를 그대로 받아 적는다 — 테두리 점과 조각의 좌표다.
        /// 숫자만으로는 "타원"이 와닿지 않는다. 그림은 이 좌표로 그리지, 따로 지어내지 않는다.
        /// </summary>
        internal static StringBuilder Snapshot;

        /// <summary>
        /// 스냅 순간의 짐을 <b>플레이어 국소 좌표</b>로 남긴다 (옆, 뒤, 반경).
        /// 카메라가 이걸 다 담는지는 <see cref="RearViewFit"/>가 묻는다 — 좌표를 또 지어내지 않으려고 돌려 쓴다.
        /// </summary>
        internal static readonly List<(float side, float back, float r)> LastLoad = new List<(float, float, float)>(64);
        const float SnapAt = 5.5f;   // 직진이 자리를 잡은 뒤, 회전 전

        const float Dt = 1f / 60f;
        const float Speed = 6f;          // moveSpeedBase와 같은 자릿수. 경로 속도
        const float SlotSpacing = 0.9f;  // CargoTow의 출하값

        /// <summary>
        /// 실제 건물 다섯 종류 그대로다 (structures.csv). 꾸며낸 값을 쓰지 않는다.
        ///
        /// <b>왜 중요한가.</b> 처음엔 전부 반경 0.45로 돌렸는데, 그 값은 slotSpacing 0.9의
        /// 정확히 절반이라 <b>Slots가 자동으로 겹침 0</b>이 된다 — 비교가 한쪽에 유리하게 기운다.
        /// 진짜 반경은 0.16~1.17로 네 배 넘게 벌어지고, 큰 쪽은 슬롯 간격보다 굵다.
        /// </summary>
        internal static readonly (string name, int count, float radius, float scatter)[] Cases =
        {
            ("t1_weak",   2, 0.16f, 1.2f),
            ("t2_light",  4, 0.32f, 1.8f),
            ("t3_normal", 12, 0.50f, 3.5f),
            ("t4_heavy",  20, 0.77f, 4.2f),
            ("t5_solid",  32, 1.17f, 8.0f),
        };

        internal sealed class Sample
        {
            public float T;
            public float Along, Across;  // 잔해 덩어리의 앞뒤 / 좌우 폭
            public float NetAlong, NetAcross;
            public float MeanSpeed, MaxSpeed;   // 튕김을 재는 자. 플레이어는 6m/s다
            public float MoveInertia, MoveContain, MoveSeparate;   // 그 이동이 어디서 왔는가
            public float RearShare;     // 뒤쪽 절반에 든 조각의 비율. 0.5면 고르게, 1이면 다 뒤에  // <b>그물</b>의 앞뒤 / 좌우 폭 — 타원은 그물의 것이다
            public float Bias;           // 무게중심이 뒤로 치우친 거리(m). +면 뒤쪽
            public float MaxR;           // 매듭에서 가장 먼 조각까지
            public float NearHand;       // 손에서 가장 가까운 조각까지
            public int Escaped, Overlaps, Contacts;
        }

        [MenuItem("Demo/15. 투망 주머니 비교", priority = 67)]
        public static void Run()
        {
            var sb = new StringBuilder();
            var csv = new StringBuilder();
            csv.AppendLine("# 투망 주머니 vs 해바라기 슬롯 — 같은 경로·같은 dt로 실측 [2026-10-08]");
            csv.AppendLine("# 경로: 6초 직진 → 90도 좌회전 → 4초 직진. dt=1/60. 걸림(14번) 없음");
            csv.AppendLine("shape,type,phase,net_along_m,net_across_m,net_aspect,rear_share,maxR_m,nearHand_m,mean_speed,max_speed,overlaps,ms_per_frame");

            Snapshot = new StringBuilder();
            sb.AppendLine("투망 주머니 비교 — 같은 경로를 두 모형으로 끈다 [2026-10-08]");
            WarnIfSceneDiffers(sb);
            sb.AppendLine();

            foreach (var cs in Cases)
            {
                int n = cs.count;
                sb.AppendLine($"── {cs.name} — 잔해 {n}개 · 반경 {cs.radius:F2}m · 흩어짐 {cs.scatter:F1}m ──");
                sb.AppendLine(Row("모형", "국면", "그물앞뒤", "그물좌우", "그물비율", "치우침",
                                  "뒤쪽", "손까지", "평균속", "최고속", "겹침", "ms"));
                foreach (CargoTow.HaulShape shape in new[] { CargoTow.HaulShape.Slots,
                                                             CargoTow.HaulShape.Pocket })
                {
                    Drive(shape, cs, out List<Sample> log, out double ms);
                    Report(sb, csv, shape, cs.name, log, ms);
                }
                sb.AppendLine();
            }

            var geo = new StringBuilder();
            geo.AppendLine("# 직진 5.5초 시점의 좌표. 플레이어 원점, +y가 끄는 반대쪽(= 짐 쪽) [2026-10-08]");
            geo.AppendLine("shape,type,kind,x,y,radius");
            geo.Append(Snapshot);
            File.WriteAllText("Tools/balance/pocket_shape.csv", geo.ToString(), new UTF8Encoding(true));
            Snapshot = null;

            string path = "Tools/balance/pocket_compare.csv";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
            sb.AppendLine($"표: {path}");
            Debug.Log(sb.ToString());
        }

        /// <summary>한 모형을 끝까지 끌고, 매 프레임 재서 돌려준다.</summary>
        internal static void Drive(CargoTow.HaulShape shape,
                                   (string name, int count, float radius, float scatter) cs,
                                   out List<Sample> log, out double msPerFrame)
        {
            int n = cs.count;
            log = new List<Sample>(700);

            // <b>전역 설정을 실제 경로로 세운다</b> [고침 2026-10-08].
            //
            // GameConfigRef.Current는 Awake에서 채워지는 static이라 에디트 모드에서는 null이다.
            // 그대로 두면 CargoTow가 케이블 길이 상한을 <b>조용히 건너뛴다</b> — 측정이 게임과
            // 다른 코드를 재게 된다. 실제로 한 번 그렇게 재서 틀린 수를 받았다.
            //
            // 비활성으로 만들어 필드를 물린 뒤 SetActive로 깨우려 했지만 <b>안 된다</b> —
            // 에디트 모드에서 Awake는 AddComponent 때만 돌고 활성화로는 안 돈다
            // ([ExecuteAlways]가 아니다). 그래서 필드를 물린 뒤 Awake 본문을 직접 부른다.
            // 베껴 쓴 것이 아니라 게임이 도는 그 메서드다.
            var boot = new GameObject("~PocketConfig");
            boot.SetActive(false);
            var reference = boot.AddComponent<Core.GameConfigRef>();
            var bso = new SerializedObject(reference);
            bso.FindProperty("config").objectReferenceValue = FindConfig();
            bso.ApplyModifiedPropertiesWithoutUndo();
            typeof(Core.GameConfigRef)
                .GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(reference, null);

            var player = new GameObject("~PocketCompare");
            var tow = player.AddComponent<CargoTow>();
            Configure(tow, shape);

            var debris = new List<Debris>(n);
            for (int i = 0; i < n; i++)
            {
                var g = new GameObject("~d" + i);
                var d = g.AddComponent<Debris>();
                d.Init(1, cs.radius);
                // 포획 직후의 모습 — 부순 자리에 그 종류의 흩어짐 반경만큼 깔린다
                // (structures.csv의 debrisScatterRadius). 두 모형 모두 같은 씨앗을 쓴다.
                float a = i * 2.39996323f;
                float r = cs.scatter * Mathf.Sqrt((i + 0.5f) / n);
                g.transform.position = new Vector3(Mathf.Cos(a) * r, 0f, -2.5f + Mathf.Sin(a) * r);
                debris.Add(d);
            }

            player.transform.position = Vector3.zero;
            player.transform.rotation = Quaternion.identity;
            tow.Add(debris);

            ResetSpeeds();   // 앞 경우의 위치가 남아 있으면 첫 프레임 속도가 터진다
            var sw = new System.Diagnostics.Stopwatch();
            int frames = 0;
            float t = 0f;
            const float Straight = 6f, Turn = 1.2f, Tail = 4f;

            while (t < Straight + Turn + Tail)
            {
                // 경로: 직진 → 1.2초에 걸쳐 90도 좌회전 → 직진
                float yaw = t <= Straight ? 0f
                          : t <= Straight + Turn ? -90f * ((t - Straight) / Turn)
                          : -90f;
                player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                player.transform.position += player.transform.forward * (Speed * Dt);

                sw.Start();
                tow.Step(Dt);
                sw.Stop();
                frames++;

                log.Add(Measure(tow, player.transform, t));
                if (t <= SnapAt && t + Dt > SnapAt) Snap(tow, player.transform, shape, cs.name);
                t += Dt;
            }

            msPerFrame = sw.Elapsed.TotalMilliseconds / Mathf.Max(1, frames);

            for (int i = 0; i < debris.Count; i++) Object.DestroyImmediate(debris[i].gameObject);
            Object.DestroyImmediate(player);
            // OnDestroy도 같은 이유로 안 돌므로 static을 손으로 되돌린다 —
            // 안 그러면 파괴된 객체의 설정이 다음 측정까지 살아남는다 (좀비 콜백과 같은 함정이다)
            typeof(Core.GameConfigRef)
                .GetMethod("OnDestroy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(reference, null);
            Object.DestroyImmediate(boot);
        }

        /// <summary>그 순간의 좌표를 그대로 적는다. 플레이어 기준 국소 좌표라 그림에서 겹쳐 볼 수 있다.</summary>
        static void Snap(CargoTow tow, Transform player, CargoTow.HaulShape shape, string type)
        {
            Vector3 dir = -player.forward; dir.y = 0f; dir.Normalize();
            Vector3 side = new Vector3(dir.z, 0f, -dir.x);
            Vector3 o = player.position;

            void Put(string kind, Vector3 w, float r)
            {
                if (Snapshot == null) return;
                Vector3 d = w - o; d.y = 0f;
                Snapshot.AppendLine(string.Join(",", shape, type, kind,
                    F(Vector3.Dot(d, side)), F(Vector3.Dot(d, dir)), F(r)));
            }

            LastLoad.Clear();
            Put("hand", player.position, 0f);
            if (tow.Net.Ready)
                for (int i = 0; i < tow.Net.Count; i++) Put("ring", tow.Net.At(i), 0f);
            for (int i = 0; i < tow.Towed.Count; i++)
            {
                Vector3 w = tow.Towed[i].transform.position;
                Put("debris", w, tow.Towed[i].SizeRadius);
                Vector3 d = w - o; d.y = 0f;
                LastLoad.Add((Vector3.Dot(d, side), Vector3.Dot(d, dir), tow.Towed[i].SizeRadius));
            }
        }

        /// <summary>인스펙터 값에 기대지 않는다 — 두 모형이 같은 설정에서 출발해야 한다.</summary>
        static void Configure(CargoTow tow, CargoTow.HaulShape shape)
        {
            var so = new SerializedObject(tow);
            so.FindProperty("mode").enumValueIndex = (int)CargoTow.PhysicsMode.Both;
            so.FindProperty("shape").enumValueIndex = (int)shape;
            so.FindProperty("trailDistance").floatValue = 2.5f;
            so.FindProperty("slotSpacing").floatValue = SlotSpacing;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (Override.HasValue)
            {
                tow.Pocket.Iterations = Override.Value.iters;
                tow.Pocket.RopeGive = Override.Value.give;
                tow.Pocket.GroundDrag = Override.Value.drag;
                tow.Pocket.Bounce = Override.Value.bounce;
                tow.Pocket.MaxStep = Override.Value.step;
            }
        }

        /// <summary>국면별로 접는다 — 직진 끝·회전 중·회전 뒤. 한 국면 안에서는 평균을 쓴다.</summary>
        static void Report(StringBuilder sb, StringBuilder csv, CargoTow.HaulShape shape, string n,
                           List<Sample> log, double ms)
        {
            var phases = new (string name, float a, float b)[]
            {
                ("직진", 4.0f, 6.0f), ("회전중", 6.0f, 7.2f), ("회전후", 9.0f, 11.2f),
            };

            foreach (var ph in phases)
            {
                float along = 0, across = 0, rear = 0, maxR = 0, near = 0;
                float mean = 0, top = 0, ovl = 0;
                int c = 0;
                foreach (Sample s in log)
                {
                    if (s.T < ph.a || s.T > ph.b) continue;
                    along += s.NetAlong; across += s.NetAcross; rear += s.RearShare;
                    maxR += s.MaxR; near += s.NearHand;
                    mean += s.MeanSpeed; top += s.MaxSpeed; ovl += s.Overlaps;
                    c++;
                }
                if (c == 0) continue;
                float inv = 1f / c;
                along *= inv; across *= inv; rear *= inv; maxR *= inv; near *= inv;
                mean *= inv; top *= inv; ovl *= inv;
                float aspect = across > 1e-3f ? along / across : 0f;

                sb.AppendLine(Row(shape.ToString(), ph.name, F(along), F(across), F(aspect),
                                  F(rear), F(maxR), F(near), F(mean), F(top), F(ovl), ms.ToString("F4")));
                csv.AppendLine(string.Join(",", shape, n, ph.name, F(along), F(across), F(aspect),
                                           F(rear), F(maxR), F(near), F(mean), F(top), F(ovl),
                                           ms.ToString("F4", CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>
        /// <b>씬이 실제로 무엇을 쓰고 있는지 먼저 말한다</b> [2026-10-08].
        ///
        /// 이 도구는 CargoTow를 <b>새로 만들어서</b> 돌린다 — 그래야 두 모형을 같은 조건으로
        /// 비교할 수 있다. 그런데 새로 만든 컴포넌트는 C# 기본값을 쓰고, <b>게임은 씬에 박힌 값을
        /// 쓴다.</b> 둘이 갈라지면 여기서는 주머니가 멀쩡히 도는데 플레이하면 예전 그대로다.
        ///
        /// 실제로 그렇게 됐다: 필드를 Slots 기본값으로 추가한 뒤 씬이 저장되면서 0이 박혔고,
        /// 나중에 코드 기본값만 Pocket으로 바꿨다. 직렬화된 값은 안 따라온다 —
        /// 이 프로젝트에서 이미 한 번 데인 함정이다. 보고서 맨 위에서 먼저 알린다.
        /// </summary>
        static void WarnIfSceneDiffers(StringBuilder sb)
        {
            var live = Object.FindFirstObjectByType<CargoTow>(FindObjectsInactive.Include);
            if (live == null)
            {
                sb.AppendLine("[주의] 씬에 CargoTow가 없다 — 이 표는 코드 기본값만 말한다.");
                sb.AppendLine();
                return;
            }

            var so = new SerializedObject(live);
            var shape = (CargoTow.HaulShape)so.FindProperty("shape").enumValueIndex;
            var mode = (CargoTow.PhysicsMode)so.FindProperty("mode").enumValueIndex;
            sb.AppendLine($"씬({live.gameObject.name})이 지금 쓰는 값: shape={shape} · mode={mode}");

            if (shape == CargoTow.HaulShape.Pocket &&
                (mode == CargoTow.PhysicsMode.RopeOnly || mode == CargoTow.PhysicsMode.Both))
            {
                sb.AppendLine();
                return;
            }

            sb.AppendLine(shape != CargoTow.HaulShape.Pocket
                ? "[주의] 씬은 주머니가 아니다 — 아래 Pocket 줄은 게임에서 안 일어나는 일이다."
                : "[주의] 주머니인데 줄이 꺼져 있다 — 경계가 곧 그물이라 Pocket은 RopeOnly/Both여야 돈다.");
            sb.AppendLine("       인스펙터에서 [Player]의 CargoTow를 고친다. 코드 기본값은 씬을 안 바꾼다.");
            sb.AppendLine();
        }

        static Demo.Data.GameConfig FindConfig()
        {
            string[] g = AssetDatabase.FindAssets("t:GameConfig");
            return g.Length > 0 ? AssetDatabase.LoadAssetAtPath<Demo.Data.GameConfig>(
                AssetDatabase.GUIDToAssetPath(g[0])) : null;
        }

        static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);

        static string Row(string a, string b, string c, string d, string e, string f,
                          string g, string h, string i, string j, string k, string l) =>
            string.Format("  {0,-7}{1,-7}{2,7}{3,7}{4,7}{5,8}{6,7}{7,8}{8,6}{9,7}{10,7}{11,9}",
                          a, b, c, d, e, f, g, h, i, j, k, l);
    }
}
