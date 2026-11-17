// ATLAS 3D (Unity) - a tiny JSON reader (no Unity types), shared by the
// Workshop list and the Blockbench / Minecraft importers.
using System.Collections.Generic;

namespace Atlas
{
    /// Tiny JSON reader (objects, arrays, strings, numbers, true / false / null).
    public static class MiniJson
    {
        public static object Parse(string s)
        {
            int i = 0;
            try { return Value(s, ref i); }
            catch (System.Exception) { return null; }
        }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>();
                i++;
                Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    string k = Str(s, ref i);
                    Ws(s, ref i);
                    i++;   // :
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++;   // }
                    return d;
                }
            }
            if (c == '[')
            {
                var l = new List<object>();
                i++;
                Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++;   // ]
                    return l;
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
            if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
            if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            double v;
            double.TryParse(s.Substring(st, i - st), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
            return v;
        }

        static string Str(string s, ref int i)
        {
            var sb = new System.Text.StringBuilder();
            i++;   // "
            while (i < s.Length && s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)System.Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            i++;
            return sb.ToString();
        }
    }
}
