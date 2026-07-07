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

        private string _account = "admin";
        private string _password = "";
        private string _status = "";

        private LoginFlow _flow;
        private PlayerAvatar _player;
        private readonly Dictionary<uint, RemoteMobile> _remotes = new Dictionary<uint, RemoteMobile>();

        private enum UiPhase { Credentials, ServerList, CharacterList, InWorld }
        private UiPhase _phase = UiPhase.Credentials;

        private LoginStage _lastLoggedStage = (LoginStage)(-1);

        private void Update()
        {
            _flow?.Pump();

            if (_flow != null && _flow.Stage != _lastLoggedStage)
            {
                _lastLoggedStage = _flow.Stage;
                Debug.Log("[UOHD2D] Stage -> " + _flow.Stage + (_flow.Stage == LoginStage.Failed ? " (" + _flow.FailReason + ")" : ""));
            }
        }

        private void OnDestroy()
        {
            _flow?.Dispose();
        }

        // Test-only hook so tooling can drive the login flow without clicking OnGUI.
        public void DebugConnect(string account, string password)
        {
            _account = account;
            _password = password;
            StartLogin();
        }

        private void StartLogin()
        {
            _flow = new LoginFlow(Host, Port, _account, _password);

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

                var firstOccupied = chars.FindIndex(c => c.Occupied);
                if (firstOccupied >= 0)
                    _flow.SelectCharacter(firstOccupied);
                else
                    Debug.LogWarning("[UOHD2D] No occupied character slots - account has no characters yet");
            };

            _flow.OnWorldEntered += () =>
            {
                _status = "In world";
                _phase = UiPhase.InWorld;
                Debug.Log("[UOHD2D] World entered at (" + _flow.Player.X + "," + _flow.Player.Y + "," + _flow.Player.Z + ")");
                SpawnPlayer();
            };

            _flow.OnMobileIncoming += HandleMobileIncoming;
            _flow.OnMobileMoving += HandleMobileMoving;
            _flow.OnMobileRemoved += HandleMobileRemoved;

            _flow.OnMovementRejected += (seq, x, y, z, dir) =>
            {
                if (_player != null)
                    _player.OnMovementRejected(x, y, z, dir);
            };

            _flow.Start();
            _status = "Connecting...";
            Debug.Log("[UOHD2D] Connecting to " + Host + ":" + Port + " as " + _account);
        }

        private void SpawnPlayer()
        {
            var go = new GameObject("Player");
            _player = go.AddComponent<PlayerAvatar>();
            _player.Flow = _flow;
            _player.Spawn(_flow.Player.X, _flow.Player.Y, _flow.Player.Z, _flow.Player.Direction);

            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.transform.SetParent(go.transform, false);
            visual.transform.localScale = new Vector3(0.4f, 0.5f, 0.4f);
            Object.Destroy(visual.GetComponent<Collider>());

            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.SetParent(go.transform, true);
            }
        }

        private void HandleMobileIncoming(MobileState state)
        {
            if (_flow.Player.Serial == state.Serial)
                return; // that's us; LoginConfirm + our own MobileIncoming both describe self

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
            GUILayout.BeginArea(new Rect(16, 16, 320, 400), GUI.skin.box);

            GUILayout.Label("UO HD2D Client");
            GUILayout.Label(_status);

            switch (_phase)
            {
                case UiPhase.Credentials:
                    GUILayout.Label("Host");
                    Host = GUILayout.TextField(Host);
                    GUILayout.Label("Account");
                    _account = GUILayout.TextField(_account);
                    GUILayout.Label("Password");
                    _password = GUILayout.PasswordField(_password, '*');

                    if (GUILayout.Button("Connect"))
                        StartLogin();
                    break;

                case UiPhase.ServerList:
                    for (var i = 0; i < _flow.Servers.Length; i++)
                    {
                        var s = _flow.Servers[i];
                        if (GUILayout.Button(s.Name))
                            _flow.SelectServer(i);
                    }
                    break;

                case UiPhase.CharacterList:
                    for (var i = 0; i < _flow.Characters.Count; i++)
                    {
                        var c = _flow.Characters[i];
                        if (!c.Occupied)
                            continue;

                        if (GUILayout.Button(c.Name))
                            _flow.SelectCharacter(i);
                    }
                    break;

                case UiPhase.InWorld:
                    GUILayout.Label("WASD to move");
                    break;
            }

            GUILayout.EndArea();
        }
    }
}
