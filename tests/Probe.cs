using System;
using System.Text;
using System.Runtime.InteropServices;
class Probe {
    [DllImport("kernel32", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibrary(string path);
    [DllImport("kernel32", CharSet=CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Check(IntPtr path, StringBuilder message, uint capacity);
    static int Main(string[] args) {
        if (args.Length != 2) return 2;
        var module = LoadLibrary(args[0]);
        if (module == IntPtr.Zero) throw new Exception("LoadLibrary: " + Marshal.GetLastWin32Error());
        var check = (Check)Marshal.GetDelegateForFunctionPointer(GetProcAddress(module,"DVLSFG_Probe"),typeof(Check));
        byte[] bytes = Encoding.UTF8.GetBytes(args[1] + "\0");
        IntPtr path = Marshal.AllocHGlobal(bytes.Length); Marshal.Copy(bytes,0,path,bytes.Length);
        try { var message = new StringBuilder(4096); int result = check(path,message,4096); Console.WriteLine(message); return result == 1 ? 0 : 1; }
        finally { Marshal.FreeHGlobal(path); }
    }
}
