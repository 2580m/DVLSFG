param([Parameter(Mandatory=$true)][string]$InputImage,[int]$Width=600,[int]$Height=90)
$ErrorActionPreference='Stop'
Add-Type -TypeDefinition @'
using System;
using System.IO;
public static class OverlayCaptureCheck {
    public static string Compare(string path,int regionWidth,int regionHeight) {
        byte[] a=File.ReadAllBytes(path),b=File.ReadAllBytes(path.Replace("-input.bmp","-generated.bmp"));
        int w=BitConverter.ToInt32(a,18),h=BitConverter.ToInt32(a,22),start=BitConverter.ToInt32(a,10);
        int maxError=0,mismatches=0,overlayPixels=0;
        int width=Math.Min(w,regionWidth),height=Math.Min(Math.Abs(h),regionHeight);
        int background=start+((h<0 ? height-1 : h-height)*w+width-1)*4;
        for(int y=0;y<height;y++) for(int x=0;x<width;x++) {
            int at=start+((h<0 ? y : h-1-y)*w+x)*4,delta=0,contrast=0;
            for(int c=0;c<3;c++) {
                delta=Math.Max(delta,Math.Abs(a[at+c]-b[at+c]));
                contrast=Math.Max(contrast,Math.Abs(a[at+c]-a[background+c]));
            }
            if(delta>2) mismatches++; maxError=Math.Max(maxError,delta);
            if(contrast>20) overlayPixels++;
        }
        if(overlayPixels<100) throw new Exception("No visible overlay in test region");
        if(mismatches>0) throw new Exception("Overlay differs: pixels="+mismatches+" max RGB error="+maxError);
        return "PASS overlay region: "+width+"x"+height+", visible pixels="+overlayPixels+", max RGB error="+maxError;
    }
}
'@
[OverlayCaptureCheck]::Compare($InputImage,$Width,$Height)
