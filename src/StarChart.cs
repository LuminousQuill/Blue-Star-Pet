using System;
using System.Windows;

namespace BlueStar
{
    sealed class StarChart
    {
        public readonly Point[] Stars;
        public readonly int CloseTo;
        readonly int[] parents;

        StarChart(Point[] stars,int closeTo=-1,int[] parents=null)
        { Stars=stars; CloseTo=closeTo; this.parents=parents; }

        public int Lines { get {return Stars.Length-1+(CloseTo>=0?1:0);} }
        public double Beat { get {return 1.48/Lines;} }
        public double Stroke(double age) {return (age-0.12)/Beat;}
        public int From(int line) {return line==Stars.Length-1 || parents==null?line:parents[line];}
        public int To(int line) {return line==Stars.Length-1?CloseTo:line+1;}

        public Point PenAt(double age)
        {
            double stroke=Stroke(age);
            int i=Math.Max(0,Math.Min(Lines-1,(int)Math.Floor(stroke)));
            double u=stroke-i;
            Point start=Stars[From(i)],end=Stars[To(i)];
            Point tip=start+(end-start)*IdleMotion.Ease(u);
            // Lift the nib back to a branch point without drawing a false connecting line.
            if(i>0 && To(i-1)!=From(i))
            {
                Point previous=Stars[To(i-1)];
                return previous+(tip-previous)*IdleMotion.Ease(u/0.38);
            }
            return tip;
        }

        public static readonly StarChart[] Shapes={
            // Zigzag, dipper, swan, hourglass, crown and crescent. Points are in drawing order.
            new StarChart(new[]{new Point(-30,16),new Point(-12,-8),new Point(9,4),new Point(23,-19)}),
            new StarChart(new[]{new Point(-33,-13),new Point(-23,-9),new Point(-15,1),new Point(-4,5),
                new Point(3,19),new Point(24,13),new Point(29,-4)},3),
            new StarChart(new[]{new Point(-8,19),new Point(1,1),new Point(-16,-5),new Point(-32,-16),
                new Point(16,7),new Point(31,1),new Point(10,-19)},parents:new[]{0,1,2,1,4,1}),
            new StarChart(new[]{new Point(-25,-17),new Point(24,-17),new Point(0,0),
                new Point(-23,17),new Point(24,17)},0),
            new StarChart(new[]{new Point(-25,14),new Point(-32,-13),new Point(-13,0),new Point(0,-19),
                new Point(13,0),new Point(32,-13),new Point(25,14)},0),
            new StarChart(new[]{new Point(-24,-13),new Point(-30,1),new Point(-20,14),new Point(0,19),
                new Point(18,10),new Point(27,-12),new Point(11,0),new Point(-8,2)},0)
        };
    }
}
