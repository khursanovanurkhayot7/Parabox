using UnityEngine;
using UnityEngine.UI;

namespace Luxodd.Game.Example.Scripts
{
    public sealed class ArcadeButtonVisual : MonoBehaviour
    {
        private static readonly int BaseColorShaderPropertyId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorShaderPropertyId = Shader.PropertyToID("_EmissionColor");

        [Header("References")]
        [SerializeField] private Transform _targetTransform;
        [SerializeField] private Renderer _targetRenderer;
        [SerializeField] private Graphic _targetGraphic;
        [SerializeField] private bool _useGraphicInitialColorAsIdle = true;
        [SerializeField] private bool _useGraphicInitialColorAsPressedWhenDefault = true;

        [Header("Color State")]
        [SerializeField] private Color _idleBaseColor = Color.white;
        [SerializeField] private Color _pressedBaseColor = Color.white;
        [SerializeField] private Color _idleEmissionColor = Color.black;
        [SerializeField] private Color _pressedEmissionColor = Color.white * 0.8f;

        [Header("Transform State")]
        [SerializeField] private Vector3 _idleLocalScale = Vector3.one;
        [SerializeField] private Vector3 _pressedLocalScale = new Vector3(0.94f, 0.94f, 0.94f);
        [SerializeField] private Vector3 _pressedLocalOffset = new Vector3(0.0f, -0.01f, 0.0f);

        [Header("Animation")]
        [SerializeField] private float _transitionSpeed = 18.0f;
        [SerializeField] private float _pulseStrength = 0.08f;
        [SerializeField] private float _pulseDecaySpeed = 8.0f;

        private MaterialPropertyBlock _propertyBlock;

        private Vector3 _idleLocalPosition;
        private float _pressedAmount;
        private float _pulseAmount;
        private bool _isPressed;
        private Material _graphicMaterialInstance;
        private Material _originalGraphicMaterial;

        public void SetPressed(bool pressed)
        {
            if (pressed && !_isPressed)
            {
                _pulseAmount = 1.0f;
            }

            _isPressed = pressed;
        }

        public void TriggerPulse()
        {
            _pulseAmount = 1.0f;
        }

        private void Awake()
        {
            if (_targetTransform == null)
            {
                _targetTransform = transform;
            }

            if (_targetGraphic == null)
            {
                _targetGraphic = GetComponent<Graphic>();
            }

            InitializeGraphicBaseColors();
            _propertyBlock = new MaterialPropertyBlock();
            InitializeGraphicMaterial();

            _idleLocalPosition = _targetTransform.localPosition;
            ApplyVisual(0.0f, 0.0f);
        }

        private void OnDestroy()
        {
            if (_targetGraphic != null && _graphicMaterialInstance != null)
            {
                _targetGraphic.material = _originalGraphicMaterial;
                Destroy(_graphicMaterialInstance);
            }
        }

        private void Update()
        {
            float targetPressedAmount = _isPressed ? 1.0f : 0.0f;

            _pressedAmount = Mathf.MoveTowards(
                _pressedAmount,
                targetPressedAmount,
                _transitionSpeed * Time.deltaTime);

            _pulseAmount = Mathf.MoveTowards(
                _pulseAmount,
                0.0f,
                _pulseDecaySpeed * Time.deltaTime);

            ApplyVisual(_pressedAmount, _pulseAmount);
        }

        private void ApplyVisual(float pressedFactor, float pulseFactor)
        {
            Color baseColor = Color.Lerp(_idleBaseColor, _pressedBaseColor, pressedFactor);
            Color emissionColor = Color.Lerp(_idleEmissionColor, _pressedEmissionColor, pressedFactor);
            emissionColor += _pressedEmissionColor * (0.35f * pulseFactor);

            if (_targetTransform != null)
            {
                Vector3 localScale = Vector3.Lerp(_idleLocalScale, _pressedLocalScale, pressedFactor);
                localScale += Vector3.one * (_pulseStrength * pulseFactor);

                Vector3 localPosition = Vector3.Lerp(
                    _idleLocalPosition,
                    _idleLocalPosition + _pressedLocalOffset,
                    pressedFactor);

                _targetTransform.localScale = localScale;
                _targetTransform.localPosition = localPosition;
            }

            if (_targetRenderer != null)
            {
                if (_propertyBlock == null)
                {
                    _propertyBlock = new MaterialPropertyBlock();
                }

                _targetRenderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(BaseColorShaderPropertyId, baseColor);
                _propertyBlock.SetColor(EmissionColorShaderPropertyId, emissionColor);
                _targetRenderer.SetPropertyBlock(_propertyBlock);
            }

            ApplyGraphicColor(baseColor, emissionColor);
        }

        private void InitializeGraphicMaterial()
        {
            if (_targetGraphic == null)
            {
                return;
            }

            _originalGraphicMaterial = _targetGraphic.material;

            if (_originalGraphicMaterial == null)
            {
                return;
            }

            _graphicMaterialInstance = new Material(_originalGraphicMaterial);
            _targetGraphic.material = _graphicMaterialInstance;
        }

        private void InitializeGraphicBaseColors()
        {
            if (_targetGraphic == null)
            {
                return;
            }

            Color initialGraphicColor = _targetGraphic.color;

            if (_useGraphicInitialColorAsIdle)
            {
                _idleBaseColor = initialGraphicColor;
            }

            if (_useGraphicInitialColorAsPressedWhenDefault && IsApproximately(_pressedBaseColor, Color.white))
            {
                _pressedBaseColor = initialGraphicColor;
            }
        }

        private void ApplyGraphicColor(Color baseColor, Color emissionColor)
        {
            if (_targetGraphic == null)
            {
                return;
            }

            _targetGraphic.color = baseColor;

            if (_graphicMaterialInstance == null)
            {
                return;
            }

            if (_graphicMaterialInstance.HasProperty(BaseColorShaderPropertyId))
            {
                _graphicMaterialInstance.SetColor(BaseColorShaderPropertyId, baseColor);
            }

            if (_graphicMaterialInstance.HasProperty(EmissionColorShaderPropertyId))
            {
                _graphicMaterialInstance.SetColor(EmissionColorShaderPropertyId, emissionColor);
            }
        }

        private static bool IsApproximately(Color a, Color b)
        {
            return Mathf.Approximately(a.r, b.r)
                   && Mathf.Approximately(a.g, b.g)
                   && Mathf.Approximately(a.b, b.b)
                   && Mathf.Approximately(a.a, b.a);
        }
    }
}
