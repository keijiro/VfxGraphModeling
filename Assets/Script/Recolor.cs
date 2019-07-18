//
// Recolor from Kino post processing effect suite
//
// Modified to use the AOV output feature of HDRP.
//

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.HDPipeline;
using RTHandle = UnityEngine.Experimental.Rendering.RTHandleSystem.RTHandle;

namespace Cb3
{
    [ExecuteAlways]
    [RequireComponent(typeof(HDAdditionalCameraData))]
    public sealed class Recolor : MonoBehaviour
    {
        #region Nested classes

        public enum EdgeSource { Color, Depth, Normal }

        #endregion

        #region Editable attributes

        [SerializeField] Color _edgeColor = Color.black;
        [SerializeField] EdgeSource _edgeSource = EdgeSource.Color;
        [SerializeField, Range(0, 1)] float _edgeThreshold = 0.5f;
        [SerializeField, Range(0, 1)] float _edgeContrast = 0.5f;
        [SerializeField] Gradient _fillGradient = null;
        [SerializeField, Range(0, 1)] float _fillOpacity = 0;
        [SerializeField] Color _backgroundColor = Color.blue;
        [SerializeField] RenderTexture _targetTexture = null;

        #endregion

        #region Public properties

        public Color edgeColor {
            get { return _edgeColor; }
            set { _edgeColor = value; }
        }

        public EdgeSource edgeSource {
            get { return _edgeSource; }
            set { _edgeSource = value; }
        }

        public float edgeThreshold {
            get { return _edgeThreshold; }
            set { _edgeThreshold = value; }
        }

        public float edgeContrast {
            get { return _edgeContrast; }
            set { _edgeContrast = value; }
        }

        public Gradient fillGradient {
            get { return _fillGradient; }
            set {
                _fillGradient = value;
                _colorKeys = value.colorKeys;
            }
        }

        public float fillOpacity {
            get { return _fillOpacity; }
            set { _fillOpacity = value; }
        }

        public RenderTexture targetTexture {
            get { return _targetTexture; }
            set { _targetTexture = value; }
        }

        #endregion

        #region Shader property IDs

        static readonly (
            int ColorTexture,
            int NormalTexture,
            int DepthTexture,
            int EdgeColor,
            int EdgeThresholds,
            int FillOpacity,
            int BgColor
        ) _ID = (
            Shader.PropertyToID("_ColorTexture"),
            Shader.PropertyToID("_NormalTexture"),
            Shader.PropertyToID("_DepthTexture"),
            Shader.PropertyToID("_EdgeColor"),
            Shader.PropertyToID("_EdgeThresholds"),
            Shader.PropertyToID("_FillOpacity"),
            Shader.PropertyToID("_BgColor")
        );

        #endregion

        #region Private variables

        Material _material;
        MaterialPropertyBlock _props;
        GradientColorKey [] _colorKeys;

        #endregion

        #region Private helper properties

        Vector2 EdgeThresholdVector {
            get {
                if (_edgeSource == EdgeSource.Depth)
                {
                    var thresh = 1 / Mathf.Lerp(1000, 1, _edgeThreshold);
                    var scaler = 1 + 2 / (1.01f - _edgeContrast);
                    return new Vector2(thresh, thresh * scaler);
                }
                else // Depth & Color
                {
                    var thresh = _edgeThreshold;
                    return new Vector2(thresh, thresh + 1.01f - _edgeContrast);
                }
            }
        }

        #endregion

        #region AOV request methods

        (RTHandle color, RTHandle normal, RTHandle depth) _rt;

        RTHandle RTAllocator(AOVBuffers bufferID)
        {
            if (bufferID == AOVBuffers.Color)
                return _rt.color ??
                    (_rt.color = RTHandles.Alloc(
                        _targetTexture.width, _targetTexture.height, 1,
                        DepthBits.None, GraphicsFormat.R8G8B8A8_SRGB));

            if (bufferID == AOVBuffers.Normals)
                return _rt.normal ??
                    (_rt.normal = RTHandles.Alloc(
                        _targetTexture.width, _targetTexture.height, 1,
                        DepthBits.None, GraphicsFormat.R8G8B8A8_UNorm));

            // bufferID == AOVBuffers.Depth
            return _rt.depth ??
                (_rt.depth = RTHandles.Alloc(
                    _targetTexture.width, _targetTexture.height, 1,
                    DepthBits.None, GraphicsFormat.R32_SFloat));
        }

        void AovCallback(
            CommandBuffer cmd,
            List<RTHandle> buffers,
            RenderOutputProperties outProps
        )
        {
            // Shader objects instantiation
            if (_material == null)
            {
                var shader = Shader.Find("Hidden/Cb3/Recolor");
                _material = new Material(shader);
                _material.hideFlags = HideFlags.DontSave;
            }

            if (_props == null) _props = new MaterialPropertyBlock();

            // AOV buffers
            _props.SetTexture(_ID. ColorTexture, buffers[0]);
            _props.SetTexture(_ID.NormalTexture, buffers[1]);
            _props.SetTexture(_ID. DepthTexture, buffers[2]);

            // Shader properties
            _props.SetColor(_ID.EdgeColor, _edgeColor);
            _props.SetVector(_ID.EdgeThresholds, EdgeThresholdVector);
            _props.SetFloat(_ID.FillOpacity, _fillOpacity);
            _props.SetColor(_ID.BgColor, _backgroundColor);
            GradientUtility.SetColorKeys(_props, _colorKeys);

            // Shader pass selection
            var pass = (int)_edgeSource;
            if (_fillOpacity > 0 && _colorKeys.Length > 3) pass += 3;
            if (_fillGradient.mode == GradientMode.Blend) pass += 6;

            // Full screen triangle
            CoreUtils.DrawFullScreen(
                cmd, _material, _targetTexture, _props, pass
            );
        }

        AOVRequestDataCollection BuildAovRequest()
        {
            return new AOVRequestBuilder().Add(
                AOVRequest.@default,
                RTAllocator,
                null, // lightFilter
                new [] {
                    AOVBuffers.Color,
                    AOVBuffers.Normals,
                    AOVBuffers.DepthStencil
                },
                AovCallback
            ).Build();
        }

        #endregion

        #region MonoBehaviour implementation

        void OnEnable()
        {
            // Do nothing if no target is given.
            if (_targetTexture == null) return;

            // AOV request
            GetComponent<HDAdditionalCameraData>().SetAOVRequests(BuildAovRequest());
        }

        void OnDisable()
        {
            GetComponent<HDAdditionalCameraData>().SetAOVRequests(null);
        }

        void OnValidate()
        {
            if (enabled)
            {
                OnDisable();
                OnEnable();
            }
        }

        void OnDestroy()
        {
            CoreUtils.Destroy(_material);
        }

        void Start()
        {
            #if !UNITY_EDITOR
            // At runtime, copy gradient color keys only once on initialization.
            _colorKeys = _fillGradient.colorKeys;
            #endif
        }

        void LateUpdate()
        {
            #if UNITY_EDITOR
            // In editor, copy gradient color keys every frame.
            _colorKeys = _fillGradient.colorKeys;
            #endif
        }

        #endregion
    }
}
