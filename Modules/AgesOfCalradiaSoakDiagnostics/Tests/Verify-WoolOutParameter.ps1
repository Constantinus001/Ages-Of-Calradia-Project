$ErrorActionPreference='Stop'
$harmonyPath=Join-Path $env:USERPROFILE '.nuget/packages/lib.harmony/2.4.2/lib/net472/0Harmony.dll'
[Reflection.Assembly]::LoadFrom($harmonyPath)|Out-Null
Add-Type -ReferencedAssemblies @($harmonyPath,'C:/Program Files (x86)/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.7.2/Facades/netstandard.dll') -TypeDefinition @'
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
public static class WoolOutFixture {
 private struct Price { internal readonly float Average, Minimum; public Price(float a,float m){Average=a;Minimum=m;} }
 public static float Seen; public static int Calls; public static bool Throw;
 [MethodImpl(MethodImplOptions.NoInlining)] private static bool Lookup(out Price price) { Calls++; if(Throw) throw new InvalidOperationException("native-sentinel"); price=new Price(1.25f,0.75f);return true; }
 public static MethodInfo Target { get { return typeof(WoolOutFixture).GetMethod("Lookup",BindingFlags.Static|BindingFlags.NonPublic); } }
 [MethodImpl(MethodImplOptions.NoInlining)] public static float Run() { Price price; if(!Lookup(out price)) throw new Exception("bool changed");return price.Average; }
 public static void Broken(object[] __args) { Seen=((Price)__args[0]).Average; }
 public static void Observe<T>(T __0) { Seen=(float)typeof(T).GetField("Average",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(__0); }
 public static MethodInfo Fixed { get { return typeof(WoolOutFixture).GetMethod("Observe").MakeGenericMethod(typeof(Price)); } }
}
'@
$h=New-Object HarmonyLib.Harmony('aoc.wool.out.fixture')
try {
 if([WoolOutFixture]::Run() -ne 1.25){throw 'Baseline wrong'}
 $h.Patch([WoolOutFixture]::Target,$null,(New-Object HarmonyLib.HarmonyMethod([WoolOutFixture].GetMethod('Broken'))),$null,$null)|Out-Null
 $broken=[WoolOutFixture]::Run()
 Write-Output "Old object-array postfix: caller=$broken observed=$([WoolOutFixture]::Seen)"
 $h.UnpatchAll('aoc.wool.out.fixture')
 $h.Patch([WoolOutFixture]::Target,$null,(New-Object HarmonyLib.HarmonyMethod([WoolOutFixture]::Fixed)),$null,$null)|Out-Null
 [WoolOutFixture]::Calls=0
 for($i=0;$i -lt 10;$i++){if([WoolOutFixture]::Run() -ne 1.25 -or [WoolOutFixture]::Seen -ne 1.25){throw 'Typed postfix changed out value'}}
 if([WoolOutFixture]::Calls -ne 10){throw 'Original called more than once'}
 [WoolOutFixture]::Throw=$true
 $caught=$false
 try{[WoolOutFixture]::Run()|Out-Null}catch{if($_.Exception.ToString() -match 'native-sentinel'){$caught=$true}else{throw}}
 if(-not $caught){throw 'Original exception suppressed'}
 Write-Output 'PASS: typed value-copy observation preserves private out struct, return, call count and original exception.'
}finally{$h.UnpatchAll('aoc.wool.out.fixture')}
