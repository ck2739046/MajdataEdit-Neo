using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using MajdataEdit_Neo.Models;
using MajSimai;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace MajdataEdit_Neo.Controls;

class SimaiVisualizerControl : Control
{
    //Set the properties
    //The naming of this should be strictly followed "Xxx" and "XxxProperty"
    public static readonly DirectProperty<SimaiVisualizerControl, double> TimeProperty =
    AvaloniaProperty.RegisterDirect<SimaiVisualizerControl, double>(
        nameof(Time),
        o => o.Time,
        (o, v) => o.Time = v,
        defaultBindingMode: Avalonia.Data.BindingMode.OneWay);
    private double _time;
    public double Time
    {
        get { return _time; }
        set { SetAndRaise(TimeProperty, ref _time, value); }
    }

    public static readonly DirectProperty<SimaiVisualizerControl, TrackInfo?> TrackIfProperty =
    AvaloniaProperty.RegisterDirect<SimaiVisualizerControl, TrackInfo?>(
        nameof(TrackIf),
        o => o.TrackIf,
        (o, v) => o.TrackIf = v,
        defaultBindingMode: Avalonia.Data.BindingMode.OneWay);
    private TrackInfo? _track;
    public TrackInfo? TrackIf
    {
        get { return _track; }
        set { SetAndRaise(TrackIfProperty, ref _track, value); }
    }

    public static readonly DirectProperty<SimaiVisualizerControl, float> ZoomLevelProperty =
    AvaloniaProperty.RegisterDirect<SimaiVisualizerControl, float>(
        nameof(ZoomLevel),
        o => o.ZoomLevel,
        (o, v) => o.ZoomLevel = v,
        defaultBindingMode: Avalonia.Data.BindingMode.OneWay);
    private float _zoomLevel;
    public float ZoomLevel
    {
        get { return _zoomLevel; }
        set { SetAndRaise(ZoomLevelProperty, ref _zoomLevel, value); }
    }

    public static readonly DirectProperty<SimaiVisualizerControl, SimaiChart?> SimaiChartProperty =
    AvaloniaProperty.RegisterDirect<SimaiVisualizerControl, SimaiChart?>(
        nameof(SimaiChart),
        o => o.SimaiChart,
        (o, v) => o.SimaiChart = v,
        defaultBindingMode: Avalonia.Data.BindingMode.OneWay);
    private SimaiChart? _simaiChart;
    public SimaiChart? SimaiChart
    {
        get { return _simaiChart; }
        set { SetAndRaise(SimaiChartProperty, ref _simaiChart, value); }
    }

    public static readonly DirectProperty<SimaiVisualizerControl, List<(double, int, int)>?> SignaturesProperty =
    AvaloniaProperty.RegisterDirect<SimaiVisualizerControl, List<(double, int, int)>?>(
        nameof(Signatures),
        o => o.Signatures,
        (o, v) => o.Signatures = v,
        defaultBindingMode: Avalonia.Data.BindingMode.OneWay);
    private List<(double, int, int)>? _signatures;
    public List<(double, int, int)>? Signatures
    {
        get { return _signatures; }
        set { SetAndRaise(SignaturesProperty, ref _signatures, value); }
    }

    public static readonly DirectProperty<SimaiVisualizerControl, float> OffsetProperty =
   AvaloniaProperty.RegisterDirect<SimaiVisualizerControl, float>(
       nameof(Offset),
       o => o.Offset,
       (o, v) => o.Offset = v,
       defaultBindingMode: Avalonia.Data.BindingMode.OneWay);
    private float _offset;
    public float Offset
    {
        get { return _offset; }
        set { SetAndRaise(OffsetProperty, ref _offset, value); }
    }

    public static readonly DirectProperty<SimaiVisualizerControl, double> CaretTimeProperty =
    AvaloniaProperty.RegisterDirect<SimaiVisualizerControl, double>(
        nameof(CaretTime),
        o => o.CaretTime,
        (o, v) => o.CaretTime = v,
        defaultBindingMode: Avalonia.Data.BindingMode.OneWay);
    private double _caretTime;
    public double CaretTime
    {
        get { return _caretTime; }
        set { SetAndRaise(CaretTimeProperty, ref _caretTime, value); }
    }

