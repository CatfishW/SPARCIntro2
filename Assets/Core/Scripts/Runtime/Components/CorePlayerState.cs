using System;
using UnityEngine;
using Unity.Netcode;
using Unity.Collections;
using Blocks.Gameplay.Core.Customization;

namespace Blocks.Gameplay.Core
{
    /// <summary>
    /// Defines the lifecycle states a player can be in.
    /// </summary>
    public enum PlayerLifeState : byte
    {
        /// <summary>
        /// The state when the player object first spawns into the scene.
        /// </summary>
        InitialSpawn,

        /// <summary>
        /// The state when the player has been defeated/killed.
        /// </summary>
        Eliminated,

        /// <summary>
        /// The state when the player returns to life after being eliminated.
        /// </summary>
        Respawned
    }

    /// <summary>
    /// Manages networked state for the player, such as their name and lifecycle state.
    /// This component separates state data from the management logic in CorePlayerManager.
    /// Network variables use Owner write permissions, allowing the owning client to set values.
    /// Non-owners must use RPCs to request changes.
    /// </summary>
    public class CorePlayerState : NetworkBehaviour
    {
        #region Fields & Properties

        [Tooltip("Global ScriptableObject event raised when player state changes.")]
        [SerializeField] private PlayerStateEvent onPlayerStateChangedGlobal;

