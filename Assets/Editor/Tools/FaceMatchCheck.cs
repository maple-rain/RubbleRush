using System.Collections.Generic;
using System.Text;
using Demo.Data;
using Demo.Destruction;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 타격 화면이 <b>필드에서 보고 있던 벽</b>을 보여주는가 [지시 2026-10-12].
    ///
    /// <b>왜 면 번호로는 모자란가.</b> 기획자가 겪은 증상은 "측면이 나온다"가 아니라
    /// <b>"다른 그림이 나온다"</b>였다. 좌우 두 면은 같은 <c>_side</c> 한 장을 쓰므로,
    /// 면 번호가 달라도 그림은 같을 수 있고 그때는 아무 문제가 없다.
    /// 그래서 이 도구는 면이 아니라 <b>그림(Texture2D)이 같은가</b>를 센다.
    ///
    /// <b>왜 카메라를 봐야 하는가.</b> 필드 카메라는 수평각이 고정이라
    /// (<see cref="Core.FollowCamera.FieldYaw"/>) 한 건물에서 화면에 나오는 벽이 늘 두 면뿐이다.
    /// 나머지 두 면은 플레이어가 한 번도 본 적이 없다 — 그 벽이 나오면 "그림이 바뀐" 것이다.
    ///
    /// <b>플레이 모드가 필요 없다.</b> 구역을 짓지 않고 <see cref="ZoneData"/>의 배치를 읽는다.
    ///
    /// <b>안 재는 것</b>: 실제 화면 픽셀 · 다른 건물에 가려지는 경우 · 전환 0.35초 동안의 그림.
    /// 픽셀까지 맞춰 보려면 플레이 모드에서 두 화면을 찍어 대조해야 한다.
    /// </summary>
    public static class FaceMatchCheck
    {
        const int Directions = 24;
        const float Standoff = 1.2f;

        /// <summary>면이 화면에 나온다고 볼 최소 내적. 0에 가까우면 모서리와 나란한 면이다.</summary>
        const float SeenDot = 0.01f;

        [MenuItem("Demo/27. 타격 화면이 보고 있던 벽을 보여주는가", priority = 127)]
        public static void Run() => Debug.Log(Report());

        public static string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("타격 화면이 필드에서 보고 있던 벽을 보여주는가");
            sb.AppendLine();

            BackdropView view = BackdropView.Measure();
            if (!view.Valid)
                return sb.Append("  카메라를 못 쟀다 — Main.unity를 열고 다시 누를 것.").ToString();

            Vector3 look = Quaternion.Euler(0f, view.Yaw, 0f) * Vector3.forward;
            look.y = 0f;
            look.Normalize();
            sb.AppendLine("  필드 카메라 수평각 " + view.Yaw.ToString("0") + "도 (고정) · 보는 방향 " + look.ToString("0.00"));
            sb.AppendLine("  거리 " + view.Distance.ToString("0.0") + "m · 내려다보기 " + view.Pitch.ToString("0") + "도 · 시야 " + view.Fov.ToString("0") + "도");
            sb.AppendLine();

            int total = 0, wrongImage = 0, notDominant = 0, noFacade = 0;
            var worst = new List<string>();
            var yawTally = new Dictionary<string, int>();

            foreach (string guid in AssetDatabase.FindAssets("t:ZoneData"))
            {
                var zone = AssetDatabase.LoadAssetAtPath<ZoneData>(AssetDatabase.GUIDToAssetPath(guid));
                if (zone == null || zone.structures == null) continue;

                foreach (ZoneData.Placement p in zone.structures)
                {
                    if (p == null || p.data == null) continue;
                    if (p.data.facadeFront == null) { noFacade++; continue; }

                    var probe = new GameObject("FaceMatchProbe").transform;
                    probe.SetPositionAndRotation(p.position, Quaternion.Euler(0f, p.yaw, 0f));

                    string vis = VisibleFaces(probe, look);
                    string key = "yaw " + Mathf.RoundToInt(p.yaw) + " -> 화면에 나오는 면: " + vis;
                    yawTally[key] = yawTally.ContainsKey(key) ? yawTally[key] + 1 : 1;

                    for (int k = 0; k < Directions; k++)
                    {
                        if (!Sample(p, probe, look, view, k, out bool imageOk, out bool dominant, out string note))
                            continue;

                        total++;
                        if (!imageOk)
                        {
                            wrongImage++;
                            if (worst.Count < 6) worst.Add("      " + note);
                        }
                        if (!dominant) notDominant++;
                    }

                    Object.DestroyImmediate(probe.gameObject);
                }
            }

            foreach (KeyValuePair<string, int> kv in yawTally)
                sb.AppendLine("  " + kv.Value + "채  " + kv.Key);
            sb.AppendLine();

            sb.AppendLine("  접근 " + total + "가지 (건물 x " + Directions + "방향)");
            sb.AppendLine(string.Format("    보고 있던 그림이 아닌 것이 나옴 : {0} ({1:0.0}%)", wrongImage, Pct(wrongImage, total)));
            sb.AppendLine(string.Format("    화면에서 더 크던 벽이 아닌 것   : {0} ({1:0.0}%)", notDominant, Pct(notDominant, total)));
            foreach (string w in worst) sb.AppendLine(w);
            if (noFacade > 0) sb.AppendLine("    (그림이 없는 종류 " + noFacade + "채는 세지 않았다)");

            sb.AppendLine();
            sb.Append(Orientation());

            sb.AppendLine();
            sb.AppendLine(wrongImage == 0
                ? "  통과 — 어느 쪽에서 다가가 눌러도 필드에서 보고 있던 그림이 나온다."
                : "  실패 — 위 경우에서 본 적 없는 그림이 나온다.");
            sb.AppendLine("  '더 크던 벽이 아닌 것'은 결함이 아니다 — 모서리에서는 두 벽이 같이 보이고,");
            sb.AppendLine("  그중 어느 쪽을 부술지는 플레이어가 선 자리가 정한다.");
            sb.AppendLine("  안 재는 것: 실제 화면 픽셀 · 다른 건물에 가려지는 경우 · 전환 0.35초 동안");
            return sb.ToString();
        }

        static float Pct(int n, int d) => d > 0 ? 100f * n / d : 0f;

        /// <summary>
        /// 필드의 벽과 조각 판이 그림을 <b>같은 방향으로</b> 얹는가 [고침 2026-10-12].
        ///
        /// <b>어느 그림인가만 재면 못 잡는다.</b> 유니티 기본 <c>Cube</c>는 -Z 면만 UV가
        /// 180도 돌아 있어서, 같은 그림이 필드에서는 거꾸로·좌우반전으로 붙고 조각 판에서는
        /// 똑바로 붙었다 — 플레이어에게는 <b>전혀 다른 그림</b>이다.
        /// 후면은 모든 건물에서 늘 보이는 면이라 이것이 계속 걸렸다.
        /// </summary>
        static string Orientation()
        {
            var sb = new StringBuilder();
            sb.AppendLine("  그림이 얹히는 방향 (필드의 벽 vs 조각 판)");

            var done = new HashSet<Mesh>();
            int bad = 0, seen = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:StructureData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<StructureData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null || data.facadeAtlas == null || data.meshIntact == null) continue;

                var mf = data.meshIntact.GetComponentInChildren<MeshFilter>(true);
                if (mf == null || mf.sharedMesh == null || !done.Add(mf.sharedMesh)) continue;

                Mesh folded = FaceSkin.Atlassed(mf.sharedMesh);
                if (folded == null) { sb.AppendLine("    " + mf.sharedMesh.name + " — UV를 못 접었다"); continue; }

                Vector3[] v = mf.sharedMesh.vertices;
                Vector3[] n = mf.sharedMesh.normals;
                Vector2[] uv = folded.uv;

                for (int f = 0; f < StructureFace.Count; f++)
                {
                    seen++;
                    if (!Axis(v, n, uv, f, out Vector3 du, out Vector3 dv)) continue;

                    Vector3 wantU = PlateRight(f);
                    bool okU = Vector3.Dot(du, wantU) > 0.9f;
                    bool okV = dv.y > 0.9f;
                    if (okU && okV) continue;

                    bad++;
                    sb.AppendLine(string.Format("    {0,-10} u+ {1} (판은 {2}) · v+ {3}  <- 어긋남",
                                                StructureFace.Name(f), du.ToString("0.0"),
                                                wantU.ToString("0.0"), dv.ToString("0.0")));
                }
            }

            sb.AppendLine(bad == 0
                ? "    네 면 모두 판과 같은 방향이다 (" + seen + "면 확인)"
                : "    " + bad + " / " + seen + "면이 어긋난다 — 같은 그림이 다른 그림으로 보인다");
            return sb.ToString();
        }

        /// <summary>그 면에서 u와 v가 커지는 방향(메시 로컬). 못 재면 false.</summary>
        static bool Axis(Vector3[] v, Vector3[] n, Vector2[] uv, int face, out Vector3 du, out Vector3 dv)
        {
            du = dv = Vector3.zero;
            Vector3 outward = PlateOut(face);

            int uLo = -1, uHi = -1, vLo = -1, vHi = -1;
            for (int i = 0; i < v.Length; i++)
            {
                if (Vector3.Dot(n[i], outward) < 0.9f) continue;
                if (uLo < 0 || uv[i].x < uv[uLo].x) uLo = i;
                if (uHi < 0 || uv[i].x > uv[uHi].x) uHi = i;
                if (vLo < 0 || uv[i].y < uv[vLo].y) vLo = i;
                if (vHi < 0 || uv[i].y > uv[vHi].y) vHi = i;
            }
            if (uLo < 0 || uLo == uHi || vLo == vHi) return false;

            du = v[uHi] - v[uLo]; du.y = 0f;
            dv = v[vHi] - v[vLo];
            if (du.sqrMagnitude < 1e-6f || dv.sqrMagnitude < 1e-6f) return false;
            du.Normalize(); dv.Normalize();
            return true;
        }

        static Vector3 PlateOut(int face)
        {
            switch (face)
            {
                case StructureFace.Front: return Vector3.forward;
                case StructureFace.Right: return Vector3.right;
                case StructureFace.Back:  return Vector3.back;
                default:                  return Vector3.left;
            }
        }

        /// <summary>조각 판이 쓰는 '바깥에서 봤을 때 오른쪽'. FractureProxy와 같은 식이다.</summary>
        static Vector3 PlateRight(int face) =>
            Quaternion.LookRotation(-PlateOut(face), Vector3.up) * Vector3.right;

        static string VisibleFaces(Transform t, Vector3 look)
        {
            var sb = new StringBuilder();
            for (int f = 0; f < StructureFace.Count; f++)
                if (Seen(t, f, look)) sb.Append(StructureFace.Name(f)).Append(' ');
            return sb.ToString();
        }

        static bool Seen(Transform t, int face, Vector3 look)
        {
            Vector3 n = StructureFace.Normal(t, face);
            n.y = 0f;
            return Vector3.Dot(n.normalized, -look) > SeenDot;
        }

        /// <summary><paramref name="k"/>번째 방향에서 다가가 눌렀을 때를 한 번 본다.</summary>
        static bool Sample(ZoneData.Placement p, Transform t, Vector3 look, BackdropView view, int k,
                           out bool imageOk, out bool dominant, out string note)
        {
            imageOk = true; dominant = true; note = null;

            Vector3 size = p.data.colliderSize;
            float a = k * Mathf.PI * 2f / Directions;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));

            float reach = Mathf.Max(Mathf.Abs(dir.x) * size.x, Mathf.Abs(dir.z) * size.z) * 0.5f + Standoff;
            Vector3 stand = p.position + dir * reach;

            int chosen = StructureFace.Facing(t, stand, size, look);
            Texture2D shown = p.data.Facade(chosen);

            // 화면에 나오던 그림들. 좌우가 같은 _side 한 장을 쓰므로 면이 아니라 그림으로 센다.
            int best = -1;
            float bestArea = -1f;
            bool found = false;
            for (int f = 0; f < StructureFace.Count; f++)
            {
                if (!Seen(t, f, look)) continue;
                if (p.data.Facade(f) == shown) found = true;

                float area = FaceArea(t, size, f, stand, view);
                if (area > bestArea) { bestArea = area; best = f; }
            }

            imageOk = found;
            dominant = chosen == best || (best >= 0 && p.data.Facade(best) == shown);

            if (!imageOk)
                note = p.data.id + " yaw " + Mathf.RoundToInt(p.yaw) + " · " + (k * 360 / Directions)
                     + "도에서 접근 -> " + StructureFace.Name(chosen) + " ("
                     + (shown != null ? shown.name : "그림 없음") + ") — 화면에 없던 것";
            return true;
        }

        /// <summary>그 면이 화면에서 차지하는 넓이. 카메라는 플레이어가 선 자리에서 유도한다.</summary>
        static float FaceArea(Transform t, Vector3 size, int face, Vector3 stand, BackdropView view)
        {
            Quaternion rot = Quaternion.Euler(view.Pitch, view.Yaw, 0f);
            Vector3 cam = stand - rot * Vector3.forward * view.Distance;

            Vector3 n = StructureFace.Normal(t, face);
            Vector3 right = Vector3.Cross(Vector3.up, n).normalized;
            float half = StructureFace.Width(size, face) * 0.5f;
            Vector3 mid = t.position + n * StructureFace.Offset(size, face);

            var corner = new Vector3[4];
            corner[0] = mid - right * half;
            corner[1] = mid + right * half;
            corner[2] = mid + right * half + Vector3.up * size.y;
            corner[3] = mid - right * half + Vector3.up * size.y;

            var sp = new Vector2[4];
            float tanHalf = Mathf.Tan(view.Fov * 0.5f * Mathf.Deg2Rad);
            for (int i = 0; i < 4; i++)
            {
                Vector3 v = Quaternion.Inverse(rot) * (corner[i] - cam);
                if (v.z <= 0.01f) return 0f;
                sp[i] = new Vector2(v.x / (v.z * tanHalf), v.y / (v.z * tanHalf));
            }

            // 신발끈 공식. 부호는 보는 방향이 정하므로 절댓값을 쓴다.
            float area = 0f;
            for (int i = 0; i < 4; i++)
            {
                Vector2 c = sp[i], d = sp[(i + 1) % 4];
                area += c.x * d.y - d.x * c.y;
            }
            return Mathf.Abs(area) * 0.5f;
        }
    }
}
