using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Friendslop
{
    /// <summary>
    /// Игрок: FPS-контроллер владельца (owner-authority NetworkTransform), ник над головой.
    /// У чужих копий контроллер выключен — позицию присылает NetworkTransform.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : NetworkBehaviour
    {
        public static readonly Color[] Palette =
        {
            new Color(0.90f, 0.25f, 0.25f), new Color(0.25f, 0.55f, 0.95f), new Color(0.30f, 0.80f, 0.35f), new Color(0.95f, 0.80f, 0.20f),
            new Color(0.70f, 0.35f, 0.90f), new Color(0.95f, 0.55f, 0.20f), new Color(0.25f, 0.85f, 0.85f), new Color(0.95f, 0.50f, 0.75f),
        };

        public static PlayerController Local;
        public static Camera LocalCamera;
        public static string LocalNick = "Игрок";
        public static readonly List<PlayerController> All = new List<PlayerController>();

        public GameConfig config;
        public Transform camPivot;
        public Camera cam;
        public AudioListener listener;
        public Renderer[] bodyRenderers;   // скрываются у владельца (вид от первого лица)
        public Humanoid humanoid;          // модель: униформа по clientId (P2)
        public Collider bodyCollider;      // у владельца выключен (там CharacterController)

        public NetworkVariable<FixedString64Bytes> Nick = new NetworkVariable<FixedString64Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public bool MenuOpen { get; private set; }
        public bool Placed { get; private set; }

        CharacterController cc;
        NetworkTransform nt;
        PlayerCombat combat;
        PlayerHands hands;
        float yaw, pitch, vy, logTimer, downBlend;
        static GUIStyle nameStyle;
        static readonly bool NetLog = NetBootstrap.Arg("-netlog");

        public static string CleanNick(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) s = "Игрок";
            return s.Length > 14 ? s.Substring(0, 14) : s;
        }

        public override void OnNetworkSpawn()
        {
            cc = GetComponent<CharacterController>();
            nt = GetComponent<NetworkTransform>();
            combat = GetComponent<PlayerCombat>();
            hands = GetComponent<PlayerHands>();
            All.Add(this);
            if (GetComponent<Footsteps>() == null) gameObject.AddComponent<Footsteps>();
            cc.enabled = false;
            if (humanoid != null)
            {
                humanoid.ApplyPlayer((int)OwnerClientId, Palette[(int)(OwnerClientId % (ulong)Palette.Length)]);
                var hh = GetComponent<PlayerHands>();
                if (hh != null) hh.PaintFpArm(humanoid.ShirtColor, humanoid.SkinColor);
            }
            cam.enabled = false;
            listener.enabled = false;

            if (IsOwner)
            {
                Local = this;
                var fs = new FixedString64Bytes();
                fs.CopyFromTruncated(CleanNick(LocalNick));
                Nick.Value = fs;
                foreach (var r in bodyRenderers) r.enabled = false;
                if (bodyCollider != null) bodyCollider.enabled = false;
            }
            Debug.Log($"[NET] player spawned: owner={OwnerClientId} local={IsOwner} total={All.Count}");
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (Local == this)
            {
                Local = null;
                LocalCamera = null;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (NetLog && IsOwner)
            {
                logTimer += Time.deltaTime;
                if (logTimer > 3f) { logTimer = 0f; Debug.Log($"[NET] me={OwnerClientId} placed={Placed} sees={All.Count} pos={transform.position} scene={SceneManager.GetActiveScene().name}"); }
            }
            if (!IsOwner) return;
            if (!Placed) { TryPlace(); return; }

            var kb = Keyboard.current;
            var ms = Mouse.current;
            bool down = combat != null && combat.IsDown.Value;       // C5: без сознания — ввод заблокирован
            bool input = Application.isFocused && kb != null && ms != null && !down;

            if (Application.isFocused && kb != null && kb.escapeKey.wasPressedThisFrame) SetMenu(!MenuOpen);
            if (!MenuOpen && Application.isFocused && Cursor.lockState != CursorLockMode.Locked)
            { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }

            Vector3 wish = Vector3.zero;
            float speed = config.moveSpeed;
            if (input && !MenuOpen)
            {
                var d = ms.delta.ReadValue() * config.mouseSensitivity;
                yaw += d.x;
                pitch = Mathf.Clamp(pitch - d.y, -85f, 85f);
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);

                float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                wish = (transform.right * x + transform.forward * z);
                if (wish.sqrMagnitude > 1f) wish.Normalize();
                if (kb.leftShiftKey.isPressed) speed *= config.runMultiplier;
                if (hands != null) speed *= hands.SpeedMul;          // V2: кофе
            }

            if (cc.isGrounded && vy < 0f) vy = -1f;
            if (input && !MenuOpen && cc.isGrounded && kb.spaceKey.wasPressedThisFrame) vy = config.jumpForce;
            vy += config.gravity * Time.deltaTime;
            cc.Move((wish * speed + Vector3.up * vy) * Time.deltaTime);

            // камера: стоя — на уровне глаз, без сознания — у пола и набок
            downBlend = Mathf.MoveTowards(downBlend, down ? 1f : 0f, Time.deltaTime * 4f);
            camPivot.localPosition = new Vector3(0f, Mathf.Lerp(1.62f, 0.25f, downBlend), 0f);
            camPivot.localRotation = Quaternion.Slerp(Quaternion.Euler(pitch, 0f, 0f), Quaternion.Euler(-8f, 0f, 70f), downBlend);
        }

        void LateUpdate()
        {
            if (IsSpawned && IsOwner && Placed && cam != null) CameraShake.Apply(cam.transform, Time.deltaTime);   // тряска от ударов (P3)
        }

        /// <summary>Скриншоты (P2): поставить игрока в точку и повернуть (yaw/pitch в градусах).</summary>
        public void ShotPose(Vector3 pos, float yawDeg, float pitchDeg)
        {
            cc.enabled = false;
            yaw = yawDeg; pitch = pitchDeg; vy = 0f;
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            if (nt != null && nt.CanCommitToTransform) nt.Teleport(pos, Quaternion.Euler(0f, yaw, 0f), transform.localScale);
            cc.enabled = true;
            camPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void TryPlace()
        {
            if (SceneManager.GetActiveScene().name != NetBootstrap.MotelScene) return;
            var sp = SpawnPoint.For(OwnerClientId);
            if (sp == null) return;

            yaw = sp.transform.eulerAngles.y;
            pitch = 0f;
            vy = 0f;
            cc.enabled = false;
            transform.SetPositionAndRotation(sp.transform.position, Quaternion.Euler(0f, yaw, 0f));
            if (nt != null && nt.CanCommitToTransform) nt.Teleport(sp.transform.position, Quaternion.Euler(0f, yaw, 0f), transform.localScale);
            cc.enabled = true;

            FallbackCamera.Hide();
            cam.enabled = true;
            listener.enabled = true;
            LocalCamera = cam;
            Placed = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Debug.Log($"[NET] player placed: owner={OwnerClientId} at {sp.transform.position}");
        }

        public void SetMenu(bool open)
        {
            if (!open) ShopUi.Close();
            MenuOpen = open;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
        }

        void OnGUI()
        {
            // ник над головой чужих игроков
            if (!IsSpawned || IsOwner || LocalCamera == null || !LocalCamera.enabled) return;
            var sp = LocalCamera.WorldToScreenPoint(transform.position + Vector3.up * 2.15f);
            if (sp.z <= 0f) return;
            Ui.Reset();
            if (nameStyle == null) nameStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            nameStyle.fontSize = Mathf.RoundToInt(18f * Ui.Scale);
            var r = new Rect(sp.x - 150f, Screen.height - sp.y - 15f, 300f, 30f);
            Ui.Shadowed(r, Nick.Value.ToString(), nameStyle, Color.white);
        }
    }
}
