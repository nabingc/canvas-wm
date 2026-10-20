using CanvasWm.Core.Geometry;

// SPEC-ASSUMPTION A1: namespace CanvasWm.Core, file at the project root.
namespace CanvasWm.Core;

/// <summary>Internal tuning constants: animation lengths, gesture thresholds, drop bands,
/// size floors, capture rates and robustness timeouts. These are not user settings; those are in
/// the Engine's policy record. Immutable: change single values with a with-expression.
/// The defaults are the values observed in the studied app on macOS. Compiler-generated value
/// equality.</summary>
public sealed record Tuning
{
    private readonly double _cameraMoveSeconds = 0.5;
    private readonly double _cameraEaseRate = 5.5;
    private readonly double _cameraMaxStepSeconds = 0.032;
    private readonly double _layoutTweenSeconds = 0.46;
    private readonly double _dragStartDistance = 5.0;
    private readonly double _pinchStartDelta = 0.04;
    private readonly double _zoomStepDelta = 0.15;
    private readonly double _touchPinchStartDistance = 0.06;
    private readonly double _touchPanStartDistance = 0.03;
    private readonly double _touchPanFactor = 1.3;
    private readonly double _pickupSeconds = 0.28;
    private readonly double _tileEdgeBandFraction = 0.28;
    private readonly double _desktopEdgeBandMax = 64.0;
    private readonly double _seamHitMinWidth = 8.0;
    private readonly double _gapHandleHitSlop = 5.0;
    private readonly double _gapDragRestSeconds = 0.15;
    private readonly double _displayHandOffDistance = 16.0;
    private readonly double _groupHoverSeconds = 0.2;
    private readonly double _groupHoverSlop = 6.0;
    private readonly Size _fixedTileMinimum = new Size(480, 320);
    private readonly Size _tileMinimumFloor = new Size(200, 120);
    private readonly Size _compactSize = new Size(380, 260);
    private readonly double _compactGrabHeight = 18.0;
    private readonly double _squeezeFloor = 120.0;
    private readonly double _groupCollapseBelow = 120.0;
    private readonly int _collapsedOutlineWidth = 2;
    // SPEC-ASSUMPTION A4: ratio default written as a C# division.
    private readonly double _floatMaxFraction = 2.0 / 3.0;
    private readonly double _floatEdgePadding = 16.0;
    private readonly double _tabStripContentHeight = 20.0;
    private readonly double _tabStripUnderlap = 10.0;
    private readonly double _tabStripButtonReserve = 72.0;
    private readonly double _nativeTabWatchSeconds = 0.25;
    private readonly double _nativeTabRecheckSeconds = 0.3;
    private readonly double _overviewTopMargin = 28.0;
    private readonly double _overviewBottomMargin = 96.0;
    private readonly double _changingPicturesPerSecond = 10.0;
    private readonly double _movingPicturesPerSecond = 3.0;
    private readonly double _livePicturesPerSecond = 0.2;
    // SPEC-ASSUMPTION A4: ratio default written as a C# division.
    private readonly double _previewPicturesPerSecond = 1.0 / 6.0;
    // SPEC-ASSUMPTION A4: ratio default written as a C# division.
    private readonly double _unseenPicturesPerSecond = 1.0 / 30.0;
    // SPEC-ASSUMPTION A4: ratio default written as a C# division.
    private readonly double _awayPicturesPerSecond = 1.0 / 60.0;
    private readonly double _coverAspectMismatch = 0.01;
    private readonly double _uncoverAspectMismatch = 0.005;
    private readonly double _reflowTimeoutSeconds = 1.2;
    private readonly double _landingRevealQuietSeconds = 0.6;
    private readonly double _freshPictureWaitSeconds = 0.35;
    private readonly double _frameRefusalSlack = 24.0;
    private readonly double _frameRefusalRecheckSeconds = 0.25;
    private readonly int _snapBackLimit = 3;
    private readonly double _snapBackWindowSeconds = 3.0;

    /// <summary>The default tuning. Equal to new Tuning(). The same instance every time.</summary>
    public static Tuning Default { get; } = new();

