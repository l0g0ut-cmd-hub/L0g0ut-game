using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BladePath.EditorTools
{
    // Full-body performances from tools/mocap/mixamo, sampled in the board's axes.
    // Retarget the paired hands around one grip. Each elbow and knee uses the
    // captured bend plane, while each destination keeps its own bone lengths.
    internal static class ArmySwordBaker
    {
        public enum Move { Guard, Walk, Run, Cut, Diagonal, Heavy, Impact }
        static readonly string[] Joints={"Hips","Spine02","Spine01","Spine","neck","Head",
            "LeftShoulder","LeftArm","LeftForeArm","LeftHand","RightShoulder","RightArm","RightForeArm","RightHand",
            "LeftUpLeg","LeftLeg","LeftFoot","LeftToeBase","RightUpLeg","RightLeg","RightFoot","RightToeBase"};
        sealed class Frame
        {
            public readonly Vector3[] points=new Vector3[Joints.Length];
            public readonly Quaternion[] delta=new Quaternion[Joints.Length];
            public Frame(BinaryReader r)
            {
                for(int i=0;i<Joints.Length;i++)
                {
                    points[i]=new Vector3(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
                    delta[i]=new Quaternion(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
                }
            }
        }
        sealed class Take { public float length;public Frame rest;public Frame[] frames; }
        static Take[] library;
        static Take Read(Move move)
        {
            if(library!=null) return library[(int)move];
            using(var r=new BinaryReader(File.OpenRead("Assets/_Project/Scripts/Editor/ArmySwordTakes.bytes")))
            {
                if(new string(r.ReadChars(4))!="ARM1" || r.ReadInt32()!=Joints.Length) throw new InvalidDataException("Army sword take format");
                library=new Take[r.ReadInt32()];
                for(int k=0;k<library.Length;k++)
                {
                    int count=r.ReadInt32();var t=new Take{length=r.ReadSingle(),rest=new Frame(r),frames=new Frame[count]};
                    for(int f=0;f<count;f++) t.frames[f]=new Frame(r);
                    library[k]=t;
                }
            }
            return library[(int)move];
        }
        static float Chain(Vector3 a,Vector3 b,Vector3 c) {return Vector3.Distance(a,b)+Vector3.Distance(b,c);}
        static void Aim(Transform joint,Transform child,Vector3 wanted)
        {
            Vector3 axis=child.position-joint.position;
            if(axis.sqrMagnitude>1e-12f && wanted.sqrMagnitude>1e-12f)
                joint.rotation=Quaternion.FromToRotation(axis,wanted)*joint.rotation;
        }
        static void Limb(Transform[] dst,Vector3[] src,int first,Vector3 end)
        {
            var a=dst[first];var b=dst[first+1];var c=dst[first+2];
            Vector3 reach=end-a.position;
            float upper=Vector3.Distance(a.position,b.position),lower=Vector3.Distance(b.position,c.position);
            float dist=Mathf.Clamp(reach.magnitude,Mathf.Abs(upper-lower)+1e-6f,(upper+lower)*.998f);
            Vector3 axis=reach.normalized;
            Vector3 bend=Vector3.ProjectOnPlane(src[first+1]-src[first],(src[first+2]-src[first]).normalized);
            bend=Vector3.ProjectOnPlane(bend,axis);
            if(bend.sqrMagnitude<1e-10f) bend=Vector3.ProjectOnPlane(b.position-a.position,axis);
            float along=(upper*upper-lower*lower+dist*dist)/(2*dist);
            Aim(a,b,axis*along+bend.normalized*Mathf.Sqrt(Mathf.Max(0,upper*upper-along*along)));
            Aim(b,c,a.position+axis*dist-b.position);
        }
        public static AnimationClip Bake(Animation anim,Move move)
        {
            var take=Read(move);var rigs=ArmyAdvanceBaker.Rig(anim);
            var joints=Joints.Select(n=>rigs[n]).ToArray();
            var tr=joints.Select(x=>x.bone).ToArray();var rest=take.rest.points;
            float leg=Chain(joints[14].origin,joints[15].origin,joints[16].origin)/Chain(rest[14],rest[15],rest[16]);
            float arm=Chain(joints[7].origin,joints[8].origin,joints[9].origin)/Chain(rest[7],rest[8],rest[9]);
            var bindings=new List<EditorCurveBinding>();var curves=new List<AnimationCurve>();
            for(int k=0;k<tr.Length;k++)
            {
                string path=AnimationUtility.CalculateTransformPath(tr[k],anim.transform);
                if(k==0) foreach(char axis in "xyz") bindings.Add(EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalPosition."+axis));
                foreach(char axis in "xyzw") bindings.Add(EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalRotation."+axis));
            }
            for(int i=0;i<bindings.Count;i++) curves.Add(new AnimationCurve());
            var previous=new Quaternion[tr.Length];
            Vector3 first=take.frames[0].points[0], last=take.frames[take.frames.Length-1].points[0];
            bool cycle=(int)move<=(int)Move.Run;
            for(int f=0;f<take.frames.Length;f++)
            {
                float phase=f/(float)(take.frames.Length-1),time=phase*take.length;
                var frame=take.frames[f];var src=frame.points;
                Vector3 shift=src[0]-rest[0];
                Vector3 travel=Vector3.ProjectOnPlane((cycle?Vector3.Lerp(first,last,phase):first)-rest[0],Vector3.up);
                shift-=travel;
                if(!cycle) {var planar=Vector3.ProjectOnPlane(shift,Vector3.up);shift+=Vector3.ClampMagnitude(planar,.28f)-planar;}
                tr[0].position=joints[0].origin+shift*leg;
                for(int k=0;k<tr.Length;k++) tr[k].rotation=frame.delta[k]*joints[k].bind;
                // One centre for both shoulders avoids two disconnected hand targets at full draw.
                Vector3 shoulders=(tr[7].position+tr[11].position)*.5f;
                Vector3 oldShoulders=(src[7]+src[11])*.5f;
                for(int k=7;k<=11;k+=4)
                {
                    Vector3 palm=shoulders+(src[k+2]-oldShoulders)*arm;
                    Limb(tr,src,k,palm);
                    // Apply the source wrist twist relative to its forearm; never inherit forearm roll twice.
                    var captured=frame.delta[k+2]*joints[k+2].bind;
                    Vector3 oldAxis=frame.delta[k+1]*(joints[k+2].origin-joints[k+1].origin);
                    tr[k+2].rotation=Quaternion.FromToRotation(oldAxis,tr[k+2].position-tr[k+1].position)*captured;
                }
                for(int k=14;k<=18;k+=4)
                {
                    var goal=tr[0].position+(src[k+2]-src[0])*leg;
                    Limb(tr,src,k,goal);
                    tr[k+2].rotation=frame.delta[k+2]*joints[k+2].bind;
                    Aim(tr[k+2],tr[k+3],src[k+3]-src[k+2]);
                }
                for(int a=0;a<3;a++) curves[a].AddKey(time,tr[0].localPosition[a]);
                for(int k=0;k<tr.Length;k++)
                {
                    var q=tr[k].localRotation;
                    if(f>0&&Quaternion.Dot(q,previous[k])<0) q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
                    previous[k]=q;
                    for(int a=0;a<4;a++) curves[3+k*4+a].AddKey(time,q[a]);
                }
            }
            var clip=new AnimationClip{legacy=true,frameRate=(take.frames.Length-1)/take.length,name="ArmySword_"+move,
                wrapMode=cycle?WrapMode.Loop:WrapMode.Once};
            AnimationUtility.SetEditorCurves(clip,bindings.ToArray(),curves.ToArray());clip.EnsureQuaternionContinuity();
            return clip;
        }
    }
}
