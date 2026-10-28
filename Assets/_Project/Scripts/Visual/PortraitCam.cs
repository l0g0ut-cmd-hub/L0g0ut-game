using UnityEngine;

namespace BladePath
{
    /// <summary>
    /// Живой портрет бойца для HUD: маленькая камера смотрит в лицо (кость head) и рисует в RenderTexture.
    /// Обновляется не каждый кадр (дёшево для телефона); при ударе — сразу, чтобы была видна реакция.
    /// </summary>
    public class PortraitCam : MonoBehaviour
    {
        public RenderTexture Texture { get; private set; }
        Camera cam;
        Transform head, root;
        int frame;

        public static PortraitCam Create(FighterController f, Color bg)
        {
            HeroRigDriver rig = f.GetComponentInChildren<HeroRigDriver>();
            if (rig == null || rig.head == null) return null;
            GameObject g = new GameObject("PortraitCam_" + f.name);
            PortraitCam p = g.AddComponent<PortraitCam>();
            p.head = rig.head; p.root = f.transform;
            p.Texture = new RenderTexture(160, 160, 16, RenderTextureFormat.ARGB32);
            p.Texture.antiAliasing = 2;
            p.cam = g.AddComponent<Camera>();
            p.cam.targetTexture = p.Texture;
            p.cam.clearFlags = CameraClearFlags.SolidColor;
            p.cam.backgroundColor = bg;
            p.cam.fieldOfView = 26f;
            p.cam.nearClipPlane = 0.05f;
            p.cam.farClipPlane = 4f;
            p.cam.enabled = false;           // рендерим вручную
            p.cam.allowMSAA = true;
            return p;
        }

        void LateUpdate()
        {
            if (head == null || root == null || cam == null) return;
            // после позы (HeroRigDriver тоже в LateUpdate): ставимся перед лицом, чуть сбоку и сверху
            Vector3 fw = root.forward, rt = root.right;
            Vector3 hp = head.position + Vector3.up * 0.05f;
            transform.position = hp + fw * 0.75f + rt * 0.22f + Vector3.up * 0.06f;
            transform.rotation = Quaternion.LookRotation(hp - transform.position, Vector3.up);
            frame++;
            if (frame % 3 == 0) cam.Render();
        }

        void OnDestroy() { if (Texture != null) Texture.Release(); }
    }
}
