using System;
using DG.Tweening;
using UnityEngine;

namespace Q3Movement
{
    /// <summary>
    /// Mouse look with DOTween-driven Quake-style camera roll based on horizontal movement input.
    /// </summary>
    [Serializable]
    public class MouseLook
    {
        [Header("Mouse Look")]
        [SerializeField] private float m_XSensitivity = 2f;
        [SerializeField] private float m_YSensitivity = 2f;
        [SerializeField] private bool m_ClampVerticalRotation = true;
        [SerializeField] private float m_MinimumX = -90f;
        [SerializeField] private float m_MaximumX = 90f;
        [SerializeField] private bool m_Smooth = false;
        [SerializeField] private float m_SmoothTime = 5f;
        [SerializeField] private bool m_LockCursor = true;

        [Header("Quake-Style Strafe Tilt")]
        [Tooltip("Maximum camera roll while horizontal movement input is held. Right input rolls negative; left input rolls positive.")]
        [SerializeField, Range(0f, 15f)] private float m_StrafeTiltAngle = 5f;
        [Tooltip("Horizontal input smaller than this magnitude is ignored, preventing tiny analog-stick drift from tilting the camera.")]
        [SerializeField, Range(0f, 1f)] private float m_StrafeTiltInputThreshold = 0.1f;
        [Tooltip("How long the camera takes to tilt or return to neutral.")]
        [SerializeField, Min(0f)] private float m_StrafeTiltDuration = 0.14f;
        [SerializeField] private Ease m_StrafeTiltEase = Ease.OutQuad;

        private Quaternion m_CharacterTargetRot;
        private Quaternion m_CameraTargetRot;
        private bool m_cursorIsLocked = true;
        private Tween m_StrafeTiltTween;
        private float m_CurrentStrafeRoll;
        private float m_RequestedStrafeRoll;

        public void Init(Transform character, Transform camera)
        {
            m_CharacterTargetRot = character.localRotation;
            m_CameraTargetRot = camera.localRotation;
            if (m_StrafeTiltTween != null && m_StrafeTiltTween.IsActive())
                m_StrafeTiltTween.Kill();

            m_CurrentStrafeRoll = 0f;
            m_RequestedStrafeRoll = 0f;
        }

        public void LookRotation(Transform character, Transform camera)
        {
            float yRot = Input.GetAxis("Mouse X") * m_XSensitivity;
            float xRot = Input.GetAxis("Mouse Y") * m_YSensitivity;

            m_CharacterTargetRot *= Quaternion.Euler(0f, yRot, 0f);
            m_CameraTargetRot *= Quaternion.Euler(-xRot, 0f, 0f);

            if (m_ClampVerticalRotation)
                m_CameraTargetRot = ClampRotationAroundXAxis(m_CameraTargetRot);

            UpdateStrafeTilt(camera);

            // Apply roll after the pitch target so the tilt is around the camera's forward axis.
            Quaternion cameraRotation = m_CameraTargetRot * Quaternion.Euler(0f, 0f, m_CurrentStrafeRoll);

            if (m_Smooth)
            {
                float blend = Mathf.Clamp01(m_SmoothTime * Time.deltaTime);
                character.localRotation = Quaternion.Slerp(
                    character.localRotation, m_CharacterTargetRot, blend);
                camera.localRotation = Quaternion.Slerp(
                    camera.localRotation, cameraRotation, blend);
            }
            else
            {
                character.localRotation = m_CharacterTargetRot;
                camera.localRotation = cameraRotation;
            }

            UpdateCursorLock();
        }

        private void UpdateStrafeTilt(Transform camera)
        {
            // Match the same Horizontal input used by Q3PlayerController. Tilt follows the
            // player's intent rather than physical velocity, so retained momentum and air
            // control won't keep the camera rolled after the player releases the strafe input.
            float horizontalInput = Mathf.Clamp(Input.GetAxisRaw("Horizontal"), -1f, 1f);

            float targetRoll = Mathf.Abs(horizontalInput) < m_StrafeTiltInputThreshold
                ? 0f
                : -Mathf.Sign(horizontalInput) * m_StrafeTiltAngle;

            // Only retarget when the desired side changes; otherwise the tween would restart
            // every frame and never complete smoothly.
            if (Mathf.Approximately(targetRoll, m_RequestedStrafeRoll))
                return;

            m_RequestedStrafeRoll = targetRoll;

            if (m_StrafeTiltTween != null && m_StrafeTiltTween.IsActive())
                m_StrafeTiltTween.Kill(false);

            float duration = Mathf.Max(0f, m_StrafeTiltDuration);
            if (duration <= 0f)
            {
                m_CurrentStrafeRoll = targetRoll;
                return;
            }

            m_StrafeTiltTween = DOTween.To(
                    () => m_CurrentStrafeRoll,
                    value => m_CurrentStrafeRoll = value,
                    targetRoll,
                    duration)
                .SetEase(m_StrafeTiltEase)
                .SetTarget(camera);
        }

        public void SetCursorLock(bool value)
        {
            m_LockCursor = value;
            if (!m_LockCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        public void UpdateCursorLock()
        {
            if (m_LockCursor)
                InternalLockUpdate();
        }

        private void InternalLockUpdate()
        {
            if (Input.GetKeyUp(KeyCode.Escape))
                m_cursorIsLocked = false;
            else if (Input.GetMouseButtonUp(0))
                m_cursorIsLocked = true;

            if (m_cursorIsLocked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private Quaternion ClampRotationAroundXAxis(Quaternion q)
        {
            // Avoid division by zero for unusual or corrupted rotation values.
            if (Mathf.Abs(q.w) < 0.000001f)
                return Quaternion.Euler(Mathf.Clamp(q.eulerAngles.x, m_MinimumX, m_MaximumX), 0f, 0f);

            q.x /= q.w;
            q.y /= q.w;
            q.z /= q.w;
            q.w = 1f;

            float angleX = 2f * Mathf.Rad2Deg * Mathf.Atan(q.x);
            angleX = Mathf.Clamp(angleX, m_MinimumX, m_MaximumX);
            q.x = Mathf.Tan(0.5f * Mathf.Deg2Rad * angleX);
            return q;
        }

        public void ResetStrafeTilt(Transform camera)
        {
            // Stop any active tween so it cannot modify the roll afterward.
            if (m_StrafeTiltTween != null && m_StrafeTiltTween.IsActive())
                m_StrafeTiltTween.Kill(false);

            m_StrafeTiltTween = null;

            // Reset both the current and requested roll.
            m_CurrentStrafeRoll = 0f;
            m_RequestedStrafeRoll = 0f;

            // Restore the camera rotation without the strafe roll.
            if (camera != null)
                camera.localRotation = m_CameraTargetRot;
        }
    }

}
