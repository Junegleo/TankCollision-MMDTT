using UnityEngine;
using UnityEngine.InputSystem;

namespace TankCollision
{
    /// <summary>
    /// Камера: вращение мышью (ЛКМ/ПКМ + движение), колесо — приближение.
    /// Следит за целью без отставания, плавно сглаживаются только скачки (смена режима).
    /// </summary>
    public class OrbitCamera : MonoBehaviour
    {
        public float yaw = -135f;
        public float pitch = 22f;
        public float distance = 38f;
        public float minDistance = 4f;
        public float maxDistance = 800f;

        /// <summary>Возвращает true, если курсор над панелью интерфейса (тогда мышь камерой не управляет).</summary>
        public System.Func<bool> pointerOverUI;

        Vector3 focus;
        Vector3 lastDesired;
        bool hasFocus;
        bool dragging;

        public void Snap(Vector3 f)
        {
            focus = lastDesired = f;
            hasFocus = true;
        }

        /// <summary>Орбитальный режим вокруг точки desiredFocus.</summary>
        public void TickOrbit(Vector3 desiredFocus, bool allowInput)
        {
            float dt = Time.unscaledDeltaTime;
            if (!hasFocus) Snap(desiredFocus);
            if (allowInput) HandleInput();

            // движение цели повторяем точно, а накопившееся расхождение (скачки) сглаживаем
            focus += desiredFocus - lastDesired;
            lastDesired = desiredFocus;
            focus = Vector3.Lerp(focus, desiredFocus, 1f - Mathf.Exp(-dt * 5f));

            var rot = Quaternion.Euler(pitch, yaw, 0);
            transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
        }

        /// <summary>Режим «кино»: камера сама летит в позицию pos и смотрит на lookAt.</summary>
        public void TickCinema(Vector3 pos, Vector3 lookAt, float sharpness)
        {
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * sharpness);
            transform.position = Vector3.Lerp(transform.position, pos, k);
            var want = Quaternion.LookRotation(lookAt - transform.position, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, want, k);
            // чтобы при возврате в орбиту не было рывка
            hasFocus = false;
        }

        void HandleInput()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            bool overUI = pointerOverUI != null && pointerOverUI();

            if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
                dragging = !overUI;
            if (!mouse.leftButton.isPressed && !mouse.rightButton.isPressed)
                dragging = false;

            if (dragging)
            {
                Vector2 d = mouse.delta.ReadValue();
                yaw += d.x * 0.25f;
                pitch = Mathf.Clamp(pitch - d.y * 0.25f, 2f, 89.5f);
            }

            float s = mouse.scroll.ReadValue().y;
            if (!overUI && Mathf.Abs(s) > 0.01f)
            {
                // в разных версиях Input System колесо даёт ±1 или ±120 за щелчок
                float notches = Mathf.Abs(s) > 10f ? s / 120f : s;
                notches = Mathf.Clamp(notches, -3f, 3f);
                distance = Mathf.Clamp(distance * Mathf.Pow(0.88f, notches), minDistance, maxDistance);
            }
        }
    }
}
