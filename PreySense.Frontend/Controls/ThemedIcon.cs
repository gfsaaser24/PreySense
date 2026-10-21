using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PreySense.Frontend.Controls;

public sealed class ThemedIcon : Control
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(ImageSource), typeof(ThemedIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (Source == null || ActualWidth <= 0 || ActualHeight <= 0) return;

        drawingContext.PushOpacityMask(new ImageBrush(Source) { Stretch = Stretch.Uniform });
        drawingContext.DrawRectangle(Foreground, null, new Rect(0, 0, ActualWidth, ActualHeight));
        drawingContext.Pop();
    }
}
