using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Aoc.BorderEditPrototype
{
    // Canonical, renderer-bound topology evidence. It deliberately omits mesh Z:
    // terrain elevation must not make an otherwise identical political graph differ.
    internal sealed class TopologyFingerprint
    {
        internal string Signature;
        internal string[] Tokens;

        // Separate diagnostic schema: IEEE754 Single bits preserve coordinates
        // exactly. Quantized topology keys must never be used as geometry.
        internal static TopologyFingerprint CreateExact(string rendererHash, Point2[] points, BorderEdge[] edges)
        {
            var canonical=Create(rendererHash,points,edges);
            var tokens=new List<string> { "schema=exact-xy-ieee754-v1" };
            for(int i=0;i<points.Length;i++)
                tokens.Add("nodebits="+i.ToString(CultureInfo.InvariantCulture)+":"+Bits(points[i].X)+","+Bits(points[i].Y));
            for(int i=points.Length;i<canonical.Tokens.Length;i++) tokens.Add(canonical.Tokens[i]);
            var input=new StringBuilder(rendererHash);
            foreach(string token in tokens) input.Append('|').Append(token);
            return new TopologyFingerprint { Signature=DraftStore.Hash(input.ToString()),Tokens=tokens.ToArray() };
        }

        private static string Bits(float value) => BitConverter.ToUInt32(BitConverter.GetBytes(value),0).ToString("X8",CultureInfo.InvariantCulture);

        internal static TopologyFingerprint Create(string rendererHash, Point2[] points, BorderEdge[] edges)
        {
            if (string.IsNullOrWhiteSpace(rendererHash) || points == null || edges == null)
                throw new ArgumentException("Topology fingerprint input is incomplete.");

            var tokens = new List<string>(points.Length + edges.Length);
            for (int i = 0; i < points.Length; i++)
            {
                int x = (int)Math.Round(points[i].X * 1000f);
                int y = (int)Math.Round(points[i].Y * 1000f);
                tokens.Add("node=" + i.ToString(CultureInfo.InvariantCulture) + ":"
                    + x.ToString(CultureInfo.InvariantCulture) + ","
                    + y.ToString(CultureInfo.InvariantCulture));
            }
            for (int i = 0; i < edges.Length; i++)
            {
                BorderEdge edge = edges[i];
                tokens.Add("edge=" + i.ToString(CultureInfo.InvariantCulture) + ":"
                    + edge.A.ToString(CultureInfo.InvariantCulture) + ","
                    + edge.B.ToString(CultureInfo.InvariantCulture) + ",row="
                    + edge.Row.ToString(CultureInfo.InvariantCulture) + ",left="
                    + edge.Left.ToString(CultureInfo.InvariantCulture) + ",right="
                    + edge.Right.ToString(CultureInfo.InvariantCulture));
            }
            var input = new StringBuilder(rendererHash);
            foreach (string token in tokens) input.Append('|').Append(token);
            return new TopologyFingerprint { Signature = DraftStore.Hash(input.ToString()), Tokens = tokens.ToArray() };
        }

        internal static string FirstDifference(string[] earlier, string[] later)
        {
            earlier = earlier ?? Array.Empty<string>();
            later = later ?? Array.Empty<string>();
            int shared = Math.Min(earlier.Length, later.Length);
            for (int i = 0; i < shared; i++)
                if (!string.Equals(earlier[i], later[i], StringComparison.Ordinal))
                    return "index=" + i + ";earlier=" + earlier[i] + ";later=" + later[i];
            if (earlier.Length != later.Length)
                return "index=" + shared + ";earlier="
                    + (shared < earlier.Length ? earlier[shared] : "<end>") + ";later="
                    + (shared < later.Length ? later[shared] : "<end>");
            return "none";
        }
    }
}
