#if UNITY_EDITOR
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
        public const string UndeadFbxPath = "Assets/_Project/Models/Samurai_Undead.fbx";
        const string ControllerPath = "Assets/_Project/Animation/Hero.controller";
        const string UndeadControllerPath = "Assets/_Project/Animation/Undead.controller";
        const float ModelScale = 0.95f;   // подгонка роста модели под зоны поражения

        public static bool TryAttach(FighterController fighter, Transform bladeRoot, Look look, bool undead = false)
        {
            string fbx = undead ? UndeadFbxPath : FbxPath;
            string ctrlPath = undead ? UndeadControllerPath : ControllerPath;
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (asset == null)
            {
                Debug.LogWarning("HeroModelBuilder: FBX не найден (" + fbx + "), используется модель из примитивов");
                return false;
            }

            ConfigureImport(fbx);
            AnimatorController ctrl = BuildController(fbx, ctrlPath);

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            model.name = undead ? "UndeadModel" : "HeroModel";
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
            model.transform.localScale = Vector3.one * ModelScale;

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
            d.spine = FindBone(model.transform, "Spine");
            d.chest = FindBone(model.transform, "Chest");
            d.upperArmL = FindBone(model.transform, "UpperArm.L");
            d.forearmL = FindBone(model.transform, "Forearm.L");
            d.handL = FindBone(model.transform, "Hand.L");
            d.upperArmR = FindBone(model.transform, "UpperArm.R");
            d.forearmR = FindBone(model.transform, "Forearm.R");
            d.handR = FindBone(model.transform, "Hand.R");

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

            Debug.Log("HeroModelBuilder: модель " + (undead ? "врага" : "героя") + " подключена (кости: " + (d.handL != null && d.handR != null ? "руки найдены" : "РУКИ НЕ НАЙДЕНЫ") + ")");
            return true;
        }

        static Material MapMaterial(string name, Look look, bool undead)
        {
            switch (name)
            {
                case "Skin": return undead ? look.deadSkin : look.skin;
                case "Armor_Black": return undead ? look.enemyArmor : look.heroArmor;
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

        static AnimatorController BuildController(string fbx, string ControllerPath)
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
