using System;
using System.Runtime.InteropServices;
using System.Diagnostics;
class Resources {
    [DllImport("kernel32",CharSet=CharSet.Unicode)] static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
    [DllImport("kernel32")] static extern bool EnumResourceNames(IntPtr module,IntPtr type,Callback callback,IntPtr param);
    delegate bool Callback(IntPtr module,IntPtr type,IntPtr name,IntPtr param);
    [DllImport("kernel32")] static extern IntPtr FindResource(IntPtr module,IntPtr name,IntPtr type);
    [DllImport("kernel32")] static extern uint SizeofResource(IntPtr module,IntPtr resource);
    [DllImport("kernel32")] static extern IntPtr LoadResource(IntPtr module,IntPtr resource);
    [DllImport("kernel32")] static extern IntPtr LockResource(IntPtr resource);
    static void Main(string[] args) {
        Console.WriteLine(FileVersionInfo.GetVersionInfo(args[0]).FileVersion);
        IntPtr mod = LoadLibraryEx(args[0],IntPtr.Zero,0x22);
        EnumResourceNames(mod,new IntPtr(10),delegate(IntPtr m,IntPtr t,IntPtr n,IntPtr p) {
            IntPtr r=FindResource(m,n,t); uint size=SizeofResource(m,r);
            IntPtr data=LockResource(LoadResource(m,r)); byte[] magic=new byte[Math.Min(8,(int)size)]; Marshal.Copy(data,magic,0,magic.Length);
            byte[] blob=new byte[(int)size]; Marshal.Copy(data,blob,0,blob.Length); uint hash=0x811c9dc5; foreach(byte b in blob) { hash^=b; hash=unchecked(hash*0x01000193); } Console.WriteLine(n.ToInt64()+" "+size+" "+hash.ToString("x8")); return true;
        },IntPtr.Zero);
    }
}
