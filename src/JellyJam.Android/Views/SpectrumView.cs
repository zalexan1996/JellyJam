using System.Diagnostics;
using JellyJam.Core.Audio;

namespace JellyJam.Mobile.Views;

public sealed class SpectrumView : GraphicsView
{
    public static readonly BindableProperty IsSpectrumEnabledProperty = BindableProperty.Create(
        nameof(IsSpectrumEnabled), typeof(bool), typeof(SpectrumView), false,
        propertyChanged: (bindable, _, _) => ((SpectrumView)bindable).UpdateActivity());

    private readonly SpectrumBands bands = new();
    private readonly SpectrumDrawing drawing;
    private AndroidSpectrumSource? source;
    private IDispatcherTimer? timer;
    private bool foreground;
    private long lastFrame;
    private int revision;

    public SpectrumView()
    {
        drawing = new SpectrumDrawing(bands);
        Drawable = drawing;
        InputTransparent = true;
        HeightRequest = 100;
        SemanticProperties.SetDescription(this, "Music frequency spectrum");
    }

    public bool IsSpectrumEnabled
    {
        get => (bool)GetValue(IsSpectrumEnabledProperty);
        set => SetValue(IsSpectrumEnabledProperty, value);
    }

    public void Connect(AndroidSpectrumSource value)
    {
        source = value;
        UpdateActivity();
    }

    public void SetForeground(bool value)
    {
        foreground = value;
        UpdateActivity();
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == nameof(IsVisible) && drawing is not null) UpdateActivity();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        UpdateActivity();
    }

    private void UpdateActivity()
    {
        var active = Handler is not null && foreground && IsVisible && IsSpectrumEnabled;
        source?.SetVisible(active);
        timer?.Stop();
        bands.Reset();
        drawing.Status = source?.Status ?? "OFF";
        Invalidate();
        if (!active) return;
        if (timer is null)
        {
            timer = Dispatcher.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(50);
            timer.Tick += OnFrame;
        }
        lastFrame = Stopwatch.GetTimestamp();
        revision = source?.Revision ?? 0;
        timer.Start();
    }

    private void OnFrame(object? sender, EventArgs args)
    {
        if (source is null) return;
        var reset = revision != source.Revision;
        if (reset)
        {
            bands.Reset();
            revision = source.Revision;
        }
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(lastFrame, now);
        lastFrame = now;
        var changed = bands.Update(source.ReadFft(), source.SamplingRateMillihertz, elapsed);
        var stateChanged = drawing.Status != source.Status;
        drawing.Status = source.Status;
        if (changed || stateChanged || reset) Invalidate();
    }

    private sealed class SpectrumDrawing(SpectrumBands bands) : IDrawable
    {
        private static readonly Color Signal = Color.FromArgb("#71F7F3");
        private static readonly Color Accent = Color.FromArgb("#FF69D4");
        private static readonly Color Grid = Color.FromArgb("#203238");
        private static readonly Color Muted = Color.FromArgb("#9AAEB5");
        private static readonly Color Hot = Color.FromArgb("#FFC36A");
        private static readonly string[] Frequencies = ["60", "250", "1k", "4k", "16k Hz"];
        public string Status { get; set; } = "OFF";

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (dirtyRect.Width < 40 || dirtyRect.Height < 40) return;
            var width = dirtyRect.Width;
            var top = 17f;
            var bottom = dirtyRect.Height - 17;
            var height = bottom - top;
            canvas.SaveState();
            canvas.Antialias = true;
            canvas.Font = new Microsoft.Maui.Graphics.Font("SpaceMono");
            canvas.FontSize = 8;
            canvas.FontColor = Status == "LIVE" ? Signal : Status == "UNAVAILABLE" ? Hot : Muted;
            canvas.DrawString(Status, 0, 0, width, 12, HorizontalAlignment.Right, VerticalAlignment.Top);
            canvas.StrokeSize = 0.5f;
            canvas.StrokeColor = Grid;
            for (var row = 0; row <= 4; row++)
                canvas.DrawLine(0, top + height * row / 4, width, top + height * row / 4);
            for (var column = 0; column <= 4; column++)
                canvas.DrawLine(width * column / 4, top, width * column / 4, bottom);

            var step = width / SpectrumBands.Count;
            var barWidth = Math.Max(1, step - 2);
            var levels = bands.Levels;
            var peaks = bands.Peaks;
            for (var band = 0; band < SpectrumBands.Count; band++)
            {
                var left = band * step + 1;
                var barHeight = height * levels[band];
                if (barHeight >= 0.5f)
                {
                    canvas.FillColor = levels[band] > 0.9f ? Hot : Signal;
                    canvas.FillRectangle(left, bottom - barHeight, barWidth, barHeight);
                }
                if (peaks[band] > 0.005f)
                {
                    canvas.FillColor = Accent;
                    canvas.FillRectangle(left, bottom - height * peaks[band], barWidth, 1.5f);
                }
            }

            canvas.StrokeSize = 1;
            canvas.StrokeColor = Signal;
            canvas.DrawLine(0, top, 8, top);
            canvas.DrawLine(0, top, 0, top + 7);
            canvas.DrawLine(width - 8, bottom, width, bottom);
            canvas.DrawLine(width, bottom - 7, width, bottom);
            canvas.FontColor = Muted;
            for (var index = 0; index < Frequencies.Length; index++)
            {
                var labelWidth = width / 5;
                var labelLeft = Math.Clamp(width * index / 4 - labelWidth / 2, 0, width - labelWidth);
                canvas.DrawString(Frequencies[index], labelLeft, bottom + 5, labelWidth, 12,
                    index == 0 ? HorizontalAlignment.Left : index == 4 ? HorizontalAlignment.Right : HorizontalAlignment.Center, VerticalAlignment.Top);
            }
            canvas.RestoreState();
        }
    }
}