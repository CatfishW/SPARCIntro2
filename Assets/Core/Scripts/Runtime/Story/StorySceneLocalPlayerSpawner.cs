using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blocks.Gameplay.Core.Story
{
    public static class StorySceneLocalPlayerSpawner
    {
        public delegate bool SpawnPoseResolver(out Vector3 position, out Quaternion rotation);

        public static IEnumerator EnsureLocalPlayerAtPoseRoutine(
            Scene targetScene,
            SpawnPoseResolver tryComputeSpawnPose,
            float timeoutSeconds = 12f,
            float tickSeconds = 0.1f,
            float cameraFallbackHeight = 1.45f,
            System.Action<Vector3> onPoseResolved = null)
        {
            var remainingSeconds = Mathf.Max(tickSeconds, timeoutSeconds);

            while (remainingSeconds > 0f)
            {
                if (tryComputeSpawnPose != null && tryComputeSpawnPose(out var position, out var rotation))
                {
                    onPoseResolved?.Invoke(position);

                    if (TryResolveSceneLocalPlayerMovement(targetScene, out var movement) ||
                        TrySpawnSceneLocalPlayer(targetScene, position, rotation, out movement))
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
                TrySpawnSceneLocalPlayer(targetScene, fallbackPosition, fallbackRotation, out lateMovement))
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
            manager?.SetMovementInputEnabled(true);
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
            }

            var managers = Object.FindObjectsByType<CorePlayerManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var index = 0; index < managers.Length; index++)
            {
                var candidate = managers[index];
                if (candidate == null || !candidate.IsOwner)
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
            }

            movement = null;
            return false;
        }

        private static bool TrySpawnSceneLocalPlayer(Scene targetScene, Vector3 position, Quaternion rotation, out CoreMovement movement)
        {
            movement = null;

            var networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsListening || !networkManager.IsServer || networkManager.LocalClient == null)
            {
                return false;
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
                return true;
            }

            var playerPrefab = networkManager.NetworkConfig.PlayerPrefab;
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
            return TryExtractMovement(spawnedNetworkObject, out movement);
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
    }
}
