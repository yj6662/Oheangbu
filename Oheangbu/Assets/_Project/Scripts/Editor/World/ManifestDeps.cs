using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Oheangbu.EditorTools
{
    // [SPEC-WORLD-MAP §4 패키지 · §7 ①] manifest.json 직접 의존 재확인 — 빌더 1단계 EnsureManifestDeps(§6 A8).
    // terrain-tools/splines/probuilder는 피처·MCP 패키지 경유 간접(depth 1)이면 그 패키지 제거 시 함께 사라진다 → 직접 등재만 인정.
    public static class ManifestDeps
    {
        public static readonly string[] Required =
        {
            "com.unity.terrain-tools",
            "com.unity.splines",
            "com.unity.probuilder",
            "com.unity.ai.navigation",
        };

        public static string ManifestPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "manifest.json"));

        // 요약 문자열 — 첫 토큰 OK/FAIL, 패키지별 DIRECT <version> / MISSING.
        public static string EnsureManifestDeps()
        {
            string report;
            TryEnsureManifestDeps(out report);
            return report;
        }

        public static bool TryEnsureManifestDeps(out string report)
        {
            string path = ManifestPath;
            if (!File.Exists(path))
            {
                report = "FAIL manifest: not found " + path;
                return false;
            }
            string json = File.ReadAllText(path);
            var depsMatch = Regex.Match(json, "\"dependencies\"\\s*:\\s*\\{([^}]*)\\}", RegexOptions.Singleline);
            string deps = depsMatch.Success ? depsMatch.Groups[1].Value : "";
            var sb = new StringBuilder();
            int missing = 0;
            foreach (var pkg in Required)
            {
                var m = Regex.Match(deps, "\"" + Regex.Escape(pkg) + "\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success) sb.Append(" · ").Append(pkg).Append(" DIRECT ").Append(m.Groups[1].Value);
                else
                {
                    missing++;
                    sb.Append(" · ").Append(pkg).Append(" MISSING");
                }
            }
            report = (missing == 0 ? "OK" : "FAIL(" + missing + ")") + " manifest" + sb;
            return missing == 0;
        }
    }
}
