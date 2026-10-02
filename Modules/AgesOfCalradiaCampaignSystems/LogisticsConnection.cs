using System;
using System.Linq;
using System.Reflection;
namespace AgesOfCalradia.CampaignSystems
{
    public sealed class LogisticsSnapshot
    {
        public string Status { get; private set; }
        public int? Reserve { get; private set; }
        public bool SupportsWorkshopTransport { get { return false; } }
        internal LogisticsSnapshot(string status,int? reserve) { Status=status;Reserve=reserve; }
    }
    public sealed class LogisticsConnection
    {
        private readonly Func<Type> _resolve;
        public LogisticsConnection() : this(Resolve) { }
        private static Type Resolve()
        {
            var assembly=AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a=>a.GetName().Name=="AgesOfCalradiaLogistics");
            return assembly==null?null:assembly.GetType("AgesOfCalradiaLogistics.CampaignSystemsApi",false);
        }
        internal LogisticsConnection(Func<Type> resolver) { _resolve=resolver; }
        // Main-thread read-only query. Resolve on demand so optional provider load order is safe.
        public LogisticsSnapshot Read(string partyId)
        {
            if(string.IsNullOrWhiteSpace(partyId)) throw new ArgumentException("Party ID is required", "partyId");
            try
            {
                var type=_resolve();
                if(type==null) return new LogisticsSnapshot("not-installed-or-no-api",null);
                var version=type.GetProperty("ApiVersion",BindingFlags.Public|BindingFlags.Static);
                var read=type.GetMethod("ReadReserve",BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(string)},null);
                if(version==null || version.PropertyType!=typeof(int) || version.GetGetMethod()==null
                    || version.GetIndexParameters().Length!=0 || read==null || read.ContainsGenericParameters
                    || read.ReturnType!=typeof(int) || (int)version.GetValue(null,null)!=1)
                    return new LogisticsSnapshot("unsupported-api",null);
                int result=(int)read.Invoke(null,new object[]{partyId});
                if(result==-1) return new LogisticsSnapshot("untracked-or-no-campaign",null);
                if(result<0 || result>100) throw new InvalidOperationException("Invalid logistics reserve");
                return new LogisticsSnapshot("available",result);
            }
            catch(Exception ex)
            { System.Diagnostics.Trace.WriteLine("Logistics optional integration failed: "+ex);return new LogisticsSnapshot("provider-error",null); }
        }
    }
}
