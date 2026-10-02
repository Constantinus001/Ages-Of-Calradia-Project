param(
    [string]$CoreModule = 'C:\Program Files\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\AOC CORE',
    [string]$PreviousDraft
)
$ErrorActionPreference='Stop'
$assets=Join-Path $CoreModule 'AuthoredBorders'
if([string]::IsNullOrWhiteSpace($PreviousDraft)) { $PreviousDraft=Join-Path $PSScriptRoot '..\output\deployment-backups\published-eb567-rebase-20260922-121441\F1B414DB1DC9677436F1AADA3E93606DBEDEB60F52E797417C379265B399FDF9.xml' }
[xml]$previous=Get-Content -LiteralPath $PreviousDraft -Raw
$topology='F1B414DB1DC9677436F1AADA3E93606DBEDEB60F52E797417C379265B399FDF9'
$currentPath=Join-Path $assets ($topology+'.xml')
$repairPath=Join-Path $assets 'Repair.xml'
[xml]$current=Get-Content -LiteralPath $currentPath -Raw
[xml]$repair=Get-Content -LiteralPath $repairPath -Raw
function Assert-True([bool]$condition,[string]$message) { if(!$condition){throw $message} }
Assert-True ($current.DocumentElement.GetAttribute('topology') -eq $topology) 'Published layout topology changed.'
$previousPoints=@{}; foreach($point in $previous.DocumentElement.Point){$previousPoints[[int]$point.id]="$($point.x),$($point.y)"}
$currentPoints=@{}; foreach($point in $current.DocumentElement.Point){$currentPoints[[int]$point.id]="$($point.x),$($point.y)"}
$original=[int]$previous.DocumentElement.GetAttribute('originalPoints')
Assert-True ($previousPoints.Count -eq $currentPoints.Count -and @($previousPoints.Keys|Where-Object {$currentPoints[$_] -ne $previousPoints[$_]}).Count -eq 0) 'A saved point differs from the reviewed source.'
$connection=@($current.DocumentElement.SelectNodes('Bridge[@a="5343" and @b="2061" and @left="4285997590" and @right="4285997590" and @authored="false"]'))
Assert-True ($connection.Count -eq 1) 'Expected exactly one reviewed connection.'
$ids=@('5343','2061');$a=$currentPoints[[int]$ids[0]].Split(',')|ForEach-Object {[double]$_};$b=$currentPoints[[int]$ids[1]].Split(',')|ForEach-Object {[double]$_}
if(-not ('PublishedRepairGeometry' -as [type])) {
Add-Type -TypeDefinition @'
using System;
public static class PublishedRepairGeometry {
    static double Cross(double[] a,double[] b,double[] c) {
        return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0]);
    }
    static double PointDistance(double[] p,double[] a,double[] b) {
        double x=b[0]-a[0],y=b[1]-a[1],length=x*x+y*y;
        double t=length==0?0:Math.Max(0,Math.Min(1,((p[0]-a[0])*x+(p[1]-a[1])*y)/length));
        x=p[0]-a[0]-t*x;y=p[1]-a[1]-t*y;return Math.Sqrt(x*x+y*y);
    }
    static double Segments(double[] a,double[] b,double[] c,double[] d) {
        double ac=Cross(a,b,c),ad=Cross(a,b,d),ca=Cross(c,d,a),cb=Cross(c,d,b);
        if(((ac>0&&ad<0)||(ac<0&&ad>0))&&((ca>0&&cb<0)||(ca<0&&cb>0))) return 0;
        return Math.Min(Math.Min(PointDistance(a,c,d),PointDistance(b,c,d)),Math.Min(PointDistance(c,a,b),PointDistance(d,a,b)));
    }
    static bool Inside(double[] p,double[] a,double[] b,double[] c) {
        if(Cross(a,b,c)==0) return false;
        double x=Cross(a,b,p),y=Cross(b,c,p),z=Cross(c,a,p);
        return !((x<0||y<0||z<0)&&(x>0||y>0||z>0));
    }
    public static double Distance(double[] a,double[] b,double[] x,double[] y,double[] z) {
        if(Inside(a,x,y,z)||Inside(b,x,y,z)) return 0;
        return Math.Min(Segments(a,b,x,y),Math.Min(Segments(a,b,y,z),Segments(a,b,z,x)));
    }
}
'@
}
# These catch the false safety claims a vertex-only distance would permit:
# crossing a large triangle, containment, and closest approach inside an edge.
Assert-True ([PublishedRepairGeometry]::Distance(@(-200,0),@(200,0),@(-100,-100),@(100,-100),@(0,100)) -eq 0) 'Triangle crossing regression.'
Assert-True ([PublishedRepairGeometry]::Distance(@(0,0),@(1,0),@(-100,-100),@(100,-100),@(0,100)) -eq 0) 'Triangle containment regression.'
Assert-True ([Math]::Abs([PublishedRepairGeometry]::Distance(@(-1,5),@(1,5),@(-100,0),@(100,0),@(0,-100))-5) -lt 1e-9) 'Edge-interior distance regression.'
Assert-True ([PublishedRepairGeometry]::Distance(@(-1,0),@(1,0),@(-100,0),@(100,0),@(0,-100)) -eq 0) 'Collinear overlap regression.'
$nearest=[double]::PositiveInfinity
$triangles=$repair.SelectNodes('//T')
Assert-True ($triangles.Count -gt 0) 'Repair has no triangles to verify.'
foreach($triangle in $triangles) {
    $x=$triangle.GetAttribute('a').Split(',')|ForEach-Object {[double]::Parse($_,[Globalization.CultureInfo]::InvariantCulture)}
    $y=$triangle.GetAttribute('b').Split(',')|ForEach-Object {[double]::Parse($_,[Globalization.CultureInfo]::InvariantCulture)}
    $z=$triangle.GetAttribute('c').Split(',')|ForEach-Object {[double]::Parse($_,[Globalization.CultureInfo]::InvariantCulture)}
    $nearest=[Math]::Min($nearest,[PublishedRepairGeometry]::Distance($a,$b,$x,$y,$z))
}
Assert-True ($nearest -gt 50) "The added bridge is too close to reviewed fill geometry ($nearest map units); regenerate Repair.xml."
$newHash=(Get-FileHash -LiteralPath $currentPath -Algorithm SHA256).Hash
Assert-True ($repair.DocumentElement.GetAttribute('draftHash') -eq $newHash) 'Repair.xml is not bound to the reviewed F1B layout.'
Write-Output "PASS: reviewed bridge ($($ids[0]) to $($ids[1])) is present; authored points are unchanged; true distance to $($triangles.Count) reviewed fill triangles is $([Math]::Round($nearest,3)) map units."