    /// <summary>Length of one camera move, seconds. Default 0.5.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double CameraMoveSeconds
    {
        get => _cameraMoveSeconds;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value > 0)) throw new ArgumentOutOfRangeException(nameof(CameraMoveSeconds), value, "Must be finite and > 0.");
            _cameraMoveSeconds = value;
        }
    }

    /// <summary>Ease-out steepness of a camera move (exponential rate, per normalised time unit).
    /// Default 5.5.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double CameraEaseRate
    {
        get => _cameraEaseRate;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value > 0)) throw new ArgumentOutOfRangeException(nameof(CameraEaseRate), value, "Must be finite and > 0.");
            _cameraEaseRate = value;
        }
    }

    /// <summary>Largest time step one animation frame may advance the camera, seconds.
    /// Default 0.032.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double CameraMaxStepSeconds
    {
        get => _cameraMaxStepSeconds;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value > 0)) throw new ArgumentOutOfRangeException(nameof(CameraMaxStepSeconds), value, "Must be finite and > 0.");
            _cameraMaxStepSeconds = value;
        }
    }

    /// <summary>Length of a layout tween (tiles sliding to new frames), seconds. Default 0.46.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double LayoutTweenSeconds
    {
        get => _layoutTweenSeconds;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value > 0)) throw new ArgumentOutOfRangeException(nameof(LayoutTweenSeconds), value, "Must be finite and > 0.");
            _layoutTweenSeconds = value;
        }
    }

    /// <summary>Pointer travel before a press becomes a drag, DIPs. Default 5.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double DragStartDistance
    {
        get => _dragStartDistance;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(DragStartDistance), value, "Must be finite and >= 0.");
            _dragStartDistance = value;
        }
    }

    /// <summary>Pinch magnitude (zoom-factor delta) before a pinch starts zooming. Default 0.04.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double PinchStartDelta
    {
        get => _pinchStartDelta;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(PinchStartDelta), value, "Must be finite and >= 0.");
            _pinchStartDelta = value;
        }
    }

    /// <summary>Zoom-factor change per discrete zoom step. Default 0.15.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double ZoomStepDelta
    {
        get => _zoomStepDelta;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value > 0)) throw new ArgumentOutOfRangeException(nameof(ZoomStepDelta), value, "Must be finite and > 0.");
            _zoomStepDelta = value;
        }
    }

    /// <summary>Two-finger spread before a raw touch pinch is recognised, in normalised touchpad
    /// units. Default 0.06. Tuned for macOS raw trackpad touches; Windows touchpad input uses
    /// different units and needs calibration.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double TouchPinchStartDistance
    {
        get => _touchPinchStartDistance;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            // SPEC-ASSUMPTION A7: macOS-specific value kept for parity.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(TouchPinchStartDistance), value, "Must be finite and >= 0.");
            _touchPinchStartDistance = value;
        }
    }

    /// <summary>Two-finger travel before a raw touch pan is recognised, in normalised touchpad
    /// units. Default 0.03. macOS-specific (see TouchPinchStartDistance).</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double TouchPanStartDistance
    {
        get => _touchPanStartDistance;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            // SPEC-ASSUMPTION A7: macOS-specific value kept for parity.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(TouchPanStartDistance), value, "Must be finite and >= 0.");
            _touchPanStartDistance = value;
        }
    }

    /// <summary>Multiplier from touch travel to camera pan (factor). Default 1.3.
    /// macOS-specific (see TouchPinchStartDistance).</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double TouchPanFactor
    {
        get => _touchPanFactor;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            // SPEC-ASSUMPTION A7: macOS-specific value kept for parity.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(TouchPanFactor), value, "Must be finite and >= 0.");
            _touchPanFactor = value;
        }
    }

    /// <summary>Length of the lift animation when a window is picked up, seconds. Default 0.28.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double PickupSeconds
    {
        get => _pickupSeconds;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(PickupSeconds), value, "Must be finite and >= 0.");
            _pickupSeconds = value;
        }
    }

    /// <summary>Fraction of a tile, measured from each edge, that counts as the
    /// "insert beside on this edge" drop zone. Default 0.28.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double TileEdgeBandFraction
    {
        get => _tileEdgeBandFraction;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!(value >= 0 && value <= 0.5)) throw new ArgumentOutOfRangeException(nameof(TileEdgeBandFraction), value, "Must be in [0, 0.5].");
            _tileEdgeBandFraction = value;
        }
    }

    /// <summary>Cap on the desktop-edge drop band, DIPs. The band is the policy's desktop-edge
    /// fraction times the stage extent, capped at this value. Default 64.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double DesktopEdgeBandMax
    {
        get => _desktopEdgeBandMax;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(DesktopEdgeBandMax), value, "Must be finite and >= 0.");
            _desktopEdgeBandMax = value;
        }
    }

    /// <summary>Minimum grab width of a split seam for resize hit-testing, DIPs. Default 8.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double SeamHitMinWidth
    {
        get => _seamHitMinWidth;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(SeamHitMinWidth), value, "Must be finite and >= 0.");
            _seamHitMinWidth = value;
        }
    }

    /// <summary>Extra hit-test reach around a gap handle, DIPs. Default 5.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double GapHandleHitSlop
    {
        get => _gapHandleHitSlop;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(GapHandleHitSlop), value, "Must be finite and >= 0.");
            _gapHandleHitSlop = value;
        }
    }

    /// <summary>Pause after a seam drag before it counts as at rest, seconds. Default 0.15.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double GapDragRestSeconds
    {
        get => _gapDragRestSeconds;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(GapDragRestSeconds), value, "Must be finite and >= 0.");
            _gapDragRestSeconds = value;
        }
    }

    /// <summary>Distance from a display edge within which a dragged window is handed to the
    /// neighbouring display, DIPs. Default 16.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double DisplayHandOffDistance
    {
        get => _displayHandOffDistance;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(DisplayHandOffDistance), value, "Must be finite and >= 0.");
            _displayHandOffDistance = value;
        }
    }

    /// <summary>Hover time over a window before a dragged window joins it as a tab group,
    /// seconds. Default 0.2.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double GroupHoverSeconds
    {
        get => _groupHoverSeconds;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(GroupHoverSeconds), value, "Must be finite and >= 0.");
            _groupHoverSeconds = value;
        }
    }

    /// <summary>Pointer movement allowed during that hover without restarting it, DIPs.
    /// Default 6.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double GroupHoverSlop
    {
        get => _groupHoverSlop;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(GroupHoverSlop), value, "Must be finite and >= 0.");
            _groupHoverSlop = value;
        }
    }

    /// <summary>Tile minimum used when the policy's fixed-minimum switch is on, DIPs.
    /// Default 480 x 320.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public Size FixedTileMinimum
    {
        get => _fixedTileMinimum;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            // SPEC-ASSUMPTION A3: kept as a size property; named to avoid colliding with the policy switch.
            if (!double.IsFinite(value.Width) || !double.IsFinite(value.Height) || !(value.Width >= 0) || !(value.Height >= 0)) throw new ArgumentOutOfRangeException(nameof(FixedTileMinimum), value, "Width and Height must be finite and >= 0.");
            _fixedTileMinimum = value;
        }
    }

    /// <summary>Tile minimum floor used when the fixed-minimum switch is off, DIPs.
    /// Default 200 x 120.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public Size TileMinimumFloor
    {
        get => _tileMinimumFloor;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value.Width) || !double.IsFinite(value.Height) || !(value.Width >= 0) || !(value.Height >= 0)) throw new ArgumentOutOfRangeException(nameof(TileMinimumFloor), value, "Width and Height must be finite and >= 0.");
            _tileMinimumFloor = value;
        }
    }

    /// <summary>Compact presentation size; a lifted window is scaled toward it while moved,
    /// DIPs. Default 380 x 260.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public Size CompactSize
    {
        get => _compactSize;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value.Width) || !double.IsFinite(value.Height) || !(value.Width >= 0) || !(value.Height >= 0)) throw new ArgumentOutOfRangeException(nameof(CompactSize), value, "Width and Height must be finite and >= 0.");
            _compactSize = value;
        }
    }

    /// <summary>Height of the grab area in compact presentation, DIPs. Default 18.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double CompactGrabHeight
    {
        get => _compactGrabHeight;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(CompactGrabHeight), value, "Must be finite and >= 0.");
            _compactGrabHeight = value;
        }
    }

    /// <summary>Smallest extent a tile may be squeezed to when squeezing below minimums is
    /// allowed, DIPs. Default 120.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double SqueezeFloor
    {
        get => _squeezeFloor;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(SqueezeFloor), value, "Must be finite and >= 0.");
            _squeezeFloor = value;
        }
    }

    /// <summary>A group whose tile is below this extent collapses to an outline (when the policy
    /// collapses small groups), DIPs. Default 120.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double GroupCollapseBelow
    {
        get => _groupCollapseBelow;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(GroupCollapseBelow), value, "Must be finite and >= 0.");
            _groupCollapseBelow = value;
        }
    }

    /// <summary>Stroke width of a collapsed group's outline, whole DIPs. Default 2.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public int CollapsedOutlineWidth
    {
        get => _collapsedOutlineWidth;
        init
        {
            // SPEC-ASSUMPTION A11: integer members stay int; A5: range is a CanvasWM choice.
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(CollapsedOutlineWidth), value, "Must be >= 0.");
            _collapsedOutlineWidth = value;
        }
    }

    /// <summary>Largest fraction of the desktop a floating window may take. Default 2/3.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double FloatMaxFraction
    {
        get => _floatMaxFraction;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!(value > 0 && value <= 1)) throw new ArgumentOutOfRangeException(nameof(FloatMaxFraction), value, "Must be in (0, 1].");
            _floatMaxFraction = value;
        }
    }

    /// <summary>Inset kept between a floating window and the desktop edge, DIPs. Default 16.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double FloatEdgePadding
    {
        get => _floatEdgePadding;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(FloatEdgePadding), value, "Must be finite and >= 0.");
            _floatEdgePadding = value;
        }
    }

    /// <summary>Content height of a tab strip, DIPs. Default 20.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double TabStripContentHeight
    {
        get => _tabStripContentHeight;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(TabStripContentHeight), value, "Must be finite and >= 0.");
            _tabStripContentHeight = value;
        }
    }

    /// <summary>How far a tab strip tucks under the window's top edge, DIPs. Default 10.
    /// Measured against a macOS title bar; may need retuning on Windows.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double TabStripUnderlap
    {
        get => _tabStripUnderlap;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            // SPEC-ASSUMPTION A7: macOS-specific value kept for parity.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(TabStripUnderlap), value, "Must be finite and >= 0.");
            _tabStripUnderlap = value;
        }
    }

    /// <summary>Space reserved in a tab strip for the window's title-bar buttons, DIPs.
    /// Default 72. On macOS the buttons are on the leading (left) side; Windows caption buttons
    /// are on the right.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double TabStripButtonReserve
    {
        get => _tabStripButtonReserve;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            // SPEC-ASSUMPTION A7: macOS-specific value kept for parity.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(TabStripButtonReserve), value, "Must be finite and >= 0.");
            _tabStripButtonReserve = value;
        }
    }

    /// <summary>Period of the native-tab watcher, seconds. Default 0.25. macOS-specific: Windows
    /// has no native window tabs.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double NativeTabWatchSeconds
    {
        get => _nativeTabWatchSeconds;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            // SPEC-ASSUMPTION A7: macOS-specific value kept for parity.
            if (!double.IsFinite(value) || !(value > 0)) throw new ArgumentOutOfRangeException(nameof(NativeTabWatchSeconds), value, "Must be finite and > 0.");
            _nativeTabWatchSeconds = value;
        }
    }

    /// <summary>Delay before native tab groups are re-checked after a window is added, removed or
    /// focused, seconds. Default 0.3. macOS-specific.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double NativeTabRecheckSeconds
    {
        get => _nativeTabRecheckSeconds;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            // SPEC-ASSUMPTION A7: macOS-specific value kept for parity.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(NativeTabRecheckSeconds), value, "Must be finite and >= 0.");
            _nativeTabRecheckSeconds = value;
        }
    }

    /// <summary>Top margin of the overview layout, DIPs. Default 28.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double OverviewTopMargin
    {
        get => _overviewTopMargin;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(OverviewTopMargin), value, "Must be finite and >= 0.");
            _overviewTopMargin = value;
        }
    }

    /// <summary>Bottom margin of the overview layout, DIPs. Default 96.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double OverviewBottomMargin
    {
        get => _overviewBottomMargin;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(OverviewBottomMargin), value, "Must be finite and >= 0.");
            _overviewBottomMargin = value;
        }
    }

    /// <summary>Rate for a window whose picture is actively changing. Default 10.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double ChangingPicturesPerSecond
    {
        get => _changingPicturesPerSecond;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value > 0)) throw new ArgumentOutOfRangeException(nameof(ChangingPicturesPerSecond), value, "Must be finite and > 0.");
            _changingPicturesPerSecond = value;
        }
    }

    /// <summary>Rate while the camera or stage is moving. Default 3.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double MovingPicturesPerSecond
    {
        get => _movingPicturesPerSecond;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value > 0)) throw new ArgumentOutOfRangeException(nameof(MovingPicturesPerSecond), value, "Must be finite and > 0.");
            _movingPicturesPerSecond = value;
        }
    }

    /// <summary>At-rest rate for a visible window on the stage. Default 0.2 (one per 5 s).</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double LivePicturesPerSecond
    {
        get => _livePicturesPerSecond;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value > 0)) throw new ArgumentOutOfRangeException(nameof(LivePicturesPerSecond), value, "Must be finite and > 0.");
            _livePicturesPerSecond = value;
        }
    }

    /// <summary>At-rest rate for a window shown only as a preview. Default 1/6 (one per 6 s).</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double PreviewPicturesPerSecond
    {
        get => _previewPicturesPerSecond;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value > 0)) throw new ArgumentOutOfRangeException(nameof(PreviewPicturesPerSecond), value, "Must be finite and > 0.");
            _previewPicturesPerSecond = value;
        }
    }

    /// <summary>At-rest rate for a window not currently seen. Default 1/30 (one per 30 s).</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double UnseenPicturesPerSecond
    {
        get => _unseenPicturesPerSecond;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value > 0)) throw new ArgumentOutOfRangeException(nameof(UnseenPicturesPerSecond), value, "Must be finite and > 0.");
            _unseenPicturesPerSecond = value;
        }
    }

    /// <summary>At-rest rate for a window far away. Default 1/60 (one per minute).</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double AwayPicturesPerSecond
    {
        get => _awayPicturesPerSecond;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value > 0)) throw new ArgumentOutOfRangeException(nameof(AwayPicturesPerSecond), value, "Must be finite and > 0.");
            _awayPicturesPerSecond = value;
        }
    }

    /// <summary>A cover is shown when a window's aspect ratio differs from its target's by more than
    /// this fraction. Default 0.01.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double CoverAspectMismatch
    {
        get => _coverAspectMismatch;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(CoverAspectMismatch), value, "Must be finite and >= 0.");
            _coverAspectMismatch = value;
        }
    }

    /// <summary>The cover is removed once the mismatch is within this fraction (hysteresis).
    /// Default 0.005.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double UncoverAspectMismatch
    {
        get => _uncoverAspectMismatch;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(UncoverAspectMismatch), value, "Must be finite and >= 0.");
            _uncoverAspectMismatch = value;
        }
    }

    /// <summary>Give-up time for reflow: covers are dropped once shapes have rested longer than
    /// this, and float settling re-checks after it, seconds. Default 1.2.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double ReflowTimeoutSeconds
    {
        get => _reflowTimeoutSeconds;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(ReflowTimeoutSeconds), value, "Must be finite and >= 0.");
            _reflowTimeoutSeconds = value;
        }
    }

    /// <summary>After a landing focuses a window, focus-driven reveal reactions are suppressed for
    /// this long, seconds. Default 0.6.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double LandingRevealQuietSeconds
    {
        get => _landingRevealQuietSeconds;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(LandingRevealQuietSeconds), value, "Must be finite and >= 0.");
            _landingRevealQuietSeconds = value;
        }
    }

    /// <summary>Longest wait for fresh pictures after real-window placement finished, seconds.
    /// Default 0.35.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double FreshPictureWaitSeconds
    {
        get => _freshPictureWaitSeconds;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(FreshPictureWaitSeconds), value, "Must be finite and >= 0.");
            _freshPictureWaitSeconds = value;
        }
    }

    /// <summary>Tolerance when comparing a written frame with the frame the app actually took,
    /// DIPs. Default 24.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double FrameRefusalSlack
    {
        get => _frameRefusalSlack;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(FrameRefusalSlack), value, "Must be finite and >= 0.");
            _frameRefusalSlack = value;
        }
    }

    /// <summary>Delay before re-reading a possibly refused frame, seconds. Default 0.25.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double FrameRefusalRecheckSeconds
    {
        get => _frameRefusalRecheckSeconds;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value >= 0)) throw new ArgumentOutOfRangeException(nameof(FrameRefusalRecheckSeconds), value, "Must be finite and >= 0.");
            _frameRefusalRecheckSeconds = value;
        }
    }

    /// <summary>Number of snap-backs within SnapBackWindowSeconds after which the engine accepts
    /// the app's own size. Default 3.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public int SnapBackLimit
    {
        get => _snapBackLimit;
        init
        {
            // SPEC-ASSUMPTION A11: integer members stay int; A5: range is a CanvasWM choice.
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(SnapBackLimit), value, "Must be >= 1.");
            _snapBackLimit = value;
        }
    }

    /// <summary>Time window over which snap-backs are counted, seconds. Default 3.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is out of range or not finite.</exception>
    public double SnapBackWindowSeconds
    {
        get => _snapBackWindowSeconds;
        init
        {
            // SPEC-ASSUMPTION A5: validation ranges are CanvasWM choices.
            if (!double.IsFinite(value) || !(value > 0)) throw new ArgumentOutOfRangeException(nameof(SnapBackWindowSeconds), value, "Must be finite and > 0.");
            _snapBackWindowSeconds = value;
        }
    }
}
