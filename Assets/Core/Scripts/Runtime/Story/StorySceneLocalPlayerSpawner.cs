using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

namespace Blocks.Gameplay.Core.Story
{
    public static class StorySceneLocalPlayerSpawner
    {
        private const float NetworkStartupGraceSeconds = 1.25f;

        public delegate bool SpawnPoseResolver(out Vector3 position, out Quaternion rotation);

        public static IEnumerator EnsureLocalPlayerAtPoseRoutine(
            Scene targetScene,
            SpawnPoseResolver tryComputeSpawnPose,
            float timeoutSeconds = 12f,
            float tickSeconds = 0.1f,
            float cameraFallbackHeight = 1.45f,
            System.Action<Vector3> onPoseResolved = null,
            GameObject fallbackPlayerPrefab = null)
        {
            var remainingSeconds = Mathf.Max(tickSeconds, timeoutSeconds);

            while (remainingSeconds > 0f)
            {
                if (tryComputeSpawnPose != null && tryComputeSpawnPose(out var position, out var rotation))
                {
                    onPoseResolved?.Invoke(position);

                    if (TryResolveSceneLocalPlayerMovement(targetScene, out var movement) ||
                        TrySpawnSceneLocalPlayer(targetScene, position, rotation, fallbackPlayerPrefab, out movement))
                    {
                        ApplySpawnPose(movement, position, rotation);
                        yield break;
                    }
                }

                remainingSeconds -= tickSeconds;
                yield return new WaitForSeconds(tickSeconds);
            }

            if (tryComputeSpawnPose == null || !tryComputeSpawnPose(out var fallbackPosition, out var fallbackRotation))
            {
                yield break;
            }

            onPoseResolved?.Invoke(fallbackPosition);

            if (TryResolveSceneLocalPlayerMovement(targetScene, out var lateMovement) ||
                TrySpawnSceneLocalPlayer(targetScene, fallbackPosition, fallbackRotation, fallbackPlayerPrefab, out lateMovement))
            {
                ApplySpawnPose(lateMovement, fallbackPosition, fallbackRotation);
                yield break;
            }

            var mainCamera = Camera.main;
            if (mainCamera != null)
            {
                mainCamera.transform.SetPositionAndRotation(
                    fallbackPosition + new Vector3(0f, cameraFallbackHeight, 0f),
                    fallbackRotation);
            }
        }

        public static bool TryEnsureLocalPlayerAtPose(
            Scene targetScene,
            Vector3 position,
            Quaternion rotation,
            out CoreMovement movement,
            GameObject fallbackPlayerPrefab = null)
        {
            movement = null;

            if (TryResolveSceneLocalPlayerMovement(targetScene, out movement) ||
                TrySpawnSceneLocalPlayer(targetScene, position, rotation, fallbackPlayerPrefab, out movement))
            {
                ApplySpawnPose(movement, position, rotation);
                return true;
            }

            return false;
        }

        public static void ApplySpawnPose(CoreMovement movement, Vector3 position, Quaternion rotation)
        {
            if (movement == null)
            {
                return;
            }

            if (!movement.gameObject.activeSelf)
            {
                movement.gameObject.SetActive(true);
            }

            movement.transform.rotation = rotation;
            movement.transform.localScale = Vector3.one;
            movement.SetMoveInput(Vector2.zero);
            movement.SetSprintState(false);
            movement.SetVerticalVelocity(0f);

            var characterController = movement.GetComponent<CharacterController>();
            if (characterController != null && !characterController.enabled)
            {
                characterController.enabled = true;
            }

            movement.SetPosition(position);
            movement.ResetMovementForces();

            var manager = movement.GetComponent<CorePlayerManager>() ??
                          movement.GetComponentInParent<CorePlayerManager>();
            if (manager != null)
            {
                manager.SetMovementInputEnabled(true);

                if (manager.CoreInput != null && !manager.CoreInput.enabled)
                {
                    manager.CoreInput.enabled = true;
                }

                if (manager.CoreCamera != null && !manager.CoreCamera.enabled)
                {
                    manager.CoreCamera.enabled = true;
                }
            }

            if (movement.GetComponentInParent<OfflineLocalPlayerMarker>() != null &&
                Mouse.current != null &&
                !Application.isMobilePlatform &&
                Application.platform != RuntimePlatform.WebGLPlayer)
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }
        }

