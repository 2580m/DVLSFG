param([Parameter(Mandatory=$true)][uint32]$ProcessId,[int]$Samples=8,[string]$ApiDll='W:\Application\MSI Afterburner\RTSS\RivaTuner Statistics Server\Plugins\Client\PresentMonDataProvider\PresentMonAPI2Loader.dll')
$ErrorActionPreference='Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PMReadOnly {
 [DllImport("kernel32",CharSet=CharSet.Unicode)] static extern IntPtr LoadLibraryEx(string name,IntPtr file,uint flags);
 [DllImport("kernel32",CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module,string name);
 [StructLayout(LayoutKind.Sequential)] public struct Element { public int metric,stat; public uint device,index; public ulong offset,size; }
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Open(out IntPtr session);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Track(IntPtr session,uint pid);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Register(IntPtr session,out IntPtr query,[In,Out] Element[] elements,ulong count,double window,double offset);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Poll(IntPtr query,uint pid,[Out] byte[] blob,ref uint count);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Close(IntPtr session);
 static T Bind<T>(IntPtr module,string name) { var address=GetProcAddress(module,name); if(address==IntPtr.Zero) throw new Exception("Missing "+name); return (T)(object)Marshal.GetDelegateForFunctionPointer(address,typeof(T)); }
 static void Check(int result,string operation) { if(result!=0) throw new Exception(operation+": PresentMon status "+result); }
 public static void Run(string dll,uint pid,int samples) {
  var module=LoadLibraryEx(dll,IntPtr.Zero,8); if(module==IntPtr.Zero) throw new Exception("Cannot load PresentMon API: "+Marshal.GetLastWin32Error());
  var open=Bind<Open>(module,"pmOpenSession"); var close=Bind<Close>(module,"pmCloseSession");
  IntPtr session; Check(open(out session),"Open existing service session");
  try {
   var start=Bind<Track>(module,"pmStartTrackingProcess"); Check(start(session,pid),"Track process");
   try {
    var elements=new[]{new Element{metric=11,stat=1},new Element{metric=12,stat=1}};
    IntPtr query; Check(Bind<Register>(module,"pmRegisterDynamicQuery")(session,out query,elements,2,1000,0),"Register display/present FPS");
    try {
     if(elements[0].size!=8 || elements[1].size!=8) throw new Exception("Unexpected metric data sizes");
     var poll=Bind<Poll>(module,"pmPollDynamicQuery"); var buffer=new byte[65536]; var stride=elements[1].offset+elements[1].size;
     Console.WriteLine("Sample,Swapchain,DisplayedFPS,PresentedFPS");
     for(int sample=0;sample<samples;sample++) {
      System.Threading.Thread.Sleep(1000); uint count=16; Check(poll(query,pid,buffer,ref count),"Poll");
      for(uint chain=0;chain<count;chain++) Console.WriteLine(sample+","+chain+","+BitConverter.ToDouble(buffer,checked((int)(chain*stride+elements[0].offset))).ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+","+BitConverter.ToDouble(buffer,checked((int)(chain*stride+elements[1].offset))).ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
     }
    } finally { Bind<Close>(module,"pmFreeDynamicQuery")(query); }
   } finally { Bind<Track>(module,"pmStopTrackingProcess")(session,pid); }
  } finally { close(session); }
 }
}
'@
[PMReadOnly]::Run($ApiDll,$ProcessId,$Samples)
