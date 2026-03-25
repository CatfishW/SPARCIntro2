using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

namespace Blocks.Gameplay.Core
{
    /// <summary>
    /// Handles core player input using Unity's Input System and broadcasts actions via GameEvents.
    /// This component handles core movement inputs (Move, Look, Jump, Sprint).
    /// </summary>
    public class CoreInputHandler : NetworkBehaviour
    {
        #region Fields

        [Header("Core Game Events")]
        [Tooltip("Raised when the player provides movement input.")]
        [SerializeField] private Vector2Event onMoveInput;
        [Tooltip("Raised when the player provides look/camera input.")]
        [SerializeField] private Vector2Event onLookInput;
        [Tooltip("Raised when the jump button is pressed.")]
        [SerializeField] private GameEvent onJumpPressed;
        [Tooltip("Raised when the jump button is released.")]
        [SerializeField] private GameEvent onJumpReleased;
        [Tooltip("Raised when the sprint state changes (pressed or released).")]
        [SerializeField] private BoolEvent onSprintStateChanged;
        [Tooltip("Raised when the primary action button is pressed.")]
        [SerializeField] private GameEvent onPrimaryActionPressed;
        [Tooltip("Raised when the primary action button is released.")]
        [SerializeField] private GameEvent onPrimaryActionReleased;
        [Tooltip("Raised when the menu button is pressed.")]
        [SerializeField] private GameEvent onMenuPressed;

        private GameplayInputSystem_Actions m_InputActions;
        private bool m_RuntimeInputInitialized;

        private bool HasLocalAuthority => IsOwner || OfflineLocalAuthority.IsActive(this);

        #endregion

        #region Unity Lifecycle & Network Callbacks

        private void Awake()
        {
            m_InputActions = new GameplayInputSystem_Actions();
        }

        private void Start()
        {
            TryInitializeOfflineRuntime();
        }

        private void OnEnable()
        {
            TryInitializeOfflineRuntime();
        }

        public override void OnNetworkSpawn()
        {
            InitializeRuntimeInput();
        }

        public override void OnNetworkDespawn()
        {
            ShutdownRuntimeInput();
        }

        public override void OnDestroy()
        {
            if (m_InputActions == null)
            {
                base.OnDestroy();
                return;
            }

            ShutdownRuntimeInput();
            m_InputActions.Dispose();
            m_InputActions = null;
            base.OnDestroy();
        }

        #endregion

        #region Input Registration

        private void RegisterInputActions()
        {
            m_InputActions.Player.Move.performed += HandleMove;
            m_InputActions.Player.Move.canceled += HandleMove;

            m_InputActions.Player.Look.performed += HandleLook;
            m_InputActions.Player.Look.canceled += HandleLook;

            m_InputActions.Player.Jump.performed += HandleJumpPressed;
            m_InputActions.Player.Jump.canceled += HandleJumpReleased;

            m_InputActions.Player.Sprint.started += HandleSprintState;
            m_InputActions.Player.Sprint.canceled += HandleSprintState;

            m_InputActions.Player.PrimaryAction.started += HandlePrimaryActionPressed;
            m_InputActions.Player.PrimaryAction.canceled += HandlePrimaryActionReleased;

            m_InputActions.Player.Menu.performed += HandleMenuPressed;
        }

        private void UnregisterInputActions()
        {
            m_InputActions.Player.Move.performed -= HandleMove;
            m_InputActions.Player.Move.canceled -= HandleMove;

            m_InputActions.Player.Look.performed -= HandleLook;
            m_InputActions.Player.Look.canceled -= HandleLook;

            m_InputActions.Player.Jump.performed -= HandleJumpPressed;
            m_InputActions.Player.Jump.canceled -= HandleJumpReleased;

            m_InputActions.Player.Sprint.started -= HandleSprintState;
            m_InputActions.Player.Sprint.canceled -= HandleSprintState;

            m_InputActions.Player.PrimaryAction.started -= HandlePrimaryActionPressed;
            m_InputActions.Player.PrimaryAction.canceled -= HandlePrimaryActionReleased;

            m_InputActions.Player.Menu.performed -= HandleMenuPressed;
        }

        #endregion

        #region Input Handlers

        private void HandleMove(InputAction.CallbackContext context) => onMoveInput?.Raise(context.ReadValue<Vector2>());
        private void HandleLook(InputAction.CallbackContext context) => onLookInput?.Raise(context.ReadValue<Vector2>());
        private void HandleJumpPressed(InputAction.CallbackContext context) => onJumpPressed?.Raise();
        private void HandleJumpReleased(InputAction.CallbackContext context) => onJumpReleased?.Raise();
        private void HandleSprintState(InputAction.CallbackContext context) => onSprintStateChanged?.Raise(context.ReadValueAsButton());
        private void HandlePrimaryActionPressed(InputAction.CallbackContext context) => onPrimaryActionPressed?.Raise();
        private void HandlePrimaryActionReleased(InputAction.CallbackContext context) => onPrimaryActionReleased?.Raise();
        private void HandleMenuPressed(InputAction.CallbackContext context) => onMenuPressed?.Raise();

        #endregion

        #region Runtime Input Injection (Touch / UI)

        public void InjectMoveInput(Vector2 input)
        {
            if (!HasLocalAuthority)
            {
                return;
            }

            onMoveInput?.Raise(input);
        }

        public void InjectLookInput(Vector2 input)
        {
            if (!HasLocalAuthority)
            {
                return;
            }

            onLookInput?.Raise(input);
        }

        public void InjectJumpPressed()
        {
            if (!HasLocalAuthority)
            {
                return;
            }

            onJumpPressed?.Raise();
        }

        public void InjectJumpReleased()
        {
            if (!HasLocalAuthority)
            {
                return;
            }

            onJumpReleased?.Raise();
        }

        public void InjectSprintState(bool sprinting)
        {
            if (!HasLocalAuthority)
            {
                return;
            }

            onSprintStateChanged?.Raise(sprinting);
        }

        public void InjectPrimaryActionPressed()
        {
            if (!HasLocalAuthority)
            {
                return;
            }

            onPrimaryActionPressed?.Raise();
        }

        public void InjectPrimaryActionReleased()
        {
            if (!HasLocalAuthority)
            {
                return;
            }

            onPrimaryActionReleased?.Raise();
        }

        public void InjectMenuPressed()
        {
            if (!HasLocalAuthority)
            {
                return;
            }

            onMenuPressed?.Raise();
        }

        #endregion

        private void OnDisable()
        {
            if (!IsSpawned)
            {
                ShutdownRuntimeInput();
            }
        }

        private void TryInitializeOfflineRuntime()
        {
            if (!IsSpawned && OfflineLocalAuthority.IsActive(this))
            {
                InitializeRuntimeInput();
            }
        }

        private void InitializeRuntimeInput()
        {
            if (m_RuntimeInputInitialized || !HasLocalAuthority || m_InputActions == null)
            {
                return;
            }

            RegisterInputActions();
            m_InputActions.Player.Enable();
            m_RuntimeInputInitialized = true;
        }

        private void ShutdownRuntimeInput()
        {
            if (!m_RuntimeInputInitialized || m_InputActions == null)
            {
                return;
            }

            m_InputActions.Player.Disable();
            UnregisterInputActions();
            m_RuntimeInputInitialized = false;
        }
    }
}
