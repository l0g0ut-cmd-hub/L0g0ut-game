using System.Collections.Generic;
using UnityEngine;

namespace BladePath
{
    // Joint directions from the project's forward Great Sword Walk recording.
    // Use each rig's limb lengths. Upper-body animation stays specific to its weapon.
    internal sealed class TacticsAdvance
    {
        readonly Transform root, hips, spine;
        readonly Transform[] legs;
        readonly float stride, footClearance, leftSign;
        readonly Vector3 restHips;
        float phase;

        public static TacticsAdvance Bind(Transform root)
        {
            var bones = new Dictionary<string, Transform>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>())
                bones[t.name] = t;
            Transform pelvis;
            if (!bones.TryGetValue("Hips", out pelvis)) return null;
            string[] names = { "LeftUpLeg", "LeftLeg", "LeftFoot", "LeftToeBase",
                               "RightUpLeg", "RightLeg", "RightFoot", "RightToeBase" };
            var chain = new Transform[names.Length];
            for (int i = 0; i < names.Length; i++)
                if (!bones.TryGetValue(names[i], out chain[i])) return null;
            Transform spine;
            bones.TryGetValue("Spine02", out spine);
            return new TacticsAdvance(root, pelvis, spine, chain);
        }

        TacticsAdvance(Transform root, Transform hips, Transform spine, Transform[] legs)
        {
            this.root = root;
            this.hips = hips;
            this.spine = spine;
            this.legs = legs;
            restHips = root.InverseTransformPoint(hips.position);
            leftSign = Mathf.Sign(Vector3.Dot(legs[0].position - legs[4].position, root.right));
            float legLength = Vector3.Distance(legs[0].position, legs[1].position)
                            + Vector3.Distance(legs[1].position, legs[2].position);
            stride = Mathf.Max(.1f, legLength * 1.74f);
            footClearance = Mathf.Clamp((legs[3].position.y + legs[7].position.y) * .5f - root.position.y, .015f, legLength * .15f);
        }

        public void Travel(float meters)
        {
            phase = Mathf.Repeat(phase + meters / stride, 1f);
        }

        public void Apply(float weight)
        {
            if (weight <= 0f) return;
            // Motion samples contain lateral pelvis travel followed by six bone directions.
            const int channels = 23;
            int frames = Samples.Length / channels;
            float sample = phase * frames;
            int frame = (int)sample;
            int a = frame * channels, b = ((frame + 1) % frames) * channels;
            float t = sample - frame;
            Vector3 stable = root.TransformPoint(restHips);
            stable += root.right * (leftSign * Channel(a, b, 0, t) * stride / 1.52f);
            stable += root.forward * (Channel(a, b, 19, t) * stride / 1.52f);
            stable.y = hips.position.y;
            hips.position = Vector3.Lerp(hips.position, stable, weight);
            for (int i = 0; i < 6; i++)
            {
                int k = 1 + 3 * i;
                int n = i < 3 ? i : i + 1;
                Vector3 heading = root.TransformDirection(new Vector3(
                    leftSign * Channel(a, b, k, t), Channel(a, b, k + 1, t), Channel(a, b, k + 2, t)));
                Vector3 actual = legs[n + 1].position - legs[n].position;
                Quaternion turn = Quaternion.FromToRotation(actual, heading);
                legs[n].rotation = Quaternion.Slerp(Quaternion.identity, turn, weight) * legs[n].rotation;
            }
            if (spine != null)
            {
                float yaw = leftSign * Channel(a, b, 20, t) * 10f;
                float lean = 4f + Channel(a, b, 21, t) * 10f;
                float side = -leftSign * Channel(a, b, 22, t) * 10f;
                Quaternion shift = Quaternion.AngleAxis(yaw, root.up) * Quaternion.AngleAxis(lean, root.right)
                    * Quaternion.AngleAxis(side, root.forward);
                spine.rotation = Quaternion.Slerp(Quaternion.identity, shift, weight * .65f) * spine.rotation;
            }
            float sole = Mathf.Min(legs[3].position.y, legs[7].position.y);
            hips.position += Vector3.up * ((root.position.y + footClearance - sole) * weight);
        }

