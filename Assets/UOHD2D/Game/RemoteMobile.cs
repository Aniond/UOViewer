using UnityEngine;
using UOHD2D.Network;

namespace UOHD2D.Game
{
    // Placeholder visual for another mobile (player or creature) driven entirely by
    // server broadcasts (MobileIncoming on entering view, MobileMoving on each step,
    // RemoveItem/RemoveMobile on leaving view). No client-side prediction: we just
    // lerp toward the last position the server told us about.
    public class RemoteMobile : MonoBehaviour
    {
        public uint Serial;

        private Vector3 _targetPos;
        private const float LerpSpeed = 8f;

        public void Apply(MobileState state)
        {
            Serial = state.Serial;

            var origin = RegionOrigin.Active;
            _targetPos = origin != null ? origin.ToWorld(state.X, state.Y, state.Z) : transform.position;

            if (transform.position == Vector3.zero)
                transform.position = _targetPos; // first sighting: snap instead of sliding in from origin
        }

        private void Update()
        {
            transform.position = Vector3.Lerp(transform.position, _targetPos, Time.deltaTime * LerpSpeed);
        }

        public static GameObject CreatePlaceholder(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            go.transform.localScale = new Vector3(0.4f, 0.5f, 0.4f);
            Object.Destroy(go.GetComponent<Collider>());
            return go;
        }
    }
}
