using System.Collections.Generic;
using UnityEngine;
using UOHD2D.Network;

namespace UOHD2D.Game
{
    // Drop this on an empty GameObject in a scene that already has an imported
    // HD2D region (with a RegionOrigin) and a camera. Drives login via OnGUI,
    // then spawns the local player and any remote mobiles the server broadcasts.
    public class GameBootstrap : MonoBehaviour
    {
        public string Host = "127.0.0.1";
        public int Port = 2593;

        // Set these in the Inspector; IMGUI text fields in the Game view are
        // unreliable for keyboard focus, so the Inspector is the primary path.
        public string Account = "admin";
        public string Password = "";
        public bool AutoConnect = true;

        // The player's visual. Leave null to spawn a primitive stand-in; assign a
        // CharacterProfile (e.g. the rigged base_human) to spawn the real character.
        public CharacterProfile PlayerProfile;

        // Maps UO item ids the server reports as worn to our 3D EquippableItems.
        public GearCatalog Gear;

        // Appearance catalog for the character creation wizard (skin/hair/clothes choices).
        public CharacterAppearanceOptions AppearanceOptions;

        private string _status = "";

        private LoginFlow _flow;
        private PlayerAvatar _player;
        private CharacterRig _playerRig;
        private PaperdollWindow _paperdoll;
        private CharacterCreationWizard _wizard;
        private string _charName = "";
        private readonly Dictionary<uint, RemoteMobile> _remotes = new Dictionary<uint, RemoteMobile>();

        private enum UiPhase { Credentials, ServerList, CharacterList, InWorld }
        private UiPhase _phase = UiPhase.Credentials;

        private LoginStage _lastLoggedStage = (LoginStage)(-1);
        private float _nextDiagAt;

        private void Start()
        {
            if (AutoConnect && !string.IsNullOrEmpty(Password))
                StartLogin();
        }

        private void Update()
        {
            _flow?.Pump();

            // Keep the character visual facing + animating per the player's movement state.
            if (_playerRig != null && _player != null)
            {
                _playerRig.SetFacing(_player.CurrentDirection);
                _playerRig.SetMoving(_player.IsMoving);
            }

            if (_flow != null && _flow.Stage != _lastLoggedStage)
            {
                _lastLoggedStage = _flow.Stage;
                Debug.Log("[UOHD2D] Stage -> " + _flow.Stage + (_flow.Stage == LoginStage.Failed ? " (" + _flow.FailReason + ")" : ""));
            }

            // Connection-truth heartbeat while the handshake is in flight: BytesSent
            // is the only witness to whether a packet actually reached the socket,
            // and ConnError is where UOConnection parks swallowed exceptions.
            if (_flow != null && _flow.Stage != LoginStage.InWorld && _flow.Stage != LoginStage.Failed && Time.unscaledTime >= _nextDiagAt)
            {
                _nextDiagAt = Time.unscaledTime + 1f;
                Debug.Log("[UOHD2D] diag stage=" + _flow.Stage
                    + " sent=" + _flow.BytesSent + " recv=" + _flow.BytesReceived
                    + " connected=" + _flow.ConnConnected
                    + " err=" + (_flow.ConnError ?? "none"));
            }
        }

        private void OnDestroy()
        {
            _flow?.Dispose();
        }

        // Test-only hook so tooling can drive the login flow without clicking OnGUI.
        public void DebugConnect(string account, string password)
        {
            Account = account;
            Password = password;
            StartLogin();
        }

        private void StartLogin()
        {
            // A second Connect (or AutoConnect + click) must not orphan a live
            // handshake: the old flow's socket would keep the server session open
            // while its packets are never pumped again.
            _flow?.Dispose();
            _phase = UiPhase.Credentials;

            _flow = new LoginFlow(Host, Port, Account, Password);

            _flow.OnServerList += servers =>
            {
                _status = servers.Length + " server(s) found";
                _phase = UiPhase.ServerList;
                Debug.Log("[UOHD2D] Server list received: " + servers.Length);
                _flow.SelectServer(0); // auto-pick the first (only) server for the debug path
            };

            _flow.OnCharacterList += chars =>
            {
                _status = "Character list received";
                _phase = UiPhase.CharacterList;
                Debug.Log("[UOHD2D] Character list received: " + chars.Count + " slots");
                for (var i = 0; i < chars.Count; i++)
                    Debug.Log("[UOHD2D]   slot " + i + ": occupied=" + chars[i].Occupied + " name=" + chars[i].Name);

                // No auto-select: the list screen is also where new characters are created
                // (classic UO), so the player picks a slot or makes one.
            };

            _flow.OnWorldEntered += () =>
            {
                _status = "In world";
                _phase = UiPhase.InWorld;
                Debug.Log("[UOHD2D] World entered at (" + _flow.Player.X + "," + _flow.Player.Y + "," + _flow.Player.Z + ")");
                SpawnPlayer();
            };

            _flow.UnhandledOpcode += (op, len) => Debug.Log("[UOHD2D] unhandled opcode 0x" + op.ToString("X2") + " len " + len);
            _flow.OnMobileIncoming += HandleMobileIncoming;
            _flow.OnMobileMoving += HandleMobileMoving;
            _flow.OnMobileRemoved += HandleMobileRemoved;

            _flow.OnMovementRejected += (seq, x, y, z, dir) =>
            {
                Debug.Log("[UOHD2D] movement rejected seq=" + seq + " -> snap to (" + x + "," + y + "," + z + ")");
                if (_player != null)
                    _player.OnMovementRejected(x, y, z, dir);
            };

            _flow.Start();
            _status = "Connecting...";
            Debug.Log("[UOHD2D] Connecting to " + Host + ":" + Port + " as " + Account);
        }

