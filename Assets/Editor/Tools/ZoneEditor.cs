using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// ZoneData 배치 편집기 — 씬 뷰 핸들로 위치를 옮기고 지지 관계를 잇는다 (TECH_SPEC §6).
    ///
    /// <b>아직 구현되지 않았다. D6 작업이다.</b>
    ///
    /// TECH_SPEC이 "이게 가장 중요하다"고 못박은 툴이다. 이것이 없으면 구역 배치를
    /// Inspector에서 좌표를 손으로 찍어야 한다. 지금은 <see cref="PlaceholderAssets"/>가
    /// 코드로 격자를 깔고 있어서 버티는 중이고, D14에 구역 3~5개를 만들 때 한계가 온다.
    ///
    /// 지지 관계(SupportLink)를 눈으로 잇는 기능이 특히 중요하다 — 숫자 id로 연결하면
    /// 틀려도 모르고, 틀린 지지 관계는 "기둥을 부쉈는데 아무것도 안 무너진다"로만 드러난다.
    /// </summary>
    public sealed class ZoneEditor : EditorWindow
    {
        [MenuItem("Demo/도구/구역 편집기 (미구현)", priority = 101)]
        public static void Open()
        {
            Debug.LogWarning("[ZoneEditor] 아직 구현되지 않았다 (TECH_SPEC §6 · D6). " +
                             "구역 배치는 현재 PlaceholderAssets.MakeZone()이 코드로 생성한다.");
            // TODO(D6): ZoneData를 선택하면 씬 뷰에 Handles로 배치를 그리고,
            //           드래그로 위치 이동, 구조물 간 드래그로 SupportLink 생성.
        }
    }
}
