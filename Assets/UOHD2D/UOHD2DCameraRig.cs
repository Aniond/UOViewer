using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace UOHD2D
{
	/*
	 * HD-2D style camera: fixed yaw looking north (+Z), pitched down at the
	 * world, perspective projection. WASD/arrows pan, mouse wheel (or Q/E)
	 * zooms by moving along the view direction. Works with both the legacy
	 * Input Manager and the new Input System.
	 */
	[ExecuteAlways]
	public class UOHD2DCameraRig : MonoBehaviour
	{
		public float Pitch = 42f;
		public float FieldOfView = 32f;
		public float Distance = 26f;
		public float MinDistance = 6f;
		public float MaxDistance = 80f;
		public float PanSpeed = 14f;
		public float ZoomSpeed = 3f;

		private Camera _camera;

		private void OnEnable()
		{
			var t = transform.Find("UO Camera");

			if (t == null)
			{
				var go = new GameObject("UO Camera");
				go.transform.SetParent(transform, false);
				_camera = go.AddComponent<Camera>();
			}
			else
			{
				_camera = t.GetComponent<Camera>();
			}
		}

		private void LateUpdate()
		{
			if (_camera == null)
				return;

			if (Application.isPlaying)
			{
				var move = ReadMove();
				transform.position += new Vector3(move.x, 0f, move.y) * (PanSpeed * Time.deltaTime * (Distance / 26f));

				Distance = Mathf.Clamp(Distance - ReadZoom() * ZoomSpeed, MinDistance, MaxDistance);
			}

			var rad = Pitch * Mathf.Deg2Rad;

			_camera.fieldOfView = FieldOfView;
			_camera.transform.localPosition = new Vector3(0f, Mathf.Sin(rad) * Distance, -Mathf.Cos(rad) * Distance);
			_camera.transform.localRotation = Quaternion.Euler(Pitch, 0f, 0f);
		}

		private static Vector2 ReadMove()
		{
#if ENABLE_INPUT_SYSTEM
			var kb = Keyboard.current;

			if (kb == null)
				return Vector2.zero;

			var x = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
			var y = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);

			return new Vector2(x, y);
#else
			return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
		}

		private static float ReadZoom()
		{
#if ENABLE_INPUT_SYSTEM
			var zoom = 0f;
			var mouse = Mouse.current;
			var kb = Keyboard.current;

			if (mouse != null)
				zoom += Mathf.Clamp(mouse.scroll.ReadValue().y, -1f, 1f);

			if (kb != null)
				zoom += (kb.eKey.isPressed ? 0.05f : 0f) - (kb.qKey.isPressed ? 0.05f : 0f);

			return zoom;
#else
			return Input.GetAxis("Mouse ScrollWheel") * 10f
				+ (Input.GetKey(KeyCode.E) ? 0.05f : 0f)
				- (Input.GetKey(KeyCode.Q) ? 0.05f : 0f);
#endif
		}
	}
}
