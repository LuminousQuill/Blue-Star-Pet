using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace BlueStar
{
    sealed partial class PetArt
    {
        delegate void PlaceLeg(int shape,int shade,double hx,double hy,double kx,double ky,double ax,double ay,
                               double ankle,int frontRow=int.MaxValue,bool heartThigh=false);
        int legLayout;
        bool guardRaised;
        Int32Collection rearTriangles,wholeTriangles;
        readonly MeshGeometry3D guardMesh=new MeshGeometry3D();
        // Assigned crown spokes stay fixed when an ankle rolls to the other side of its calf.
        static readonly int[] crownSpokes={11,8,14,12,0,20,2,23,3,24,15,17,4,25,7,5,19,22,18,6,16,21,1,13,10,9};
        readonly System.Windows.Controls.Viewport3D guardView;
        public void SetLegLayout(int layout)
        {
            layout=layout==2?2:1;
            if(layout==legLayout) return;
            BuildLegs(layout);
            gripPointer=null;
            Idle.RebindLegs(legs,true);
            Draw();
        }

        void UpdateGuardDepth(Vector core,Point3DCollection points)
        {
            if(legLayout!=2) return;
            // Once both guarding feet have cleared the heart during the strike, the entire
            // limb belongs to its radial spoke. Rejoin the shin to its thigh in paint order.
            bool raised=true;
            if(GrandAge>=GrandWave.Release && GrandAge<GrandWave.Release+1.4)
            {
                raised=false;
                for(int i=0;i<2;i++)
                {
                    var toe=legs[i].ToeOnMesh(positions,i*LegRig.VertexCount);
                    if((new Vector(toe.X*300,-toe.Y*300)-core).LengthSquared<60*60) raised=true;
                }
            }
            if(raised!=guardRaised)
            {
                guardRaised=raised;
                mesh.TriangleIndices=raised?rearTriangles:wholeTriangles;
                guardView.Visibility=raised?Visibility.Visible:Visibility.Hidden;
            }
            guardMesh.Positions=raised?points:null;
        }

        void BuildLegs(int layout)
        {
            legLayout=layout;
            var uv=new PointCollection();
            var indices=new Int32Collection();
            var limbs=new List<LegRig>();
            var guardIndices=new Int32Collection();
            var whole=new Int32Collection();
            if(layout==1)
            {
                // Back to front. Angles are measured from downward; every hip is hidden by the core.
                Action<int,int,double,double,double,double,double> add=(shape,shade,angle,length,mirror,x,y)=>
                    limbs.Add(new LegRig(shape,shade,angle,length,mirror,x,y,limbs.Count,uv,indices));
                add(1,0,175,87,1,-4,-9);    add(0,0,-147,102,1,5,-9);
                add(1,0,-106,114,-1,12,-1); add(0,0,-55,117,1,9,6);
                add(1,0,-16,128,-1,5,10);   add(1,0,44,122,1,-8,8);
                add(0,0,105,108,-1,-12,0);  add(1,0,137,107,-1,-8,-5);
                add(0,1,-179,106,-1,1,-11); add(1,1,-89,119,1,13,1);
                add(1,1,-46,119,1,10,9);    add(0,1,20,132,-1,-4,11);
                add(1,1,75,109,-1,-13,4);   add(0,1,120,108,1,-11,-4);
                add(1,2,-142,101,1,9,-7);   add(2,2,-17,119,1,8,7);
                add(0,2,54,120,-1,-12,7);   add(1,2,153,102,1,-7,-8);
                add(1,3,-78,94,-1,13,3);   add(0,3,1,118,1,0,12);
                add(1,4,79,116,1,0,0);      add(0,3,-160,85,-1,6,-7);
                add(0,3,130,79,-1,-7,-2);   add(1,4,-113,82,1,0,0);
                add(0,4,-37,86,-1,0,0);     add(1,3,40,84,1,-4,5);
            }
            else
            {
                // Coordinates are relative to the heart. The last pair sets the direction
                // of the calf from the authored knee. The atlas supplies the bone lengths.
                PlaceLeg leg=(shape,shade,hx,hy,kx,ky,ax,ay,ankle,frontRow,heartThigh)=>{
                    int rear=indices.Count,front=guardIndices.Count;
                    limbs.Add(LegRig.Folded(shape,shade,new Point(hx,hy),new Point(kx,ky),
                        new Vector(ax-kx,ay-ky),ankle,limbs.Count,uv,indices,guardIndices,frontRow,heartThigh));
                    for(int i=rear;i<indices.Count;i++) whole.Add(indices[i]);
                    for(int i=front;i<guardIndices.Count;i++) whole.Add(guardIndices[i]);
                };

                // Each shin crosses to the opposite lobe. The left knee passes in front.
                leg(1,2, 49,-45, 19,-73,-12,-32, 66, 9,true);
                leg(1,3,-41,-38,-18,-71, 13,-31, 63, 6);
                var upperThighs=new Int32Collection(indices);
                var upperLegs=new Int32Collection(whole);
                indices.Clear(); whole.Clear();

                // Deep knees peeking through the crown and the gaps between hanging feet.
                leg(1,0, -8, -5,-43,-55,-43,-17, -8);
                leg(0,0,  7, -8, 25,-66, -3,-28, -5);
                leg(2,0, -8, 10,-34, 45,-40, 87,  2);
                leg(0,0,  6, 14, 26, 52, 13, 98, -8);
                leg(1,0, 15,  8, 51, 34, 60, 78,  8);
                leg(2,0,  1, 19, -8, 55,  0,112,  0);

                // Folded upper legs alternate high knees with upturned ankles.
                leg(LegRig.ProfileFoot,1,-14, -3,-53,-13,-60,-63, 38);
                leg(LegRig.ProfileFoot,1, 13, -7, 42,-34, 40,-78, 25);
                leg(1,1,  3,-10,-21,-63,  8,-53,  0);
                leg(1,2,  8,-12, 39,-45, 17,-57, 20);
                // The deep knees sit behind the two crossing legs. The flanking limbs
                // below pass in front of the hips, hiding their attachment contours.
                foreach(int i in upperThighs) indices.Add(i);
                foreach(int i in upperLegs) whole.Add(i);
                limbs[0].PaintDepth=(limbs.Count-0.7)*0.0001;
                limbs[1].PaintDepth=(limbs.Count-0.3)*0.0001;
                leg(LegRig.ProfileFoot,2,-18, -4,-58,  9,-73,-28, -1);
                leg(1,2, 16, -1, 45,-23, 50,-67, -3);
                leg(1,3,-12, -4,-32,-43,-52,-54, 10);
                limbs[limbs.Count-1].TuckedToe=new Vector(-29,-33);

                // The lower tier splits into clusters with staggered heels.
                leg(LegRig.ProfileFoot,1,-10, 11,-39, 45,-57, 78, 37);
                leg(1,3, 14, 10, 41, 51, 63, 91,  8);
                leg(2,2, -6, 17,-23, 55,-39,109, -9);
                leg(0,2,  3, 15, 13, 55, 34,100,  7);
                limbs[limbs.Count-1].TuckedToe=new Vector(24,42);
                leg(2,1, 11, 13, 36, 45, 27, 85,  4);

                // Opposing thighs cradle the point. Their shins cross below it.
                leg(1,2,-21, 13,-49, 28,-30,  1, -6, 6);
                leg(LegRig.ProfileFoot,2, 19, 11, 58, 24, 73, -9,-17);
                leg(1,2,  8, 22,-41, 49, 12, 59, -4);
                leg(LegRig.ProfileFoot,4,-11, 15, 39, 21,-14, 34, 28, 6);
                limbs[limbs.Count-1].SupportsHeartTip=true;
                leg(1,3, 11, 22,-34, 33,-51, 71, -2);
                leg(2,3, -4, 21,  6, 50, -4, 89,  6);
            }
            legs=limbs.ToArray(); positions=new Point3D[legs.Length*LegRig.VertexCount];
            if(layout==1)
                limbs.Sort((a,b)=>Math.Atan2(a.RestToe.X,a.RestToe.Y).CompareTo(Math.Atan2(b.RestToe.X,b.RestToe.Y)));
            for(int i=0;i<limbs.Count;i++)
                limbs[layout==1?i:crownSpokes[i]].SetBurstSpoke((2.0*(i+0.5)/limbs.Count-1)*Math.PI);
            if(grip==null) grip=new GripPose(legs);
            else grip.RebindLegs(legs);
            uv.Freeze(); indices.Freeze(); guardIndices.Freeze(); whole.Freeze();
            rearTriangles=indices; wholeTriangles=whole; guardRaised=layout==2;
            if(layout==1) guardMesh.Positions=null;
            guardMesh.TextureCoordinates=uv; guardMesh.TriangleIndices=guardIndices;
            if(guardView!=null) guardView.Visibility=layout==2?Visibility.Visible:Visibility.Hidden;
            mesh.TextureCoordinates=uv; mesh.TriangleIndices=indices;
        }
    }
}
