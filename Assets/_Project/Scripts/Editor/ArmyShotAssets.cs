using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BladePath.EditorTools
{
    internal static class ArmyShotAssets
    {
        const string Source="Assets/_Project/Scripts/Editor/";
        // UV correspondences come from the two source FBXs. Stow and draw use the same
        // skin and animation. Close the connected shoulder mesh without leaving open edges.
        static Mesh DrawnSkin(Mesh original,bool gun)
        {
            string[] lines=File.ReadAllLines(Source+(gun?"teppo":"archer")+"-sling.txt");
            var uv=original.uv;var points=original.vertices;var weights=original.boneWeights;
            var edited=(Vector3[])points.Clone();var skin=(BoneWeight[])weights.Clone();
            var ids=new Dictionary<int,int>();
            var keys=new int[uv.Length];
            for(int i=0;i<uv.Length;i++)
            {
                keys[i]=(Mathf.RoundToInt(uv[i].x*16384)<<16)|Mathf.RoundToInt(uv[i].y*16384);
                ids[keys[i]]=i;
            }
            var map=new Dictionary<int,int>();
            foreach(string line in lines)
            {
                if(line.Length!=16) continue;
                int source=int.Parse(line.Substring(0,8),NumberStyles.HexNumber);
                int target=int.Parse(line.Substring(8,8),NumberStyles.HexNumber);
                if(Nearest(ids,target,out int index)) map[source]=index;
            }
            int moved=0;
            for(int i=0;i<uv.Length;i++)
                if(Nearest(map,keys[i],out int target))
                { edited[i]=points[target];skin[i]=weights[target];moved++; }
            if(moved<50) throw new InvalidDataException("Missing source UVs for "+original.name+", matched "+moved);
            var mesh=UnityEngine.Object.Instantiate(original);mesh.name="Shoulder sling";
            mesh.vertices=edited;mesh.boneWeights=skin;mesh.RecalculateBounds();
            return mesh;
        }

        static bool Nearest(Dictionary<int,int> values,int key,out int value)
        {
            if(values.TryGetValue(key,out value)) return true;
            for(int x=-1;x<=1;x++) for(int y=-1;y<=1;y++)
                if(values.TryGetValue(key+x*65536+y,out value)) return true;
            value=0;return false;
        }

        sealed class Surface
        {
            readonly List<Vector3> verts=new List<Vector3>();
            readonly List<Vector2> uv=new List<Vector2>();
            readonly List<int> tris=new List<int>();
            public void Tube(Vector3[] path,float[] widths,float[] depths,int tone,int sides=8)
            {
                int first=verts.Count;
                for(int j=0;j<path.Length;j++)
                {
                    Vector3 tangent=(path[Mathf.Min(path.Length-1,j+1)]-path[Mathf.Max(0,j-1)]).normalized;
                    Vector3 across=Vector3.Cross(tangent,Vector3.right).normalized;
                    for(int i=0;i<sides;i++)
                    {
                        float t=2f*Mathf.PI*i/sides;
                        verts.Add(path[j]+Vector3.right*(Mathf.Cos(t)*widths[j])+across*(Mathf.Sin(t)*depths[j]));
                        uv.Add(new Vector2((tone+.5f)/6f,.5f));
                        if(j==0) continue;
                        int a=first+(j-1)*sides+i,b=first+(j-1)*sides+(i+1)%sides;
                        tris.Add(a);tris.Add(b);tris.Add(b+sides);tris.Add(a);tris.Add(b+sides);tris.Add(a+sides);
                    }
                }
                int end=first+(path.Length-1)*sides;
                for(int i=1;i<sides-1;i++)
                { tris.Add(first);tris.Add(first+i+1);tris.Add(first+i);tris.Add(end);tris.Add(end+i);tris.Add(end+i+1); }
            }
            public Mesh Make(string name)
            {
                var mesh=new Mesh {name=name};mesh.SetVertices(verts);mesh.SetUVs(0,uv);mesh.SetTriangles(tris,0);
                mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
            }
        }
        static Vector3 V(float y,float z) { return new Vector3(0,y,z); }
        public static void Bake(TacticsFigureProfile profile,Animation an,bool gun)
        {
            var skin=an.GetComponentInChildren<SkinnedMeshRenderer>();
            profile.drawnBody=DrawnSkin(skin.sharedMesh,gun);
            var geo=new Surface();
            if(gun)
            {
                geo.Tube(new[]{V(-.08f,-.31f),V(-.06f,-.21f),V(-.025f,-.065f),V(-.018f,.12f),V(-.012f,.56f)},
                    new[]{.024f,.032f,.023f,.025f,.014f},new[]{.055f,.05f,.028f,.031f,.018f},0);
                geo.Tube(new[]{V(.016f,-.03f),V(.016f,.56f),V(.017f,.64f)},new[]{.022f,.017f,.021f},new[]{.022f,.017f,.021f},2);
                foreach(float z in new[]{.09f,.28f,.49f})
                    geo.Tube(new[]{V(.003f,z),V(.003f,z+.018f)},new[]{.026f,.026f},new[]{.037f,.037f},3);
            }
            else
            {
                geo.Tube(new[]{V(-.45f,-.14f),V(-.39f,-.12f),V(-.28f,-.047f),V(-.12f,-.009f),V(0,0),
                    V(.2f,.014f),V(.4f,-.01f),V(.57f,-.069f),V(.72f,-.15f),V(.79f,-.14f)},
                    new[]{.008f,.013f,.016f,.018f,.018f,.019f,.016f,.014f,.011f,.007f},
                    new[]{.009f,.012f,.014f,.015f,.017f,.014f,.013f,.012f,.009f,.007f},1);
                geo.Tube(new[]{V(-.067f,-.003f),V(.068f,.004f)},new[]{.019f,.019f},new[]{.020f,.020f},4);
                for(int i=0;i<5;i++){float y=-.055f+.024f*i;geo.Tube(new[]{V(y,0),V(y+.007f,0)},new[]{.02f,.02f},new[]{.021f,.021f},3);}
            }
            profile.heldWeapon=geo.Make(gun?"Teppo":"Yumi");
            var line=new Surface();line.Tube(new[]{V(0,0),V(0,1)},new[]{.0028f,.0028f},new[]{.0028f,.0028f},5,5);
            profile.bowCord=gun?null:line.Make("Yumi cord");
            var palette=new Texture2D(6,1,TextureFormat.RGBA32,false){name="Lacquer, bamboo and silk",filterMode=FilterMode.Point};
            palette.SetPixels32(new[]{new Color32(123,73,43,255),new Color32(153,92,49,255),new Color32(102,104,101,255),
                new Color32(176,139,75,255),new Color32(76,49,37,255),new Color32(207,187,148,255)});
            palette.Apply();
            profile.weaponSurface=new Material(Resources.Load<Shader>("TacticsUnit")){name="Army equipment",mainTexture=palette};
            AssetDatabase.AddObjectToAsset(profile.drawnBody,profile);AssetDatabase.AddObjectToAsset(profile.heldWeapon,profile);
            AssetDatabase.AddObjectToAsset(palette,profile);AssetDatabase.AddObjectToAsset(profile.weaponSurface,profile);
            if(profile.bowCord) AssetDatabase.AddObjectToAsset(profile.bowCord,profile);
        }
    }
}
