namespace Aoc.BorderEditPrototype
{
    // A click selects without creating an accidental edit from hand jitter.
    internal sealed class DragIntent
    {
        private Point2 _origin;
        private float _threshold;
        private bool _moving;
        internal void Begin(Point2 origin,float threshold)
        { _origin=origin;_threshold=threshold;_moving=false; }
        internal bool Update(Point2 pointer)
        { if(pointer.Finite && (pointer-_origin).Length>=_threshold)_moving=true;return _moving; }
    }
}
