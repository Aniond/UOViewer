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

        // Current facing (UO direction byte, running flag masked off) and whether the avatar is
        // travelling - read by the CharacterRig each frame to face and animate the visual.
        // IsMoving uses a small grace window past the step cooldown: between consecutive steps
        // the timer hits zero for a frame, and without the grace that one-frame false would
        // bounce the animator Walk state back through Idle, restarting the walk cycle every
        // step (legs looked frozen mid-glide).
        public byte CurrentDirection => _direction;
        public bool IsMoving => Time.time - _lastStepAt < StepSeconds + MoveGraceSeconds;

        private const float MoveGraceSeconds = 0.2f;
        private float _lastStepAt = float.NegativeInfinity;

        private int _uoX, _uoY, _uoZ;
        private byte _direction;
        // ServUO rejects unless the FIRST request is seq 0 (PacketHandlers.Movement:
        // "state.Sequence == 0 && seq != 0" -> MovementRej). 0 start, wrap 255 -> 1.
        private byte _sequence;
        private float _stepTimer;
        private bool _moving;

        // The logical tile position updates instantly (prediction), but the TRANSFORM glides
        // toward it at one tile per StepSeconds - teleporting the visual a full tile per step
        // made the walk animation read as a backstep after every jump.
        private Vector3 _glideTarget;
        private bool _hasGlideTarget;

        public void Spawn(int uoX, int uoY, int uoZ, byte direction)
        {
            _uoX = uoX;
            _uoY = uoY;
            _uoZ = uoZ;
            _direction = direction;

            var origin = RegionOrigin.Active;
            if (origin != null)
            {
                transform.position = origin.ToWorld(_uoX, _uoY, _uoZ);
                _glideTarget = transform.position;
                _hasGlideTarget = true;
            }
        }

        private void Update()
        {
            if (Flow == null)
                return;

            // Glide the visual toward the current tile at step speed (~1 tile / StepSeconds,
            // slightly faster so it lands before the next step is issued).
            if (_hasGlideTarget && transform.position != _glideTarget)
            {
                var speed = 1.15f / Mathf.Max(0.05f, StepSeconds);
                transform.position = Vector3.MoveTowards(transform.position, _glideTarget, speed * Time.deltaTime);
            }

            if (_stepTimer > 0f)
            {
                _stepTimer -= Time.deltaTime;
                return;
            }

            // The creation wizard is modal: no walking (keys or mouse) while dressing.
            if (CharacterCreationWizard.IsOpen)
                return;

            var dx = 0;
            var dy = 0;

            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) dy -= 1;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) dy += 1;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) dx -= 1;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) dx += 1;

            if (dx != 0 || dy != 0)
            {
                TryStep(DeltaToDirection(dx, dy));
                return;
            }

            // Classic-UO mouse walk: hold right button, step toward the cursor.
            // The follow rig keeps the player centered and the camera yaw is fixed
            // north, so screen-up is UO north and the octant maps straight onto
            // the Direction byte (N=0 .. NW=7 clockwise).
            if (Input.GetMouseButton(1))
            {
                var d = (Vector2)Input.mousePosition - new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

                if (d.sqrMagnitude >= 40f * 40f) // dead zone so clicks near the player don't jitter
                {
                    var angle = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                    var octant = (byte)((Mathf.RoundToInt(angle / 45f) % 8 + 8) % 8);
                    TryStep(octant);
                }
            }
        }

        // Tooling hook: lets editor automation exercise a server-validated step
        // without synthesizing keyboard input.
        public void DebugStep(byte dir)
        {
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
            {
                _glideTarget = origin.ToWorld(_uoX, _uoY, _uoZ);
                _hasGlideTarget = true;
            }

            Flow.SendMovement((byte)(dir | Running), _sequence);
            _sequence++;
            if (_sequence == 0) _sequence = 1; // ServUO wraps 0 -> resets client seq tracking

            _stepTimer = StepSeconds;
            _lastStepAt = Time.time;
        }

        // Server disagreed with our predicted position; snap back to its authoritative state.
        public void OnMovementRejected(int x, int y, int z, byte dir)
        {
            _uoX = x;
            _uoY = y;
            _uoZ = z;
            _direction = dir;
            _sequence = 0; // server reset its Sequence to 0 on reject; it now expects 0 again

            var origin = RegionOrigin.Active;
            if (origin != null)
            {
                transform.position = origin.ToWorld(_uoX, _uoY, _uoZ); // authoritative correction: hard snap
                _glideTarget = transform.position;
            }
        }
    }
}
