using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BladePath.EditorTools
{
    internal static class ArmyAdvanceBaker
    {
        static readonly string[] SourceBones = { "Hips", "Spine", "Spine1", "Spine2", "Neck", "Head",
            "LeftUpLeg", "LeftLeg", "LeftFoot", "RightUpLeg", "RightLeg", "RightFoot" };
        static readonly string[] TargetBones = { "Hips", "Spine02", "Spine01", "Spine", "neck", "Head",
            "LeftUpLeg", "LeftLeg", "LeftFoot", "RightUpLeg", "RightLeg", "RightFoot" };

        static Dictionary<string, Transform> Bones(GameObject root)
        {
            var result = new Dictionary<string, Transform>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                result[t.name.Substring(t.name.LastIndexOf(':') + 1)] = t;
            return result;
        }

        static Quaternion Basis(Dictionary<string, Transform> b)
        {
            Vector3 up = (b["Head"].position - b["Hips"].position).normalized;
            Vector3 across = b["LeftUpLeg"].position - b["RightUpLeg"].position;
            Vector3 forward = Vector3.Cross(across, up).normalized;
            if (Vector3.Dot(forward, b["LeftToeBase"].position - b["LeftFoot"].position) < 0) forward = -forward;
            return Quaternion.LookRotation(forward, up);
        }

        // Keep native weapon carries. Replace only Casual_Walk with one coordinated,
        // in-place capture. Bake real local rotations, including twist, for this skeleton.
        public static AnimationClip Bake(GameObject source, AnimationClip motion, GameObject model,
            AnimationClip idle, AnimationClip walk, out float stride)
        {
            var s = Bones(source);
            var b = Bones(model);
            var hips = b["Hips"];
            bool replace = walk.name.IndexOf("casual", StringComparison.OrdinalIgnoreCase) >= 0;
            var src = SourceBones.Select(n => s[n]).ToArray();
            var dst = TargetBones.Select(n => b[n]).ToArray();
            var srcRest = src.Select(t => t.rotation).ToArray();
            var dstRest = dst.Select(t => t.rotation).ToArray();
            Quaternion map = Basis(b) * Quaternion.Inverse(Basis(s));
            float legRatio = (Vector3.Distance(b["LeftUpLeg"].position, b["LeftLeg"].position)
                            + Vector3.Distance(b["LeftLeg"].position, b["LeftFoot"].position))
                           / (Vector3.Distance(s["LeftUpLeg"].position, s["LeftLeg"].position)
                            + Vector3.Distance(s["LeftLeg"].position, s["LeftFoot"].position));
            float length = replace ? motion.length : walk.length;
            Transform animRoot = model.GetComponentInChildren<Animation>().transform;
            AnimationClip basis = replace ? idle : walk;
            // Clone all authored channels, including non-bone rig transforms.
            var clip = UnityEngine.Object.Instantiate(basis);
            clip.legacy = true; clip.name = "BoardAdvance"; clip.wrapMode = WrapMode.Loop;
            var tracks = new Dictionary<EditorCurveBinding, AnimationCurve>();
            Action<Transform,string,float,float> key = (t, prop, time, val) =>
            {
                var bind = EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(t, animRoot), typeof(Transform), prop);
                AnimationCurve curve;
                if (!tracks.TryGetValue(bind,out curve)) tracks.Add(bind,curve=new AnimationCurve());
                curve.AddKey(time,val);
            };
            // Resample upper-body and rig-node curves onto the new cycle as well.
            // Otherwise a long idle curve can extend the legacy clip past the step seam.
            foreach (var binding in AnimationUtility.GetCurveBindings(clip)) AnimationUtility.SetEditorCurve(clip,binding,null);
            var all = animRoot.GetComponentsInChildren<Transform>(true);
            var paths = new HashSet<string>(AnimationUtility.GetCurveBindings(basis).Select(x=>x.path));
            var keyed = all.Where(t => paths.Contains(AnimationUtility.CalculateTransformPath(t,animRoot)) || dst.Contains(t)).ToArray();
            var previous = new Quaternion[keyed.Length];
            var startP = new Vector3[keyed.Length]; var startQ = new Quaternion[keyed.Length]; var startS = new Vector3[keyed.Length];
            basis.SampleAnimation(animRoot.gameObject,0);
            Vector3 startHip = hips.position;
            basis.SampleAnimation(animRoot.gameObject,basis.length);
            Vector3 endHip = hips.position;
            var sourceRoot = source.GetComponentInChildren<Animation>().gameObject;
            motion.SampleAnimation(sourceRoot,0); Vector3 first = src[0].position;
            motion.SampleAnimation(sourceRoot,motion.length); Vector3 last = src[0].position;
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
            Vector3 heading = model.transform.forward;
            const int count = 40;
            for (int frame=0;frame<=count;frame++)
            {
                float phase=frame/(float)count, time=phase*length;
                basis.SampleAnimation(animRoot.gameObject,basis.length*phase);
                if (replace)
                {
                    motion.SampleAnimation(sourceRoot,motion.length*phase);
                    Vector3 residual=src[0].position-Vector3.Lerp(first,last,phase);
                    hips.position=startHip+map*residual*legRatio;
                    for (int k=0;k<dst.Length;k++)
                        dst[k].rotation=map*(src[k].rotation*Quaternion.Inverse(srcRest[k]))*Quaternion.Inverse(map)*dstRest[k];
                }
                else
                {
                    Vector3 drift=(endHip-startHip)*phase;
                    hips.position-=Vector3.ProjectOnPlane(drift,Vector3.up);
                }
                float foot=Vector3.Dot(b["LeftFoot"].position-startHip,heading);
                lo=Mathf.Min(lo,foot);hi=Mathf.Max(hi,foot);
                for (int i=0;i<keyed.Length;i++)
                {
                    Transform bone=keyed[i];
                    Vector3 pos=bone.localPosition, scale=bone.localScale;
                    Quaternion rot=bone.localRotation;
                    if (frame==0) {startP[i]=pos;startQ[i]=rot;startS[i]=scale;}
                    if (frame==count) {pos=startP[i];rot=startQ[i];scale=startS[i];}
                    if (frame>0 && Quaternion.Dot(previous[i],rot)<0) rot=new Quaternion(-rot.x,-rot.y,-rot.z,-rot.w);
                    previous[i]=rot;
                    for(int axis=0;axis<3;axis++)
                    {
                        key(bone,"m_LocalPosition."+"xyz"[axis],time,pos[axis]);
                        key(bone,"m_LocalScale."+"xyz"[axis],time,scale[axis]);
                    }
                    for(int axis=0;axis<4;axis++) key(bone,"m_LocalRotation."+"xyzw"[axis],time,rot[axis]);
                }
            }
            var bindings=tracks.Keys.ToArray();
            AnimationUtility.SetEditorCurves(clip,bindings,bindings.Select(x=>tracks[x]).ToArray());
            clip.EnsureQuaternionContinuity();
            stride=replace ? Vector3.ProjectOnPlane(last-first,Vector3.up).magnitude*legRatio : 2f*(hi-lo);
            return clip;
        }
    }
}
