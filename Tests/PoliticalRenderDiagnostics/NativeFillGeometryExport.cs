using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using TaleWorlds.Library;

namespace AgesOfCalradia.PoliticalRenderDiagnostics
{
    // First-builder raw logical triangles only. One logical triangle corresponds
    // to the approved renderer's two opposite-winding submitted faces.
    internal sealed class NativeFillGeometryExport : IDisposable
    {
        private const int Capacity = 262144;
        private struct Triangle { internal Vec3 A, B, C; }
        private readonly Triangle[] _triangles = new Triangle[Capacity];
        private readonly StreamWriter _writer;
        private int _captured, _written;
        private long _seen;
        internal NativeFillGeometryExport(string path)
        {
            _writer = new StreamWriter(path);
            _writer.WriteLine("triangle,ax,ay,az,bx,by,bz,cx,cy,cz");
        }
        internal bool HasPending { get { return _written < _captured; } }
        internal void Capture(Vec3 first, Vec3 second, Vec3 third)
        {
            _seen++;
            if (_captured < Capacity) _triangles[_captured++] = new Triangle { A = first, B = second, C = third };
        }
        internal void Drain(Stopwatch timer, double deadlineMilliseconds)
        {
            while (_written < _captured && timer.Elapsed.TotalMilliseconds < deadlineMilliseconds)
            {
                Triangle t = _triangles[_written];
                _writer.WriteLine((_written + 1).ToString(CultureInfo.InvariantCulture) + "," + V(t.A) + "," + V(t.B) + "," + V(t.C));
                _written++;
            }
        }
        internal string Complete()
        {
            _writer.Flush();
            return "fillGeometryCaptured=" + _captured + ";fillGeometrySeen=" + _seen + ";fillGeometryCap=" + Capacity
                + ";fillGeometryTruncated=" + (_seen > Capacity ? "true" : "false") + ";fillGeometryWritten=" + _written
                + ";raw identity-frame logical triangles; reversed faces not duplicated in export";
        }
        private static string V(Vec3 p) { return F(p.x) + "," + F(p.y) + "," + F(p.z); }
        private static string F(float v) { return v.ToString("R", CultureInfo.InvariantCulture); }
        public void Dispose() { _writer.Dispose(); }
    }
}
