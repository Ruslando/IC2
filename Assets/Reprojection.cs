using UnityEngine;
//using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

#region Effect settings

[System.Serializable]
[PostProcess(typeof(ReprojectionRenderer), PostProcessEvent.BeforeStack, "Reprojection")]
public sealed class Reprojection : PostProcessEffectSettings
{
    //public FloatParameter exampleFloat   = new FloatParameter { value = 1 };
    public IntParameter holdFrame = new IntParameter { value = 0 };
}

#endregion

#region Effect renderer

sealed class ReprojectionRenderer : PostProcessEffectRenderer<Reprojection>
{
    static class ShaderIDs
    {
        // Texture buffers
        internal static readonly int PreviousColorTexture               = Shader.PropertyToID("_PreviousColorTexture");
        internal static readonly int PreviousMotionDepthTexture         = Shader.PropertyToID("_PreviousMotionDepthTexture");

        // Previous camera matrices & vectors
        internal static readonly int PreviousCameraPosition             = Shader.PropertyToID("_PreviousCameraPosition");
        internal static readonly int PreviousInvViewMatrix              = Shader.PropertyToID("_PreviousInvViewMatrix");
        internal static readonly int PreviousInvProjectionMatrix        = Shader.PropertyToID("_PreviousInvProjectionMatrix");
        internal static readonly int PreviousProjectionViewMatrix       = Shader.PropertyToID("_PreviousProjectionViewMatrix");

        // Current camera matrices & vectors
        internal static readonly int CameraPosition                     = Shader.PropertyToID("_CameraPosition");
        internal static readonly int InvViewMatrix                      = Shader.PropertyToID("_InvViewMatrix");
        internal static readonly int InvProjectionMatrix                = Shader.PropertyToID("_InvProjectionMatrix");
        internal static readonly int ProjectionViewMatrix               = Shader.PropertyToID("_ProjectionViewMatrix");
    }

    enum ShaderPassId
    {
        Initialize,
        Timewarp,
        Display,
    }

    RenderTexture _previousColorTexture;
    RenderTexture _previousMotionDepthTexture;

    int _previousHoldFrame;

    UnityEngine.Rendering.RenderTargetIdentifier[] _mrt = new UnityEngine.Rendering.RenderTargetIdentifier[2];  // Main Render Targets

    // float _prevDeltaTime;

    public bool CheckHoldFrame()
    {
        if(_previousHoldFrame == 0 && settings.holdFrame == 1)
        {
            _previousHoldFrame = settings.holdFrame;
            return true;
        }

        _previousHoldFrame = settings.holdFrame;
        return false;
    }

    public override void Release()
    {
        if (_previousColorTexture != null)
        {
            RenderTexture.ReleaseTemporary(_previousColorTexture);
            _previousColorTexture = null;
        }

        if (_previousMotionDepthTexture != null)
        {
            RenderTexture.ReleaseTemporary(_previousMotionDepthTexture);
            _previousMotionDepthTexture = null;
        }

        base.Release();
    }

    public override DepthTextureMode GetCameraFlags()
    {
        return DepthTextureMode.MotionVectors | DepthTextureMode.Depth;
    }

    public override void Render(PostProcessRenderContext context)
    {   
        context.command.BeginSample("TemporalReprojection");

        // Set the shader uniforms.
        var sheet = context.propertySheets.Get(Shader.Find("Hidden/Reprojection"));

        bool holdFrame = CheckHoldFrame();

        if(holdFrame)
        {
            Debug.Log("bruh");
            // (Re-)Initializes reference frames and motion vector history
            InitializeReferenceFrame(sheet, context);
            // Display current source image
            context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, (int) ShaderPassId.Display);
        }

