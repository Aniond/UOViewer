using UnityEngine;
using UOHD2D.Network;

namespace UOHD2D.Game
{
    // Local player. Movement is client-predicted (dead reckoning) the same way the
    // classic client behaves: we move immediately on input and only snap back on
    // MovementRej. The server is the sole authority on whether a step is legal.
    public class PlayerAvatar : MonoBehaviour
    {
        // UO Direction byte values (Server/Mobile.cs Direction enum).
        private const byte DirNorth = 0x0, DirRight = 0x1, DirEast = 0x2, DirDown = 0x3;
        private const byte DirSouth = 0x4, DirLeft = 0x5, DirWest = 0x6, DirUp = 0x7;
        private const byte Running = 0x80;

        // Per-direction UO tile delta (dx, dy). UO Y increases southward.
        private static readonly int[,] DirDelta =
        {
            { 0, -1 }, // North
            { 1, -1 }, // Right (NE)
            { 1, 0 },  // East
            { 1, 1 },  // Down (SE)
            { 0, 1 },  // South
            { -1, 1 }, // Left (SW)
            { -1, 0 }, // West
            { -1, -1 }, // Up (NW)
        };

        public LoginFlow Flow;
        public float StepSeconds = 0.4f;

        private int _uoX, _uoY, _uoZ;
        private byte _direction;
        private byte _sequence = 1;
        private float _stepTimer;
        private bool _moving;

        public void Spawn(int uoX, int uoY, int uoZ, byte direction)
        {
            _uoX = uoX;
            _uoY = uoY;
            _uoZ = uoZ;
            _direction = direction;

            var origin = RegionOrigin.Active;
            if (origin != null)
                transform.position = origin.ToWorld(_uoX, _uoY, _uoZ);
        }

        private void Update()
        {
            if (Flow == null)
                return;

            if (_stepTimer > 0f)
            {
                _stepTimer -= Time.deltaTime;
                return;
            }

            var dx = 0;
            var dy = 0;

            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) dy -= 1;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) dy += 1;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) dx -= 1;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) dx += 1;

            if (dx == 0 && dy == 0)
                return;

            var dir = DeltaToDirection(dx, dy);
            TryStep(dir);
        }

        private static byte DeltaToDirection(int dx, int dy)
        {
            if (dx == 0 && dy < 0) return DirNorth;
            if (dx > 0 && dy < 0) return DirRight;
            if (dx > 0 && dy == 0) return DirEast;
            if (dx > 0 && dy > 0) return DirDown;
            if (dx == 0 && dy > 0) return DirSouth;
            if (dx < 0 && dy > 0) return DirLeft;
            if (dx < 0 && dy == 0) return DirWest;
            return DirUp;
        }

        private void TryStep(byte dir)
        {
            // Turning in place first, like the classic client, rather than always stepping.
            if (dir != _direction)
            {
                _direction = dir;
            }

            var delta0 = DirDelta[dir, 0];
            var delta1 = DirDelta[dir, 1];

            _uoX += delta0;
            _uoY += delta1;

            var origin = RegionOrigin.Active;
            if (origin != null)
                transform.position = origin.ToWorld(_uoX, _uoY, _uoZ);

            Flow.SendMovement((byte)(dir | Running), _sequence);
            _sequence++;
            if (_sequence == 0) _sequence = 1; // ServUO wraps 0 -> resets client seq tracking

            _stepTimer = StepSeconds;
        }

        // Server disagreed with our predicted position; snap back to its authoritative state.
        public void OnMovementRejected(int x, int y, int z, byte dir)
        {
            _uoX = x;
            _uoY = y;
            _uoZ = z;
            _direction = dir;
            _sequence = 1;

            var origin = RegionOrigin.Active;
            if (origin != null)
                transform.position = origin.ToWorld(_uoX, _uoY, _uoZ);
        }
    }
}
