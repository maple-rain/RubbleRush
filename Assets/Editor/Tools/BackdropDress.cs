using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 배경을 입힌다 [2026-10-08]. <see cref="BackdropCheck"/>가 짚어낸 것을 고친다.
    ///
    /// <b>왜 지면만 손대는가.</b> 재 보니 시야의 위쪽 모서리가 수평에서 20도 <b>아래</b>라
    /// 하늘도 경계 벽(60m)도 화면에 한 번도 안 들어온다 — 이 게임에서 배경은 지면 전부다.
    /// 스카이라인을 그리려던 계획을 여기서 접었다.
    ///
    /// <b>값을 내가 고르지 않는다.</b> 타일링도 안개 거리도 안개 색도 전부
    /// <see cref="BackdropView"/>(카메라)와 <b>텍스처 자신</b>에서 끌어낸다.
    /// 그래야 카메라 설정이 바뀌면 다시 눌러서 따라가게 할 수 있다 (CLAUDE.md 규칙 7).
    ///
    /// <b>되돌릴 수 있다.</b> Undo로 묶으므로 Ctrl+Z 한 번이면 전부 제자리다.
    /// </summary>
    public static class BackdropDress
    {
        const string GroundTex = "Assets/_Project/Art/Textures/T_Ground_Site.png";
        const string GroundMat = "Assets/_Project/Art/Materials/M_Ground_Site.mat";

        /// <summary>
        /// 안개가 시작되는 지점 — 시야 끝까지의 몇 할인가.
        ///
        /// <b>처음엔 0.45였다가 올렸다.</b> 그때는 안개의 일이 "시야 끝에서 지면이 잘리는 자리를
        /// 덮는 것"이었는데, <see cref="BackdropFit"/>이 지면을 벽보다 시야만큼 더 넓게 깔면서
        /// <b>덮을 잘림이 아예 없어졌다</b>. 남은 일은 깊이감뿐이라 훨씬 옅어야 한다 —
        /// 0.45로 두니 원경 지면이 하얗게 날아갔다.
        /// </summary>
        const float FogStartShare = 0.60f;

        /// <summary>안개가 가장 짙어지는 지점. 시야 끝보다 한참 뒤에 둬 화면 안에서는 절반도 안 찬다.</summary>
        const float FogEndShare = 1.60f;

        /// <summary>먼지빛을 만들 때 흰색 쪽으로 당기는 정도. 크면 원경이 날아간다.</summary>
        const float DustLift = 0.12f;

        [MenuItem("Demo/19. 배경 입히기", priority = 73)]
        public static void Run()
        {
            var view = BackdropView.Measure();
            if (!view.Valid)
            {
                Debug.LogError("[배경 입히기] 카메라를 못 쟀다. Main.unity를 열고 다시 누를 것.");
                return;
            }

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(GroundTex);
            if (tex == null)
            {
                Debug.LogError("[배경 입히기] 지면 텍스처가 없다: " + GroundTex);
                return;
            }

            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("배경 입히기");

            var log = new System.Text.StringBuilder();
            log.AppendLine("배경 입히기");
            log.AppendLine();

            // 순서가 중요하다 — 타일링은 지면의 크기에서 나오므로 크기를 먼저 맞춰야 한다.
            log.Append(BackdropFit.Run(view));
            log.AppendLine();
            Ground(log, view, tex);
            Fog(log, view, tex);

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();

            log.AppendLine();
            log.AppendLine("되돌리려면 Ctrl+Z 한 번. 확인은 'Demo/18. 배경·비율 점검'.");
            log.AppendLine("안 한 것: 실시간 그림자 끄기 — 블롭 섀도우 대체가 아직 없어서 그냥 끄면 " +
                           "물체가 떠 보인다. 성능 항목이지 배경 항목이 아니다.");
            Debug.Log(log.ToString());
        }

        /// <summary>
        /// 지면에 그림을 깐다. 타일링은 <b>카메라가 정한다</b> —
        /// 화면에서 1m가 117px이면 1024px짜리 그림은 8.7m마다 반복돼야 1:1로 또렷하다.
        /// </summary>
        static void Ground(System.Text.StringBuilder log, BackdropView view, Texture2D tex)
        {
            var go = GameObject.Find("Ground");
            if (go == null) { log.AppendLine("  Ground 없음 — 건너뜀"); return; }

            var r = go.GetComponent<Renderer>();
            if (r == null) { log.AppendLine("  Ground에 렌더러 없음 — 건너뜀"); return; }

            float span = go.transform.localScale.x;
            float repeat = tex.width / Mathf.Max(1f, view.PixelsPerMeter);   // 반복 주기(m)
            float tiling = Mathf.Max(1f, Mathf.Round(span / repeat));

            Material mat = Load(tex, tiling, span);

            Undo.RecordObject(r, "지면 머티리얼");
            string was = r.sharedMaterial != null ? r.sharedMaterial.name : "(없음)";
            r.sharedMaterial = mat;

            // 120m 큐브가 그림자를 드리운다. 드리울 곳이 없고 그림자 맵만 먹는다.
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            EditorUtility.SetDirty(r);

            float px = tex.width / (span / tiling);
            log.AppendLine("[지면]");
            log.AppendLine("  머티리얼 " + was + " -> " + mat.name);
            log.AppendLine("  타일링 " + tiling.ToString("F0") + " (" + (span / tiling).ToString("F1") + "m마다 반복)");
            log.AppendLine("  1m당 " + px.ToString("F0") + "px (목표 " + view.PixelsPerMeter.ToString("F0") + "px)");
            log.AppendLine("  화면 한 폭에 같은 타일이 " + (view.ViewWide / (span / tiling)).ToString("F1") + "번");
            log.AppendLine("  그림자 드리우기 끔 — 지면이 드리울 곳은 없다");
        }

        /// <summary>지면 머티리얼. 없으면 만들고, 있으면 값만 다시 맞춘다.</summary>
        static Material Load(Texture2D tex, float tiling, float span)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(GroundMat);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_Ground_Site" };
                Directory.CreateDirectory(Path.GetDirectoryName(GroundMat));
                AssetDatabase.CreateAsset(mat, GroundMat);
            }

            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            mat.SetColor("_BaseColor", Color.white);

            // 흙과 자갈은 반질거리지 않는다. 기본값(0.5)이면 햇빛이 번들거려 플라스틱처럼 보인다.
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// 안개를 건다. <b>거리는 시야에서, 색은 지면 그림 자체에서</b> 나온다.
        ///
        /// 안개 색이 지면과 따로 놀면 먼 지면이 회색 띠로 보여 더 가짜가 된다.
        /// 지면의 평균색을 뽑아 먼지처럼 조금 밝히고 채도를 낮춰 쓴다.
        /// </summary>
        static void Fog(System.Text.StringBuilder log, BackdropView view, Texture2D tex)
        {
            Color ground = Average(GroundTex);
            Color fog = Dusty(ground);

            // 안개는 Undo에 안 묶인다 — RenderSettings는 씬의 숨은 단일 객체라
            // 리플렉션 없이는 손잡이를 못 잡는다. 대신 <b>전 값을 로그에 남겨</b> 손으로 되돌릴 수 있게 한다.
            string before = RenderSettings.fog
                ? RenderSettings.fogMode + " " + RenderSettings.fogStartDistance.ToString("F0") + "~"
                  + RenderSettings.fogEndDistance.ToString("F0") + "m " + Hex(RenderSettings.fogColor)
                : "꺼짐";

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fog;
            RenderSettings.fogStartDistance = view.Reach * FogStartShare;
            RenderSettings.fogEndDistance = view.Reach * FogEndShare;

            log.AppendLine();
            log.AppendLine("[안개] 시야 끝에서 지면이 잘리는 자리를 덮는다");
            log.AppendLine("  " + RenderSettings.fogStartDistance.ToString("F0") + "m ~ "
                           + RenderSettings.fogEndDistance.ToString("F0") + "m (시야 끝 "
                           + view.Reach.ToString("F0") + "m 기준)");
            log.AppendLine("  색: 지면 평균 " + Hex(ground) + " -> 먼지빛 " + Hex(fog));
            log.AppendLine("  * 안개는 Undo에 안 묶인다. 전 값: " + before);
        }

        /// <summary>
        /// 그림의 평균색. <b>임포트 설정을 건드리지 않는다</b> — 원본 PNG를 직접 읽어
        /// 임시 텍스처로 풀면 <c>isReadable</c>을 켰다 끄는 왕복이 필요 없다.
        /// 16픽셀마다 하나씩만 본다. 평균을 내는 데 백만 개를 다 볼 이유가 없다.
        /// </summary>
        static Color Average(string path)
        {
            var tmp = new Texture2D(2, 2);
            if (!tmp.LoadImage(File.ReadAllBytes(path))) return new Color(0.5f, 0.48f, 0.45f);

            Color32[] px = tmp.GetPixels32();
            double r = 0, g = 0, b = 0;
            int n = 0;
            for (int i = 0; i < px.Length; i += 16, n++) { r += px[i].r; g += px[i].g; b += px[i].b; }
            Object.DestroyImmediate(tmp);

            return n == 0 ? Color.grey : new Color((float)(r / n / 255.0), (float)(g / n / 255.0), (float)(b / n / 255.0));
        }

        /// <summary>공중의 먼지는 땅보다 조금 밝고 색이 옅다. 밝기를 올리고 채도를 깎는다.</summary>
        static Color Dusty(Color c)
        {
            float grey = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            Color flat = Color.Lerp(c, new Color(grey, grey, grey), 0.5f);
            return Color.Lerp(flat, Color.white, DustLift);
        }

        static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
