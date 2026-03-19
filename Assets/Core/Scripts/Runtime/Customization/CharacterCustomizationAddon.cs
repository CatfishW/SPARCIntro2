using System;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;

namespace Blocks.Gameplay.Core.Customization
{
    public sealed class CharacterCustomizationAddon : NetworkBehaviour, IPlayerAddon
    {
        [Header("Catalog")]
        [SerializeField] private CharacterCustomizationCatalog catalogOverride;

        [Header("Startup")]
        [SerializeField] private bool applyPersistedPresetOnSpawn;
        [SerializeField] private bool applyCatalogDefaultPresetOnSpawn;

        private CorePlayerManager m_PlayerManager;
        private CorePlayerState m_PlayerState;
        private CoreAnimator m_CoreAnimator;
        private string m_AppliedPresetId = string.Empty;
        private bool m_Initialized;
        private bool m_PlayerSpawned;
        private bool m_StateSubscribed;

        public CharacterCustomizationCatalog ActiveCatalog => catalogOverride != null ? catalogOverride : CharacterCustomizationCatalogRegistry.GetActiveCatalog();

        private void Awake()
        {
            CacheDependencies();
        }

        private void OnEnable()
        {
            CharacterCustomizationCatalogRegistry.ActiveCatalogChanged += HandleCatalogChanged;
        }

        private void OnDisable()
        {
            CharacterCustomizationCatalogRegistry.ActiveCatalogChanged -= HandleCatalogChanged;
            UnsubscribeFromState();
        }

        private void Update()
        {
            if (!m_Initialized || !m_PlayerSpawned)
            {
                TryLateBind();
            }
        }

        public void Initialize(CorePlayerManager playerManager)
        {
            m_PlayerManager = playerManager;
            CacheDependencies();
            SubscribeToState();
            m_Initialized = true;
        }

        public void OnPlayerSpawn()
        {
            CacheDependencies();
            SubscribeToState();
            m_PlayerSpawned = true;
            ResolveInitialPreset();
        }

        public void OnPlayerDespawn()
        {
            m_PlayerSpawned = false;
            UnsubscribeFromState();
        }

        public void OnLifeStateChanged(PlayerLifeState previousState, PlayerLifeState newState)
        {
            if (newState == PlayerLifeState.Respawned || newState == PlayerLifeState.InitialSpawn)
            {
                ApplyPreset(m_PlayerState != null ? m_PlayerState.CharacterPresetId : string.Empty);
            }
        }

        public bool RequestPreset(string presetId)
        {
            CharacterCustomizationCatalog catalog = ActiveCatalog;
            if (catalog == null || m_PlayerState == null || !catalog.TryGetPreset(presetId, out _))
            {
                return false;
            }

            if (IsOwner)
            {
                ApplyPreset(presetId, true);
            }

            m_PlayerState.SetCharacterPresetId(presetId);
            return true;
        }

        public bool RequestRandomPreset()
        {
            CharacterCustomizationCatalog catalog = ActiveCatalog;
            if (catalog == null || catalog.Presets == null || catalog.Presets.Count == 0)
            {
                return false;
            }

            int startIndex = UnityEngine.Random.Range(0, catalog.Presets.Count);
            for (int offset = 0; offset < catalog.Presets.Count; offset++)
            {
                CharacterCustomizationPreset preset = catalog.Presets[(startIndex + offset) % catalog.Presets.Count];
                if (preset.characterPrefab == null || string.IsNullOrWhiteSpace(preset.id))
                {
                    continue;
                }

                return RequestPreset(preset.id);
            }

            return false;
        }

        private void TryLateBind()
        {
            if (m_PlayerManager == null)
            {
                m_PlayerManager = GetComponent<CorePlayerManager>();
            }

            if (!m_Initialized && m_PlayerManager != null)
            {
                Initialize(m_PlayerManager);
            }

            if (!m_PlayerSpawned && m_PlayerManager != null && m_PlayerManager.IsSpawned)
            {
                OnPlayerSpawn();
            }
        }

        private void CacheDependencies()
        {
            if (m_PlayerState == null)
            {
                m_PlayerState = GetComponent<CorePlayerState>();
            }

            if (m_CoreAnimator == null)
            {
                m_CoreAnimator = GetComponentInChildren<CoreAnimator>(true);
            }
        }

