using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BladePath.EditorTools
{
    internal static class ArmyAdvanceBaker
    {
        static readonly string[] Sides = { "Left", "Right" };
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

        static void Aim(Transform parent,Transform child,Vector3 target)
        {
            Vector3 before=child.position-parent.position;
            if(before.sqrMagnitude>1e-12f && target.sqrMagnitude>1e-12f)
                parent.rotation=Quaternion.FromToRotation(before,target)*parent.rotation;
        }

        static void PlaceLeg(Dictionary<string,Transform> source,Dictionary<string,Transform> target,
            string side,Quaternion alignment, Vector3 goal)
        {
            Transform a=target[side+"UpLeg"], b=target[side+"Leg"], c=target[side+"Foot"];
            Transform toe=target[side+"ToeBase"];
            Vector3 from=source[side+"UpLeg"].position, knee=source[side+"Leg"].position;
            Vector3 ankle=source[side+"Foot"].position;
            float upper=Vector3.Distance(a.position,b.position),lower=Vector3.Distance(b.position,c.position);
            Vector3 toAnkle=goal-a.position;
            Vector3 along=toAnkle.normalized;
            float dist=Mathf.Clamp(toAnkle.magnitude,Mathf.Abs(upper-lower)+.00001f,(upper+lower)*.999f);
            // The captured knee supplies the bend plane. This avoids reversed knees when the
            // FBX bone roll or the bind-pose bend differs from that of the motion capture.
            Vector3 pole=Vector3.ProjectOnPlane(alignment*(knee-from),along).normalized;
            if(pole.sqrMagnitude<.1f) pole=Vector3.ProjectOnPlane(b.position-a.position,along).normalized;
            float projection=(upper*upper-lower*lower+dist*dist)/(2f*dist);
            Vector3 toKnee=along*projection+pole*Mathf.Sqrt(Mathf.Max(0f,upper*upper-projection*projection));
            Vector3 footTarget=a.position+along*dist;
            Aim(a,b,toKnee);Aim(b,c,footTarget-b.position);
            Aim(c,toe,alignment*(source[side+"ToeBase"].position-ankle));
        }

        // One captured advance supplies foot contacts and weight transfer. Weapon-specific
        // arm poses come from the original fighter and are baked on the same time axis.
        public static AnimationClip Bake(GameObject source, AnimationClip motion, GameObject model,
            AnimationClip idle, AnimationClip walk, out float stride)
        {
            var s = Bones(source);
            var b = Bones(model);
            var hips = b["Hips"];
            bool replace = walk.name.IndexOf("casual", StringComparison.OrdinalIgnoreCase) >= 0;
            var src = SourceBones.Select(n => s[n]).ToArray();
            var dst = TargetBones.Select(n => b[n]).ToArray();

            float legRatio = (Vector3.Distance(b["LeftUpLeg"].position, b["LeftLeg"].position)
                            + Vector3.Distance(b["LeftLeg"].position, b["LeftFoot"].position))
                           / (Vector3.Distance(s["LeftUpLeg"].position, s["LeftLeg"].position)
                            + Vector3.Distance(s["LeftLeg"].position, s["LeftFoot"].position));
            float length = motion.length;
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
            idle.SampleAnimation(animRoot.gameObject,idle.length*.3f);
            Vector3 startHip = hips.position;
            float groundHeel=Mathf.Min(b["LeftFoot"].position.y,b["RightFoot"].position.y);
            float hipOffset = startHip.y - .5f * (b["LeftUpLeg"].position.y + b["RightUpLeg"].position.y);
            var dstPose=dst.Select(t=>t.rotation).ToArray();
            var sourceRoot = source.GetComponentInChildren<Animation>().gameObject;
            motion.SampleAnimation(sourceRoot,0); Vector3 first = src[0].position;
            var srcPose=src.Select(t=>t.rotation).ToArray();
            motion.SampleAnimation(sourceRoot,motion.length); Vector3 last = src[0].position;
            const int count = 40;
            Vector3 travel = Vector3.ProjectOnPlane(last-first,Vector3.up);
            if (travel.magnitude < .001f) throw new InvalidOperationException("Guard advance has no horizontal travel.");
            Quaternion map = Quaternion.AngleAxis(Vector3.SignedAngle(travel,Vector3.forward,Vector3.up),Vector3.up);
            float sourceGround = float.PositiveInfinity;
            for (int frame=0; frame<count; frame++)
            {
                motion.SampleAnimation(sourceRoot,length*frame/count);
                sourceGround = Mathf.Min(sourceGround, s["LeftFoot"].position.y, s["RightFoot"].position.y);
            }
            for (int frame=0;frame<=count;frame++)
            {
                float phase=frame/(float)count, time=phase*length;
                basis.SampleAnimation(animRoot.gameObject,basis.length*phase);
                motion.SampleAnimation(sourceRoot,motion.length*phase);
                Vector3 origin = first + travel*phase;
                Vector3 pelvis = startHip + map*(src[0].position-origin)*legRatio;
                float thighY = .5f*(s["LeftUpLeg"].position.y+s["RightUpLeg"].position.y);
                pelvis.y = groundHeel + hipOffset + (thighY-sourceGround)*legRatio;
                hips.position=pelvis;
                // Use motion relative to the guarded pose, preserving the original weapon grip.
                // Absolute rest-pose rotation transfer can put already-bent limbs at the wrong angle.
                int bodyBones=replace?6:0;
                for(int k=0;k<bodyBones;k++)
                    dst[k].rotation=map*(src[k].rotation*Quaternion.Inverse(srcPose[k]))*Quaternion.Inverse(map)*dstPose[k];
                foreach (string side in Sides)
                {
                    Vector3 foot = s[side+"Foot"].position;
                    Vector3 goal = startHip + map*(foot-origin)*legRatio;
                    goal.y = groundHeel + (foot.y-sourceGround)*legRatio;
                    PlaceLeg(s,b,side,map,goal);
                }
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
            stride=Vector3.ProjectOnPlane(last-first,Vector3.up).magnitude*legRatio;
            return clip;
        }
    }
}
