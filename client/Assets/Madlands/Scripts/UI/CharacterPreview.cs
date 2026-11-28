using UnityEngine;
using UnityEngine.UIElements;

namespace Madlands.Client
{
    /// <summary>An isolated rendering stage for the same character models used in the world.</summary>
    public sealed class CharacterPreview : MonoBehaviour
    {
        private const int PreviewLayer = 31;
        private Image _image;
        private RenderTexture _texture;
        private Transform _modelRoot;
        private UnitAnimator _animation;
        private DisplayModel _model;
        private Camera _camera;

        // The current server content assigns display IDs to classes, not races. Keep this
        // selection in one place so it can later be replaced by saved appearance data.
        public static uint DisplayForClass(byte classId) => classId switch
        {
            1 => EntityVisuals.WarriorDisplay,
            2 => EntityVisuals.ShardmageDisplay,
            3 => EntityVisuals.KorrinWardenDisplay,
            _ => 0,
        };

        public void Initialize(Image image)
        {
            _image = image;
            transform.position = new Vector3(0f, 1000f, 0f);
            _texture = new RenderTexture(512, 640, 24) { name = "Character preview" };
            _texture.Create();
            _image.image = _texture;

            var cameraObject = new GameObject("Preview camera");
            cameraObject.transform.SetParent(transform, false);
            _camera = cameraObject.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.075f, 0.10f, 0.15f);
            _camera.cullingMask = 1 << PreviewLayer;
            _camera.targetTexture = _texture;
            _camera.fieldOfView = 32f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 20f;
            AddLight("Preview key", new Vector3(35f, -35f, 0f), 1.35f, new Color(1f, 0.91f, 0.78f));
            AddLight("Preview fill", new Vector3(15f, 145f, 0f), 0.55f, new Color(0.65f, 0.76f, 1f));
        }

        private void AddLight(string name, Vector3 rotation, float intensity, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(transform, false);
            obj.transform.localRotation = Quaternion.Euler(rotation);
            var light = obj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.cullingMask = 1 << PreviewLayer;
        }

        public bool ShowClass(byte classId)
        {
            _animation?.Destroy();
            _animation = null;
            _model = null;
            if (_modelRoot != null)
            {
                _modelRoot.gameObject.SetActive(false);
                Destroy(_modelRoot.gameObject);
            }
            uint display = DisplayForClass(classId);
            if (display == 0) return false;
            _modelRoot = new GameObject("Preview model").transform;
            _modelRoot.SetParent(transform, false);
            var shape = EntityVisuals.Create(_modelRoot, display, out var model);
            _model = model;
            foreach (var child in _modelRoot.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = PreviewLayer;
            if (model != null && model.Animator != null && model.Clips != null)
                _animation = new UnitAnimator(model.Animator, model.Clips, model.Humanoid, model.WalkSpeed, model.RunSpeed);
            float height = Mathf.Max(1f, shape.Height);
            _camera.transform.localPosition = new Vector3(0f, height * 0.53f, height * 2.25f);
            _camera.transform.LookAt(transform.position + Vector3.up * (height * 0.53f));
            return model != null;
        }

        private void LateUpdate()
        {
            _animation?.Update(Time.deltaTime);
            // The same relaxed stance as the in-world UnitView for the bulky class models.
            if (_animation == null || _model == null || _model.ArmsOut <= 0f) return;
            var forward = _modelRoot.forward;
            if (_model.RightUpperArm != null)
                _model.RightUpperArm.rotation = Quaternion.AngleAxis(_model.ArmsOut, forward) * _model.RightUpperArm.rotation;
            if (_model.LeftUpperArm != null)
                _model.LeftUpperArm.rotation = Quaternion.AngleAxis(-_model.ArmsOut, forward) * _model.LeftUpperArm.rotation;
        }

        private void OnDestroy()
        {
            _animation?.Destroy();
            if (_image != null) _image.image = null;
            if (_camera != null) _camera.targetTexture = null;
            if (_texture != null) { _texture.Release(); Destroy(_texture); }
        }
    }
}