        static float Channel(int a, int b, int k, float t)
        {
            return Mathf.Lerp(Samples[a + k], Samples[b + k], t) / 1000f;
        }

        // Source axes converted to anatomical left, up and forward. The end frame duplicates the first.
        static readonly short[] Samples = {
            -43, -96, -768, 633, 230, -687, -690, 68, -969, 239, -219, -844, 490, 324, -902, -286, -431, -656, 619, 0, -141, 37, 90,
            -44, -113, -740, 663, 263, -783, -564, 84, -899, 431, -224, -862, 454, 328, -893, -308, -431, -664, 611, -8, -145, 97, 75,
            -44, -122, -726, 677, 283, -867, -411, 28, -807, 590, -230, -878, 420, 330, -885, -330, -439, -670, 599, -17, -142, 153, 53,
            -44, -133, -712, 690, 286, -926, -246, -44, -689, 724, -234, -892, 386, 333, -875, -351, -445, -676, 587, -26, -139, 209, 28,
            -42, -129, -721, 681, 274, -959, -70, -121, -570, 813, -242, -904, 354, 336, -863, -379, -459, -676, 576, -33, -157, 244, 26,
            -40, -125, -729, 673, 242, -964, 106, -207, -438, 875, -248, -914, 321, 340, -849, -405, -472, -676, 565, -41, -175, 278, 23,
            -38, -103, -747, 657, 205, -946, 250, -245, -304, 921, -253, -926, 282, 343, -832, -436, -493, -665, 561, -44, -180, 279, 27,
            -33, -100, -749, 655, 191, -940, 283, -221, -298, 929, -255, -938, 233, 333, -812, -479, -509, -655, 558, -39, -157, 251, 36,
            -28, -97, -751, 653, 179, -931, 317, -199, -291, 936, -255, -949, 184, 325, -790, -520, -524, -646, 555, -34, -135, 222, 45,
            -23, -93, -753, 651, 170, -921, 350, -179, -285, 942, -254, -958, 134, 319, -764, -561, -536, -637, 554, -29, -114, 194, 56,
            -16, -97, -747, 658, 176, -944, 279, -233, -515, 825, -242, -968, 70, 292, -732, -616, -543, -653, 528, -10, -109, 90, 44,
            -7, -122, -726, 677, 181, -972, 151, -233, -602, 764, -230, -973, 12, 257, -698, -669, -531, -708, 465, 10, -103, -30, 24,
            5, -150, -712, 686, 177, -984, 14, -215, -636, 741, -246, -969, -27, 238, -658, -715, -515, -807, 289, 33, -79, -140, 1,
            16, -167, -714, 680, 167, -980, -107, -210, -642, 737, -286, -958, -30, 230, -624, -747, -431, -902, -12, 53, -53, -227, -24,
            27, -178, -727, 663, 153, -968, -200, -206, -641, 739, -327, -945, 7, 227, -586, -778, -327, -898, -294, 66, -35, -283, -50,
            36, -187, -747, 638, 140, -952, -273, -201, -639, 742, -372, -925, 80, 242, -534, -810, -229, -881, -413, 73, -24, -313, -75,
            41, -193, -769, 610, 133, -934, -332, -198, -640, 742, -409, -898, 160, 265, -502, -824, -157, -936, -316, 75, -13, -327, -102,
            44, -195, -790, 581, 127, -916, -380, -197, -640, 743, -435, -867, 244, 286, -502, -816, -129, -970, -205, 72, 1, -321, -114,
            46, -195, -814, 547, 120, -900, -419, -197, -641, 742, -451, -831, 326, 309, -540, -783, -177, -976, -125, 69, 17, -291, -109,
            49, -190, -840, 508, 110, -887, -449, -196, -641, 742, -453, -794, 405, 312, -606, -731, -217, -976, 8, 63, 46, -240, -102,
            50, -180, -865, 469, 97, -877, -471, -193, -648, 736, -439, -762, 476, 286, -702, -652, -219, -958, 184, 54, 73, -200, -103,
            51, -171, -887, 429, 83, -866, -492, -191, -655, 731, -423, -724, 544, 265, -786, -559, -212, -911, 354, 46, 99, -159, -105,
            51, -158, -905, 394, 68, -857, -511, -184, -668, 721, -397, -698, 596, 231, -869, -438, -198, -816, 543, 36, 85, -97, -113,
            49, -147, -917, 370, 60, -844, -533, -174, -688, 704, -371, -681, 631, 196, -931, -307, -242, -726, 644, 23, 63, -18, -115,
            47, -137, -929, 345, 50, -830, -555, -165, -707, 687, -343, -664, 665, 161, -973, -165, -278, -619, 734, 11, 38, 61, -115,
            46, -128, -939, 319, 39, -816, -576, -156, -726, 670, -312, -645, 697, 129, -991, -16, -307, -499, 810, -1, 11, 140, -112,
            44, -120, -949, 293, 28, -802, -597, -148, -744, 652, -280, -626, 727, 102, -986, 134, -328, -369, 869, -13, -19, 219, -107,
            39, -111, -957, 268, 25, -778, -628, -141, -757, 638, -272, -615, 741, 103, -967, 235, -390, -272, 880, -22, -14, 251, -111,
            34, -101, -967, 236, 24, -750, -661, -135, -769, 625, -264, -605, 751, 107, -953, 283, -392, -238, 889, -28, 19, 239, -106,
            28, -89, -978, 190, 22, -721, -693, -129, -777, 616, -234, -627, 743, 109, -948, 298, -390, -245, 887, -27, 76, 188, -82,
            21, -71, -989, 128, 18, -692, -722, -121, -793, 598, -203, -669, 716, 138, -947, 291, -407, -334, 850, -19, 135, 120, -39,
            13, -53, -996, 72, 16, -661, -750, -116, -840, 531, -185, -724, 665, 165, -942, 293, -426, -483, 765, -7, 176, 63, 14,
            5, -37, -999, 24, 21, -628, -778, -120, -907, 405, -191, -747, 637, 195, -950, 244, -418, -587, 693, 6, 210, 10, 61,
            -3, -26, -1000, -10, 43, -593, -804, -167, -952, 258, -206, -746, 633, 231, -960, 157, -413, -627, 661, 16, 256, -64, 98,
            -9, -26, -1000, 13, 85, -503, -860, -234, -959, 158, -215, -736, 642, 254, -965, 61, -410, -630, 659, 20, 293, -146, 121,
            -17, -29, -994, 101, 107, -387, -916, -225, -974, 6, -213, -725, 655, 269, -963, -35, -409, -633, 658, 22, 262, -173, 123,
            -24, -32, -981, 190, 123, -320, -939, -191, -977, -96, -207, -731, 650, 278, -954, -109, -411, -635, 654, 22, 196, -160, 130,
            -29, -40, -958, 284, 140, -335, -932, -122, -992, -40, -205, -750, 628, 286, -945, -161, -415, -638, 649, 18, 117, -121, 144,
            -33, -52, -925, 377, 165, -385, -908, -74, -997, 12, -209, -773, 600, 299, -932, -204, -419, -640, 644, 15, 32, -66, 140,
            -38, -65, -877, 475, 184, -471, -863, -9, -997, 75, -211, -798, 564, 309, -921, -235, -427, -642, 637, 9, -34, -22, 127,
            -41, -81, -821, 565, 206, -579, -789, 37, -987, 159, -215, -822, 528, 318, -912, -261, -429, -649, 628, 2, -91, 16, 107,
        };
    }
}
