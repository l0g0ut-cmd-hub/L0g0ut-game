using UnityEngine;

namespace BladePath
{
    public sealed class TacticsShotRig
    {
        Transform root,left,right,upper,lower;
        public Transform tip { get; private set; }
        SkinnedMeshRenderer body;
        Mesh stowed,drawn;
        bool gun, bow, carried;
        int kind;
        Quaternion lastGrip;
        bool hasGrip;
        float scale;

        static Transform Part(string name,Transform parent,Mesh mesh,Material material)
        {
            var t=new GameObject(name).transform;t.SetParent(parent,false);
            t.gameObject.layer=parent.gameObject.layer;
            if(mesh)
            {
                t.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
                var r=t.gameObject.AddComponent<MeshRenderer>();r.sharedMaterial=material;
                r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return t;
        }
        public static TacticsShotRig Create(GameObject figure,TacticsFigureProfile profile,Transform left,Transform right,bool gun)
        {
            var skin=figure.GetComponentInChildren<SkinnedMeshRenderer>();
            if(!skin || !left || !right || !profile.heldWeapon) return null;
            var result=new TacticsShotRig {left=left,right=right,body=skin,stowed=skin.sharedMesh,drawn=profile.drawnBody,gun=gun,bow=profile.gearKind==1,kind=profile.gearKind,carried=profile.handCarried};
            result.root=Part("Held equipment",figure.transform,profile.heldWeapon,profile.weaponSurface);
            result.tip=Part("Release",result.root,null,null);
            result.tip.localPosition=gun?new Vector3(0,.016f,.64f):Vector3.up*.015f;
            // Keep the prop in proportion to the source rig even when its FBX uses centimetres.
            result.scale=Vector3.Distance(left.position,left.parent.position)/(.24f*Mathf.Max(.00001f,figure.transform.lossyScale.x));
            result.root.localScale=Vector3.one*result.scale;
            if(result.bow)
            {
                result.upper=Part("Upper cord",result.root,profile.bowCord,profile.weaponSurface);
                result.lower=Part("Lower cord",result.root,profile.bowCord,profile.weaponSurface);
            }
            result.root.gameObject.SetActive(false);
            return result;
        }
        static void Cord(Transform line,Vector3 a,Vector3 b)
        {
            line.localPosition=a;Vector3 d=b-a;
            if(d.sqrMagnitude>.00001f) line.localRotation=Quaternion.LookRotation(d);
            line.localScale=new Vector3(1,1,d.magnitude);
        }
        public void Pose(bool shooting,float phase,bool walking=false,bool fighting=false)
        {
            bool aimed=shooting && phase>.035f && phase<.96f;
            bool held=aimed||walking||carried;
            if(root.gameObject.activeSelf!=held)
            {
                root.gameObject.SetActive(held);
                if(drawn) body.sharedMesh=held?drawn:stowed;
            }
            if(!held)return;
            float size=root.lossyScale.x;
            if(kind>=3)
            {
                Vector3 r=right.position+(right.position-right.parent.position).normalized*.057f*size;
                Vector3 l=left.position+(left.position-left.parent.position).normalized*.057f*size;
                Vector3 line=(r-l).normalized;
                if(line.sqrMagnitude<.5f)line=root.parent.forward;
                var grip=Quaternion.LookRotation(line,Vector3.ProjectOnPlane(right.position-right.parent.position,line).sqrMagnitude>.000001f?
                    right.position-right.parent.position:root.parent.up);
                if(fighting||!hasGrip) {lastGrip=Quaternion.Inverse(right.rotation)*grip;hasGrip=true;}
                root.SetPositionAndRotation(r, right.rotation*lastGrip);
                return;
            }
            Vector3 aim=left.position-right.position;
            if(aim.sqrMagnitude<.0000001f) return;
            Vector3 gripPos=gun?right.position:left.position;
            var rotation=Quaternion.LookRotation(aim,Vector3.up);
            if(!aimed)
            {
                if(gun)rotation=Quaternion.LookRotation((root.parent.forward+.65f*Vector3.up).normalized);
                else rotation=Quaternion.LookRotation(root.parent.forward,Vector3.up);
            }
            root.SetPositionAndRotation(gripPos,rotation);
            if(gun)return;
            var nock=new Vector3(0,.025f,-.14f);
            float draw=aimed?Mathf.SmoothStep(0,1,Mathf.InverseLerp(.31f,.47f,phase))*
                (1f-Mathf.SmoothStep(0,1,Mathf.InverseLerp(Tactics.BowRelease,Tactics.BowRelease+.035f,phase))):0f;
            nock=Vector3.Lerp(nock,root.InverseTransformPoint(right.position),draw);
            Cord(upper,new Vector3(0,.79f,-.14f),nock);Cord(lower,new Vector3(0,-.45f,-.14f),nock);
        }
    }
}
