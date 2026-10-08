using System.Collections.Generic;
using System.Text;
using Demo.Data;
using UnityEditor;
using UnityEngine;

namespace Demo.EditorTools
{
    /// <summary>
    /// 건물 정면도를 파일 이름으로 <see cref="StructureData"/>에 꽂는다 [2026-10-08].
    ///
    /// <b>왜 손으로 안 끄는가.</b> 종류 다섯에 면이 넷이라 최대 스무 칸이고, VARCO에서
    /// 새로 뽑을 때마다 다시 해야 한다. 빠뜨려도 <b>에러가 안 난다</b> — 그 면만 민짜로
    /// 남는다. 효과음 쪽에서 똑같은 이유로 <see cref="SoundBankBuilder"/>를 만들었다
    /// (CLAUDE.md 규칙 8).
    ///
    /// <b>규칙</b>: <c>Assets/_Project/Art/Facades/T_&lt;id&gt;_&lt;면&gt;.png</c>에서
    /// <c>&lt;id&gt;</c>가 StructureData의 <c>id</c> 필드와 같으면 그 자리에 들어간다.
    /// 면은 <c>front · right · back · left</c>. <b>front만 있으면 나머지 세 면도 같은 것을
    /// 쓴다</b> — 입구가 사방에 생기지만, 민짜 회색 박스보다는 낫고 면당 한 장씩 뽑는 것은
    /// 나중에 해도 된다.
    ///
    /// <b>임포트 설정도 같이 맞춘다.</b> 기본값으로 들어오면 2048에 비압축이라 한 장에
    /// 16MB를 먹는다 — AAB 200MB 예산에서 다섯 종이면 끝이다. 1024·압축·sRGB로 맞춘다.
    /// </summary>
    public static class FacadeBinder
    {
        const string Dir = "Assets/_Project/Art/Facades";
        static readonly string[] Faces = { "front", "right", "back", "left" };

        [MenuItem("Demo/17. 건물 정면도 연결 — 파일 이름으로", priority = 71)]
        public static void Bind()
        {
            var byKey = new Dictionary<string, Texture2D>();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Dir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue;
                Configure(path);
                byKey[tex.name.ToLowerInvariant()] = tex;
            }

            var sb = new StringBuilder();
            sb.AppendLine("건물 정면도 연결 — " + Dir);
            sb.AppendLine("  front만 있으면 나머지 세 면도 같은 것을 쓴다 (면당 따로 뽑는 것은 나중)");
            sb.AppendLine();
            sb.AppendLine(string.Format("  {0,-14} {1,-22} {2}", "종류", "쓴 텍스처", "면"));

            int bound = 0, missing = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:StructureData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<StructureData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null || string.IsNullOrEmpty(data.id)) continue;

                var so = new SerializedObject(data);
                string id = data.id.ToLowerInvariant();

                byKey.TryGetValue("t_" + id + "_front", out Texture2D front);
                if (front == null)
                {
                    missing++;
                    sb.AppendLine(string.Format("  {0,-14} {1,-22} {2}", data.id, "없음",
                        "X — T_" + id + "_front.png 가 없다"));
                    continue;
                }

                var used = new List<string>();
                for (int f = 0; f < Faces.Length; f++)
                {
                    byKey.TryGetValue("t_" + id + "_" + Faces[f], out Texture2D tex);
                    bool own = tex != null;
                    if (!own) tex = front;                       // 전용이 없으면 앞면을 돌려 쓴다
                    so.FindProperty("facade" + Cap(Faces[f])).objectReferenceValue = tex;
                    used.Add(own ? Faces[f] : Faces[f] + "(앞면)");
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(data);
                bound++;
                sb.AppendLine(string.Format("  {0,-14} {1,-22} {2}", data.id, front.name,
                    string.Join(" ", used)));
            }

            AssetDatabase.SaveAssets();
            sb.AppendLine();
            sb.AppendLine($"연결됨 {bound} · 비어 있음 {missing}. " +
                          "비어 있는 것은 민짜 회색으로 남는다 — 에러가 안 나므로 이 표로만 보인다.");
            if (missing > 0) Debug.LogWarning(sb.ToString()); else Debug.Log(sb.ToString());
        }

        static string Cap(string s) => char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>
        /// 모바일 예산에 맞춰 임포트 설정을 박는다. 기본값으로 두면 한 장이 16MB다.
        /// 이미 같은 설정이면 건드리지 않는다 — 괜히 재임포트하면 느리다.
        /// </summary>
        static void Configure(string path)
        {
            var im = AssetImporter.GetAtPath(path) as TextureImporter;
            if (im == null) return;

            bool changed = false;
            if (im.textureType != TextureImporterType.Default) { im.textureType = TextureImporterType.Default; changed = true; }
            if (!im.sRGBTexture) { im.sRGBTexture = true; changed = true; }
            if (!im.mipmapEnabled) { im.mipmapEnabled = true; changed = true; }   // 멀리서 보면 지글거린다
            if (im.maxTextureSize != 1024) { im.maxTextureSize = 1024; changed = true; }
            if (im.textureCompression != TextureImporterCompression.Compressed)
            { im.textureCompression = TextureImporterCompression.Compressed; changed = true; }
            if (im.wrapMode != TextureWrapMode.Clamp) { im.wrapMode = TextureWrapMode.Clamp; changed = true; }

            // <b>포맷을 직접 박는다</b> [2026-10-08]. '압축'으로 두기만 하면 2의 거듭제곱이
            // 아닌 1024x768짜리가 <b>비압축 RGB24로 들어온다</b> — 실측에서 그 두 장만
            // 7.13MB였고 나머지 정사각은 DXT1 1.36MB였다(5.2배). npotScale은 이미 기본값이라
            // 건드려도 안 바뀐다. 플랫폼별로 포맷을 지정하는 쪽이 확실하다.
            changed |= ForceFormat(im, "Standalone", TextureImporterFormat.DXT1);
            changed |= ForceFormat(im, "Android", TextureImporterFormat.ASTC_6x6);

            if (changed) im.SaveAndReimport();
        }

        /// <summary>플랫폼 하나의 포맷을 못 박는다. 이미 같으면 false를 돌려 재임포트를 막는다.</summary>
        static bool ForceFormat(TextureImporter im, string platform, TextureImporterFormat format)
        {
            TextureImporterPlatformSettings ps = im.GetPlatformTextureSettings(platform);
            if (ps.overridden && ps.format == format && ps.maxTextureSize == 1024) return false;

            ps.overridden = true;
            ps.format = format;
            ps.maxTextureSize = 1024;
            ps.textureCompression = TextureImporterCompression.Compressed;
            im.SetPlatformTextureSettings(ps);
            return true;
        }
    }
}
