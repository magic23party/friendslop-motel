using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Friendslop.EditorTools
{
    /// <summary>
    /// P2: человечки из примитивов (игрок и гости делят одну сборку), рука от первого лица (viewmodel).
    /// Всё без коллайдеров; коллайдер персонажа — отдельная невидимая капсула «Body» (как было до P2).
    /// </summary>
    public static partial class Builder
    {
        static GameObject Part(List<Renderer> list, PrimitiveType t, string name, Transform parent, Vector3 pos, Vector3 scale, Material m, Vector3? euler = null)
        {
            var g = Prim(t, name, parent, pos, scale, m, false);
            if (euler.HasValue) g.transform.localRotation = Quaternion.Euler(euler.Value);
            var r = g.GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            list?.Add(r);
            return g;
        }

        static Transform Pivot(string n, Transform p, Vector3 pos)
        {
            var g = new GameObject(n);
            g.transform.SetParent(p, false);
            g.transform.localPosition = pos;
            return g.transform;
        }

        /// <summary>Невидимая капсула-коллайдер персонажа (лучи рук/ударов попадают в неё).</summary>
        static GameObject BodyCollider(Transform root)
        {
            var body = Prim(PrimitiveType.Capsule, "Body", root, new Vector3(0, 0.9f, 0), new Vector3(0.8f, 0.9f, 0.8f), null);
            Object.DestroyImmediate(body.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(body.GetComponent<MeshFilter>());
            return body;
        }

        /// <summary>Человечек ~1.8 м (ноги, торс, руки, голова; аксессуары выключены/включает Humanoid). Узел «Model» под parent.</summary>
        static Humanoid BuildHumanoid(Transform parent)
        {
            var go = new GameObject("Model");
            go.transform.SetParent(parent, false);
            var hm = go.AddComponent<Humanoid>();
            var t = go.transform;

            var shirt = new List<Renderer>(); var pants = new List<Renderer>(); var skin = new List<Renderer>();
            var hair = new List<Renderer>(); var hat = new List<Renderer>(); var cs = new List<Renderer>();

            var mShirt = Mat("ChShirt", new Color(0.8f, 0.2f, 0.18f));
            var mPants = Mat("ChPants", new Color(0.16f, 0.2f, 0.32f));
            var mSkin = Mat("ChSkin", new Color(0.93f, 0.74f, 0.58f));
            var mHair = Mat("ChHair", new Color(0.2f, 0.12f, 0.07f));
            var mHat = Mat("ChHat", Palette.Red);
            var mShoe = Mat("ChShoe", new Color(0.10f, 0.09f, 0.10f));
            var mDark = Mat("ChDark", new Color(0.07f, 0.07f, 0.09f));
            var mMouth = Mat("ChMouth", new Color(0.45f, 0.14f, 0.14f));
            var mWhite = Mat("Eye", Color.white);
            var mPupil = Mat("Pupil", new Color(0.05f, 0.05f, 0.05f));
            var mCase = Mat("ChCase", new Color(0.6f, 0.3f, 0.16f));
            var mMetal = Mat("ChMetal", Palette.Metal);

            // ноги (поворот в бедре) и ботинки
            hm.legL = Pivot("LegL", t, new Vector3(-0.105f, 0.83f, 0f));
            hm.legR = Pivot("LegR", t, new Vector3(0.105f, 0.83f, 0f));
            foreach (var leg in new[] { hm.legL, hm.legR })
            {
                Part(pants, PrimitiveType.Cube, "Leg", leg, new Vector3(0, -0.37f, 0), new Vector3(0.16f, 0.74f, 0.18f), mPants);
                Part(null, PrimitiveType.Cube, "Shoe", leg, new Vector3(0, -0.785f, 0.045f), new Vector3(0.17f, 0.09f, 0.28f), mShoe);
            }

            // торс, ремень, шея
            Part(shirt, PrimitiveType.Cube, "Torso", t, new Vector3(0, 1.13f, 0), new Vector3(0.46f, 0.60f, 0.25f), mShirt);
            Part(null, PrimitiveType.Cube, "Belt", t, new Vector3(0, 0.86f, 0), new Vector3(0.47f, 0.05f, 0.26f), mDark);
            Part(skin, PrimitiveType.Cylinder, "Neck", t, new Vector3(0, 1.46f, 0), new Vector3(0.10f, 0.05f, 0.10f), mSkin);

            // руки (поворот в плече): рукав, предплечье, кисть
            hm.armL = Pivot("ArmL", t, new Vector3(-0.29f, 1.38f, 0f));
            hm.armR = Pivot("ArmR", t, new Vector3(0.29f, 1.38f, 0f));
            foreach (var arm in new[] { hm.armL, hm.armR })
            {
                Part(shirt, PrimitiveType.Cube, "Sleeve", arm, new Vector3(0, -0.15f, 0), new Vector3(0.13f, 0.30f, 0.14f), mShirt);
                Part(skin, PrimitiveType.Cube, "Forearm", arm, new Vector3(0, -0.44f, 0), new Vector3(0.10f, 0.28f, 0.11f), mSkin);
                Part(skin, PrimitiveType.Sphere, "Hand", arm, new Vector3(0, -0.60f, 0), Vector3.one * 0.11f, mSkin);
            }
            hm.handR = Pivot("HandWorld", hm.armR, new Vector3(0, -0.60f, 0.03f));

            // голова
            hm.head = Pivot("Head", t, new Vector3(0, 1.50f, 0));
            var h = hm.head;
            Part(skin, PrimitiveType.Sphere, "Skull", h, new Vector3(0, 0.15f, 0), new Vector3(0.25f, 0.29f, 0.27f), mSkin);
            foreach (float sx in new[] { -0.07f, 0.07f })
            {
                Part(null, PrimitiveType.Sphere, "Eye", h, new Vector3(sx, 0.17f, 0.112f), new Vector3(0.06f, 0.065f, 0.034f), mWhite);
                Part(null, PrimitiveType.Sphere, "Pupil", h, new Vector3(sx, 0.17f, 0.128f), new Vector3(0.028f, 0.032f, 0.018f), mPupil);
            }
            Part(skin, PrimitiveType.Cube, "Nose", h, new Vector3(0, 0.115f, 0.135f), new Vector3(0.04f, 0.05f, 0.05f), mSkin);
            Part(null, PrimitiveType.Cube, "Mouth", h, new Vector3(0, 0.06f, 0.125f), new Vector3(0.08f, 0.015f, 0.03f), mMouth);
            Part(hair, PrimitiveType.Sphere, "Hair", h, new Vector3(0, 0.205f, -0.012f), new Vector3(0.27f, 0.20f, 0.29f), mHair);

            var hl = Pivot("HairLong", h, Vector3.zero);
            Part(hair, PrimitiveType.Cube, "Back", hl, new Vector3(0, 0.08f, -0.115f), new Vector3(0.29f, 0.32f, 0.09f), mHair);
            foreach (float sx in new[] { -0.135f, 0.135f })
                Part(hair, PrimitiveType.Cube, "Side", hl, new Vector3(sx, 0.10f, -0.04f), new Vector3(0.04f, 0.24f, 0.16f), mHair);
            hm.hairLong = hl.gameObject;

            var cap = Pivot("Cap", h, Vector3.zero);
            Part(hat, PrimitiveType.Sphere, "Dome", cap, new Vector3(0, 0.23f, 0), new Vector3(0.285f, 0.17f, 0.295f), mHat);
            Part(hat, PrimitiveType.Cube, "Visor", cap, new Vector3(0, 0.205f, 0.15f), new Vector3(0.20f, 0.018f, 0.14f), mHat);
            hm.hatCap = cap.gameObject;

            var fed = Pivot("Fedora", h, Vector3.zero);
            Part(hat, PrimitiveType.Cylinder, "Brim", fed, new Vector3(0, 0.215f, 0), new Vector3(0.46f, 0.008f, 0.46f), mHat);
            Part(hat, PrimitiveType.Cylinder, "Crown", fed, new Vector3(0, 0.285f, 0), new Vector3(0.25f, 0.065f, 0.25f), mHat);
            Part(null, PrimitiveType.Cylinder, "Band", fed, new Vector3(0, 0.238f, 0), new Vector3(0.256f, 0.014f, 0.256f), mDark);
            hm.hatFedora = fed.gameObject;

            var bea = Pivot("Beanie", h, Vector3.zero);
            Part(hat, PrimitiveType.Sphere, "Dome", bea, new Vector3(0, 0.22f, 0), new Vector3(0.29f, 0.25f, 0.30f), mHat);
            Part(null, PrimitiveType.Sphere, "Pom", bea, new Vector3(0, 0.355f, 0), Vector3.one * 0.08f, mWhite);
            hm.hatBeanie = bea.gameObject;

            var gl = Pivot("Glasses", h, Vector3.zero);
            foreach (float sx in new[] { -0.07f, 0.07f })
            {
                Part(null, PrimitiveType.Cylinder, "Lens", gl, new Vector3(sx, 0.17f, 0.14f), new Vector3(0.095f, 0.006f, 0.095f), mDark, new Vector3(90f, 0, 0));
                Part(null, PrimitiveType.Cube, "Temple", gl, new Vector3(sx * 1.8f, 0.172f, 0.07f), new Vector3(0.01f, 0.012f, 0.14f), mDark);
            }
            Part(null, PrimitiveType.Cube, "Bridge", gl, new Vector3(0, 0.175f, 0.142f), new Vector3(0.05f, 0.012f, 0.012f), mDark);
            hm.glasses = gl.gameObject;

            // чемодан в левой руке (качается вместе с рукой)
            var sc = Pivot("Suitcase", hm.armL, new Vector3(-0.04f, -0.62f, 0f));
            Part(cs, PrimitiveType.Cube, "Case", sc, new Vector3(0, -0.17f, 0), new Vector3(0.12f, 0.30f, 0.40f), mCase);
            foreach (float sz in new[] { -0.11f, 0.11f })
                Part(null, PrimitiveType.Cube, "Strap", sc, new Vector3(0, -0.17f, sz), new Vector3(0.126f, 0.31f, 0.025f), mDark);
            Part(null, PrimitiveType.Cube, "Handle", sc, new Vector3(0, -0.01f, 0), new Vector3(0.025f, 0.05f, 0.14f), mMetal);
            hm.suitcase = sc.gameObject;

            // бейдж на груди (у игроков)
            var bd = Pivot("Badge", t, new Vector3(0.11f, 1.27f, 0.125f));
            Part(null, PrimitiveType.Cube, "Card", bd, Vector3.zero, new Vector3(0.10f, 0.065f, 0.012f), mWhite);
            Part(null, PrimitiveType.Cube, "Stripe", bd, new Vector3(0, 0.02f, 0.0065f), new Vector3(0.10f, 0.022f, 0.004f), Mat("BadgeStripe", Palette.Teal));
            Part(null, PrimitiveType.Cube, "Line", bd, new Vector3(0, -0.012f, 0.0065f), new Vector3(0.07f, 0.008f, 0.004f), mDark);
            hm.badge = bd.gameObject;

            foreach (var o in new[] { hm.hatCap, hm.hatFedora, hm.hatBeanie, hm.glasses, hm.suitcase, hm.hairLong, hm.badge }) o.SetActive(false);

            hm.shirtR = shirt.ToArray(); hm.pantsR = pants.ToArray(); hm.skinR = skin.ToArray();
            hm.hairR = hair.ToArray(); hm.hatR = hat.ToArray(); hm.caseR = cs.ToArray();
            return hm;
        }

        /// <summary>Рука от первого лица под HandView (виден только владельцу, когда в руке предмет). Кисть — в начале координат (там хват).</summary>
        static GameObject BuildFpArm(Transform handView, out Renderer[] sleeve, out Renderer[] skin)
        {
            var fp = new GameObject("FpArm").transform;
            fp.SetParent(handView, false);
            var sl = new List<Renderer>(); var sk = new List<Renderer>();
            var mSkin = Mat("ChSkin", new Color(0.93f, 0.74f, 0.58f));
            var mShirt = Mat("ChShirt", new Color(0.8f, 0.2f, 0.18f));
            var d = new Vector3(0.12f, -0.38f, -0.85f).normalized;
            var rot = Quaternion.LookRotation(d).eulerAngles;
            Part(sk, PrimitiveType.Sphere, "Palm", fp, new Vector3(0, -0.01f, 0), new Vector3(0.11f, 0.10f, 0.13f), mSkin);
            Part(sk, PrimitiveType.Cube, "Forearm", fp, d * 0.26f, new Vector3(0.085f, 0.085f, 0.46f), mSkin, rot);
            Part(sl, PrimitiveType.Cube, "Sleeve", fp, d * 0.74f, new Vector3(0.115f, 0.115f, 0.56f), mShirt, rot);
            sleeve = sl.ToArray(); skin = sk.ToArray();
            fp.gameObject.SetActive(false);
            return fp.gameObject;
        }
    }
}