        if(settings.holdFrame == 1)
        {
            // Applies reprojection from the reference image
            ApplyReprojection(sheet, context);
        } 
        else 
        {
            context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, (int) ShaderPassId.Display);
        }

        // switch (_currentOptimizationOption)
        // {
        //     case OptimizationOption.None:
        //         {
        //             context.command.BlitFullscreenTriangle(context.source, context.destination , sheet, (int) ShaderPassId.Display);
        //             break;
        //         }
        //     case OptimizationOption.LatencyReduction:
        //         {
        //             if(frameState == FrameState.SimulatedFrame)
        //             {
        //                 ApplyReprojectionLateApplyReprojectionLatency(sheet, context);
        //             }

        //             UpdateMotionVectorHistory(sheet, context);

        //             context.command.BlitFullscreenTriangle(_previousReprojectedTexture, context.destination , sheet, (int) ShaderPassId.Display);
        //             break;
        //         }
        //     case OptimizationOption.FrameGeneration:
        //         {
        //             if(frameState == FrameState.SimulatedFrame)
        //             { 
        //                 // (Re-)Initializes reference frames and motion vector history
        //                 InitializeReferenceFrame(sheet, context);
        //                 // Display current source image
        //                 context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, (int) ShaderPassId.Display);
        //             }

        //             if(frameState == FrameState.ExtrapolatedFrame)
        //             {
        //                 // Applies reprojection from the reference image
        //                 ApplyReprojection(sheet, context);
        //             }

        //             // Adds current motion vector to motion vector history
        //             UpdateMotionVectorHistory(sheet, context);
        //             break;
        //         }
        // }

        context.command.EndSample("Reprojection");
    }

    private void ApplyReprojectionLateApplyReprojectionLatency(PropertySheet sheet, PostProcessRenderContext context)
    {
        // // Update the "previous" texture at each frame
        // if (_previousColorTexture != null) sheet.properties.SetTexture(ShaderIDs.PreviousColorTexture, _previousColorTexture);
        // if (_previousMotionDepthTexture != null) sheet.properties.SetTexture(ShaderIDs.PreviousMotionDepthTexture, _previousMotionDepthTexture);

        // // Set camera matrices for the "current" frame
        // setCameraMatrices(false, sheet, context);

        // // release previous reprojected texture, if it exists
        // if (_previousReprojectedTexture != null) RenderTexture.ReleaseTemporary(_previousReprojectedTexture);

        // if(_currentReprojectionMode != ReprojectionMode.None) {
        //     // create new temporary render texture for new reprojected texture
        //     var newReprojectedTexture = RenderTexture.GetTemporary(context.width, context.height, 0, RenderTextureFormat.ARGBHalf);

        //     // call render pass with specified reprojection technique
        //     context.command.BlitFullscreenTriangle(context.source, newReprojectedTexture, newReprojectedTexture.colorBuffer , sheet, (int) _currentReprojectionMode);

        //     // save reprojection texture for extrapolated frame pass
        //     _previousReprojectedTexture = newReprojectedTexture;
        // } else {
        //     // create new temporary render texture for previous color
        //     var newReprojectedTexture = RenderTexture.GetTemporary(context.width, context.height, 0, RenderTextureFormat.ARGBHalf);
        //     // copy previous color texture to new temporary render texture
        //     context.command.BlitFullscreenTriangle(_previousColorTexture, newReprojectedTexture, newReprojectedTexture.colorBuffer , sheet, (int) ShaderPassId.Display);
        //     // set previous color texture to "reprojected texture"
        //     _previousReprojectedTexture = newReprojectedTexture;
        // }

        // // display current reprojected frame
        // context.command.BlitFullscreenTriangle(_previousReprojectedTexture, context.destination , sheet, (int) ShaderPassId.Display);

        // // Discard the previous frame state.
        // if (_previousColorTexture != null) RenderTexture.ReleaseTemporary(_previousColorTexture);
        // if (_previousMotionDepthTexture != null) RenderTexture.ReleaseTemporary(_previousMotionDepthTexture);
        // if (_motionVectorHistoryTexture != null) RenderTexture.ReleaseTemporary(_motionVectorHistoryTexture);

        // // Allocate Render Textures for storing the next frame state.
        // var newColorTexture = RenderTexture.GetTemporary(context.width, context.height, 0, RenderTextureFormat.ARGBHalf);
        // var newMotionDepthTexture = RenderTexture.GetTemporary(context.width, context.height, 0, RenderTextureFormat.ARGBHalf);
        // var newMotionVectorHistory = RenderTexture.GetTemporary(context.width, context.height, 0, RenderTextureFormat.ARGBHalf);

        // _mrt[0] = newColorTexture.colorBuffer;
        // _mrt[1] = newMotionDepthTexture.colorBuffer;

        // // Copy the current frame state into the new textures.
        // context.command.BlitFullscreenTriangle(context.source, _mrt, newColorTexture.depthBuffer, sheet, (int) ShaderPassId.Initialize);

        // // Reset motion vector history
        // context.command.BlitFullscreenTriangle(context.source, newMotionVectorHistory, newMotionVectorHistory.colorBuffer, sheet, (int) ShaderPassId.ResetMotionVectorHistory);

        // // Update the internal state.
        // _previousColorTexture = newColorTexture;
        // _previousMotionDepthTexture = newMotionDepthTexture;
        // _motionVectorHistoryTexture = newMotionVectorHistory;

        // // Set camera matrices for the "previous"
        // setCameraMatrices(true, sheet, context);

        // sheet.properties.SetTexture(ShaderIDs.MotionVectorHistory, _motionVectorHistoryTexture);
    }

    private void InitializeReferenceFrame(PropertySheet sheet, PostProcessRenderContext context)
    {
        // Discard the previous frame states.
        InitializeReferenceFrameStates(sheet, context);

        // Set camera matrices for the "previous"
        setCameraMatrices(true, sheet, context);
    }

    private void InitializeReferenceFrameStates(PropertySheet sheet, PostProcessRenderContext context)
    {
        if (_previousColorTexture != null) RenderTexture.ReleaseTemporary(_previousColorTexture);
        if (_previousMotionDepthTexture != null) RenderTexture.ReleaseTemporary(_previousMotionDepthTexture);

        // Allocate Render Textures for storing the next frame state.
        var newColorTexture = RenderTexture.GetTemporary(context.width, context.height, 0, RenderTextureFormat.ARGBHalf);
        var newMotionDepthTexture = RenderTexture.GetTemporary(context.width, context.height, 0, RenderTextureFormat.ARGBHalf);

        _mrt[0] = newColorTexture.colorBuffer;
        _mrt[1] = newMotionDepthTexture.colorBuffer;

        // Copy the current frame state into the new textures.
        context.command.BlitFullscreenTriangle(context.source, _mrt, newColorTexture.depthBuffer, sheet, (int) ShaderPassId.Initialize);

        // Update the internal state.
        _previousColorTexture = newColorTexture;
        _previousMotionDepthTexture = newMotionDepthTexture;
    }

    private void ApplyReprojection(PropertySheet sheet, PostProcessRenderContext context)
    {
        // Set reference color and motion / depth texture to shader
        if (_previousColorTexture != null) sheet.properties.SetTexture(ShaderIDs.PreviousColorTexture, _previousColorTexture);
        if (_previousMotionDepthTexture != null) sheet.properties.SetTexture(ShaderIDs.PreviousMotionDepthTexture, _previousMotionDepthTexture);

        // Set camera matrices for "current" frame
        setCameraMatrices(false, sheet, context);

        // Apply and display reprojection by currently selected reprojection mode
        context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, (int) ShaderPassId.Timewarp);
    }

    private void setCameraMatrices(bool previous, PropertySheet sheet, PostProcessRenderContext context)
    {
        if(previous)
        {
            sheet.properties.SetVector(ShaderIDs.PreviousCameraPosition, context.camera.transform.position);
            sheet.properties.SetMatrix(ShaderIDs.PreviousInvViewMatrix, context.camera.worldToCameraMatrix.inverse);
            sheet.properties.SetMatrix(ShaderIDs.PreviousInvProjectionMatrix, context.camera.projectionMatrix.inverse);
            sheet.properties.SetMatrix(ShaderIDs.PreviousProjectionViewMatrix, context.camera.nonJitteredProjectionMatrix * context.camera.worldToCameraMatrix);
        } else {
            sheet.properties.SetVector(ShaderIDs.CameraPosition, context.camera.transform.position);
            sheet.properties.SetMatrix(ShaderIDs.InvViewMatrix, context.camera.worldToCameraMatrix.inverse);
            sheet.properties.SetMatrix(ShaderIDs.InvProjectionMatrix, context.camera.projectionMatrix.inverse);
            sheet.properties.SetMatrix(ShaderIDs.ProjectionViewMatrix, context.camera.nonJitteredProjectionMatrix * context.camera.worldToCameraMatrix);
        }
    }
}
#endregion