    public static readonly DirectProperty<SimaiVisualizerControl, bool> IsAnimatedProperty =
    AvaloniaProperty.RegisterDirect<SimaiVisualizerControl, bool>(
        nameof(IsAnimated),
        o => o.IsAnimated,
        (o, v) => o.IsAnimated = v,
        defaultBindingMode: Avalonia.Data.BindingMode.OneWay);
    private bool _isAnimated;
    public bool IsAnimated
    {
        get { return _isAnimated; }
        set { SetAndRaise(IsAnimatedProperty, ref _isAnimated, value); }
    }

    public SimaiVisualizerControl()
    {
        ClipToBounds = true;

        AffectsRender<SimaiVisualizerControl>(TimeProperty, TrackIfProperty, ZoomLevelProperty, SimaiChartProperty, OffsetProperty, CaretTimeProperty);
    }
    class CustomDrawOp : ICustomDrawOperation
    {
        private readonly TrackInfo _trackInfo;
        private readonly SimaiChart _simaiChart;
        private readonly List<(double, int, int)> _signatures;
        private readonly double _time;
        private readonly double _caretTime;
        private readonly float _zoomLevel;
        private readonly float _offset;
        private static double _lastTime;
        private static double _lastZoom;
        private readonly bool _isAnimated;
        public static bool NeedsRender { get; set; }
        private static SimaiChart? _lastSimaiChart;

        // Cached resources to avoid per-frame allocations
        static readonly SKTypeface ConsolasBold = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold);
        static readonly SKFont TextFont = new(ConsolasBold, 12);

        static readonly SKPaint HanabiPaint = new()
        {
            Style = SKPaintStyle.Fill
        };

        // Note colors
        static readonly SKColor WaveformColor = new(0, 100, 0, 150);
        static readonly SKColor BpmLineColor = SKColors.Yellow;
        static readonly SKColor TimingTickColor = SKColors.White;

        static readonly SKColor TapColor = SKColors.LightPink;
        static readonly SKColor TouchColor = SKColors.DeepSkyBlue;
        static readonly SKColor SlideHeadColor = SKColors.DeepSkyBlue;
        static readonly SKColor SlideBodyColor = SKColors.SkyBlue;

        static readonly SKColor BreakColor = SKColors.OrangeRed;
        static readonly SKColor EachColor = SKColors.Gold;
        static readonly SKColor MineColor = new(160, 32, 240);
        static readonly SKColor MineBreakColor = new(220, 80, 160);
        static readonly SKColor MineSlideColor = new(160, 32, 240);
        static readonly SKColor HanabiColorStart = new(255, 0, 0, 100);
        static readonly SKColor HanabiColorEnd = new(255, 0, 0, 0);
        static readonly SKColor[] HanabiColors = [HanabiColorStart, HanabiColorEnd];

        static readonly float[] DashIntervals = [4, 4];
        static readonly SKPathEffect DashEffect = SKPathEffect.CreateDash(DashIntervals, 0);

        // TouchHold layer colors
        static readonly SKColor TouchHoldLayer1 = SKColors.Blue;
        static readonly SKColor TouchHoldLayer2 = SKColors.Green;
        static readonly SKColor TouchHoldLayer3 = SKColors.Yellow;
        static readonly SKColor TouchHoldLayer4 = SKColors.Orange;
        static readonly SKColor[] TouchHoldMineColors = [MineBreakColor, MineColor, MineColor, MineColor];
        static readonly SKColor[] TouchHoldNormalColors = [TouchHoldLayer1, TouchHoldLayer2, TouchHoldLayer3, TouchHoldLayer4];

        static readonly SKColor CaretColor = new(200, 0, 0, 200);
        static readonly SKPath CursorPath = new();
        static readonly SKColor GhostCursorColor = SKColors.Orange;

        // Cached lists reused across frames
        static readonly List<SKPoint> WavePoints = new(1024);
        static readonly List<double> BpmChangeTimes = new(32);
        static readonly List<float> BpmChangeValues = new(32);
        static readonly List<double> StrongBeat = new(64);
        static readonly List<double> WeakBeat = new(128);

        static CustomDrawOp()
        {
            CursorPath.MoveTo(-5, 0);
            CursorPath.LineTo(5, 0);
            CursorPath.LineTo(0, 8f);
            CursorPath.Close();
        }