        public static bool TryResolveSceneLocalPlayerMovement(Scene targetScene, out CoreMovement movement)
        {
            movement = null;

            var networkManager = NetworkManager.Singleton;
            if (networkManager != null && networkManager.LocalClient != null)
            {
                var localPlayerObject = networkManager.LocalClient.PlayerObject;
                if (TryExtractMovement(localPlayerObject, out movement) && movement.gameObject.scene == targetScene)
                {
                    RemoveRedundantOfflineLocalPlayers(targetScene, movement.gameObject);
                    return true;
                }
            }

            var taggedPlayer = GameObject.FindGameObjectWithTag("Player");
            if (taggedPlayer != null)
            {
                movement = taggedPlayer.GetComponent<CoreMovement>() ??
                           taggedPlayer.GetComponentInChildren<CoreMovement>(true);
                if (movement != null && movement.gameObject.scene == targetScene)
                {
                    return true;
                }

                if (movement != null && TryMoveRootToScene(movement.gameObject, targetScene))
                {
                    return true;
                }
            }

            var managers = Object.FindObjectsByType<CorePlayerManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < managers.Length; index++)
            {
                var candidate = managers[index];
                if (candidate == null || (!candidate.IsOwner && !OfflineLocalAuthority.IsActive(candidate)))
                {
                    continue;
                }

                movement = candidate.CoreMovement != null
                    ? candidate.CoreMovement
                    : candidate.GetComponent<CoreMovement>() ??
                      candidate.GetComponentInChildren<CoreMovement>(true);

                if (movement != null && movement.gameObject.scene == targetScene)
                {
                    return true;
                }

                if (movement != null && TryMoveRootToScene(movement.gameObject, targetScene))
                {
                    return true;
                }
            }

