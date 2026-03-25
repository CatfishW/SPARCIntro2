using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ItemInteraction
{
    [DisallowMultipleComponent]
    public class ItemInspectionPresenter : MonoBehaviour
    {
        private const int DefaultPreviewResolution = 1024;
        private const int PreviewLayer = 31;

        private Canvas overlayCanvas;
        private RectTransform overlayRoot;
        private RawImage backdropImage;
        private RawImage previewImage;
        private Text captionText;
        private Text helpText;
        private Image darkenImage;
        private Material blurMaterial;
        private RenderTexture previewTexture;
        private Texture2D capturedBackdrop;
        private Camera previewCamera;
        private Light previewLight;
        private Transform rigRoot;
        private Transform rotationRoot;
        private GameObject activeClone;
        private InspectionPresentation activePresentation;
        private bool initialized;
        private bool isOpen;
        private float yaw;
        private float pitch;
        private float roll;
        private float baseDistance = 1f;
        private float zoomMultiplier = 1f;
        private CursorLockMode previousCursorLockMode;
        private bool previousCursorVisible;

        [Header("Inspection Controls")]
        [SerializeField] private bool unlockCursorDuringInspection = true;
        [SerializeField, Min(0.01f)] private float previewDragRotationMultiplier = 1.5f;

        public bool IsOpen => isOpen;

        public IEnumerator Open(InteractableItem source, InspectionPresentation presentation)
        {
            EnsureInitialized();
            Close();

            activePresentation = presentation ?? new InspectionPresentation();
            overlayRoot.gameObject.SetActive(false);

            yield return new WaitForEndOfFrame();
            CaptureBackdrop();
            SetupClone(source, activePresentation);
            EnsurePreviewTexture();

            if (previewLight != null)
            {
                previewLight.enabled = true;
            }

            previewCamera.targetTexture = previewTexture;
            previewImage.texture = previewTexture;
            previewImage.color = Color.white;
            captionText.text = string.IsNullOrWhiteSpace(activePresentation.caption) ? source.displayName : activePresentation.caption;

            previousCursorLockMode = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            if (unlockCursorDuringInspection)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            overlayRoot.gameObject.SetActive(true);
            isOpen = true;
        }

        public void Tick(InteractionInputSource inputSource)
        {
            if (!isOpen || activeClone == null || inputSource == null)
            {
                return;
            }

            EnsurePreviewTexture();

            var rotationDelta = inputSource.GetInspectionRotateDelta();
            yaw += rotationDelta.x * previewDragRotationMultiplier;
            pitch = Mathf.Clamp(pitch + rotationDelta.y * previewDragRotationMultiplier, -80f, 80f);
            rotationRoot.localRotation = Quaternion.Euler(pitch, yaw, roll);

            zoomMultiplier = Mathf.Clamp(zoomMultiplier + inputSource.GetInspectionZoomDelta(), 0.7f, 1.5f);
            previewCamera.transform.localPosition = new Vector3(0f, 0f, -baseDistance * zoomMultiplier);
            previewCamera.Render();
        }

        public void Close()
        {
            isOpen = false;
            if (previewCamera != null)
            {
                previewCamera.targetTexture = null;
            }

            if (previewLight != null)
            {
                previewLight.enabled = false;
            }

            if (unlockCursorDuringInspection)
            {
                if (Application.platform == RuntimePlatform.WebGLPlayer)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                else
                {
                    Cursor.lockState = previousCursorLockMode;
                    Cursor.visible = previousCursorVisible;
                }
            }

            if (overlayRoot != null)
            {
                overlayRoot.gameObject.SetActive(false);
            }

            if (previewImage != null)
            {
                previewImage.texture = null;
            }

            if (capturedBackdrop != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    DestroyImmediate(capturedBackdrop);
                }
                else
#endif
                {
                    Destroy(capturedBackdrop);
                }

                capturedBackdrop = null;
            }

            if (backdropImage != null)
            {
                backdropImage.texture = null;
            }

            if (activeClone != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    DestroyImmediate(activeClone);
                }
                else
#endif
                {
                    Destroy(activeClone);
                }

                activeClone = null;
            }
        }

        private void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            overlayCanvas = gameObject.GetComponent<Canvas>();
            if (overlayCanvas == null)
            {
                overlayCanvas = gameObject.AddComponent<Canvas>();
                overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                overlayCanvas.sortingOrder = 1000;
            }

            if (gameObject.GetComponent<CanvasScaler>() == null)
            {
                var scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
            }

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            overlayRoot = RuntimeUiFactory.CreateUiObject("InspectionOverlay", transform).GetComponent<RectTransform>();
            overlayRoot.anchorMin = Vector2.zero;
            overlayRoot.anchorMax = Vector2.one;
            overlayRoot.offsetMin = Vector2.zero;
            overlayRoot.offsetMax = Vector2.zero;

            backdropImage = RuntimeUiFactory.CreateUiObject("Backdrop", overlayRoot).AddComponent<RawImage>();
            backdropImage.rectTransform.anchorMin = Vector2.zero;
            backdropImage.rectTransform.anchorMax = Vector2.one;
            backdropImage.rectTransform.offsetMin = Vector2.zero;
            backdropImage.rectTransform.offsetMax = Vector2.zero;
            backdropImage.color = Color.white;

            darkenImage = RuntimeUiFactory.CreateImage("Darken", overlayRoot, new Color(0f, 0f, 0f, 0.55f));
            darkenImage.rectTransform.anchorMin = Vector2.zero;
            darkenImage.rectTransform.anchorMax = Vector2.one;
            darkenImage.rectTransform.offsetMin = Vector2.zero;
            darkenImage.rectTransform.offsetMax = Vector2.zero;

            previewImage = RuntimeUiFactory.CreateUiObject("Preview", overlayRoot).AddComponent<RawImage>();
            previewImage.rectTransform.anchorMin = new Vector2(0.12f, 0.15f);
            previewImage.rectTransform.anchorMax = new Vector2(0.88f, 0.86f);
            previewImage.rectTransform.offsetMin = Vector2.zero;
            previewImage.rectTransform.offsetMax = Vector2.zero;
            previewImage.raycastTarget = false;

            captionText = RuntimeUiFactory.CreateText("Caption", overlayRoot, 24, TextAnchor.MiddleCenter);
            captionText.rectTransform.anchorMin = new Vector2(0.1f, 0.04f);
            captionText.rectTransform.anchorMax = new Vector2(0.9f, 0.10f);
            captionText.rectTransform.offsetMin = Vector2.zero;
            captionText.rectTransform.offsetMax = Vector2.zero;
            captionText.fontStyle = FontStyle.Bold;

            helpText = RuntimeUiFactory.CreateText("Help", overlayRoot, 18, TextAnchor.MiddleRight);
            helpText.rectTransform.anchorMin = new Vector2(0.45f, 0.10f);
            helpText.rectTransform.anchorMax = new Vector2(0.96f, 0.15f);
            helpText.rectTransform.offsetMin = Vector2.zero;
            helpText.rectTransform.offsetMax = Vector2.zero;
            helpText.text = "Hold LMB to rotate  •  Mouse Wheel to zoom  •  RMB / Esc to close";

            var blurShader = Shader.Find("LifeIsStrangeInteraction/BlurredTexture");
            if (blurShader != null)
            {
                blurMaterial = new Material(blurShader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                blurMaterial.SetFloat("_BlurRadius", 2.25f);
                blurMaterial.SetColor("_Tint", new Color(0.94f, 0.94f, 0.94f, 1f));
                backdropImage.material = blurMaterial;
            }

            CreatePreviewRig();
            overlayRoot.gameObject.SetActive(false);
            initialized = true;
        }

        private void CreatePreviewRig()
        {
            rigRoot = new GameObject("InspectionPreviewRig").transform;
            rigRoot.SetParent(transform, false);
            rigRoot.gameObject.hideFlags = HideFlags.HideAndDontSave;
            rigRoot.position = new Vector3(10000f, 10000f, 10000f);

            rotationRoot = new GameObject("RotationRoot").transform;
            rotationRoot.SetParent(rigRoot, false);
            rotationRoot.localPosition = Vector3.zero;
            rotationRoot.localRotation = Quaternion.identity;
            rotationRoot.gameObject.hideFlags = HideFlags.HideAndDontSave;

            var cameraGo = new GameObject("PreviewCamera", typeof(Camera));
            cameraGo.transform.SetParent(rigRoot, false);
            cameraGo.transform.localPosition = new Vector3(0f, 0f, -1f);
            cameraGo.transform.localRotation = Quaternion.identity;
            previewCamera = cameraGo.GetComponent<Camera>();
            previewCamera.enabled = false;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            previewCamera.cullingMask = 1 << PreviewLayer;
            previewCamera.nearClipPlane = 0.01f;
            previewCamera.farClipPlane = 50f;
            previewCamera.allowHDR = false;
            previewCamera.allowMSAA = true;
            previewCamera.depth = 100f;

            var lightGo = new GameObject("PreviewLight", typeof(Light));
            lightGo.transform.SetParent(rigRoot, false);
            lightGo.transform.localPosition = new Vector3(1.5f, 2f, -2f);
            lightGo.transform.LookAt(rigRoot.position);
            previewLight = lightGo.GetComponent<Light>();
            previewLight.type = LightType.Directional;
            previewLight.intensity = 1.25f;
            previewLight.color = Color.white;
            previewLight.cullingMask = 1 << PreviewLayer;
        }

        private void CaptureBackdrop()
        {
            if (capturedBackdrop != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    DestroyImmediate(capturedBackdrop);
                }
                else
#endif
                {
                    Destroy(capturedBackdrop);
                }
            }

            capturedBackdrop = ScreenCapture.CaptureScreenshotAsTexture();
            backdropImage.texture = capturedBackdrop;
        }

        private void SetupClone(InteractableItem source, InspectionPresentation presentation)
        {
            activeClone = VisualOnlyCloneFactory.CreateInspectionClone(source, presentation, PreviewLayer);
            activeClone.transform.SetParent(rotationRoot, false);
            activeClone.transform.localPosition = Vector3.zero;
            activeClone.transform.localRotation = Quaternion.identity;
            activeClone.transform.localScale = Vector3.one;

            RendererBoundsUtility.TryCalculateBounds(activeClone, out var bounds);
            var offset = rigRoot.position - bounds.center + presentation.pivotOffset;
            activeClone.transform.position += offset;

            yaw = presentation.defaultEulerAngles.y;
            pitch = presentation.defaultEulerAngles.x;
            roll = presentation.defaultEulerAngles.z;
            rotationRoot.localRotation = Quaternion.Euler(pitch, yaw, roll);

            var radius = Mathf.Max(bounds.extents.magnitude, 0.08f);
            baseDistance = (radius / Mathf.Tan(presentation.previewFieldOfView * 0.5f * Mathf.Deg2Rad)) * Mathf.Max(0.1f, presentation.framingPadding);
            zoomMultiplier = 1f;

            previewCamera.fieldOfView = presentation.previewFieldOfView;
            previewCamera.transform.localPosition = new Vector3(0f, 0f, -baseDistance);
            previewCamera.Render();
        }

        private void EnsurePreviewTexture()
        {
            var width = Mathf.Max(DefaultPreviewResolution, Screen.width);
            var height = Mathf.Max(DefaultPreviewResolution, Screen.height);

            if (previewTexture != null && previewTexture.width == width && previewTexture.height == height)
            {
                return;
            }

            if (previewTexture != null)
            {
                previewTexture.Release();
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    DestroyImmediate(previewTexture);
                }
                else
#endif
                {
                    Destroy(previewTexture);
                }
            }

            previewTexture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32)
            {
                name = "InspectionPreviewTexture",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            previewTexture.Create();
        }

        private void OnDestroy()
        {
            Close();

            if (previewTexture != null)
            {
                previewTexture.Release();
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    DestroyImmediate(previewTexture);
                }
                else
#endif
                {
                    Destroy(previewTexture);
                }
            }

            if (blurMaterial != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    DestroyImmediate(blurMaterial);
                }
                else
#endif
                {
                    Destroy(blurMaterial);
                }
            }

            if (rigRoot != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    DestroyImmediate(rigRoot.gameObject);
                }
                else
#endif
                {
                    Destroy(rigRoot.gameObject);
                }
            }
        }
    }
}
