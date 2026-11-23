using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BladePath.EditorTools
{
    internal static class ArmyAdvanceBaker
    {
        internal sealed class Joint
        {
            public Transform bone;
            public Quaternion bind;
            public Vector3 origin;
        }

        internal static Dictionary<string,Joint> Rig(Animation anim)
        {
            var result=new Dictionary<string,Joint>();
            foreach(var skin in anim.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var binds=skin.sharedMesh.bindposes;
                var bones=skin.bones;
                for(int i=0;i<bones.Length;i++)
                {
                    var bone=bones[i];
                    if(!bone || result.ContainsKey(bone.name)) continue;
                    var rest=skin.localToWorldMatrix*binds[i].inverse;
                    result[bone.name]=new Joint { bone=bone, bind=rest.rotation, origin=rest.MultiplyPoint3x4(Vector3.zero) };
                }
            }
            // Unity may omit a non-weighted shoulder or toe from the skin palette
            // while still importing it as an animated transform in the hierarchy.
            foreach(var bone in anim.GetComponentsInChildren<Transform>())
            {
                if(result.ContainsKey(bone.name)||!bone.parent||!result.TryGetValue(bone.parent.name,out var parent))continue;
                var original=PrefabUtility.GetCorrespondingObjectFromOriginalSource(bone);
                var local=original?original:bone;
                result[bone.name]=new Joint {bone=bone,bind=parent.bind*local.localRotation,
                    origin=parent.origin+parent.bind*Vector3.Scale(local.localPosition,bone.parent.lossyScale)};
            }
            return result;
        }

        // Meshy's human FBXs share joint names but differ in proportions and bind rotations.
        // Keep the destination's bone lengths and bring over the complete captured performance.
        static AnimationClip Transfer(Animation donor, AnimationClip source, Animation target)
        {
            var src=Rig(donor);
            var dst=Rig(target);
            var joints=target.GetComponentsInChildren<Transform>().Where(t=>dst.ContainsKey(t.name)&&src.ContainsKey(t.name)).ToArray();
            var hips=dst["Hips"];
            float ratio=Vector3.Distance(dst["Head"].origin,hips.origin)/Vector3.Distance(src["Head"].origin,src["Hips"].origin);
            int count=Mathf.Max(2,Mathf.CeilToInt(source.length*40f));
            var curves=new List<AnimationCurve>();
            var bindings=new List<EditorCurveBinding>();
            var previous=new Quaternion[joints.Length];
            var rootPath=AnimationUtility.CalculateTransformPath(hips.bone,target.transform);
            foreach (char axis in "xyz")
            {
                bindings.Add(EditorCurveBinding.FloatCurve(rootPath,typeof(Transform),"m_LocalPosition."+axis));
                curves.Add(new AnimationCurve());
            }
            foreach(var t in joints)
            {
                string path=AnimationUtility.CalculateTransformPath(t,target.transform);
                foreach(char axis in "xyzw")
                {
                    bindings.Add(EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalRotation."+axis));
                    curves.Add(new AnimationCurve());
                }
            }
            for(int frame=0;frame<=count;frame++)
            {
                float time=source.length*frame/count;
                source.SampleAnimation(donor.gameObject,time);
                hips.bone.position=hips.origin+(src["Hips"].bone.position-src["Hips"].origin)*ratio;
                for(int axis=0;axis<3;axis++) curves[axis].AddKey(time,hips.bone.localPosition[axis]);
                for(int i=0;i<joints.Length;i++)
                {
                    var t=joints[i];var a=src[t.name];var b=dst[t.name];
                    t.rotation=a.bone.rotation*Quaternion.Inverse(a.bind)*b.bind;
                    Quaternion q=t.localRotation;
                    if(frame>0 && Quaternion.Dot(previous[i],q)<0f) q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
                    previous[i]=q;
                    for(int axis=0;axis<4;axis++) curves[3+i*4+axis].AddKey(time,q[axis]);
                }
            }
            var clip=new AnimationClip { legacy=true,frameRate=40f };
            AnimationUtility.SetEditorCurves(clip,bindings.ToArray(),curves.ToArray());
            clip.EnsureQuaternionContinuity();
            return clip;
        }

        public static AnimationClip FullBody(Animation target, Animation donor, AnimationClip authored)
        {
            var clip=target==donor ? UnityEngine.Object.Instantiate(authored) : Transfer(donor,authored,target);
            clip.name="Board_"+authored.name.Split('|').Last();
            return clip;
        }

        // A planted foot in an in-place Meshy cycle travels backward. Integrate its
        // measured travel, then invert this table at runtime to drive the original clip by distance.
        public static float MeasureStride(Animation anim, AnimationClip walk, out float[] distances)
        {
            var rig=Rig(anim);
            Transform left=rig["LeftFoot"].bone,right=rig["RightFoot"].bone;
            const int count=64;
            distances=new float[count+1];
            walk.SampleAnimation(anim.gameObject,0f);
            Vector3 lastLeft=left.position,lastRight=right.position;
            for(int i=1;i<=count;i++)
            {
                walk.SampleAnimation(anim.gameObject,walk.length*i/count);
                float backward=Mathf.Max(lastLeft.z-left.position.z,lastRight.z-right.position.z);
                distances[i]=distances[i-1]+Mathf.Max(0f,backward);
                lastLeft=left.position;lastRight=right.position;
            }
            float stride=distances[count];
            if(!(stride>.00001f)) throw new InvalidOperationException("No stride for "+walk.name);
            for(int i=1;i<=count;i++) distances[i]/=stride;
            return stride;
        }

        public static AnimationClip AlignShot(Animation anim, AnimationClip original, float release)
        {
            var rig=Rig(anim);
            var hip=rig["Hips"].bone;
            original.SampleAnimation(anim.gameObject,original.length*release);
            Vector3 aim=Vector3.ProjectOnPlane(rig["LeftHand"].bone.position-rig["RightHand"].bone.position,Vector3.up);
            float yaw=Vector3.SignedAngle(aim,Vector3.forward,Vector3.up);
            var output=UnityEngine.Object.Instantiate(original);
            output.name="Board_"+original.name.Split('|').Last();
            string path=AnimationUtility.CalculateTransformPath(hip,anim.transform);
            var curves=Enumerable.Range(0,7).Select(_=>new AnimationCurve()).ToArray();
            original.SampleAnimation(anim.gameObject,0f);
            Vector3 pivot=hip.position;
            Quaternion last=hip.localRotation;
            int count=Mathf.CeilToInt(original.length*40f);
            for(int i=0;i<=count;i++)
            {
                float phase=i/(float)count,time=original.length*phase;
                original.SampleAnimation(anim.gameObject,time);
                float entering=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.03f,release*.86f,phase));
                float leaving=1f-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.73f,1f,phase));
                var rotation=Quaternion.AngleAxis(yaw*entering*leaving,Vector3.up);
                hip.SetPositionAndRotation(pivot+rotation*(hip.position-pivot),rotation*hip.rotation);
                Quaternion q=hip.localRotation;
                if(Quaternion.Dot(q,last)<0) q=new Quaternion(-q.x,-q.y,-q.z,-q.w);
                last=q;
                for(int a=0;a<3;a++) curves[a].AddKey(time,hip.localPosition[a]);
                for(int a=0;a<4;a++) curves[3+a].AddKey(time,q[a]);
            }
            for(int i=0;i<7;i++)
                AnimationUtility.SetEditorCurve(output,EditorCurveBinding.FloatCurve(path,typeof(Transform),
                    i<3?"m_LocalPosition."+"xyz"[i]:"m_LocalRotation."+"xyzw"[i-3]),curves[i]);
            output.EnsureQuaternionContinuity();
            return output;
        }

        static void SetHipCurves(Animation anim,Transform hip,AnimationClip clip,AnimationCurve[] curves)
        {
            string path=AnimationUtility.CalculateTransformPath(hip,anim.transform);
            for(int axis=0;axis<3;axis++) AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(path,typeof(Transform),"m_LocalPosition."+"xyz"[axis]),curves[axis]);
        }

        public static void AlignCycle(Animation anim,AnimationClip cycle,AnimationClip idle)
        {
            var hip=Rig(anim)["Hips"].bone;
            idle.SampleAnimation(anim.gameObject,.3f*idle.length);
            Vector3 stance=hip.position;
            int count=Mathf.Max(2,Mathf.CeilToInt(cycle.length*40));
            var positions=new Vector3[count+1];Vector3 mean=Vector3.zero;
            for(int i=0;i<=count;i++)
            {
                cycle.SampleAnimation(anim.gameObject,cycle.length*i/count);
                positions[i]=hip.position;
            }
            Vector3 drift=Vector3.ProjectOnPlane(positions[count]-positions[0],Vector3.up);
            for(int i=0;i<count;i++) mean+=positions[i]-drift*(i/(float)count);
            Vector3 offset=Vector3.ProjectOnPlane(stance-mean/count,Vector3.up);
            var curves=new[]{new AnimationCurve(),new AnimationCurve(),new AnimationCurve()};
            for(int i=0;i<=count;i++)
            {
                float t=cycle.length*i/count;cycle.SampleAnimation(anim.gameObject,t);
                Vector3 v=hip.parent.InverseTransformPoint(positions[i]+offset-drift*(i/(float)count));
                for(int axis=0;axis<3;axis++)curves[axis].AddKey(t,v[axis]);
            }
            SetHipCurves(anim,hip,cycle,curves);
        }

        public static AnimationClip AnchorAction(Animation anim, AnimationClip clip,AnimationClip idle)
        {
            if(!clip)return null;
            var rig=Rig(anim);
            if(!rig.TryGetValue("Hips",out var pelvis))return null;
            var hip=pelvis.bone;
            idle.SampleAnimation(anim.gameObject,idle.length*.3f);
            Vector3 stance=hip.position;
            float height=Vector3.Distance(hip.position,rig["Head"].bone.position)+
                Vector3.Distance(hip.position,rig["LeftFoot"].bone.position);
            clip.SampleAnimation(anim.gameObject,0);
            Vector3 start=hip.position;
            float allowance=Mathf.Max(.00001f,height*.14f);
            var curves=new[]{new AnimationCurve(),new AnimationCurve(),new AnimationCurve()};
            float max=0;
            int count=Mathf.Max(2,Mathf.CeilToInt(clip.length*40));
            for(int i=0;i<=count;i++)
            {
                float time=clip.length*i/count;clip.SampleAnimation(anim.gameObject,time);
                // Meshy takes can start in entirely different places. The hex supplies
                // approach and lunge. Retain the performer's small, continuous weight transfer.
                Vector3 travel=Vector3.ProjectOnPlane(hip.position-start,Vector3.up);
                Vector3 allowed=travel/Mathf.Sqrt(1+travel.sqrMagnitude/(allowance*allowance));
                Vector3 correction=Vector3.ProjectOnPlane(stance-hip.position,Vector3.up)+allowed;
                Vector3 local=hip.parent.InverseTransformPoint(hip.position+correction);
                for(int axis=0;axis<3;axis++)curves[axis].AddKey(time,local[axis]);
                max=Mathf.Max(max,correction.magnitude);
            }
            if(max<.008f*height)return null;
            var output=UnityEngine.Object.Instantiate(clip);output.name="Board_"+clip.name.Split('|').Last();
            SetHipCurves(anim,hip,output,curves);return output;
        }
    }
}