        private void SubscribeToState()
        {
            if (m_StateSubscribed || m_PlayerState == null)
            {
                return;
            }

            m_PlayerState.OnCharacterPresetChanged += HandleCharacterPresetChanged;
            m_StateSubscribed = true;
        }

        private void UnsubscribeFromState()
        {
            if (!m_StateSubscribed || m_PlayerState == null)
            {
                return;
            }

            m_PlayerState.OnCharacterPresetChanged -= HandleCharacterPresetChanged;
            m_StateSubscribed = false;
        }

        private void ResolveInitialPreset()
        {
            CharacterCustomizationCatalog catalog = ActiveCatalog;
            if (catalog == null || m_PlayerState == null)
            {
                return;
            }

            string desiredPresetId = m_PlayerState.CharacterPresetId;

            if (string.IsNullOrWhiteSpace(desiredPresetId) && IsOwner && applyPersistedPresetOnSpawn)
            {
                string persistedPresetId = CharacterCustomizationStorage.LoadSelectedPresetId();
                if (!string.IsNullOrWhiteSpace(persistedPresetId))
                {
                    desiredPresetId = persistedPresetId;
                }
            }

            if (string.IsNullOrWhiteSpace(desiredPresetId) && applyCatalogDefaultPresetOnSpawn)
            {
                desiredPresetId = catalog.DefaultPresetId;
            }

            if (string.IsNullOrWhiteSpace(desiredPresetId))
            {
                m_AppliedPresetId = string.Empty;
                return;
            }

            if (IsOwner)
            {
                if (!string.IsNullOrWhiteSpace(desiredPresetId) &&
                    !string.Equals(m_PlayerState.CharacterPresetId, desiredPresetId, StringComparison.Ordinal))
                {
                    m_PlayerState.SetCharacterPresetId(desiredPresetId);
                    return;
                }
            }

            ApplyPreset(desiredPresetId);
        }

        private void HandleCharacterPresetChanged(string presetId)
        {
            if (IsOwner)
            {
                CharacterCustomizationStorage.SaveSelectedPresetId(presetId);
            }

            ApplyPreset(presetId);
        }

        private void HandleCatalogChanged(CharacterCustomizationCatalog _)
        {
            ApplyPreset(m_PlayerState != null ? m_PlayerState.CharacterPresetId : string.Empty, true);
        }

        private void ApplyPreset(string presetId, bool forceReapply = false)
        {
            if (m_CoreAnimator == null)
            {
                CacheDependencies();
            }

            CharacterCustomizationCatalog catalog = ActiveCatalog;
            if (catalog == null || m_CoreAnimator == null)
            {
                return;
            }

            if (!catalog.TryGetPreset(presetId, out CharacterCustomizationPreset preset))
            {
                return;
            }

            if (!forceReapply && string.Equals(m_AppliedPresetId, preset.id, StringComparison.Ordinal))
            {
                return;
            }

            if (TryUseEmbeddedDefaultModel(preset.id, catalog))
            {
                return;
            }

            if (CharacterCustomizationModelSwapper.TryApplyPreset(m_CoreAnimator, preset))
            {
                m_AppliedPresetId = preset.id;
            }
        }

        private bool TryUseEmbeddedDefaultModel(string presetId, CharacterCustomizationCatalog catalog)
        {
            if (catalog == null ||
                m_CoreAnimator == null ||
                !string.IsNullOrWhiteSpace(m_AppliedPresetId) ||
                !string.Equals(presetId, catalog.DefaultPresetId, StringComparison.Ordinal))
            {
                return false;
            }

            Transform modelRoot = m_CoreAnimator.transform;
            if (modelRoot == null || modelRoot.childCount != 1)
            {
                return false;
            }

            Transform existingModel = modelRoot.GetChild(0);
            Animator boundAnimator = m_CoreAnimator.BoundAnimator;
            if (existingModel == null ||
                !string.Equals(existingModel.name, "ModelData", StringComparison.Ordinal) ||
                boundAnimator == null ||
                boundAnimator.avatar == null ||
                boundAnimator.runtimeAnimatorController == null)
            {
                return false;
            }

            m_AppliedPresetId = presetId;
            return true;
        }
    }
}
