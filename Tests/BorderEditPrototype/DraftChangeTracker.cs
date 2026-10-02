namespace Aoc.BorderEditPrototype
{
    // Disk-save baseline, independent of the state used by Discard when reopening the editor.
    internal sealed class DraftChangeTracker
    {
        private EditSnapshot _saved;
        private BorderGraph _graph;
        private int _revision=-1;
        private bool _dirty;
        internal DraftChangeTracker(EditSnapshot saved){MarkSaved(saved);}
        internal void MarkSaved(EditSnapshot saved){_saved=saved.Copy();_graph=null;_revision=-1;}
        internal bool IsDirty(BorderGraph graph)
        {
            if(!ReferenceEquals(_graph,graph)||_revision!=graph.Revision)
            { _graph=graph;_revision=graph.Revision;_dirty=!BorderGraph.Equal(_saved,graph.Current); }
            return _dirty;
        }
    }
}
