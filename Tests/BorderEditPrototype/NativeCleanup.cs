using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Engine;

namespace Aoc.BorderEditPrototype
{
    // Scene teardown boundary: failure on one object must not skip restoration of the other rows.
    internal static class NativeCleanup
    {
        private static readonly Dictionary<GameEntity,int> Pending=new Dictionary<GameEntity,int>();
        internal static bool Try(Action action,string description)
        {
            try { action(); return true; }
            catch(Exception error) { PrototypeLog.Write(description+": "+error); return false; }
        }
        internal static void Release(GameEntity entity)
        {
            if(entity==null || Pending.ContainsKey(entity)) return;
            Pending.Add(entity,0); Remove(entity);
        }
        private static void Remove(GameEntity entity)
        {
            int attempt=Pending[entity];
            try { entity.SetVisibilityExcludeParents(false); }
            catch(Exception error) { if(attempt==0) PrototypeLog.Write("Entity hide failed; attempting removal: "+error); }
            try { entity.Remove(0); Pending.Remove(entity); }
            catch(Exception error)
            { Pending[entity]=attempt+1; if(attempt==0 || attempt==4) PrototypeLog.Write("Entity removal deferred: "+error); }
        }
        internal static void Drain()
        { foreach(GameEntity entity in Pending.Keys.Take(16).ToArray()) Remove(entity); }
    }
}
