using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo.EditorTools
{
    /// <summary>
    /// 활성 URP 에셋의 <b>그림자 지원을 끈다</b> [지시 2026-10-15].
    ///
    /// 기획자: "그림자 지원을 꺼서 부담을 줄이자."
    ///
    /// <b>왜 메뉴인가.</b> URP 설정은 <c>.asset</c>이고 그것은 손으로 고치지 않는다
    /// (CLAUDE.md 규칙 1). 에디터 API로 돌려야 YAML이 깨지지 않고, 메뉴로 두면
    /// 되돌리기도 같은 값어치로 남는다.
    ///
    /// <b>무엇이 줄어드는가.</b> 지원을 끄면 셰이더 변형에서 그림자 키워드가 통째로
    /// 빠진다 — 그림자 맵을 그리는 패스가 사라지고, 빛 계산에서 그림자 샘플링이
    /// 빠지고, 빌드 크기도 준다. 광원 하나하나의 <c>Cast Shadows</c>를 끄는 것과
    /// 다르다. 그쪽은 그리지만 않을 뿐 경로는 남는다.
    ///
    /// CLAUDE.md 성능 예산의 "실시간 그림자: 사용 안 함"이 이것이다. 발밑 그림자는
    /// <see cref="Demo.Utils.BlobShadow"/>가 대신한다.
    ///
    /// <b>이 메뉴가 안 건드리는 것</b>: 활성 파이프라인이 <c>PC_RPAsset</c>이라는 사실.
    /// <c>Mobile_RPAsset</c>으로 갈아타는 것은 MSAA·렌더 스케일·렌더러까지 같이 바뀌는
    /// 별개의 결정이라 여기서 끼워 넣지 않는다.
    /// </summary>
    public static class ShadowBudget
    {
        [MenuItem("Demo/19. 그림자 지원 끄기 (URP)", priority = 73)]
        public static void Off() => Apply(false);

        [MenuItem("Demo/19. 그림자 지원 되돌리기 (URP)", priority = 74)]
        public static void On() => Apply(true);

        static void Apply(bool on)
        {
            RenderPipelineAsset rp = GraphicsSettings.defaultRenderPipeline;
            if (rp == null)
            {
                Debug.LogError("[그림자] 활성 렌더 파이프라인 에셋이 없다. " +
                               "Project Settings > Graphics 를 확인할 것.");
                return;
            }

            var so = new SerializedObject(rp);
            var log = new System.Text.StringBuilder(
                "[그림자] " + AssetDatabase.GetAssetPath(rp) + "\n");

            // 필드 이름은 URP 버전에 따라 생길 수도 없을 수도 있다. 없는 것은 건너뛴다.
            Set(so, "m_MainLightShadowsSupported", on, log);
            Set(so, "m_AdditionalLightShadowsSupported", on, log);
            Set(so, "m_SoftShadowsSupported", on, log);

            // 거리는 끄면 0, 켜면 URP 기본값(50)으로 되돌린다.
            SerializedProperty dist = so.FindProperty("m_ShadowDistance");
            if (dist != null)
            {
                log.AppendLine(string.Format("  {0,-36} {1:0} -> {2:0}",
                               "m_ShadowDistance", dist.floatValue, on ? 50f : 0f));
                dist.floatValue = on ? 50f : 0f;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rp);
            AssetDatabase.SaveAssets();

            log.AppendLine(on
                ? "  되돌렸다 — 실시간 그림자가 다시 켜진다."
                : "  껐다. 셰이더에서 그림자 키워드가 빠진다. 발밑은 BlobShadow가 맡는다.");
            log.Append("  셰이더가 다시 컴파일되므로 첫 재생이 한 번 느릴 수 있다.");
            Debug.Log(log.ToString(), rp);
        }

        static void Set(SerializedObject so, string field, bool value,
                        System.Text.StringBuilder log)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null) { log.AppendLine("  [?] " + field + " — 그런 필드가 없다"); return; }
            log.AppendLine(string.Format("  {0,-36} {1} -> {2}", field, p.boolValue, value));
            p.boolValue = value;
        }
    }
}
