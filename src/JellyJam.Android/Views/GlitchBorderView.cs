namespace JellyJam.Mobile.Views;

public sealed class GlitchBorderView : GraphicsView
{
    private readonly Drawing drawing = new();
    private IDispatcherTimer? timer;

    public GlitchBorderView()
    {
        Drawable = drawing;
        InputTransparent = true;
        AutomationProperties.SetIsInAccessibleTree(this, false);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler is null) { timer?.Stop(); return; }
        if (timer is null)
        {
            timer = Dispatcher.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(70);
            timer.Tick += (_, _) => { drawing.Frame++; Invalidate(); };
        }
        timer.Start();
    }

    private sealed class Drawing : IDrawable
    {
        private static readonly Color Cyan = Color.FromArgb("#00F0FF");
        private static readonly Color Magenta = Color.FromArgb("#FF2BD6");
        private static readonly Color Amber = Color.FromArgb("#FFC36A");
        public int Frame { get; set; }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (dirtyRect.Width < 20 || dirtyRect.Height < 20) return;
            var random = new Random(Frame);
            var box = new RectF(2, 2, dirtyRect.Width - 4, dirtyRect.Height - 4);
            var shift = random.NextDouble() < 0.2 ? 1.5f + (float)random.NextDouble() * 3 : 1.5f;

            canvas.SaveState();
            canvas.StrokeSize = 1.5f;
            canvas.StrokeColor = Cyan.WithAlpha(0.7f);
            canvas.DrawRectangle(box.X - shift, box.Y, box.Width, box.Height);
            canvas.StrokeColor = Magenta.WithAlpha(0.7f);
            canvas.DrawRectangle(box.X + shift, box.Y, box.Width, box.Height);

            DrawGradientEdges(canvas, box);

            if (random.NextDouble() < 0.35)
            {
                for (var slice = 0; slice < 3; slice++)
                {
                    var y = (float)random.NextDouble() * box.Height;
                    var offset = (float)(random.NextDouble() - 0.5) * 14;
                    canvas.StrokeColor = random.NextDouble() < 0.5 ? Cyan : Magenta;
                    canvas.StrokeSize = 2;
                    canvas.DrawLine(box.X + offset, y, box.X + offset + 14 + (float)random.NextDouble() * 24, y);
                    canvas.DrawLine(box.Right + offset, y, box.Right + offset - 14, y);
                }
            }

            var noise = (int)(box.Width + box.Height) / 3;
            for (var dot = 0; dot < noise; dot++)
            {
                var onHorizontal = random.NextDouble() < 0.5;
                var along = (float)random.NextDouble();
                var jitter = (float)(random.NextDouble() - 0.5) * 6;
                var x = onHorizontal ? box.X + along * box.Width : (random.NextDouble() < 0.5 ? box.X : box.Right) + jitter;
                var y2 = onHorizontal ? (random.NextDouble() < 0.5 ? box.Y : box.Bottom) + jitter : box.Y + along * box.Height;
                canvas.FillColor = (random.Next(3) switch { 0 => Cyan, 1 => Magenta, _ => Colors.White }).WithAlpha(0.35f + (float)random.NextDouble() * 0.5f);
                canvas.FillRectangle(x, y2, 1.5f, 1.5f);
            }
            canvas.RestoreState();
        }

        private static void DrawGradientEdges(ICanvas canvas, RectF box)
        {
            canvas.StrokeSize = 2;
            const int segments = 24;
            var perimeter = 2 * (box.Width + box.Height);
            for (var segment = 0; segment < segments; segment++)
            {
                var from = Point(box, perimeter * segment / segments);
                var to = Point(box, perimeter * (segment + 1) / segments);
                var t = segment / (float)(segments - 1);
                canvas.StrokeColor = t < 0.5f ? Mix(Cyan, Magenta, t * 2) : Mix(Magenta, Amber, (t - 0.5f) * 2);
                canvas.DrawLine(from, to);
            }
        }

        private static Color Mix(Color a, Color b, float t)
            => new(a.Red + (b.Red - a.Red) * t, a.Green + (b.Green - a.Green) * t, a.Blue + (b.Blue - a.Blue) * t);

        private static PointF Point(RectF box, float distance)
        {
            if (distance <= box.Width) return new PointF(box.X + distance, box.Y);
            distance -= box.Width;
            if (distance <= box.Height) return new PointF(box.Right, box.Y + distance);
            distance -= box.Height;
            if (distance <= box.Width) return new PointF(box.Right - distance, box.Bottom);
            return new PointF(box.X, box.Bottom - (distance - box.Width));
        }
    }
}