            movement = null;
            return false;
        }

        private static bool TrySpawnSceneLocalPlayer(
            Scene targetScene,
            Vector3 position,
            Quaternion rotation,
            GameObject fallbackPlayerPrefab,
            out CoreMovement movement)
        {
            movement = null;

            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                return TrySpawnOfflineLocalPlayer(targetScene, position, rotation, fallbackPlayerPrefab, out movement);
            }

            if (!networkManager.IsListening || !networkManager.IsServer || networkManager.LocalClient == null)
            {
                if (ShouldWaitForNetworkLocalPlayer(networkManager))
                {
                    return false;
                }

                return TrySpawnOfflineLocalPlayer(targetScene, position, rotation, fallbackPlayerPrefab, out movement);
            }

            var localClientId = networkManager.LocalClientId;
            var existingPlayerObject = networkManager.LocalClient.PlayerObject;
            if (existingPlayerObject != null && existingPlayerObject.gameObject.scene != targetScene)
            {
                if (existingPlayerObject.IsSpawned)
                {
                    existingPlayerObject.Despawn(true);
                }
                else
                {
                    Object.Destroy(existingPlayerObject.gameObject);
                }
            }

            if (TryResolveSceneLocalPlayerMovement(targetScene, out movement))
            {
                RemoveRedundantOfflineLocalPlayers(targetScene, movement != null ? movement.gameObject : null);
                return true;
            }

            var playerPrefab = ResolveConfiguredPlayerPrefab(fallbackPlayerPrefab);
            if (playerPrefab == null)
            {
                Debug.LogWarning("[StorySceneLocalPlayerSpawner] NetworkManager has no PlayerPrefab configured.");
                return false;
            }

            var spawnedNetworkObject = NetworkObject.InstantiateAndSpawn(
                playerPrefab,
                networkManager,
                localClientId,
                destroyWithScene: true,
                isPlayerObject: true,
                forceOverride: false,
                position: position,
                rotation: rotation);

            if (spawnedNetworkObject == null)
            {
                return false;
            }

            if (spawnedNetworkObject.gameObject.scene != targetScene)
            {
                SceneManager.MoveGameObjectToScene(spawnedNetworkObject.gameObject, targetScene);
            }

            InitializeLocalPlayerState(spawnedNetworkObject, localClientId);
            var resolved = TryExtractMovement(spawnedNetworkObject, out movement);
            if (resolved)
            {
                RemoveRedundantOfflineLocalPlayers(targetScene, movement != null ? movement.gameObject : spawnedNetworkObject.gameObject);
            }

            return resolved;
        }

        private static bool TrySpawnOfflineLocalPlayer(
            Scene targetScene,
            Vector3 position,
            Quaternion rotation,
            GameObject fallbackPlayerPrefab,
            out CoreMovement movement)
        {
            movement = null;

            if (TryResolveSceneLocalPlayerMovement(targetScene, out movement))
            {
                return true;
            }

            var playerPrefab = ResolveConfiguredPlayerPrefab(fallbackPlayerPrefab);
            if (playerPrefab == null)
            {
                var networkManager = NetworkManager.Singleton;
                Debug.LogWarning($"[StorySceneLocalPlayerSpawner] Unable to spawn offline local player because no PlayerPrefab is configured. targetScene={targetScene.name} networkManager={(networkManager != null ? networkManager.name : "null")}");
                return false;
            }

            var playerInstance = Object.Instantiate(playerPrefab, position, rotation);
            playerInstance.name = playerPrefab.name;
            playerInstance.SetActive(true);
            if (playerInstance.scene != targetScene)
            {
                SceneManager.MoveGameObjectToScene(playerInstance, targetScene);
            }

            if (playerInstance.GetComponent<OfflineLocalPlayerMarker>() == null)
            {
                playerInstance.AddComponent<OfflineLocalPlayerMarker>();
            }

            InitializeOfflineLocalPlayerState(playerInstance);
            if (TryExtractMovement(playerInstance, out movement))
            {
                if (movement.gameObject.scene != targetScene)
                {
                    TryMoveRootToScene(movement.gameObject, targetScene);
                }

                return true;
            }

            Debug.LogWarning($"[StorySceneLocalPlayerSpawner] Offline local player spawned without CoreMovement. prefab={playerPrefab.name} targetScene={targetScene.name}");
            return false;
        }

        private static GameObject ResolveConfiguredPlayerPrefab(GameObject fallbackPlayerPrefab)
        {
            var networkManager = NetworkManager.Singleton;
            var playerPrefab = networkManager != null ? networkManager.NetworkConfig.PlayerPrefab : null;
            return playerPrefab != null ? playerPrefab : fallbackPlayerPrefab;
        }

        private static bool ShouldWaitForNetworkLocalPlayer(NetworkManager networkManager)
        {
            if (networkManager == null ||
                networkManager.IsListening ||
                Application.platform == RuntimePlatform.WebGLPlayer)
            {
                return false;
            }

            if (networkManager is GameNetworkManager gameNetworkManager)
            {
                var connectionState = gameNetworkManager.NetworkState != null
                    ? gameNetworkManager.NetworkState.ConnectionState
                    : GameNetworkManager.ConnectionStates.None;

                if (connectionState == GameNetworkManager.ConnectionStates.Connecting)
                {
                    return true;
                }

                if (connectionState == GameNetworkManager.ConnectionStates.Failed)
                {
                    return false;
                }
            }

            return Application.isPlaying && Time.realtimeSinceStartup <= NetworkStartupGraceSeconds;
        }

        private static void RemoveRedundantOfflineLocalPlayers(Scene targetScene, GameObject keepObject)
        {
            var keepRoot = keepObject != null ? keepObject.transform.root.gameObject : null;
            var offlineMarkers = Object.FindObjectsByType<OfflineLocalPlayerMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < offlineMarkers.Length; index++)
            {
                var marker = offlineMarkers[index];
                if (marker == null)
                {
                    continue;
                }

                var root = marker.transform.root.gameObject;
                if (root == null || root == keepRoot || root.scene != targetScene)
                {
                    continue;
                }

                root.SetActive(false);
                Object.Destroy(root);
            }
        }

        private static bool TryMoveRootToScene(GameObject gameObject, Scene targetScene)
        {
            if (gameObject == null || !targetScene.IsValid() || !targetScene.isLoaded)
            {
                return false;
            }

            var root = gameObject.transform.root.gameObject;
            if (root.scene == targetScene)
            {
                return true;
            }

            try
            {
                SceneManager.MoveGameObjectToScene(root, targetScene);
                return root.scene == targetScene;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[StorySceneLocalPlayerSpawner] Failed to move '{root.name}' into scene '{targetScene.name}': {exception.Message}");
                return false;
            }
        }

        private static void InitializeLocalPlayerState(NetworkObject playerObject, ulong localClientId)
        {
            if (playerObject == null)
            {
                return;
            }

            var playerState = playerObject.GetComponent<CorePlayerState>() ??
                              playerObject.GetComponentInChildren<CorePlayerState>(true);
            if (playerState != null)
            {
                playerState.SetPlayerName($"Player{localClientId}");
                playerState.SetLifeState(PlayerLifeState.InitialSpawn);
            }

            var statsHandler = playerObject.GetComponent<CoreStatsHandler>() ??
                               playerObject.GetComponentInChildren<CoreStatsHandler>(true);
            if (statsHandler != null && statsHandler.IsOwner)
            {
                statsHandler.ModifyStat(StatKeys.Health, 100f, localClientId, ModificationSource.Regeneration);
            }
        }

        private static void InitializeOfflineLocalPlayerState(GameObject playerObject)
        {
            if (playerObject == null)
            {
                return;
            }

            var playerState = playerObject.GetComponent<CorePlayerState>() ??
                              playerObject.GetComponentInChildren<CorePlayerState>(true);
            if (playerState != null)
            {
                playerState.SetPlayerName("Player");
                playerState.SetLifeState(PlayerLifeState.InitialSpawn);
            }
        }

        private static bool TryExtractMovement(NetworkObject playerObject, out CoreMovement movement)
        {
            movement = null;
            if (playerObject == null)
            {
                return false;
            }

            movement = playerObject.GetComponent<CoreMovement>() ??
                       playerObject.GetComponentInChildren<CoreMovement>(true);
            return movement != null;
        }

        private static bool TryExtractMovement(GameObject playerObject, out CoreMovement movement)
        {
            movement = null;
            if (playerObject == null)
            {
                return false;
            }

            movement = playerObject.GetComponent<CoreMovement>() ??
                       playerObject.GetComponentInChildren<CoreMovement>(true);
            return movement != null;
        }
    }
}