        public CustomDrawOp(Rect bounds,
            TrackInfo trackInfo, double time, float zoomLevel, SimaiChart simaiChart, List<(double, int, int)> signatures,
            float offset, double caretTime, bool isAnimated)
        {
            _trackInfo = trackInfo;
            _time = time;
            _zoomLevel = zoomLevel;
            _simaiChart = simaiChart;
            _signatures = signatures;
            _offset = offset;
            _caretTime = caretTime;
            _isAnimated = isAnimated;
            Bounds = bounds;
        }
        public void Dispose() { }
        public Rect Bounds { get; }
        public bool HitTest(Point p) => true;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Render(ImmediateDrawingContext context)
        {
            if (_trackInfo is null) return;
            if (_simaiChart is null) return;
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature == null)
                Debug.WriteLine("SkiaSharp lease feature not available. Cannot render waveform.");
            else
            {
                using var lease = leaseFeature.Lease();
                var canvas = lease.SkCanvas;
                var paint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = WaveformColor,
                    //StrokeCap = SKStrokeCap.Round
                };
                canvas.Save();
                var width = Bounds.Width;
                var height = Bounds.Height;
                //Actuall Drawing here
                //make it smooth
                //TODO; Add Deltatime
                if (_isAnimated)
                {
                    _lastTime += 0.2 * (_time - _lastTime);

                }
                else
                {
                    _lastTime = _time;
                }

                _lastZoom += 0.2 * (_zoomLevel - _lastZoom);
                
                NeedsRender = _isAnimated && (Math.Abs(_time - _lastTime) > 0.005 || Math.Abs(_zoomLevel - _lastZoom) > 0.005);

                var waveLevels = _trackInfo.RawWave;
                if (_lastZoom > 3) waveLevels = _trackInfo.GetWaveThumbnails(2);
                if (_lastZoom > 2) waveLevels = _trackInfo.GetWaveThumbnails(1);
                if (_lastZoom > 1) waveLevels = _trackInfo.GetWaveThumbnails(0);
                var songLength = _trackInfo.Length;

                var currentTime = _lastTime;
                var step = songLength / waveLevels.Length;
                var deltatime = _lastZoom;

                var startindex = (int)((currentTime - deltatime) / step);
                var stopindex = (int)((currentTime + deltatime) / step);
                var linewidth = (float)(width / (stopindex - startindex));

                WavePoints.Clear();
                for (var i = startindex; i < stopindex; i++)
                {
                    if (i < 0) i = 0;
                    if (i >= waveLevels.Length - 1) break;

                    var x = (i - startindex) * linewidth;
                    var y = waveLevels[i] / 65535f * height + height / 2;

                    WavePoints.Add(new SKPoint((float)x, (float)y));
                }
                canvas.DrawPoints(SKPointMode.Polygon, WavePoints.ToArray(), paint);

                paint.IsAntialias = true;

                //Draw Bpm Lines
                if (!ReferenceEquals(_simaiChart, _lastSimaiChart))
                {
                    _lastSimaiChart = _simaiChart;
                    var lastbpm = -1f;
                    BpmChangeTimes.Clear();
                    BpmChangeValues.Clear();

                    //scan to get bpm change time and value
                    foreach (var timing in _simaiChart.CommaTimings)
                    {
                        if (timing.Bpm != lastbpm)
                        {
                            BpmChangeTimes.Add(timing.Timing + _offset);
                            BpmChangeValues.Add(timing.Bpm);
                            lastbpm = timing.Bpm;
                        }
                    }
                    BpmChangeTimes.Add(_trackInfo.Length);

                    double timeBeats = BpmChangeTimes.Count > 0 ? BpmChangeTimes[0] : 0;
                    var signatureNum = 4; // Time signature
                    var signatureDeno = 4; // Time signature
                    var currentBeat = 1;
                    double timePerBeat;
                    StrongBeat.Clear();
                    WeakBeat.Clear();

                    for (var i = 1; i < BpmChangeTimes.Count; i++)
                    {
                        while (timeBeats < BpmChangeTimes[i] - 0.05)
                        {
                            var sig = default((double, int, int));
                            for (var s = _signatures.Count - 1; s >= 0; s--)
                            {
                                if (timeBeats > _signatures[s].Item1 - 0.05)
                                {
                                    sig = _signatures[s];
                                    break;
                                }
                            }
                            if (sig != default)
                            {
                                signatureNum = sig.Item2;
                                signatureDeno = sig.Item3;
                            }

                            if (currentBeat > signatureNum) currentBeat = 1;
                            timePerBeat = 60.0 / BpmChangeValues[i - 1] * 4 / signatureDeno;

                            if (currentBeat == 1)
                                StrongBeat.Add(timeBeats);
                            else
                                WeakBeat.Add(timeBeats);

                            currentBeat++;
                            timeBeats += timePerBeat;
                        }
                        timeBeats = BpmChangeTimes[i];
                        currentBeat = 1;
                    }
                }

