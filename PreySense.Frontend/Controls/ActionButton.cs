using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PreySense.Frontend.Controls
{
    public class ActionButton : Button
    {
        static ActionButton()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ActionButton),
                new FrameworkPropertyMetadata(typeof(ActionButton)));
        }

        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(
                nameof(Icon),
                typeof(ImageSource),
                typeof(ActionButton),
                new PropertyMetadata(null));

        public ImageSource? Icon
        {
            get => (ImageSource?)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(
                nameof(CornerRadius),
                typeof(CornerRadius),
                typeof(ActionButton),
                new PropertyMetadata(new CornerRadius(4)));

        public CornerRadius CornerRadius
        {
            get => (CornerRadius)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }
    }
}