        private void SpawnPlayer()
        {
            var go = new GameObject("Player");
            _player = go.AddComponent<PlayerAvatar>();
            _player.Flow = _flow;
            _player.Spawn(_flow.Player.X, _flow.Player.Y, _flow.Player.Z, _flow.Player.Direction);

            // The live character visual: the rigged model from PlayerProfile, or a stand-in.
            _playerRig = CharacterFactory.Create(PlayerProfile, LayerMask.NameToLayer("Character"));
            _playerRig.transform.SetParent(go.transform, false);
            _playerRig.SetFacing(_flow.Player.Direction);

            // The paperdoll window (P): live view of the dressed character + worn gear.
            _paperdoll = gameObject.GetComponent<PaperdollWindow>();
            if (_paperdoll == null)
                _paperdoll = gameObject.AddComponent<PaperdollWindow>();
            _paperdoll.Target = _playerRig;
            _paperdoll.Gear = Gear;
            _paperdoll.CharacterName = _charName;

            // Dress the character in the wizard's saved appearance (skin/hair/clothes).
            if (AppearanceOptions != null)
                CharacterAppearance.Load().Apply(_playerRig, AppearanceOptions);

            var rig = Object.FindAnyObjectByType<UOHD2DCameraRig>();
            if (rig != null)
            {
                rig.Follow = go.transform;
            }
            else
            {
                var cam = Camera.main;
                if (cam != null)
                    cam.transform.SetParent(go.transform, true);
            }
        }

        private void HandleMobileIncoming(MobileState state)
        {
            if (_flow.Player.Serial == state.Serial)
            {
                // Our own MobileIncoming carries what WE are wearing - apply it to the player rig
                // and mirror the list in the paperdoll.
                if (_playerRig != null && Gear != null)
                    _playerRig.ApplyEquipment(state.Equipment, Gear);
                if (_paperdoll != null)
                    _paperdoll.SetEquipment(state.Equipment);
                return;
            }

            if (!_remotes.TryGetValue(state.Serial, out var remote))
            {
                var go = RemoteMobile.CreatePlaceholder("Mobile_" + state.Serial);
                remote = go.AddComponent<RemoteMobile>();
                _remotes[state.Serial] = remote;
            }

            remote.Apply(state);
        }

        private void HandleMobileMoving(MobileState state)
        {
            if (_remotes.TryGetValue(state.Serial, out var remote))
                remote.Apply(state);
        }

        private void HandleMobileRemoved(uint serial)
        {
            if (_remotes.TryGetValue(serial, out var remote))
            {
                Destroy(remote.gameObject);
                _remotes.Remove(serial);
            }
        }

        private void OnGUI()
        {
            if (_wizard != null)
                return; // the wizard owns the screen until Done

            GUILayout.BeginArea(new Rect(16, 16, 320, 400), GUI.skin.box);

            GUILayout.Label("UO HD2D Client");
            GUILayout.Label(_status);

            switch (_phase)
            {
                case UiPhase.Credentials:
                    GUILayout.Label("Host");
                    Host = GUILayout.TextField(Host);
                    GUILayout.Label("Account");
                    Account = GUILayout.TextField(Account);
                    GUILayout.Label("Password");
                    Password = GUILayout.PasswordField(Password, '*');

                    if (GUILayout.Button("Connect"))
                        StartLogin();

                    GUILayout.Space(8);
                    if (GUILayout.Button("Customize Character"))
                        _wizard = CharacterCreationWizard.Open(gameObject, PlayerProfile, AppearanceOptions);
                    break;

                case UiPhase.ServerList:
                    if (_flow?.Servers == null)
                        break; // flow restarted; list not in yet (a null deref here + Error Pause froze whole sessions)

                    for (var i = 0; i < _flow.Servers.Length; i++)
                    {
                        var s = _flow.Servers[i];
                        if (GUILayout.Button(s.Name))
                            _flow.SelectServer(i);
                    }
                    break;

                case UiPhase.CharacterList:
                {
                    var freeSlot = -1;

                    for (var i = 0; i < _flow.Characters.Count; i++)
                    {
                        var c = _flow.Characters[i];
                        if (!c.Occupied)
                        {
                            if (freeSlot < 0)
                                freeSlot = i;
                            continue;
                        }

                        if (GUILayout.Button(c.Name))
                        {
                            _charName = c.Name;
                            _flow.SelectCharacter(i);
                        }
                    }

                    // Classic UO character creation: the wizard IS the creation screen (name,
                    // front-facing preview, appearance choices). Its callback sends the 0x00
                    // packet and the server spawns the character wearing REAL clothes items
                    // (shirt/pants/shoes in the chosen hues) plus a hair layer.
                    if (freeSlot >= 0)
                    {
                        GUILayout.Space(8);

                        if (GUILayout.Button("New Character"))
                        {
                            var slot = freeSlot;
                            _wizard = CharacterCreationWizard.Open(gameObject, PlayerProfile, AppearanceOptions);
                            _wizard.OnCreate = (app, name, profession) =>
                            {
                                app.ToUoCreation(AppearanceOptions,
                                    out var skinHue, out var hairId, out var hairHue, out var shirtHue, out var pantsHue);

                                _charName = name;
                                _flow.CreateCharacter(name, slot, false, profession,
                                    skinHue, hairId, hairHue, shirtHue, pantsHue);
                                _status = "Creating " + name + "...";
                            };
                        }
                    }
                    break;
                }

                case UiPhase.InWorld:
                    GUILayout.Label("WASD to move  |  P: paperdoll  |  F3: art style");
                    break;
            }

            GUILayout.EndArea();
        }
    }
}
