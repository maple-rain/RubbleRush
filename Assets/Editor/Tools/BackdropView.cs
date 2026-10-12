using Demo.Core;
using Demo.Data;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 게임에서 카메라가 실제로 무엇을 보는가 [2026-10-08].
    /// <see cref="BackdropCheck"/>(재기)와 <see cref="BackdropDress"/>(고치기)가 같이 쓴다 —
    /// 재는 쪽과 고치는 쪽이 <b>다른 수를 쓰면</b> 고쳐 놓고 안 고쳐졌다고 나온다.
    ///
    /// <b>카메라의 트랜스폼을 읽지 않는다.</b> 에디트 모드의 위치는 마지막에 씬 뷰에서
    /// 어쩌다 멈춘 자리라 게임과 무관하다 — 처음 재 봤을 때 높이가 1.0m로 나와
    /// "1m당 935px가 필요하다"는 터무니없는 기준이 잡혔다. 게임의 거리는
    /// <see cref="GameConfig.cameraDistance"/>와 <c>FollowCamera</c>에 <b>씬에 박힌</b>
    /// 내림각이 정하므로 그 둘을 읽는다 (C# 기본값이 아니라 씬의 값이어야 한다).
    /// </summary>
    public struct BackdropView
    {
        /// <summary>세로 1080 · 가로 1920 기준. 타깃이 Landscape다.</summary>
        public const float ScreenWide = 1920f;

        public bool Valid;
        public float Pitch;          // 내려다보는 각도(도)
        public float Yaw;            // 수평 회전(도). 고정이다
        public float Fov;
        public float Distance;       // 기본 줌에서 초점까지
        public float ZoomMax;
        public float EyeHeight;

        /// <summary>최대 줌에서의 카메라 높이. 시야가 가장 멀리 닿는 경우다.</summary>
        public float CameraY;

        /// <summary>시야 위쪽 모서리가 수평에서 몇 도 <b>아래</b>인가. 양수면 하늘이 안 보인다.</summary>
        public float TopDeg;

        /// <summary>지면이 보이는 최대 거리(m). 그 너머는 손대도 화면에 안 나온다.</summary>
        public float Reach;

        /// <summary>초점에서 화면이 담는 가로 폭(m).</summary>
        public float ViewWide;

        /// <summary>화면에서 1m가 몇 픽셀인가. 지면 텍스처의 목표 해상도가 여기서 나온다.</summary>
        public float PixelsPerMeter;

        public static BackdropView Measure()
        {
            var v = new BackdropView();

            var cam = Object.FindFirstObjectByType<Camera>();
            var follow = Object.FindFirstObjectByType<FollowCamera>();
            var cfgRef = Object.FindFirstObjectByType<GameConfigRef>();
            if (cam == null || follow == null || cfgRef == null) return v;

            var conf = new SerializedObject(cfgRef).FindProperty("config").objectReferenceValue as GameConfig;
            if (conf == null || conf.cameraDistance <= 0.01f) return v;

            var so = new SerializedObject(follow);
            v.Pitch = Prop(so, "pitch", 50f);
            v.Yaw = Prop(so, "yaw", 45f);
            v.EyeHeight = Prop(so, "eyeHeight", 1.5f);
            v.Fov = cam.fieldOfView;
            v.Distance = conf.cameraDistance;
            v.ZoomMax = conf.cameraZoomMax;

            v.CameraY = v.EyeHeight + (v.Distance + v.ZoomMax) * Mathf.Sin(v.Pitch * Mathf.Deg2Rad);
            v.TopDeg = v.Pitch - v.Fov * 0.5f;
            v.Reach = v.TopDeg > 0.5f ? v.CameraY / Mathf.Tan(v.TopDeg * Mathf.Deg2Rad) : -1f;

            float halfTall = v.Distance * Mathf.Tan(v.Fov * 0.5f * Mathf.Deg2Rad);
            v.ViewWide = halfTall * 2f * (16f / 9f);
            v.PixelsPerMeter = v.ViewWide > 0.01f ? ScreenWide / v.ViewWide : 0f;

            v.Valid = true;
            return v;
        }

        /// <summary>그 높이의 꼭대기가 화면에 드는 최대 거리(m). 0 이하면 기본 줌에서 잘린다.</summary>
        public float TopVisibleAt(float height)
        {
            if (TopDeg <= 0.5f) return 999f;
            return (CameraY - height) / Mathf.Tan(TopDeg * Mathf.Deg2Rad);
        }

        static float Prop(SerializedObject so, string field, float fallback)
        {
            var p = so.FindProperty(field);
            return p != null ? p.floatValue : fallback;
        }
    }
}
