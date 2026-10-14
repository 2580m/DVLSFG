using System;
using System.IO;
using UnityEditor;
using UnityEngine;
public static class Fsr3BundleBuilder {
    public static void Build() {
        PlayerSettings.stripEngineCode=false;
        File.WriteAllText("Assets/fsr3-version.txt","FSR 3.1, Unity 2019 DX11 port");
        AssetDatabase.ImportAsset("Assets/fsr3-version.txt");
        var version=AssetImporter.GetAtPath("Assets/fsr3-version.txt");
        version.assetBundleName="dv-scaling-fsr3";
        version.SaveAndReimport();
        var temporal=AssetImporter.GetAtPath("Assets/Shaders/TemporalCapture.shader");
        temporal.assetBundleName="dv-scaling-fsr3"; temporal.SaveAndReimport();
        foreach(var file in Directory.GetFiles("Assets/Shaders","*fsr3upscaler*.compute")) {
            var path=file.Replace('\\','/');
            var importer=AssetImporter.GetAtPath(path);
            importer.assetBundleName="dv-scaling-fsr3";
            importer.SaveAndReimport();
        }
        var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../bundle"));
        Directory.CreateDirectory(output);
        var manifest=BuildPipeline.BuildAssetBundles(output,BuildAssetBundleOptions.ForceRebuildAssetBundle|BuildAssetBundleOptions.AssetBundleStripUnityVersion,BuildTarget.StandaloneWindows64);
        if(manifest==null) throw new Exception("FSR3 AssetBundle build failed");
    }
}