                double time = BpmChangeTimes.Count > 0 ? BpmChangeTimes[0] : 0;
                paint.Color = BpmLineColor;
                paint.StrokeWidth = 1;

                for (var i = 1; i < BpmChangeTimes.Count; i++)
                {
                    time = BpmChangeTimes[i-1];
                    if (time - currentTime > deltatime) continue;
                    var x = ((float)(time / step) - startindex) * linewidth;
                    canvas.DrawText(BpmChangeValues[i - 1].ToString(), (float)x + 3f, 10, TextFont, paint);
                }

                foreach (var btime in StrongBeat)
                {
                    if (btime - currentTime < -deltatime) continue;
                    if (btime - currentTime > deltatime) break;
                    var x = ((float)(btime / step) - startindex) * linewidth;
                    canvas.DrawLine((float)x, 0, (float)x, (float)height, paint);
                }

                foreach (var btime in WeakBeat)
                {
                    if (btime - currentTime < -deltatime) continue;
                    if (btime - currentTime > deltatime) break;
                    var x = ((float)(btime / step) - startindex) * linewidth;
                    canvas.DrawLine((float)x, 0, (float)x, 10, paint);
                }

                //timing white line
                paint.Color = TimingTickColor;
                foreach (var note in _simaiChart.CommaTimings)
                {
                    time = note.Timing + _offset;
                    if (time - currentTime < -deltatime) continue;
                    if (time - currentTime > deltatime) break;
                    var x = ((float)(time / step) - startindex) * linewidth;
                    canvas.DrawLine((float)x, (float)height - 10, (float)x, (float)height, paint);
                }

                paint.Color = CaretColor;
                paint.StrokeWidth = 2;
                canvas.DrawLine((float)width / 2, 15, (float)width / 2, (float)height - 15, paint);

