using System;
namespace DVLSFG {
    internal static class TemporalHistory {
        internal static bool ProjectionDiscontinuity(float previousFov,float currentFov,float deltaSeconds) {
            if(!ValidFov(previousFov) || !ValidFov(currentFov) || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds)) return true;
            // Motion vectors include ordinary projection changes. Compare optical
            // magnification, not a tiny angular delta: native SmoothDamp zoom must
            // keep its history, while an abrupt lens/camera cut still invalidates it.
            double previous=Math.Tan(previousFov*Math.PI/360);
            double current=Math.Tan(currentFov*Math.PI/360);
            double allowance=.2+2*Math.Min(Math.Max(deltaSeconds,0),.1);
            return Math.Abs(Math.Log(previous/current))>allowance;
        }
        static bool ValidFov(float value) {return value>0 && value<180 && !float.IsInfinity(value);}
    }
}
