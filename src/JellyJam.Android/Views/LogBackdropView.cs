namespace JellyJam.Mobile.Views;

public sealed class LogBackdropView : GraphicsView
{
    private const float LineHeight = 12;
    private readonly Drawing drawing = new();
    private LogTail? tail;
    private IDispatcherTimer? timer;
    private int version = -1;

    public LogBackdropView()
    {
        Drawable = drawing;
        InputTransparent = true;
        SemanticProperties.SetDescription(this, "Decorative application log");
        AutomationProperties.SetIsInAccessibleTree(this, false);
    }

    public void Connect(LogTail value)
    {
        tail = value;
        value.Start();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler is null) { timer?.Stop(); return; }
        timer ??= CreateTimer();
        timer.Start();
    }

    private IDispatcherTimer CreateTimer()
    {
        var created = Dispatcher.CreateTimer();
        created.Interval = TimeSpan.FromMilliseconds(50);
        created.Tick += (_, _) =>
        {
            if (tail is null || tail.Version == version) return;
            version = tail.Version;
            drawing.Lines = tail.Snapshot((int)(Height / LineHeight) + 1);
            Invalidate();
        };
        return created;
    }

    private sealed class Drawing : IDrawable
    {
        private static readonly Color Text = Color.FromArgb("#5B676C");
        public string[] Lines { get; set; } = [];

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.SaveState();
            canvas.Font = new Microsoft.Maui.Graphics.Font("SpaceMono");
            canvas.FontSize = 9;
            canvas.FontColor = Text;
            var characters = Math.Max(8, (int)(dirtyRect.Width / 5.4f));
            var y = dirtyRect.Height - LineHeight;
            for (var index = Lines.Length - 1; index >= 0 && y > -LineHeight; index--, y -= LineHeight)
            {
                var line = Lines[index];
                if (line.Length > characters) line = line[..characters];
                canvas.DrawString(line, 4, y, dirtyRect.Width - 8, LineHeight, HorizontalAlignment.Left, VerticalAlignment.Top);
            }
            canvas.RestoreState();
        }
    }
}
