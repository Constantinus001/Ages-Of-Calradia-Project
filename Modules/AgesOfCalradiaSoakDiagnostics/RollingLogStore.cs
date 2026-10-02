using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace AgesOfCalradia.SoakDiagnostics
{
    // Diagnostics-only persistence, never campaign save data. Owns precisely two
    // named files. A foreign campaign, rollback, interrupted write or malformed
    // checkpoint is preserved and rejected rather than silently overwritten.
    internal sealed class RollingLogStore
    {
        private const string Magic="AOC_FRAMEWORK_LOG_V1";
        internal readonly string Path;
        private readonly string _statePath, _campaign;
        internal double Anchor { get; private set; }
        internal bool Append { get; private set; }
        internal long Length { get; private set; }
        internal RollingLogStore(string directory,string campaign,double day)
        {
            if(string.IsNullOrWhiteSpace(campaign)||!EconomyLedger.Finite(day)||day<0)
                throw new ArgumentException("Invalid rolling campaign identity/time");
            Path=System.IO.Path.Combine(directory,"AocFramework-current.tsv");
            _statePath=Path+".state";_campaign=campaign;Anchor=day;
            bool log=File.Exists(Path),state=File.Exists(_statePath);
            if(log!=state)throw new InvalidDataException("Unpaired framework log/checkpoint; preserved for inspection");
            if(!log)return;
            var data=File.ReadAllLines(_statePath,Encoding.UTF8);
            if(data.Length!=6||data[0]!=Magic)throw new InvalidDataException("Unknown framework log checkpoint");
            if(data[5]!="closed")throw new InvalidDataException("Interrupted framework period; preserved for inspection");
            string previous=Encoding.UTF8.GetString(Convert.FromBase64String(data[1]));
            double anchor=double.Parse(data[2],CultureInfo.InvariantCulture),last=double.Parse(data[3],CultureInfo.InvariantCulture);
            long length=long.Parse(data[4],CultureInfo.InvariantCulture);
            if(!EconomyLedger.Finite(anchor)||!EconomyLedger.Finite(last)||anchor<0||last<anchor||length<0
                ||new FileInfo(Path).Length!=length)
                throw new InvalidDataException("Incomplete framework log/checkpoint; preserved for inspection");
            if(previous!=campaign)throw new InvalidDataException("Different campaign; existing framework log preserved");
            if(day<last)throw new InvalidDataException("Campaign rollback; existing framework log preserved");
            if(length>=SupplyCapture.ByteLimit)throw new InvalidDataException("Framework log byte limit reached; no automatic continuation");
            Append=day-anchor<SupplyCapture.TargetDays;
            Anchor=Append?anchor:day;Length=Append?length:0;
        }
        internal FileStream Open()
        {
            if(Append)return new FileStream(Path,FileMode.Append,FileAccess.Write,FileShare.Read);
            // Atomic replacement: a failed creation cannot truncate the old log.
            string temporary=Path+".new-"+Guid.NewGuid().ToString("N");
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){}
            if(File.Exists(Path))File.Replace(temporary,Path,null);
            else File.Move(temporary,Path);
            return new FileStream(Path,FileMode.Append,FileAccess.Write,FileShare.Read);
        }
        internal void Checkpoint(double day,bool closed=false)
        {
            if(!EconomyLedger.Finite(day)||day<Anchor)throw new InvalidDataException("Invalid framework checkpoint day");
            Length=new FileInfo(Path).Length;
            string temporary=_statePath+".new-"+Guid.NewGuid().ToString("N");
            File.WriteAllLines(temporary,new[]{Magic,Convert.ToBase64String(Encoding.UTF8.GetBytes(_campaign)),
                SupplyCapture.N(Anchor),SupplyCapture.N(day),Length.ToString(CultureInfo.InvariantCulture),closed?"closed":"open"},new UTF8Encoding(false));
            if(File.Exists(_statePath))File.Replace(temporary,_statePath,null);
            else File.Move(temporary,_statePath);
        }
    }
}
