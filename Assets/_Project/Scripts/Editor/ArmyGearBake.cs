using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace BladePath.EditorTools
{
    internal static class ArmyGearBake
    {
        sealed class Mask {public readonly HashSet<int> weapon=new HashSet<int>(),belt=new HashSet<int>();}
        static Dictionary<string,Mask> masks;
        public static Mesh Prepared(SkinnedMeshRenderer skin,string name)
        {
            if(masks==null)
            {
                masks=new Dictionary<string,Mask>();
                using(var r=new BinaryReader(File.OpenRead("Assets/_Project/Scripts/Editor/ArmyWeaponBindings.bytes")))
                {
                    int count=r.ReadInt32();
                    for(int i=0;i<count;i++)
                    {
                        string model=r.ReadString();var mask=new Mask();
                        int n=r.ReadInt32();for(int j=0;j<n;j++){mask.weapon.Add(r.ReadInt32());r.ReadInt32();}
                        n=r.ReadInt32();for(int j=0;j<n;j++)mask.belt.Add(r.ReadInt32());
                        masks["unit_"+model]=mask;
                    }
                }
            }
            if(!masks.TryGetValue(name,out var m))return null;
            var mesh=UnityEngine.Object.Instantiate(skin.sharedMesh);mesh.name="Socketed body";
            var uv=mesh.uv;var hidden=new bool[uv.Length];var weights=mesh.boneWeights;
            int hip=Array.FindIndex(skin.bones,b=>b && b.name=="Hips");
            int changed=0;
            for(int i=0;i<uv.Length;i++)
            {
                int k=(Mathf.RoundToInt(uv[i].x*16384)<<16)|Mathf.RoundToInt(uv[i].y*16384);
                if(m.weapon.Contains(k)){hidden[i]=true;changed++;}
                if(hip>=0&&m.belt.Contains(k))weights[i]=new BoneWeight{boneIndex0=hip,weight0=1};
            }
            if(changed<16)throw new InvalidDataException("Weapon socket UV mismatch on "+name+": "+changed);
            mesh.boneWeights=weights;
            for(int sub=0;sub<mesh.subMeshCount;sub++)
            {
                int[] triangles=mesh.GetTriangles(sub);var kept=new List<int>(triangles.Length);
                for(int i=0;i<triangles.Length;i+=3)
                    if(!hidden[triangles[i]]&&!hidden[triangles[i+1]]&&!hidden[triangles[i+2]])
                    {kept.Add(triangles[i]);kept.Add(triangles[i+1]);kept.Add(triangles[i+2]);}
                mesh.SetTriangles(kept,sub,false);
            }
            return mesh;
        }
    }
}
