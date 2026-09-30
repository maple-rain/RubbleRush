using Demo.Collection;
using Demo.Core;
using Demo.Data;
using Demo.Destruction;
using Demo.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// Main.unity를 짓는다 — 루트 6개와 컴포넌트 연결까지.
    ///
    /// CLAUDE.md 규칙 1은 Claude가 .unity를 <b>직접 편집</b>하는 것을 금지한다. 이 툴은 그것을 어기지 않는다 —
    /// YAML을 쓰는 것은 Unity의 직렬화기이고, Claude는 .cs만 쓴다.
    /// 규칙 8("반복 수작업이 보이면 즉시 스크립트로 만든다")이 가리키는 바로 그 자리다.
    ///
    /// 덤으로 씬이 깨져도 1초 만에 다시 짓는다. 손으로 배치한 씬은 그게 안 된다.
    /// </summary>
    public static class SceneBootstrap
    {
        const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        const string ConfigPath = "Assets/_Project/Data/Config/GameConfig_TEMP.asset";
        const string ZonePath = "Assets/_Project/Data/Zones/Zone_TEMP_Test.asset";
        const string HolePath = "Assets/_Project/Prefabs/VFX/P_TEMP_Hole.prefab";
        const string DebrisPath = "Assets/_Project/Prefabs/Structures/P_TEMP_Debris.prefab";

        [MenuItem("Demo/2. Main 씬 생성", priority = 20)]
        public static void BuildScene() => BuildScene(true);

        /// <summary>
        /// 확인 없이 다시 짓는다. <b>스크립트·MCP에서 부를 때는 이쪽을 쓴다.</b>
        ///
        /// 덮어쓰기 확인 대화상자는 사람에게는 안전장치지만, 자동 호출에서는
        /// <b>모달이 뜬 채로 에디터 전체가 멈춘다</b> — 아무도 누르지 않기 때문이다.
        /// MCP 호출이 타임아웃되고 브리지 하트비트까지 끊긴다.
        /// </summary>
        [MenuItem("Demo/2b. Main 씬 다시 짓기 (확인 없음)", priority = 21)]
        public static void RebuildSceneNoPrompt() => BuildScene(false);

        /// <summary>
        /// 이 도구가 <b>다시 만들지 못하는</b> 것이 지금 씬에 있는가.
        ///
        /// BuildPlayer는 원시 큐브 로봇을 만든다 — 주석에 "스켈레탈 애니메이션은 쓰지 않는다"가
        /// 그대로 남아 있고 콜라이더도 height 1.0이다. VARCO 휴머노이드 작업 이전의 도구라,
        /// 지금 이 씬을 다시 지으면 <b>1~4번 작업이 통째로 사라진다</b>.
        /// 특히 StrikeArmRig.armsOnlyMesh(R_Arms 메시)는 씬에만 있어서 되살릴 방법이 없다.
        ///
        /// 도구를 지금 씬에 맞게 갱신하는 것은 별도 작업이다. 그때까지 모르고 누르는 것만 막는다.
        /// </summary>
        static string WhatWouldBeLost()
        {
            var lost = new System.Collections.Generic.List<string>();
            if (Object.FindFirstObjectByType<Demo.Player.StrikeArmRig>(FindObjectsInactive.Include) != null)
                lost.Add("StrikeArmRig (팔 리그 · R_Arms 메시 참조)");
            if (Object.FindFirstObjectByType<Demo.Player.PlayerAnimator>(FindObjectsInactive.Include) != null)
                lost.Add("PlayerAnimator (걷기/달리기/타격 · 러쉬)");
            if (Object.FindFirstObjectByType<Demo.Destruction.DestructionFX>(FindObjectsInactive.Include) != null)
                lost.Add("DestructionFX (셰이크 · 히트스톱 값)");
            return lost.Count == 0 ? null : string.Join("\n  · ", lost);
        }

        static void BuildScene(bool askBeforeOverwrite)
        {
            string lost = WhatWouldBeLost();
            if (lost != null)
            {
                string msg = "이 도구는 <b>큐브 로봇</b>을 만든다 (휴머노이드 작업 이전 버전).\n" +
                             "다시 지으면 아래가 사라지고, 메시 참조는 되살릴 수 없다:\n\n  · " + lost;
                if (!askBeforeOverwrite)
                {
                    // 자동 호출에서는 모달을 띄우면 에디터가 멈춘다. 막고 이유만 남긴다.
                    Debug.LogError("[SceneBootstrap] 다시 짓기를 멈췄다.\n" + msg.Replace("<b>", "").Replace("</b>", "") +
                                   "\n\n정말 다시 지으려면 이 검사(WhatWouldBeLost)를 먼저 지울 것.");
                    return;
                }
                if (!EditorUtility.DisplayDialog("지금 씬을 다시 지으면 작업이 사라진다",
                        msg.Replace("<b>", "").Replace("</b>", ""), "그래도 짓는다", "취소"))
                    return;
            }

            // 존재 확인은 경로로만 한다. 여기서 객체를 로드하면 안 된다 —
            // 아래 NewScene이 참조 없는 에셋을 언로드해서 그 객체가 "fake null"이 되고,
            // 그 상태로 SerializedProperty에 넣으면 에러 없이 {fileID: 0}이 조용히 박힌다.
            foreach (string p in new[] { ConfigPath, ZonePath, HolePath, DebrisPath })
            {
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(p))) continue;
                EditorUtility.DisplayDialog("임시 에셋이 없다",
                    $"{p} 가 없다.\n먼저 메뉴 > Demo > 1. 임시 에셋 생성 을 실행할 것.", "확인");
                return;
            }

            // 덮어쓸 씬이 있을 때만 묻는다. 없으면 잃을 것이 없다.
            // askBeforeOverwrite가 false면 아예 묻지 않는다 — 모달은 자동 호출에서
            // 에디터를 통째로 멈춰 세운다.
            bool sceneExists = System.IO.File.Exists(ScenePath);
            if (askBeforeOverwrite && sceneExists &&
                !EditorUtility.DisplayDialog("Main 씬을 새로 짓는다",
                    "기존 Main.unity가 덮어써진다. 계속할까?", "짓는다", "취소"))
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,
                                                    NewSceneMode.Single);

            // 씬을 간 뒤에 로드한다. 순서를 뒤집으면 위 주석의 fake null 사고가 난다.
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            var zone = AssetDatabase.LoadAssetAtPath<ZoneData>(ZonePath);
            var holePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HolePath);
            var debrisPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DebrisPath);

            BuildGround();
            Transform zoneRoot = new GameObject("[Zone]").transform;
            new GameObject("[VFX Pools]");

            GameObject player = BuildPlayer(debrisPrefab);

            // 터치 입력 레이어. PC에서는 스스로 꺼진다 (터치스크린이 없으면).
            var ui = new GameObject("[UI]");
            ui.AddComponent<Demo.UI.TouchControls>();
            FollowCamera cam = BuildCamera(player.transform);
            BuildBootstrap(config, zone, zoneRoot, holePrefab, player);

            var cargo = player.GetComponent<CargoTow>();
            SetRef(cam, "cargo", cargo);

            EditorSceneManager.MarkSceneDirty(scene);
            System.IO.Directory.CreateDirectory("Assets/_Project/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[SceneBootstrap] {ScenePath} 생성 완료. Play를 누르면 루프가 돈다.\n" +
                      "조작: WASD 이동 · E 부수기(파괴 화면) · Space 케이블 토글 / 파괴 화면에서 두드리기 · Q 내려놓기");
        }

        /// <summary>구역이 차지하는 반경(m). 바닥과 경계벽이 이 값 하나를 본다.</summary>
        const float GroundHalf = 60f;

        static void BuildGround()
        {
            // Plane은 MeshCollider가 딸려 온다 — CLAUDE.md 규칙 4 위반이라 납작한 Cube를 쓴다.
            //
            // [지시 2026-09-22] 폭 80(±40)에서 120(±60)으로 넓힌다. 건물의 X·Z를 키우면서
            // 제일 뒤 띠(코어, z=38에 깊이 8m)가 z=42까지 나가 바닥 밖으로 튀어나왔다 —
            // 그 뒤로 돌아가면 플레이어가 그대로 떨어졌다.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(GroundHalf * 2f, 1f, GroundHalf * 2f);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);

            var mat = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/_Project/Art/Materials/M_TEMP_Rubble.mat");
            if (mat != null) ground.GetComponent<MeshRenderer>().sharedMaterial = mat;

            BuildBounds();
        }

        /// <summary>
        /// 보이지 않는 경계벽. 바닥을 넓히는 것만으로는 "떨어진다"가 사라지지 않는다 —
        /// 넓힌 만큼 더 걸어가면 여전히 끝이 있다. 벽이 있어야 끝이 없어진다.
        ///
        /// 렌더러는 지운다. 산업 톤의 펜스 에셋이 들어오면 그때 이 자리에 세우면 된다.
        /// </summary>
        static void BuildBounds()
        {
            var root = new GameObject("[Bounds]");
            float h = 6f, t = 1f, e = GroundHalf;

            var sides = new[] {
                (pos: new Vector3(0f, h * 0.5f,  e), scale: new Vector3(e * 2f, h, t), name: "N"),
                (pos: new Vector3(0f, h * 0.5f, -e), scale: new Vector3(e * 2f, h, t), name: "S"),
                (pos: new Vector3( e, h * 0.5f, 0f), scale: new Vector3(t, h, e * 2f), name: "E"),
                (pos: new Vector3(-e, h * 0.5f, 0f), scale: new Vector3(t, h, e * 2f), name: "W"),
            };

            foreach (var s in sides)
            {
                var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
                w.name = "Wall_" + s.name;
                w.transform.SetParent(root.transform, false);
                w.transform.position = s.pos;
                w.transform.localScale = s.scale;
                Object.DestroyImmediate(w.GetComponent<MeshRenderer>());
            }
        }

        static GameObject BuildPlayer(GameObject debrisPrefab)
        {
            // [지시 2026-09-22] 캐릭터를 줄인다. 건물이 커진 게 아니라 로봇이 컸던 것에 가깝다 —
            // 1.6×1.0×2.4의 로봇 옆에서는 4m 벽이 '벽 한 장'으로만 보인다.
            // 전체를 0.625배로 줄여 콜라이더·몸체·표식의 비례는 그대로 유지한다.
            var root = new GameObject("[Player]");
            root.transform.position = new Vector3(0f, 0.55f, -12f);

            var cc = root.AddComponent<CharacterController>();
            cc.height = 1.0f;
            cc.radius = 0.5f;
            cc.center = new Vector3(0f, 0.5f, 0f);

            // 무한궤도 차량이라 납작하게. 스켈레탈 애니메이션은 쓰지 않는다 [확정].
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(1.0f, 0.65f, 1.5f);
            body.transform.localPosition = new Vector3(0f, 0.32f, 0f);

            // 앞을 알아보게 표식을 하나. 조준이 없어서 방향이 안 보이면 답답하다.
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(nose.GetComponent<Collider>());
            nose.name = "Breaker";
            nose.transform.SetParent(root.transform, false);
            nose.transform.localScale = new Vector3(0.25f, 0.25f, 0.75f);
            nose.transform.localPosition = new Vector3(0f, 0.38f, 0.88f);

            root.AddComponent<InputRouter>();
            root.AddComponent<PlayerController>();
            root.AddComponent<InteractionProbe>();   // 근접 시 상호작용 키
            root.AddComponent<PlayerVisibility>();   // 파괴 화면 동안 본체를 숨긴다
            root.AddComponent<CargoTow>();

            var cable = root.AddComponent<CableController>();
            SetUpLine(root.GetComponent<LineRenderer>());

            var spawner = root.AddComponent<DebrisSpawner>();
            spawner.DebrisPrefab = debrisPrefab;
            EditorUtility.SetDirty(spawner);

            root.AddComponent<Breaker>();
            _ = cable;

            return root;
        }

        static void SetUpLine(LineRenderer line)
        {
            if (line == null) return;

            line.startWidth = line.endWidth = 0.18f;
            line.numCapVertices = 2;
            line.positionCount = 0;
            line.textureMode = LineTextureMode.Tile;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var mat = new Material(shader);
            Color c = new Color(1f, 0.75f, 0.15f);
            mat.color = c;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);

            AssetDatabase.CreateAsset(mat, "Assets/_Project/Art/Materials/M_TEMP_Cable.mat");
            line.sharedMaterial = mat;
        }

        static FollowCamera BuildCamera(Transform target)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                go.tag = "MainCamera";
                cam = go.GetComponent<Camera>();
            }

            cam.transform.parent = null;
            cam.gameObject.name = "[Camera]";

            var follow = cam.GetComponent<FollowCamera>();
            if (follow == null) follow = cam.gameObject.AddComponent<FollowCamera>();
            follow.Target = target;
            EditorUtility.SetDirty(follow);

            // 카메라와 목표 건물 사이에 낀 잔해를 잠깐 숨긴다 [지시 2026-09-22].
            // 탐지기는 런타임에 스스로 찾는다 — 여기서 엮으면 플레이어를 다시 지을 때마다 끊긴다.
            if (cam.GetComponent<TargetSightline>() == null)
                cam.gameObject.AddComponent<TargetSightline>();

            // 구멍이 화면 밖일 때 가장자리 화살표 [지시 2026-09-23].
            // 구멍은 런타임에 스스로 찾지만 텍스처는 에셋이라 여기서 물려 줘야 한다.
            var marker = cam.GetComponent<Demo.UI.OffscreenMarker>();
            if (marker == null) marker = cam.gameObject.AddComponent<Demo.UI.OffscreenMarker>();
            SetRef(marker, "arrow", UiTextures.LoadArrow());

            return follow;
        }

        static void BuildBootstrap(GameConfig config, ZoneData zone, Transform zoneRoot,
                                   GameObject holePrefab, GameObject player)
        {
            var root = new GameObject("[Bootstrap]");

            var cfgRef = root.AddComponent<GameConfigRef>();
            SetRef(cfgRef, "config", config);

            root.AddComponent<PoolManager>();

            var builder = root.AddComponent<ZoneBuilder>();
            builder.ZoneRoot = zoneRoot;
            builder.HolePrefab = holePrefab;
            EditorUtility.SetDirty(builder);

            // 필드 ↔ 파괴 화면 전환의 단일 창구
            root.AddComponent<GameModeController>();

            var session = root.AddComponent<Demo.Destruction.DemolitionSession>();
            SetRef(session, "spawner", player.GetComponent<DebrisSpawner>());
            SetRef(session, "input", player.GetComponent<InputRouter>());

            // 시연·검증용 단축키와 오버레이. D15 동결 때 제거 대상이다.
            root.AddComponent<Demo.Utils.DebugHotkeys>();

            var loop = root.AddComponent<GameLoop>();
            SetRef(loop, "zone", zone);
            SetRef(loop, "builder", builder);
            SetRef(loop, "player", player.GetComponent<PlayerController>());
            SetRef(loop, "cargo", player.GetComponent<CargoTow>());
            SetRef(loop, "spawner", player.GetComponent<DebrisSpawner>());
        }

        /// <summary>
        /// private [SerializeField]에 값을 넣는다. 인스펙터에서 손으로 끌어다 놓는 것과 같은 일을
        /// 코드로 한다 — 이게 없으면 씬 생성 뒤에 사람이 연결을 10개쯤 해야 한다.
        /// </summary>
        static void SetRef(Object target, string fieldName, Object value)
        {
            // null은 항상 버그다. 조용히 넣으면 {fileID: 0}이 박히고,
            // 증상은 한참 뒤 플레이할 때 "에셋이 물려 있지 않다"로만 나타난다.
            // 파괴된 객체(fake null)도 여기서 같이 걸린다 — Unity의 == null이 그것까지 본다.
            if (value == null)
            {
                Debug.LogError($"[SceneBootstrap] {target.GetType().Name}.{fieldName}에 넣을 값이 null이다. " +
                               "에셋이 아직 없거나, 씬 교체로 언로드된 참조를 들고 있다.", target);
                return;
            }

            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(fieldName);

            if (prop == null)
            {
                Debug.LogError($"[SceneBootstrap] {target.GetType().Name}에 '{fieldName}' 필드가 없다. " +
                               "필드 이름이 바뀌었으면 이 툴도 같이 고쳐야 한다.", target);
                return;
            }

            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (prop.objectReferenceValue == null)
                Debug.LogError($"[SceneBootstrap] {target.GetType().Name}.{fieldName} 연결이 " +
                               "적용 직후에도 비어 있다. 참조가 유효하지 않다.", target);
        }
    }
}
