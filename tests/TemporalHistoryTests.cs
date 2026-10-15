using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using DVLSFG;
class TemporalHistoryTests {
    static void Assert(bool value,string message) {if(!value) throw new Exception(message);}
    static int Main(string[] args) {
        Assert(TemporalHistory.ProjectionDiscontinuity(50,20,1f/90),"Instant zoom-in cut was not reset");
        Assert(TemporalHistory.ProjectionDiscontinuity(20,50,1f/90),"Instant zoom-out cut was not reset");
        Assert(TemporalHistory.ProjectionDiscontinuity(0,50,1f/90),"Uninitialized projection was accepted");
        Assert(TemporalHistory.ProjectionDiscontinuity(float.NaN,50,1f/90),"NaN projection was accepted");
        Assert(TemporalHistory.ProjectionDiscontinuity(50,float.PositiveInfinity,1f/90),"Infinite projection was accepted");
        Assert(!TemporalHistory.ProjectionDiscontinuity(50,50,1f/90),"Unchanged view was reset");
        var time=new List<double>(); var fov=new List<float>();
        foreach(string line in File.ReadAllLines(args[0])) {
            if(line.StartsWith("qpc,")) continue;
            if(string.IsNullOrWhiteSpace(line)) continue;
            var fields=line.Split(','); time.Add(double.Parse(fields[1],CultureInfo.InvariantCulture)); fov.Add(float.Parse(fields[3],CultureInfo.InvariantCulture));
        }
        Assert(time.Count>100,"Measured native zoom reference missing");
        foreach(int rate in new[]{30,60,90,144}) {
            int index=0,changes=0; float previous=fov[0];
            for(double t=time[0]+1.0/rate;t<time[time.Count-1];t+=1.0/rate) {
                while(index+1<time.Count-1 && time[index+1]<t) index++;
                double blend=(t-time[index])/(time[index+1]-time[index]);
                float next=(float)(fov[index]+(fov[index+1]-fov[index])*blend);
                Assert(!TemporalHistory.ProjectionDiscontinuity(previous,next,1f/rate),"Native smooth zoom was reset at "+rate+" FPS, t="+t);
                if(Math.Abs(next-previous)>.1f) changes++;
                previous=next;
            }
            Assert(changes>20,"Reference did not exercise zoom transitions");
            Console.WriteLine("PASS measured native zoom at "+rate+" FPS: "+changes+" changing samples, zero projection resets");
        }
        Console.WriteLine("PASS abrupt cuts and invalid projection protection"); return 0;
    }
}
