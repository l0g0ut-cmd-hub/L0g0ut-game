#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace BladePath.EditorTools
{
    /// <summary>
    /// Подключает скинутую модель героя (Assets/_Project/Models/Samurai_Hero.fbx, её делает tools/blender/build_samurai.py):
    /// настройка импорта, аниматор-контроллер из клипов FBX, посадка на бойца с поправкой на ориентацию и масштаб,
    /// подмена материалов на наши текстурированные, привязка HeroRigDriver.
    /// Если FBX нет — возвращает false, и боец остаётся «одетым» из примитивов (CharacterVisuals.DressHero).
    /// </summary>
    public static class HeroModelBuilder
    {
        public const string FbxPath = "Assets/_Project/Models/Samurai_Hero.fbx";
        public const string RoninFbxPath = "Assets/_Project/Models/Ronin_Player.fbx";   // игрок из Meshy (tools/blender/build_ronin.py)
        const float RoninScale = 0.91f;                                                  // рост модели 1.90 м → как у зон поражения
        public const string Hero3FbxPath = "Assets/_Project/Models/Hero3_Player.fbx";       // игрок: Meshy «Crimson Shadow Samurai» (пучок, чёрный доспех, красные шнуры), tools/blender/build_hero3.py
        const float Hero3Scale = 0.985f;                                                     // с пучком 1,80 м, макушка ~1,72 м — как у остальных
        public const string Samurai2FbxPath = "Assets/_Project/Models/Samurai2_Player.fbx"; // игрок: самурай в красном доспехе с текстурами (tools/blender/build_samurai_m2.py)
        public const string ShadowFbxPath = "Assets/_Project/Models/Samurai_Shadow.fbx";    // враг: самурай в рогатом шлеме (CGTrader, Rigify → наш скелет, tools/blender/build_samurai_cg.py), тёмная раскраска
        const float SamuraiScale = 1f;                                                       // обе собраны в метрах: макушка 1.72 м
        public const string UndeadFbxPath = "Assets/_Project/Models/Samurai_Undead.fbx";
        const string ControllerPath = "Assets/_Project/Animation/Hero.controller";
        const string UndeadControllerPath = "Assets/_Project/Animation/Undead.controller";
        const float ModelScale = 0.95f;   // подгонка роста модели под зоны поражения

        static bool shadowLook;
        static bool Exists(string path) { return AssetDatabase.LoadAssetAtPath<GameObject>(path) != null; }

        const string EnemyRoninControllerPath = "Assets/_Project/Animation/EnemyRonin.controller";

        public static bool TryAttach(FighterController fighter, Transform bladeRoot, Look look, bool undead = false)
        {
            // враг — несколько обликов по ступеням «Пути»: самурай в красном доспехе с полумесяцем на кабуто и ронин в чёрно-красном
            // доспехе с бородой (Meshy); у каждого свой скелет и клипы, активен один (EnemySkins)
            if (undead)
            {
                string first = Exists(Samurai2FbxPath) ? Samurai2FbxPath : Exists(ShadowFbxPath) ? ShadowFbxPath : UndeadFbxPath;
                GameObject a = Attach(fighter, bladeRoot, look, true, first, UndeadControllerPath, "Undead_", "UndeadModel");
                if (a == null) return false;
                if (Exists(RoninFbxPath) && first != RoninFbxPath)
                {
                    GameObject b = Attach(fighter, bladeRoot, look, true, RoninFbxPath, EnemyRoninControllerPath, "EnemyRonin_", "EnemyRoninModel");
                    if (b != null)
                    {
                        EnemySkins sk = fighter.gameObject.AddComponent<EnemySkins>();
                        sk.models = new[] { a, b };
                        b.SetActive(false);
                    }
                }
                return true;
            }
            string heroFbx = Exists(Hero3FbxPath) ? Hero3FbxPath : Exists(Samurai2FbxPath) ? Samurai2FbxPath : Exists(RoninFbxPath) ? RoninFbxPath : FbxPath;
            return Attach(fighter, bladeRoot, look, false, heroFbx, ControllerPath, "Hero_", null) != null;
        }

        static GameObject Attach(FighterController fighter, Transform bladeRoot, Look look, bool undead, string fbx, string ctrlPath, string clipPrefix, string modelName)
        {
            bool hero3 = fbx == Hero3FbxPath;
            bool ronin = fbx == RoninFbxPath;
            bool samurai = fbx == Samurai2FbxPath || fbx == ShadowFbxPath || hero3;
            shadowLook = fbx == ShadowFbxPath;
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (asset == null)
            {
                Debug.LogWarning("HeroModelBuilder: FBX не найден (" + fbx + "), используется модель из примитивов");
                return null;
            }

            ConfigureImport(fbx);

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            model.name = modelName ?? (undead ? "UndeadModel" : (ronin ? "RoninModel" : "HeroModel"));
            model.transform.SetParent(fighter.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            // Ориентация: по маркеру FrontMarker (стоит перед тазом) — не зависим от того, как FBX-импорт повернул оси
            Transform hips = FindBone(model.transform, "Hips");
            Transform marker = FindBone(model.transform, "FrontMarker");
            if (hips != null && marker != null)
            {
                Vector3 fwd = fighter.transform.InverseTransformDirection(marker.position - hips.position);
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 1e-6f)
                    model.transform.localRotation = Quaternion.Euler(0f, Vector3.SignedAngle(fwd, Vector3.forward, Vector3.up), 0f);
            }
            else Debug.LogWarning("HeroModelBuilder: не найден Hips/FrontMarker — ориентация модели не скорректирована");
            model.transform.localScale = Vector3.one * (hero3 ? Hero3Scale : samurai ? SamuraiScale : ronin ? RoninScale : ModelScale);

            // Материалы: по имени из Blender → наши текстурированные
            foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Material[] mats = smr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material mapped = MapMaterial(mats[i] != null ? mats[i].name : "", look, undead);
                    if (mapped != null) mats[i] = mapped;
                }
                smr.sharedMaterials = mats;
                smr.updateWhenOffscreen = true;   // модель сильно двигается, bounds из позы покоя не годятся для отсечения
            }

            // раны (шейдер персонажа): точка позы покоя в UV3 — брызги крови берутся из 3D-шума по телу, без швов развёртки
            // (по развёртке пятна резались на краях островков, и на доспехе это выглядело как разрывы)
            BakeWoundCoords(model, modelName ?? model.name);

            // Мокап-ходьба: клипы под скелет этой модели (вперёд/назад/вбок), до сборки контроллера
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Animation")) AssetDatabase.CreateFolder("Assets/_Project", "Animation");
            List<MocapBaker.Result> loco = MocapBaker.Bake(model.transform, fighter.transform, n => FindBone(model.transform, n), Clip(fbx, "Idle"), "Assets/_Project/Animation", clipPrefix);
            AnimatorController ctrl = BuildController(fbx, ctrlPath, loco);

            // Аниматор
            Animator anim = model.GetComponent<Animator>();
            if (anim == null) anim = model.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            if (anim.avatar == null)
            {
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(fbx))
                {
                    Avatar av = o as Avatar;
                    if (av != null) { anim.avatar = av; break; }
                }
            }

            // Драйвер скелета: IK рук к рукояти, наклон корпуса, параметры аниматора
            HeroRigDriver d = model.AddComponent<HeroRigDriver>();
            d.fighter = fighter;
            d.weapon = fighter.weapon;
            d.bladeRoot = bladeRoot;
            d.animator = anim;
            d.hips = FindBone(model.transform, "Hips");
            d.head = FindBone(model.transform, "Head");
            d.upperLegL = FindBone(model.transform, "UpperLeg.L");
            d.lowerLegL = FindBone(model.transform, "LowerLeg.L");
            d.footL = FindBone(model.transform, "Foot.L");
            d.upperLegR = FindBone(model.transform, "UpperLeg.R");
            d.lowerLegR = FindBone(model.transform, "LowerLeg.R");
            d.footR = FindBone(model.transform, "Foot.R");
            d.spine = FindBone(model.transform, "Spine");
            d.chest = FindBone(model.transform, "Chest");
            d.upperArmL = FindBone(model.transform, "UpperArm.L");
            d.forearmL = FindBone(model.transform, "Forearm.L");
            d.handL = FindBone(model.transform, "Hand.L");
            d.upperArmR = FindBone(model.transform, "UpperArm.R");
            d.forearmR = FindBone(model.transform, "Forearm.R");
            d.handR = FindBone(model.transform, "Hand.R");
            d.mocapLoco = loco.Count >= 3;
            d.locoDirs = new Vector2[loco.Count]; d.locoSpeeds = new float[loco.Count];
            for (int i = 0; i < loco.Count; i++) { d.locoDirs[i] = loco[i].dir; d.locoSpeeds[i] = loco[i].speed; }
            if (samurai) { d.palmOffset = fbx == Samurai2FbxPath ? 0.085f : 0.09f; d.palmLift = fbx == Samurai2FbxPath ? 0.03f : 0.035f; d.eyeHeight = 0.10f; }
            if (hero3) { d.palmOffset = 0.111f; d.palmLift = 0.017f; d.gripFromBind = true; d.gripDown = 0.113f * Hero3Scale; d.gripIn = 0.017f * Hero3Scale; d.handForward = 0.04f; d.handLift = -0.05f; if (fighter.weapon != null) { fighter.weapon.openHips = 24f; fighter.weapon.openChest = 10f; } }   // кулак согнут в меше: центр хвата 0,113 м ниже запястья, 1,7 см к ладони   // кулак сжат вокруг рукояти: рукоять внутри пальцев

            // разворот корпуса в стойке по облику: у героя и ронина (широкие плечи, короткие руки) — закрытее, у самурая — как в профиле
            if (hero3) { d.stanceOpenHips = 24f; d.stanceOpenChest = 10f; }
            else if (undead) { d.stanceOpenHips = ronin ? 20f : 0f; d.stanceOpenChest = ronin ? 12f : 0f; }
            model.AddComponent<ContactShadow>().rig = d;   // мягкая тень под стопами

            // полы плаща (кости Coat.*) — на пружинах: инерция ткани, бёдра раздвигают полы
            {
                CoatSpring cs = model.AddComponent<CoatSpring>();
                cs.Collect(model.transform);
                if (cs.panels.Count == 0) Object.DestroyImmediate(cs);
                else Debug.Log("HeroModelBuilder: полы плаща на пружинах — " + cs.panels.Count + " костей");
            }

            if (undead)
            {
                TentacleBoneWiggle tw = model.AddComponent<TentacleBoneWiggle>();
                tw.Collect();
            }

            // Накладки подсветки попадания: зоны (капсулы) в покое невидимы
            foreach (BodyZone z in fighter.zones)
            {
                z.overlayFlash = true;
                if (z.flashRenderer != null) z.flashRenderer.enabled = false;
            }

            Debug.Log("HeroModelBuilder: модель " + (undead ? "врага" : "героя") + " " + model.name + " подключена (кости: " + (d.handL != null && d.handR != null ? "руки найдены" : "РУКИ НЕ НАЙДЕНЫ") + ")");
            return model;
        }

        static void BakeWoundCoords(GameObject model, string key)
        {
            const string folder = "Assets/_Project/Models/Generated";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/_Project/Models", "Generated");
            int i = 0;
            foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh src = smr.sharedMesh;
                if (src == null) continue;
                Mesh m = Object.Instantiate(src);
                m.name = src.name + "_wound";
                Vector3[] v = src.vertices;
                Bounds b = src.bounds;
                // рост — по наибольшему размеру (у части FBX ось вверх — Z, и по size.y шум выходил втрое мельче: крапины-«дыры»)
                float h = Mathf.Max(1e-4f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
                // «вверх» в позе привязки: от центра к кости головы (иначе — по наибольшей оси); w — доля роста 0 (стопы) … 1 (макушка)
                Vector3 up = b.size.y >= b.size.x && b.size.y >= b.size.z ? Vector3.up : b.size.z >= b.size.x ? Vector3.forward : Vector3.right;
                Transform[] bones = smr.bones; Matrix4x4[] bind = src.bindposes;
                for (int j = 0; j < bones.Length && j < bind.Length; j++)
                {
                    if (bones[j] == null) continue;
                    string bn = bones[j].name.ToLowerInvariant();
                    if (!bn.EndsWith("head") && !bn.EndsWith(":head") && bn != "head") continue;
                    Vector3 hp = bind[j].inverse.MultiplyPoint3x4(Vector3.zero);
                    if ((hp - b.center).sqrMagnitude > 1e-8f) up = (hp - b.center).normalized;
                    break;
                }
                float lo = float.MaxValue, hi = float.MinValue;
                for (int k = 0; k < v.Length; k++) { float d = Vector3.Dot(v[k] - b.center, up); lo = Mathf.Min(lo, d); hi = Mathf.Max(hi, d); }
                float span = Mathf.Max(1e-4f, hi - lo);
                List<Vector4> p = new List<Vector4>(v.Length);
                for (int k = 0; k < v.Length; k++)
                {
                    Vector3 q = (v[k] - b.min) / h * 1.8f;   // в «метрах» фигуры ростом 1,8 независимо от единиц FBX
                    p.Add(new Vector4(q.x, q.y, q.z, (Vector3.Dot(v[k] - b.center, up) - lo) / span));
                }
                m.SetUVs(3, p);
                string path = folder + "/" + key + "_" + (i++) + "_wound.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(m, path);
                smr.sharedMesh = m;
                foreach (Material mat in smr.sharedMaterials) if (mat != null && mat.HasProperty("_WoundTri")) { mat.SetFloat("_WoundTri", 1f); EditorUtility.SetDirty(mat); }
            }
        }

        static Material MapMaterial(string name, Look look, bool undead)
        {
            if (name.StartsWith("SAM_")) return VisualMaterials.CharacterRim(VisualMaterials.Samurai(name.Substring(4), shadowLook), shadowLook);
            if (name == "M2_Body") return VisualMaterials.CharacterRim(VisualMaterials.Samurai2(), undead);
            if (name == "H3_Body") return VisualMaterials.CharacterRim(VisualMaterials.Hero3(), false);
            switch (name)
            {
                case "Skin": return undead ? look.deadSkin : look.skin;
                case "Armor_Black": return undead ? look.enemyArmor : look.heroArmor;
                case "Ronin":
                    {
                        if (!undead) return look.ronin;
                        Material rm = VisualMaterials.CharacterRim(look.ronin, true);
                        // у тёмного доспеха ронина фиолетовый контур врага заливал всю фигуру сиреневым — контур холодный стальной,
                        // а сам доспех чуть теплее (под луной чёрно-красный уходил в фиолет)
                        if (rm != null) { rm.SetColor("_RimColor", new Color(0.12f, 0.12f, 0.15f)); rm.color = new Color(1f, 0.93f, 0.88f); EditorUtility.SetDirty(rm); }
                        return rm;
                    }
                case "Void": return look.tentacle;
                case "Ember": return look.ember;
                case "Flesh": return look.flesh;
                case "Tooth": return look.tooth;
                case "Maw": return look.maw;
                case "Lacquer_Red": return look.lacquer;
                case "Brass": return look.bronze;
                case "Dark_Cloth": return look.cloth;
                case "Beard": return look.hair;
                default: return null;
            }
        }

        static void ConfigureImport(string fbx)
        {
            ModelImporter mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (mi == null) return;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.importAnimation = true;
            mi.importCameras = false;
            mi.importLights = false;
            ModelImporterClipAnimation[] clips = mi.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
            {
                bool loop = clips[i].name.Contains("Idle") || clips[i].name.Contains("WalkF");
                clips[i].loopTime = loop;
                clips[i].loopPose = loop;
            }
            mi.clipAnimations = clips;
            mi.SaveAndReimport();
        }

        static AnimationClip Clip(string fbx, string key)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(fbx))
            {
                AnimationClip c = o as AnimationClip;
                if (c != null && !c.name.StartsWith("__preview") && c.name.Contains(key)) return c;
            }
            Debug.LogWarning("HeroModelBuilder: клип '" + key + "' не найден в FBX");
            return null;
        }

        static AnimatorController BuildController(string fbx, string ControllerPath, List<MocapBaker.Result> loco)
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Animation")) AssetDatabase.CreateFolder("Assets/_Project", "Animation");
            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController c = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            c.AddParameter("Move", AnimatorControllerParameterType.Float);
            c.AddParameter("WalkSpeed", AnimatorControllerParameterType.Float);
            c.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            c.AddParameter("Hit", AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine sm = c.layers[0].stateMachine;
            AnimatorState idle = sm.AddState("Idle"); idle.motion = Clip(fbx, "Idle");
            AnimatorState walk = sm.AddState("Walk"); walk.motion = Clip(fbx, "WalkF");
            walk.speedParameterActive = true; walk.speedParameter = "WalkSpeed";
            AnimatorState jump = sm.AddState("Jump"); jump.motion = Clip(fbx, "Jump");
            AnimatorState hit = sm.AddState("Hit"); hit.motion = Clip(fbx, "Hit");
            sm.defaultState = idle;

            // мокап-ходьба: 2D-смесь клипов по направлению хода в системе бойца (x — вправо, y — вперёд), темп — LocoSpeed
            if (loco != null && loco.Count >= 3)
            {
                c.AddParameter("MoveX", AnimatorControllerParameterType.Float);
                c.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
                c.AddParameter("Moving", AnimatorControllerParameterType.Bool);
                c.AddParameter("LocoSpeed", AnimatorControllerParameterType.Float);
                BlendTree bt = new BlendTree { name = "Loco", blendType = BlendTreeType.FreeformDirectional2D, blendParameter = "MoveX", blendParameterY = "MoveZ", useAutomaticThresholds = false };
                AssetDatabase.AddObjectToAsset(bt, c);
                foreach (MocapBaker.Result r in loco) bt.AddChild(r.clip, r.dir);
                AnimatorState ls = sm.AddState("Loco"); ls.motion = bt;
                ls.speedParameterActive = true; ls.speedParameter = "LocoSpeed";
                AnimatorStateTransition lt = idle.AddTransition(ls);
                lt.hasExitTime = false; lt.duration = 0.2f;
                lt.AddCondition(AnimatorConditionMode.If, 0f, "Moving");
                lt = ls.AddTransition(idle);
                lt.hasExitTime = false; lt.duration = 0.2f;
                lt.AddCondition(AnimatorConditionMode.IfNot, 0f, "Moving");
                lt = ls.AddTransition(jump);
                lt.hasExitTime = false; lt.duration = 0.08f;
                lt.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            }

            // стойка → шаг (вперёд или назад)
            AnimatorStateTransition t = idle.AddTransition(walk);
            t.hasExitTime = false; t.duration = 0.12f;
            t.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Move");
            t = idle.AddTransition(walk);
            t.hasExitTime = false; t.duration = 0.12f;
            t.AddCondition(AnimatorConditionMode.Less, -0.1f, "Move");
            // шаг → стойка
            t = walk.AddTransition(idle);
            t.hasExitTime = false; t.duration = 0.12f;
            t.AddCondition(AnimatorConditionMode.Less, 0.1f, "Move");
            t.AddCondition(AnimatorConditionMode.Greater, -0.1f, "Move");
            // прыжок
            t = sm.AddAnyStateTransition(jump);
            t.hasExitTime = false; t.duration = 0.08f; t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            t = jump.AddTransition(idle);
            t.hasExitTime = false; t.duration = 0.10f;
            t.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
            // получение удара
            t = sm.AddAnyStateTransition(hit);
            t.hasExitTime = false; t.duration = 0.05f; t.canTransitionToSelf = true;
            t.AddCondition(AnimatorConditionMode.If, 0f, "Hit");
            t = hit.AddTransition(idle);
            t.hasExitTime = true; t.exitTime = 0.9f; t.duration = 0.10f;

            EditorUtility.SetDirty(c);
            AssetDatabase.SaveAssets();
            return c;
        }

        /// <summary>Ищет кость по имени, игнорируя регистр и разделители ('.', '_', ':') — импорт FBX может их менять.</summary>
        static Transform FindBone(Transform root, string name)
        {
            string key = Normalize(name);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (Normalize(t.name) == key) return t;
            return null;
        }

        static string Normalize(string s) { return s.Replace(".", "").Replace("_", "").Replace(":", "").ToLowerInvariant(); }
    }
}
#endif
