param([Parameter(Mandatory=$true)][string]$InputImage)
$ErrorActionPreference='Stop'
Add-Type -TypeDefinition @'
using System;
using System.IO;
public static class FrameGenerationCaptureCheck {
    public static string Compare(string path) {
        byte[] a=File.ReadAllBytes(path),b=File.ReadAllBytes(path.Replace("-input.bmp","-generated.bmp"));
        byte[] mask=File.ReadAllBytes(path.Replace("-input.bmp","-overlay-mask.bmp"));
        int w=BitConverter.ToInt32(a,18),h=BitConverter.ToInt32(a,22),start=BitConverter.ToInt32(a,10);
        int total=0,preserved=0,changed=0;
        // Exclude the OSD and ordinary HUD margins from the world-motion region.
        for(int y=120;y<Math.Abs(h)-120;y++) for(int x=100;x<w-100;x++) {
            int at=start+((h<0 ? y : h-1-y)*w+x)*4,delta=0;
            if(mask[at]>127) preserved++;
            for(int c=0;c<3;c++) delta=Math.Max(delta,Math.Abs(a[at+c]-b[at+c]));
            if(delta>10) changed++; total++;
        }
        double fraction=(double)preserved/total;
        string result="World pixels="+total+", wrongly preserved="+preserved+" ("+(fraction*100).ToString("F3")+"%), interpolated differences="+changed;
        if(fraction>.01) throw new Exception("Scene misclassified as external overlay: "+result);
        if(changed<1000) throw new Exception("Final world image lacks motion interpolation: "+result);
        return "PASS "+result;
    }
}
'@
[FrameGenerationCaptureCheck]::Compare($InputImage)
