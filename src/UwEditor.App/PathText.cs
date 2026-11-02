using System;
using System.Collections.Generic;
using System.Linq;

namespace UwEditor.App
{
    /// <summary>Short forms of file paths for menus.</summary>
    public static class PathText
    {
        /// <summary>
        /// A path at most max characters long with its middle left out: the root (drive or "/") and
        /// the first folder, an ellipsis, then as many of the last folders as fit. A path that
        /// fits comes back as it is; a last folder too long by itself loses its own middle.
        /// </summary>
        public static string MiddleEllipsis(string path, int max = 60)
        {
            if (string.IsNullOrEmpty(path) || path.Length <= max) return path ?? "";
            max = Math.Max(max, 12);
            char sep = path.IndexOf('\\') >= 0 && (path.IndexOf('/') < 0 || path.IndexOf('\\') < path.IndexOf('/')) ? '\\' : '/';
            const string dots = "…";
            // the root: "C:\", "\\server\share\", "/" or nothing
            string root = "";
            string rest = path;
            if (path.StartsWith("\\\\") || path.StartsWith("//"))
            {
                int a = path.IndexOfAny(new[] { '\\', '/' }, 2), b = a < 0 ? -1 : path.IndexOfAny(new[] { '\\', '/' }, a + 1);
                root = b < 0 ? path : path.Substring(0, b + 1);
            }
            else if (path.Length >= 2 && path[1] == ':') root = path.Length >= 3 && (path[2] == '\\' || path[2] == '/') ? path.Substring(0, 3) : path.Substring(0, 2);
            else if (path[0] == '/' || path[0] == '\\') root = path.Substring(0, 1);
            rest = path.Substring(root.Length);
            var parts = rest.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (parts.Count == 0) return Cut(path, max);
            string last = parts[parts.Count - 1];
            // the root and the first folder, when room is left for the last folder
            string head = root;
            if (parts.Count > 2 && root.Length + parts[0].Length + 1 + dots.Length + 1 + last.Length <= max) head = root + parts[0] + sep;
            int used = 1;
            if (parts.Count <= 1 || head.Length + dots.Length + 1 + last.Length > max)
                return head.Length + dots.Length + 1 + 8 > max ? Cut(path, max) : head + dots + sep + Cut(last, max - head.Length - dots.Length - 1);
            var tail = new List<string> { last };
            int len = last.Length;
            int firstKept = head.Length > root.Length ? 1 : 0;
            for (int i = parts.Count - 2; i >= firstKept; i--)
            {
                if (head.Length + dots.Length + 1 + len + 1 + parts[i].Length > max) break;
                tail.Insert(0, parts[i]);
                len += 1 + parts[i].Length;
                used++;
            }
            if (used + firstKept >= parts.Count) return path;   // nothing left out after all
            return head + dots + sep + string.Join(sep.ToString(), tail);
        }

        /// <summary>A text at most max characters with its middle replaced by an ellipsis.</summary>
        public static string Cut(string s, int max)
        {
            if (s == null || s.Length <= max) return s ?? "";
            if (max < 3) return s.Substring(0, Math.Max(0, max));
            int left = (max - 1) / 2, right = max - 1 - left;
            return s.Substring(0, left) + "…" + s.Substring(s.Length - right);
        }
    }
}
