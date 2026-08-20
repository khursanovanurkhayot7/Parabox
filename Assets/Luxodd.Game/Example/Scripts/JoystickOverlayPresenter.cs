using UnityEngine;

namespace Luxodd.Game.Example.Scripts
{
    public sealed class JoystickOverlayPresenter : MonoBehaviour
    {
        private static readonly int VectorShaderPropertyId = Shader.PropertyToID("_Vector");

        [Header("References")]
        [SerializeField] private Material _overlayMaterial;
        [SerializeField] private Transform _joystickHandleTransform;

        [Header("Input Source")]
        [SerializeField] private bool _readVectorFromTransform = true;
        [SerializeField] private Vector2 _manualVector = Vector2.zero;

        [Header("Transform Mapping")]
        [SerializeField] private float _joystickRange = 40.0f;
        [SerializeField] private bool _clampMagnitude = true;
        [SerializeField] private bool _moveHandleFromVector;

        private Vector3 _handleInitialLocalPosition;
        private bool _hasHandleInitialLocalPosition;

        private void Awake()
        {
            CacheHandleInitialLocalPosition();
        }

        public void SetVector(Vector2 value)
        {
            _readVectorFromTransform = false;
            _manualVector = _clampMagnitude ? Vector2.ClampMagnitude(value, 1.0f) : value;
            ApplyVectorToMaterial(_manualVector);
            ApplyVectorToHandle(_manualVector);
        }

        private void LateUpdate()
        {
            Vector2 vector = _readVectorFromTransform
                ? ReadVectorFromTransform()
                : _manualVector;

            ApplyVectorToMaterial(vector);
        }

        private Vector2 ReadVectorFromTransform()
        {
            if (_joystickHandleTransform == null)
            {
                return Vector2.zero;
            }

            if (Mathf.Approximately(_joystickRange, 0.0f))
            {
                return Vector2.zero;
            }

            Vector3 localPosition = _joystickHandleTransform.localPosition;
            Vector2 vector = new Vector2(localPosition.x, localPosition.y) / _joystickRange;

            if (_clampMagnitude)
            {
                vector = Vector2.ClampMagnitude(vector, 1.0f);
            }

            return vector;
        }

        private void CacheHandleInitialLocalPosition()
        {
            if (_joystickHandleTransform == null)
            {
                _hasHandleInitialLocalPosition = false;
                return;
            }

            _handleInitialLocalPosition = _joystickHandleTransform.localPosition;
            _hasHandleInitialLocalPosition = true;
        }

        private void ApplyVectorToHandle(Vector2 value)
        {
            if (_moveHandleFromVector == false || _readVectorFromTransform || _joystickHandleTransform == null)
            {
                return;
            }

            if (_hasHandleInitialLocalPosition == false)
            {
                CacheHandleInitialLocalPosition();
            }

            Vector2 clampedValue = _clampMagnitude ? Vector2.ClampMagnitude(value, 1.0f) : value;
            Vector3 offset = new Vector3(clampedValue.x, clampedValue.y, 0.0f) * _joystickRange;
            _joystickHandleTransform.localPosition = _handleInitialLocalPosition + offset;
        }

        private void ApplyVectorToMaterial(Vector2 value)
        {
            if (_overlayMaterial == null)
            {
                return;
            }

            _overlayMaterial.SetVector(
                VectorShaderPropertyId,
                new Vector4(value.x, value.y, 0.0f, 0.0f));
        }

        private void OnDisable()
        {
            ApplyVectorToMaterial(Vector2.zero);

            if (_moveHandleFromVector && _joystickHandleTransform != null && _hasHandleInitialLocalPosition)
            {
                _joystickHandleTransform.localPosition = _handleInitialLocalPosition;
            }
        }
    }
}