                paint.Style = SKPaintStyle.Stroke;
                // Draw notes
                foreach (var note in _simaiChart.NoteTimings)
                {
                    time = note.Timing + _offset;
                    if (time - currentTime < -deltatime - 10.0) continue;
                    if (time - currentTime > deltatime) break;
                    var notes = note.Notes;

                    // manual count non-slide-head notes
                    var nonSlideHeadCount = 0;
                    foreach (var n in notes)
                        if (!n.IsSlideNoHead) nonSlideHeadCount++;
                    var isEach = nonSlideHeadCount > 1;

                    // manual count slide notes
                    var slideCount = 0;
                    foreach (var n in notes)
                        if (n.Type == SimaiNoteType.Slide) slideCount++;

                    var x = (float)(((float)(time / step) - startindex) * linewidth);

                    foreach (var noteD in notes)
                    {
                        var seprate = (height - 30f) / 8f;
                        var y = (float)(noteD.StartPosition * seprate + 10f);

                        if (noteD.IsHanabi)
                        {
                            var xDeltaHanabi = (float)(1f / step) * linewidth; // Hanabi is 1s due to frame analyze
                            var rectangleF = new SKRect(x, 0, x + xDeltaHanabi, (float)height);

                            if (noteD.Type == SimaiNoteType.TouchHold)
                                rectangleF.Left += (float)(noteD.HoldTime / step) * linewidth;

                            HanabiPaint.Shader = SKShader.CreateLinearGradient(
                                new SKPoint(rectangleF.Left, rectangleF.Top),
                                new SKPoint(rectangleF.Right, rectangleF.Top),
                                HanabiColors,
                                null,
                                SKShaderTileMode.Clamp
                            );
                            canvas.DrawRect(rectangleF, HanabiPaint);
                        }

                        switch (noteD.Type)
                        {
                            case SimaiNoteType.Tap:
                                paint.StrokeWidth = noteD.IsForceStar ? 3 : 2;
                                paint.Color = noteD.IsMine ? (noteD.IsBreak ? MineBreakColor : MineColor) :
                                              noteD.IsBreak ? BreakColor :
                                              isEach ? EachColor :
                                              TapColor;

                                if (noteD.IsForceStar)
                                {
                                    canvas.DrawText("*", x - 7f, y - 7f, TextFont, paint);
                                }
                                else
                                {
                                    canvas.DrawOval(x, y, 3.5f, 3.5f, paint);
                                }
                                break;

                            case SimaiNoteType.Touch:
                                paint.StrokeWidth = 2;
                                paint.Color = noteD.IsMine ? (noteD.IsBreak ? MineBreakColor : MineColor) :
                                              isEach ? EachColor : TouchColor;
                                canvas.DrawRect(x - 2.5f, y - 2.5f, 7, 7, paint);
                                break;

                            case SimaiNoteType.Hold:
                                paint.StrokeWidth = 3.5f;
                                paint.Color = noteD.IsMine ? (noteD.IsBreak ? MineBreakColor : MineColor) :
                                              noteD.IsBreak ? BreakColor :
                                              isEach ? EachColor :
                                              TapColor;

                                var xRight = (float)(x + (noteD.HoldTime / step) * linewidth);
                                if (!float.IsNormal(xRight)) xRight = ushort.MaxValue;
                                if (xRight - x < 1f) xRight = x + 5;
                                canvas.DrawLine(x, y, xRight, y, paint);
                                break;

                            case SimaiNoteType.TouchHold:
                                paint.StrokeWidth = 3.5f;
                                var xDelta = (float)(noteD.HoldTime / step) * linewidth / 4f;
                                if (!float.IsNormal(xDelta)) xDelta = ushort.MaxValue;
                                if (xDelta < 1f) xDelta = 1;

                                var touchHoldColors = noteD.IsMine ? TouchHoldMineColors : TouchHoldNormalColors;
                                for (var j = 0; j < 4; j++)
                                {
                                    paint.Color = touchHoldColors[j];
                                    canvas.DrawLine(x, y, x + xDelta * (4 - j), y, paint);
                                }
                                break;

                            case SimaiNoteType.Slide:
                                paint.StrokeWidth = 1.5f;

                                if (!noteD.IsSlideNoHead)
                                {
                                    paint.Color = noteD.IsMine ? (noteD.IsBreak ? MineBreakColor : MineColor) :
                                                  noteD.IsBreak ? BreakColor :
                                                  isEach ? EachColor :
                                                  SlideHeadColor;
                                    var rad = 5f;
                                    var rad2 = rad * 1.414f / 2f;
                                    canvas.DrawLine(x - rad2, y - rad2, x + rad2, y + rad2, paint);
                                    canvas.DrawLine(x + rad2, y - rad2, x - rad2, y + rad2, paint);
                                    canvas.DrawLine(x, y - rad, x, y + rad, paint);
                                    canvas.DrawLine(x - rad, y, x + rad, y, paint);
                                }

                                paint.StrokeWidth = 3.5f;
                                paint.Color = noteD.IsMineSlide ? MineSlideColor :
                                              noteD.IsSlideBreak ? BreakColor :
                                              slideCount >= 2 ? EachColor :
                                              SlideBodyColor;
                                paint.PathEffect = DashEffect;
                                var xSlide = (float)((noteD.SlideStartTime + _offset) / step - startindex) * linewidth;
                                var xSlideRight = (float)(noteD.SlideTime / step) * linewidth + xSlide;

                                if (!float.IsNormal(xSlideRight)) xSlideRight = ushort.MaxValue;
                                if (!float.IsNormal(xSlide)) xSlide = ushort.MaxValue;

                                canvas.DrawLine(xSlide, y, xSlideRight, y, paint);
                                paint.PathEffect = null;
                                break;
                        }
                    }
                }

                time = _caretTime + _offset;
                if (time - currentTime <= deltatime)
                {
                    //Draw ghost cusor
                    paint.Color = GhostCursorColor;
                    paint.Style = SKPaintStyle.Fill;
                    var x2 = (float)(time / step - startindex) * linewidth;
                    CursorPath.Transform(SKMatrix.CreateTranslation(x2, 0));
                    canvas.DrawPath(CursorPath, paint);
                    CursorPath.Transform(SKMatrix.CreateTranslation(-x2, 0));
                }

                canvas.Restore();
            }
        }
    }
    public override void Render(DrawingContext context)
    {
        if (TrackIf == null || SimaiChart == null) return;
        
        context.Custom(new CustomDrawOp(new Rect(0, 0, Bounds.Width, Bounds.Height),
            TrackIf, Time, ZoomLevel, SimaiChart, Signatures, Offset, CaretTime, IsAnimated));
        
        if (CustomDrawOp.NeedsRender)
        {
            Dispatcher.UIThread.InvokeAsync(InvalidateVisual, DispatcherPriority.Background);
        }
    }
}
