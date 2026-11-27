using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using EzCompany.App.Services;
using EzCompany.Core.Data.Entities;

namespace EzCompany.App.Controls;

public partial class ParallelTimelineControl : UserControl
{
    public static readonly DependencyProperty LayoutResultProperty =
        DependencyProperty.Register(
            nameof(LayoutResult),
            typeof(TimelineLayoutResult),
            typeof(ParallelTimelineControl),
            new PropertyMetadata(null, OnLayoutResultChanged));

    public ParallelTimelineControl()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Redraw();
        Loaded += (_, _) => Redraw();
    }

    public TimelineLayoutResult? LayoutResult
    {
        get => (TimelineLayoutResult?)GetValue(LayoutResultProperty);
        set => SetValue(LayoutResultProperty, value);
    }

    public event EventHandler<TimelinePlanEntity>? PlanClicked;

    private static void OnLayoutResultChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ParallelTimelineControl ctrl)
        {
            ctrl.Redraw();
        }
    }

    private void Redraw()
    {
        TimelineCanvas.Children.Clear();
        TimelineLayoutResult? layout = LayoutResult;
        if (layout is null)
        {
            return;
        }

        TimelineCanvas.Width = layout.CanvasWidth;
        TimelineCanvas.Height = layout.CanvasHeight;

        Brush accentBrush = TryFindResource("AppAccentBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
        Brush borderBrush = TryFindResource("AppCardBorderBrush") as Brush ?? new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));
        Brush textBrush = TryFindResource("TextFillColorPrimaryBrush") as Brush ?? Brushes.White;

        // 1. Vertical date gridlines + top date header labels
        foreach (TimelineDateTick tick in layout.Ticks)
        {
            var gridLine = new Line
            {
                X1 = tick.X,
                X2 = tick.X,
                Y1 = 26,
                Y2 = layout.CanvasHeight - 10,
                Stroke = tick.IsToday ? accentBrush : borderBrush,
                StrokeThickness = tick.IsToday ? 2.0 : 1.0,
                Opacity = tick.IsToday ? 0.85 : 0.25
            };
            if (tick.IsToday)
            {
                gridLine.StrokeDashArray = new DoubleCollection { 4, 3 };
            }
            TimelineCanvas.Children.Add(gridLine);

            var label = new TextBlock
            {
                Text = tick.IsToday ? $"Hôm nay ({tick.Label})" : tick.Label,
                FontSize = tick.IsToday ? 11.5 : 10.5,
                FontWeight = tick.IsToday ? FontWeights.Bold : FontWeights.Normal,
                Foreground = tick.IsToday ? accentBrush : textBrush,
                Opacity = tick.IsToday ? 1.0 : 0.72
            };
            Canvas.SetLeft(label, Math.Max(4, tick.X - (tick.IsToday ? 42 : 16)));
            Canvas.SetTop(label, 5);
            TimelineCanvas.Children.Add(label);
        }

        // 2. Parallel horizontal track lines + date marker dots on every line + colored plan bars
        foreach (TimelineTrackRow track in layout.Tracks)
        {
            var trackLine = new Line
            {
                X1 = 16,
                X2 = layout.CanvasWidth - 16,
                Y1 = track.CenterY,
                Y2 = track.CenterY,
                Stroke = borderBrush,
                StrokeThickness = 2.0,
                Opacity = 0.75
            };
            TimelineCanvas.Children.Add(trackLine);

            foreach (TimelineDateTick tick in layout.Ticks)
            {
                double dotSize = tick.IsToday ? 9.0 : 6.0;
                var dot = new Ellipse
                {
                    Width = dotSize,
                    Height = dotSize,
                    Fill = tick.IsToday ? accentBrush : borderBrush,
                    Stroke = TryFindResource("AppCardBackgroundBrush") as Brush ?? Brushes.Transparent,
                    StrokeThickness = 1,
                    ToolTip = tick.Date.ToString("dd/MM/yyyy")
                };
                Canvas.SetLeft(dot, tick.X - dotSize / 2.0);
                Canvas.SetTop(dot, track.CenterY - dotSize / 2.0);
                TimelineCanvas.Children.Add(dot);
            }

            foreach (TimelineBarItem bar in track.Bars)
            {
                var fillColor = (Color)ColorConverter.ConvertFromString(bar.FillHex);
                var pill = new Border
                {
                    Width = bar.Width,
                    Height = 28,
                    CornerRadius = new CornerRadius(12),
                    Background = new SolidColorBrush(Color.FromArgb(232, fillColor.R, fillColor.G, fillColor.B)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(9, 0, 9, 0),
                    Cursor = Cursors.Hand,
                    ToolTip = bar.ToolTipText
                };

                var contentPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center
                };
                contentPanel.Children.Add(new TextBlock
                {
                    Text = $"{bar.StatusBadge} {bar.Entity.Title}",
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                });

                if (bar.Width >= 145)
                {
                    contentPanel.Children.Add(new TextBlock
                    {
                        Text = $" • {bar.DateRangeLabel}",
                        Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                        FontSize = 10.5,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }

                pill.Child = contentPanel;
                TimelinePlanEntity capturedPlan = bar.Entity;
                pill.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true;
                    PlanClicked?.Invoke(this, capturedPlan);
                };

                Canvas.SetLeft(pill, bar.LeftX);
                Canvas.SetTop(pill, track.CenterY - 14);
                TimelineCanvas.Children.Add(pill);
            }
        }
    }
}
