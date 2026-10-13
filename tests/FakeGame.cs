using System;
using System.IO;
class FakeGame {
    static void Main(string[] args) {
        File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"launch-capture.txt"),new[]{
            string.Join(" ",args),
            "DVLSFG_CONFIG="+Environment.GetEnvironmentVariable("DVLSFG_CONFIG"),
            "VK_INSTANCE_LAYERS="+Environment.GetEnvironmentVariable("VK_INSTANCE_LAYERS"),
            "VK_ADD_LAYER_PATH="+Environment.GetEnvironmentVariable("VK_ADD_LAYER_PATH"),
            "DVLSFG_NO_FRAMEGEN="+Environment.GetEnvironmentVariable("DVLSFG_NO_FRAMEGEN")
        });
    }
}
