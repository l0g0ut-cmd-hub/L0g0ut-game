using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BladePath.EditorTools
{
    // This uses the Android-imported assets and shader. Inspect alpha coverage in camera
    // pixels, including the final view at 34 degrees; renderer bounds alone cannot see a blank unit.
    public static class ArmyRosterProbe
    {
        const int Size=192;
        const int Layer=29;
        const string Models="Assets/_Project/Resources/Units/";
        const string Output="Assets/_Project/Resources/ArmyFigures";
        const string Capture="build/shots";


        sealed class View : IDisposable
        {
            readonly GameObject cameraRoot=new GameObject("Army roster camera");
            readonly Camera cam;
            readonly RenderTexture rt=new RenderTexture(Size,Size,24,RenderTextureFormat.ARGB32);
            readonly Texture2D image=new Texture2D(Size,Size,TextureFormat.RGBA32,false);
            public Color32[] pixels;
            public View()
            {
                cam=cameraRoot.AddComponent<Camera>(); cam.enabled=false;cam.orthographic=true;
                cam.backgroundColor=Color.clear; cam.clearFlags=CameraClearFlags.SolidColor;
                cam.cullingMask=1<<Layer;cam.allowHDR=false;cam.allowMSAA=false;cam.useOcclusionCulling=false;
                cam.targetTexture=rt;cam.aspect=1f;
            }
            public Bounds Frame(Vector3 center,Quaternion rotation,float span)
            {
                float distance=Mathf.Max(span*2,3f);
                cam.transform.SetPositionAndRotation(center-rotation*Vector3.forward*distance,rotation);
                cam.nearClipPlane=.001f;cam.farClipPlane=distance*2.5f;
                cam.orthographicSize=span*.5f;
                var previous=RenderTexture.active;
                try {cam.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,Size,Size),0,0);image.Apply(false);}
                finally {RenderTexture.active=previous;}
                pixels=image.GetPixels32();
                var xs=new int[Size];var ys=new int[Size];int total=0;
                for(int i=0;i<pixels.Length;i++)
                    if(pixels[i].a>96) {xs[i%Size]++;ys[i/Size]++;total++;}
                if(total<12) throw new InvalidOperationException("No visible body in camera, opaque pixels="+total+", center="+center+", span="+span);
                int x0=Quantile(xs,total,.005f), x1=Quantile(xs,total,.995f);
                int y0=Quantile(ys,total,.003f), y1=Quantile(ys,total,.997f);
                Vector3 min=new Vector3((x0/(float)Size-.5f)*span,(y0/(float)Size-.5f)*span,0);
                Vector3 max=new Vector3(((x1+1f)/Size-.5f)*span,((y1+1f)/Size-.5f)*span,0);
                var b=new Bounds();b.SetMinMax(min,max);return b;
            }
            static int Quantile(int[] bins,int total,float f)
            {
                int threshold=Mathf.Max(1,Mathf.CeilToInt(total*f)),sum=0;
                for(int i=0;i<bins.Length;i++) {sum+=bins[i];if(sum>=threshold)return i;}
                return bins.Length-1;
            }
            public void Save(string file)
            {
                if(pixels!=null) File.WriteAllBytes(file,image.EncodeToPNG());
            }

            public void Dispose()
            {
                cam.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);
                Object.DestroyImmediate(image);Object.DestroyImmediate(cameraRoot);
            }
        }

        // Bound the initial camera using actual skin weights and bind matrices. This
        // calculation is independent of FBX scale conventions and of BakeMesh(useScale).
        static Bounds SkinBounds(GameObject model)
        {
            var result=new Bounds();bool any=false;
            foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh=skin.sharedMesh;var verts=mesh.vertices;var weights=mesh.boneWeights;
                var bind=mesh.bindposes;var bones=skin.bones;
                var matrices=new Matrix4x4[bind.Length];
                for(int i=0;i<bind.Length;i++)matrices[i]=bones[i].localToWorldMatrix*bind[i];
                for(int i=0;i<verts.Length;i+=Mathf.Max(1,verts.Length/2048))
                {
                    var w=weights[i];Vector3 v=verts[i];
                    Vector3 p=matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0;
                    if(w.weight1>0)p+=matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1;
                    if(w.weight2>0)p+=matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2;
                    if(w.weight3>0)p+=matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
                    if(!any){result=new Bounds(p,Vector3.zero);any=true;}else result.Encapsulate(p);
                }
            }
            if(!any) throw new Exception("Empty skinned figure: "+model.name);
            return result;
        }

        static void CheckFootContacts(string name, Animation anim, TacticsFigureProfile profile,
            Transform anchor, Transform[] bones)
        {
            var feet=new[] {bones.First(t=>t.name=="LeftFoot"),bones.First(t=>t.name=="RightFoot")};
            const int samples=64;
            var positions=new Vector3[2,samples+1];
            for(int i=0;i<=samples;i++)
            {
                float phase=i/(float)samples;
                profile.advance.SampleAnimation(anim.gameObject,profile.advance.length*phase);
                float distance=profile.walkDistances[i]*profile.StrideAt(anchor.eulerAngles.y);
                for(int foot=0;foot<2;foot++) positions[foot,i]=feet[foot].position+anchor.forward*distance;
            }
            float worst=0f;
            for(int foot=0;foot<2;foot++)
            {
                float min=float.PositiveInfinity;
                for(int i=0;i<=samples;i++) min=Mathf.Min(min,positions[foot,i].y);
                float floor=min+profile.stride*.023f;
                float longest=0f;
                for(int i=1;i<=samples;i++)
                {
                    if(positions[foot,i].y>floor || positions[foot,i-1].y>floor) continue;
                    Vector3 step=Vector3.ProjectOnPlane(positions[foot,i]-positions[foot,i-1],Vector3.up);
                    longest=Mathf.Max(longest,step.magnitude);
                }
                worst=Mathf.Max(worst,longest/profile.stride);
            }
            Debug.Log("ARMY_FOOT "+name+" maximum_grounded_frame_drift_fraction="+worst.ToString("F4"));
            if(!(worst<.10f)) throw new Exception("Discontinuous planted foot "+name+" "+worst);
        }

        static AnimationClip Named(Animation donor,string key)
        {
            return donor.Cast<AnimationState>().First(a=>a.name.Split('|').Last()
                .Equals(key,StringComparison.OrdinalIgnoreCase)).clip;
        }

        static void CombatSequence(View view, Texture2D sheet, int y, Animation anim,
            AnimationClip walk, AnimationClip hit, AnimationClip idle, Vector3 center, Quaternion board)
        {
            if (anim.GetClip(walk.name)==null) anim.AddClip(walk,walk.name);
            var walking = anim[walk.name];
            var attacking = anim[hit.name];
            var standing = anim[idle.name];
            for (int frame=0; frame<12; frame++)
            {
                // Sample the legacy blend used on the board, including both transitions.
                anim.Stop();
                AnimationState from, to;
                float phase, blend;
                if (frame<=3)
                {
                    from=walking; to=attacking;
                    phase=Mathf.Max(0,frame-1)*.055f;
                    blend=Mathf.Clamp01((frame-1)/2f);
                    from.time=walk.length*(.25f+frame*.065f);
                }
                else if (frame<=8)
                {
                    from=walking; to=attacking; phase=(frame-3)/5f*.86f+.11f; blend=1f;
                }
                else
                {
                    from=attacking; to=standing;
                    from.time=hit.length*.98f;
                    phase=(frame-8)*.04f; blend=(frame-8)/3f;
                }
                // Flying units can use idle for both advancing and recovering.
                from.enabled=true; from.speed=0; from.weight=1f-blend;
                to.enabled=true; to.speed=0; to.weight=blend; to.time=phase*to.length;
                anim.Sample();
                Bounds body = view.Frame(center,board,4.2f);
                if (body.size.y<.25f || Mathf.Abs(body.center.x)>1.55f)
                    throw new Exception("Body outside combat frame "+frame+": "+body);
                sheet.SetPixels32(frame*Size,y,Size,Size,view.pixels);
            }
            anim.Stop();
        }

        [MenuItem("BladePath/Verify army roster")]
        public static void Build()
        {
            Directory.CreateDirectory(Output);Directory.CreateDirectory(Capture);AssetDatabase.Refresh();

            var names=Directory.GetFiles(Models,"*.fbx").OrderBy(p=>p).ToArray();
            var sheet=new Texture2D(Size*4,Size*names.Length,TextureFormat.RGBA32,false);
            var gaitSheet=new Texture2D(Size*8,Size*names.Length,TextureFormat.RGBA32,false);
            var combatSheet=new Texture2D(Size*12,Size*names.Length,TextureFormat.RGBA32,false);
            var report=new List<string>{"model,stance_height,advance_height,attack_height,side2_height,stride"};
            using(var view=new View())
            {
                for(int row=0;row<names.Length;row++)
                {
                    string path=names[row].Replace('\\','/'), name=Path.GetFileNameWithoutExtension(path);
                    var anchor=new GameObject("roster "+name);
                    var mount=new GameObject("Figure").transform;mount.SetParent(anchor.transform,false);
                    GameObject model=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path),mount,false);
                    GameObject donor=null;
                    var mat=new Material(Resources.Load<Shader>("TacticsUnit"));
                    try
                    {
                        mat.mainTexture=Resources.Load<Texture2D>("Units/"+name+"_albedo");
                        if (mat.mainTexture == null) throw new Exception("Missing albedo "+name);
                        foreach(Transform t in model.GetComponentsInChildren<Transform>(true))t.gameObject.layer=Layer;
                        TacticsFigureProfile.Dress(model,mat);
                        var an=model.GetComponentInChildren<Animation>();an.Stop();an.playAutomatically=false;
                        var clips = new TacticsFigureProfile.CombatClips(an);
                        var idle = clips.idle == null ? null : an.GetClip(clips.idle);
                        var walk = clips.walk == null ? null : an.GetClip(clips.walk);
                        var hit = clips.attack == null ? null : an.GetClip(clips.attack);
                        if(!idle||!walk||!hit)throw new Exception("Missing combat clip. Available: "+string.Join(", ",an.Cast<AnimationState>().Select(x=>x.name)));
                        string asset=Output+"/"+name+".asset";
                        if(AssetDatabase.LoadAssetAtPath<TacticsFigureProfile>(asset))AssetDatabase.DeleteAsset(asset);
                        var profile=ScriptableObject.CreateInstance<TacticsFigureProfile>();AssetDatabase.CreateAsset(profile,asset);
                        bool mounted=name=="unit_cavalry"||name=="unit_general",dog=name=="unit_dog";
                        bool flying=name=="unit_tengu"||name=="unit_daitengu";
                        float rawStride=0f;
                        AnimationClip authoredAttack=hit;
                        if(!mounted&&!dog&&!flying)
                        {
                            string donorName=null, take=null;
                            string walkName=walk.name.Split('|').Last().ToLowerInvariant();
                            if(name=="unit_banner") { donorName="unit_sohei"; take="Spear_Walk"; }
                            else if(name=="unit_samurai" || walkName.Contains("casual") || name=="unit_oni")
                            { donorName="unit_hatamoto"; take="Walk_Fight_Forward"; }
                            Animation from=an;
                            if(donorName!=null)
                            {
                                donor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Models+donorName+".fbx"));
                                from=donor.GetComponentInChildren<Animation>();from.Stop();
                                walk=Named(from,take);
                            }
                            profile.advance=ArmyAdvanceBaker.FullBody(an,from,walk);
                            profile.advance.wrapMode=WrapMode.Loop;
                            AssetDatabase.AddObjectToAsset(profile.advance,profile);
                            rawStride=ArmyAdvanceBaker.MeasureStride(an,profile.advance,out profile.walkDistances);
                            Debug.Log("ARMY_MESHY "+name+" "+walk.name.Split('|').Last());
                        }
                        if(name=="unit_samurai")
                        {
                            // The base samurai FBX supplies a spear thrust. Use the sword take
                            // from the project's archived Meshy swordsman on this rig.
                            var swordsman=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Models+"unit_ronin.fbx"));
                            try
                            {
                                var from=swordsman.GetComponentInChildren<Animation>();from.Stop();
                                authoredAttack=ArmyAdvanceBaker.FullBody(an,from,Named(from,"Right_Hand_Sword_Slash"));
                            }
                            finally {Object.DestroyImmediate(swordsman);}
                        }
                        profile.attack=ArmyAdvanceBaker.AnchorAction(an,authoredAttack);
                        if(authoredAttack!=hit)
                        {
                            if(profile.attack) Object.DestroyImmediate(authoredAttack);
                            else profile.attack=authoredAttack;
                        }
                        profile.reaction=ArmyAdvanceBaker.AnchorAction(an,clips.hit==null?null:an.GetClip(clips.hit));
                        if(profile.attack) AssetDatabase.AddObjectToAsset(profile.attack,profile);
                        if(profile.reaction) AssetDatabase.AddObjectToAsset(profile.reaction,profile);
                        hit=an.GetClip(TacticsFigureProfile.InstallAction(an,profile.attack,clips.attack));
                        idle.SampleAnimation(an.gameObject,.3f*idle.length);
                        Bounds approx=SkinBounds(model);Vector3 c=approx.center;
                        float span=Mathf.Max(.001f,Mathf.Max(approx.size.x,Mathf.Max(approx.size.y,approx.size.z))*1.4f);
                        var front=view.Frame(c,Quaternion.identity,span);
                        var side=view.Frame(c,Quaternion.Euler(0,90,0),span);
                        Vector3 basePoint=c+new Vector3(front.center.x,Mathf.Min(front.min.y,side.min.y),-side.center.x);
                        float target=dog?.79f:mounted?1.82f:1.34f;
                        profile.scale=target/Mathf.Max(.00001f,front.size.y);
                        profile.offset=-basePoint*profile.scale;profile.Fit(mount);
                        anchor.transform.rotation=Quaternion.Euler(0,90,0);
                        Quaternion board=Quaternion.Euler(34,0,0);
                        Vector3 center=Vector3.up*target*.47f;
                        const int directions=8;
                        var factors=new float[directions];
                        for(int i=0;i<directions;i++)
                        {
                            anchor.transform.rotation=Quaternion.Euler(0,i*360f/directions,0);
                            var facing=view.Frame(center,board,3.6f);
                            factors[i]=target/facing.size.y;
                        }
                        profile.headingScale=factors;
                        profile.stride=rawStride*profile.scale*factors[2];
                        float[] heights=new float[4];
                        for(int column=0;column<4;column++)
                        {
                            var clip=column==1?(profile.advance?profile.advance:flying?idle:walk):column==2?hit:idle;
                            anchor.transform.rotation=Quaternion.Euler(0,column==3?-90:90,0);
                            profile.Fit(mount,anchor.transform.eulerAngles.y);
                            clip.SampleAnimation(an.gameObject,clip.length*(column==1?.25f:column==2?.43f:.3f));
                            var visible=view.Frame(center,board,3.6f);heights[column]=visible.size.y;
                            // Fixed camera and origin for every row. A missing body or one moving into another hex fails.
                            if(visible.size.y<target*.42f||visible.size.y>target*1.9f||Mathf.Abs(visible.center.x)>1.2f)
                                throw new Exception("Body outside its hex: "+clip.name+" "+visible);
                            if((column==0||column==3)&&Mathf.Abs(visible.size.y-target)>.07f) throw new Exception("Incorrect projected height "+visible.size.y+" expected "+target);
                            sheet.SetPixels32(column*Size,(names.Length-row-1)*Size,Size,Size,view.pixels);
                        }
                        if(profile.advance!=null)
                        {
                            Transform[] joints=model.GetComponentsInChildren<Transform>();
                            Transform foot=joints.First(t=>t.name=="LeftFoot"),hip=joints.First(t=>t.name=="Hips");
                            anchor.transform.rotation=Quaternion.Euler(0,90,0);
                            profile.Fit(mount,90f);
                            Vector3 firstFoot=Vector3.zero;float excursion=0f;
                            for(int i=0;i<8;i++)
                            {
                                profile.advance.SampleAnimation(an.gameObject,profile.advance.length*i/8f);
                                Vector3 relative=foot.position-hip.position;
                                if(i==0)firstFoot=relative;
                                excursion=Mathf.Max(excursion,(relative-firstFoot).magnitude);
                                Bounds moving=view.Frame(center,board,3.6f);
                                gaitSheet.SetPixels32(i*Size,(names.Length-row-1)*Size,Size,Size,view.pixels);
                                if(moving.size.y<target*.55f||Mathf.Abs(moving.center.x)>1f)
                                    throw new Exception("Advance frame "+i+" outside cell: "+moving);
                            }
                            if(excursion<target*.12f)throw new Exception("Insufficient foot travel: "+excursion);
                        }
                        anchor.transform.rotation=Quaternion.Euler(0,90,0);
                        profile.Fit(mount,90f);
                        if(profile.advance) CheckFootContacts(name,an,profile,anchor.transform,model.GetComponentsInChildren<Transform>());
                        CombatSequence(view,combatSheet,(names.Length-row-1)*Size,an,
                            profile.advance?profile.advance:flying?idle:walk,hit,idle,center,board);
                        EditorUtility.SetDirty(profile);
                        report.Add(name+","+string.Join(",",heights.Select(h=>h.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)))+","+profile.stride.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
                        Debug.Log("ARMY_ROSTER "+report.Last());
                    }
                    catch(Exception ex){view.Save(Capture+"/failed-"+name+".png");throw new Exception("Army roster "+name+": "+ex.Message,ex);}
                    finally {if(donor) Object.DestroyImmediate(donor);Object.DestroyImmediate(anchor);Object.DestroyImmediate(mat);}
                }
            }
            AssetDatabase.SaveAssets();
            var types=Tactics.Ours.Concat(Tactics.Foes).ToArray();
            const int crop=144;
            var deployed=new Texture2D(crop*2,crop*types.Length,TextureFormat.RGBA32,false);
            for(int i=0;i<types.Length;i++)
            {
                for(int side=0;side<2;side++)
                {
                    var pixels=Tactics.CaptureBoardUnit(types[i],side==1,crop);
                    int opaque=pixels.Count(p=>p.a>96);
                    int lit=pixels.Count(p=>p.a>96 && Mathf.Max(p.r,p.g,p.b)>32);
                    if(opaque<80 || lit<opaque*.15f) throw new Exception("Missing or dark unit "+types[i].name+" opaque="+opaque+" lit="+lit);
                    deployed.SetPixels32(side*crop,(types.Length-i-1)*crop,crop,crop,pixels);
                }
                Debug.Log("ARMY_BIND "+types[i].name+" both sides including revival");
            }
            deployed.Apply();File.WriteAllBytes(Capture+"/army-deployment.png",deployed.EncodeToPNG());Object.DestroyImmediate(deployed);
            sheet.Apply();File.WriteAllBytes(Capture+"/army-roster.png",sheet.EncodeToPNG());Object.DestroyImmediate(sheet);
            gaitSheet.Apply();File.WriteAllBytes(Capture+"/army-advance.png",gaitSheet.EncodeToPNG());Object.DestroyImmediate(gaitSheet);
            combatSheet.Apply();File.WriteAllBytes(Capture+"/army-combat.png",combatSheet.EncodeToPNG());Object.DestroyImmediate(combatSheet);
            File.WriteAllLines("build/army-roster.csv",report);
        }
    }
}