        // Networked variable for the player name
        // Everyone can read, only the owner can write directly
        private readonly NetworkVariable<FixedString64Bytes> m_NetworkedPlayerName = new NetworkVariable<FixedString64Bytes>(
            new FixedString64Bytes(),
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        // Networked variable for the Life State
        // Everyone can read, only the owner can write directly
        private readonly NetworkVariable<PlayerLifeState> m_LifeState = new NetworkVariable<PlayerLifeState>(
            PlayerLifeState.InitialSpawn,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<FixedString64Bytes> m_CharacterPresetId = new NetworkVariable<FixedString64Bytes>(
            new FixedString64Bytes(),
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

        private string m_OfflinePlayerName = "Player";
        private PlayerLifeState m_OfflineLifeState = PlayerLifeState.InitialSpawn;
        private string m_OfflineCharacterPresetId = string.Empty;
        private bool m_OfflineInitialized;

        private bool HasLocalAuthority => IsOwner || OfflineLocalAuthority.IsActive(this);
        private bool UseOfflineState => OfflineLocalAuthority.IsActive(this) && !IsSpawned;

        /// <summary>
        /// Gets the current player name string.
        /// </summary>
        public string PlayerName => UseOfflineState ? m_OfflinePlayerName : m_NetworkedPlayerName.Value.ToString();

        /// <summary>
        /// Gets the current Life State.
        /// </summary>
        public PlayerLifeState LifeState => UseOfflineState ? m_OfflineLifeState : m_LifeState.Value;

        /// <summary>
        /// Helper to check if the player is currently considered "Active" (Not eliminated).
        /// </summary>
        public bool IsActive => LifeState == PlayerLifeState.InitialSpawn || LifeState == PlayerLifeState.Respawned;

        /// <summary>
        /// Gets the replicated character preset identifier selected for this player.
        /// </summary>
        public string CharacterPresetId => UseOfflineState ? m_OfflineCharacterPresetId : m_CharacterPresetId.Value.ToString();

        #endregion

        #region Events

        /// <summary>
        /// Event raised when the networked player name changes.
        /// </summary>
        public event Action<string> OnNameChanged;

        /// <summary>
        /// Event raised when the player's life state changes (InitialSpawn -> Eliminated -> Respawned).
        /// </summary>
        public event Action<PlayerLifeState> OnLifeStateChanged;

        /// <summary>
        /// Event raised when the replicated character preset selection changes.
        /// </summary>
        public event Action<string> OnCharacterPresetChanged;

        #endregion

        #region Unity Methods

        private void Awake()
        {
            CharacterCustomizationBootstrap.EnsureAttached(gameObject);
        }

        private void Start()
        {
            TryInitializeOfflineState();
        }

        public override void OnNetworkSpawn()
        {
            // Subscribe to value changes to trigger the local event
            m_NetworkedPlayerName.OnValueChanged += HandleNameChanged;
            m_LifeState.OnValueChanged += HandleLifeStateChanged;
            m_CharacterPresetId.OnValueChanged += HandleCharacterPresetChanged;

            // For late-joining clients, network variables may already have values set
            // Trigger events immediately to ensure subscribers receive the current state
            if (!m_NetworkedPlayerName.Value.IsEmpty)
            {
                OnNameChanged?.Invoke(m_NetworkedPlayerName.Value.ToString());
            }

            // Always broadcast initial life state to ensure all systems are synchronized
            OnLifeStateChanged?.Invoke(m_LifeState.Value);

            if (!m_CharacterPresetId.Value.IsEmpty)
            {
                OnCharacterPresetChanged?.Invoke(m_CharacterPresetId.Value.ToString());
            }
        }

        public override void OnNetworkDespawn()
        {
            m_NetworkedPlayerName.OnValueChanged -= HandleNameChanged;
            m_LifeState.OnValueChanged -= HandleLifeStateChanged;
            m_CharacterPresetId.OnValueChanged -= HandleCharacterPresetChanged;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Sets the player name.
        /// If called by the Owner, it sets the value directly.
        /// If called by non-Owner (e.g., Server), it sends an RPC to the Owner to set it.
        /// </summary>
        /// <param name="newName">The new name to set for the player.</param>
        public void SetPlayerName(string newName)
        {
            if (string.IsNullOrEmpty(newName)) return;

            if (UseOfflineState)
            {
                m_OfflinePlayerName = newName;
                OnNameChanged?.Invoke(m_OfflinePlayerName);
            }
            else if (IsOwner)
            {
                m_NetworkedPlayerName.Value = new FixedString64Bytes(newName);
            }
            else
            {
                // Server or other clients must request the owner to set the value via RPC
                SetPlayerNameRpc(newName);
            }
        }

        /// <summary>
        /// Sets the player's life state.
        /// If called by the Owner, it sets the value directly.
        /// If called by non-Owner (e.g., Server), it sends an RPC to the Owner to set it.
        /// </summary>
        /// <param name="newState">The new state to transition to.</param>
        public void SetLifeState(PlayerLifeState newState)
        {
            if (UseOfflineState)
            {
                PlayerLifeState previousState = m_OfflineLifeState;
                m_OfflineLifeState = newState;
                HandleLifeStateChanged(previousState, newState);
            }
            else if (IsOwner)
            {
                m_LifeState.Value = newState;
            }
            else
            {
                // Server or other clients must request the owner to set the value via RPC
                SetLifeStateRpc(newState);
            }
        }

        /// <summary>
        /// Sets the replicated character preset identifier for this player.
        /// </summary>
        /// <param name="presetId">Stable preset identifier from the character catalog.</param>
        public void SetCharacterPresetId(string presetId)
        {
            FixedString64Bytes value = string.IsNullOrWhiteSpace(presetId)
                ? new FixedString64Bytes()
                : new FixedString64Bytes(presetId);

            if (UseOfflineState)
            {
                m_OfflineCharacterPresetId = value.ToString();
                OnCharacterPresetChanged?.Invoke(m_OfflineCharacterPresetId);
            }
            else if (IsOwner)
            {
                m_CharacterPresetId.Value = value;
            }
            else
            {
                SetCharacterPresetIdRpc(value.ToString());
            }
        }

        #endregion

        #region RPCs

        /// <summary>
        /// RPC sent to the owner to set the player name.
        /// Required because network variables with Owner write permission can only be set by the owner.
        /// </summary>
        /// <param name="newName">The new name to set for the player.</param>
        [Rpc(SendTo.Owner)]
        private void SetPlayerNameRpc(string newName)
        {
            m_NetworkedPlayerName.Value = new FixedString64Bytes(newName);
        }

        /// <summary>
        /// RPC sent to the owner to set the life state.
        /// Required because network variables with Owner write permission can only be set by the owner.
        /// </summary>
        /// <param name="newState">The new state to transition to.</param>
        [Rpc(SendTo.Owner)]
        private void SetLifeStateRpc(PlayerLifeState newState)
        {
            m_LifeState.Value = newState;
        }

        [Rpc(SendTo.Owner)]
        private void SetCharacterPresetIdRpc(string presetId)
        {
            m_CharacterPresetId.Value = string.IsNullOrWhiteSpace(presetId)
                ? new FixedString64Bytes()
                : new FixedString64Bytes(presetId);
        }

        #endregion

        #region Private Methods

        private void HandleNameChanged(FixedString64Bytes oldName, FixedString64Bytes newName)
        {
            OnNameChanged?.Invoke(newName.ToString());
        }

        private void HandleLifeStateChanged(PlayerLifeState oldState, PlayerLifeState newState)
        {
            // Trigger local C# event for components on this GameObject (e.g., abilities, visual effects)
            OnLifeStateChanged?.Invoke(newState);

            // Trigger global ScriptableObject event for systems elsewhere in the scene
            // This allows managers, UI, and other decoupled systems to respond to state changes
            if (onPlayerStateChangedGlobal != null)
            {
                onPlayerStateChangedGlobal.Raise(new PlayerStatePayload { playerId = OwnerClientId, newState = newState, oldState = oldState });
            }
        }

        private void HandleCharacterPresetChanged(FixedString64Bytes oldPresetId, FixedString64Bytes newPresetId)
        {
            OnCharacterPresetChanged?.Invoke(newPresetId.ToString());
        }

        private void OnDisable()
        {
            if (!IsSpawned)
            {
                m_OfflineInitialized = false;
            }
        }

        private void TryInitializeOfflineState()
        {
            if (m_OfflineInitialized || !UseOfflineState)
            {
                return;
            }

            m_OfflineInitialized = true;

            if (!string.IsNullOrWhiteSpace(m_OfflinePlayerName))
            {
                OnNameChanged?.Invoke(m_OfflinePlayerName);
            }

            OnLifeStateChanged?.Invoke(m_OfflineLifeState);

            if (!string.IsNullOrWhiteSpace(m_OfflineCharacterPresetId))
            {
                OnCharacterPresetChanged?.Invoke(m_OfflineCharacterPresetId);
            }
        }

        #endregion
    }
}
