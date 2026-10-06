using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;

namespace Oheangbu.EditorTools.WorldMacro
{
    // #308 quadruped animation sources: download (never import) user-approved FREE Asset Store packages into the local Asset Store
    // cache through the Package Manager's internal download service, so their FBX files can be read by Blender without touching the
    // project. Queue-safe, read-mostly. Run("probe") lists the internal types; Run("download:<id>,<id>") starts downloads;
    // Run("status") reports the cache folder contents.
    public static class AssetStoreFetch308
    {
        public static string Run(string arg)
        {
            arg = (arg ?? "").Trim();
            try
            {
                if (arg == "probe") return Probe();
                if (arg.StartsWith("download:", StringComparison.Ordinal)) return Download(arg.Substring(9).Split(',').Select(s => long.Parse(s.Trim())).ToArray());
                if (arg == "status") return Status();
                if (arg == "open-myassets") return OpenMyAssets();
                if (arg.StartsWith("info:", StringComparison.Ordinal)) return Info(long.Parse(arg.Substring(5)));
                return "REFUSED unknown (probe | download:<ids> | status)";
            }
            catch (Exception e) { return "FAILED " + e; }
        }

        static Type[] EditorTypes() => AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name.StartsWith("UnityEditor", StringComparison.Ordinal))
            .SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); } }).ToArray();

        static string Probe()
        {
            var sb = new StringBuilder();
            foreach (var t in EditorTypes().Where(t => t.FullName != null && t.FullName.Contains("PackageManager.UI.Internal") &&
                (t.Name.Contains("DownloadManager") || t.Name == "ServicesContainer" || t.Name.Contains("AssetStoreCache") || t.Name.Contains("UnityConnect"))))
            {
                sb.AppendLine(t.FullName + (t.IsInterface ? " (interface)" : ""));
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Take(40))
                    sb.AppendLine("   " + m.ReturnType.Name + " " + m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
            }
            return sb.ToString();
        }

        static object Service(string interfaceName)
        {
            var types = EditorTypes();
            var container = types.First(t => t.Name == "ServicesContainer" && t.FullName.Contains("PackageManager.UI.Internal"));
            object instance = null;
            foreach (var p in container.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)) if (p.PropertyType == container) { instance = p.GetValue(null); if (instance != null) break; }
            if (instance == null) foreach (var f in container.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)) if (f.FieldType == container) { instance = f.GetValue(null); if (instance != null) break; }
            if (instance == null)
            {
                // ScriptableSingleton-style: find a live instance among loaded objects
                instance = UnityEngine.Resources.FindObjectsOfTypeAll(container).FirstOrDefault();
            }
            if (instance == null) throw new InvalidOperationException("ServicesContainer instance not found; static members: " + string.Join(",", container.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Select(m => m.MemberType + ":" + m.Name)) + " base " + container.BaseType);
            var iface = types.First(t => t.Name == interfaceName && t.FullName.Contains("PackageManager.UI.Internal"));
            var resolve = container.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).First(m => m.Name == "Resolve" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
            return resolve.MakeGenericMethod(iface).Invoke(instance, null);
        }

        static string Download(long[] ids)
        {
            var mgr = Service("IAssetStoreDownloadManager");
            var t = mgr.GetType();
            var sb = new StringBuilder("download manager " + t.FullName + "\n");
            var multi = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(m => m.Name == "Download" && m.GetParameters().Length == 1 && typeof(IEnumerable).IsAssignableFrom(m.GetParameters()[0].ParameterType));
            if (multi == null) return sb.Append("no Download(IEnumerable<long>) — methods: " + string.Join(", ", t.GetMethods().Select(m => m.Name).Distinct())).ToString();
            var r = multi.Invoke(mgr, new object[] { ids.ToList() });
            return sb.Append("Download(" + string.Join(",", ids) + ") -> " + (r ?? "void")).ToString();
        }

        static string OpenMyAssets()
        {
            var w = EditorTypes().FirstOrDefault(t => t.Name == "PackageManagerWindow");
            if (w == null) return "no PackageManagerWindow";
            var m = w.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Where(x => x.Name.StartsWith("Open", StringComparison.Ordinal)).ToArray();
            var sel = m.FirstOrDefault(x => x.Name == "OpenAndSelectPage" && x.GetParameters().Length == 2);
            if (sel != null) { sel.Invoke(null, new object[] { "MyAssets", "" }); return "opened via OpenAndSelectPage"; }
            return "candidates: " + string.Join(", ", m.Select(x => x.Name + "(" + string.Join(",", x.GetParameters().Select(p => p.ParameterType.Name)) + ")"));
        }

        static string Info(long id)
        {
            var cache = Service("IAssetStoreCache"); var t = cache.GetType();
            object Get(string name) => t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Invoke(cache, new object[] { (long?)id });
            string J(object o) => o == null ? "null" : UnityEngine.JsonUtility.ToJson(o);
            var dl = Service("IAssetStoreDownloadManager"); var op = dl.GetType().GetMethod("GetDownloadOperation").Invoke(dl, new object[] { (long?)id });
            return "purchase " + J(Get("GetPurchaseInfo")) + " | product " + (Get("GetProductInfo") != null) + " | local " + J(Get("GetLocalInfo")) + " | op " + (op == null ? "null" : J(op));
        }

        static string Status()
        {
            string root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Unity", "Asset Store-5.x");
            if (!System.IO.Directory.Exists(root)) return "no cache folder " + root;
            var files = System.IO.Directory.GetFiles(root, "*.unitypackage", System.IO.SearchOption.AllDirectories)
                .Select(f => new System.IO.FileInfo(f)).OrderByDescending(f => f.LastWriteTimeUtc).Take(20);
            return string.Join("\n", files.Select(f => f.LastWriteTime.ToString("s") + " " + (f.Length / 1048576.0).ToString("F1") + " MB " + f.FullName.Substring(root.Length)));
        }
    }
}
