using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clipcy.Controls;

public sealed class SettingsCard : ContentControl
{
    public SettingsCard()
    {
        DefaultStyleKey = typeof(SettingsCard);

        // Initialize visibility states
        UpdateHeaderIconVisibility();
        UpdateHeaderTextVisibility();
        UpdateDescriptionVisibility();
        UpdateHeaderVisibility();
    }

    // HeaderIcon property (can be a FontIcon, BitmapIcon, or any icon)
    public static readonly DependencyProperty HeaderIconProperty = DependencyProperty.Register(
        nameof(HeaderIcon),
        typeof(object),
        typeof(SettingsCard),
        new PropertyMetadata(null, OnHeaderIconChanged)
    );

    public object? HeaderIcon
    {
        get => GetValue(HeaderIconProperty);
        set => SetValue(HeaderIconProperty, value);
    }

    private static void OnHeaderIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SettingsCard card)
        {
            card.UpdateHeaderIconVisibility();
        }
    }

    // Header property (main title text)
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header),
        typeof(string),
        typeof(SettingsCard),
        new PropertyMetadata(string.Empty, OnHeaderChanged)
    );

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SettingsCard card)
        {
            card.UpdateHeaderTextVisibility();
            card.UpdateHeaderVisibility();
        }
    }

    // Description property (subtitle/caption text)
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(SettingsCard),
        new PropertyMetadata(string.Empty, OnDescriptionChanged)
    );

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    private static void OnDescriptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SettingsCard card)
        {
            card.UpdateDescriptionVisibility();
            card.UpdateHeaderVisibility();
        }
    }

    // HeaderIconVisibility property
    public static readonly DependencyProperty HeaderIconVisibilityProperty = DependencyProperty.Register(
        nameof(HeaderIconVisibility),
        typeof(Visibility),
        typeof(SettingsCard),
        new PropertyMetadata(Visibility.Collapsed)
    );

    public Visibility HeaderIconVisibility
    {
        get => (Visibility)GetValue(HeaderIconVisibilityProperty);
        private set => SetValue(HeaderIconVisibilityProperty, value);
    }

    // HeaderTextVisibility property
    public static readonly DependencyProperty HeaderTextVisibilityProperty = DependencyProperty.Register(
        nameof(HeaderTextVisibility),
        typeof(Visibility),
        typeof(SettingsCard),
        new PropertyMetadata(Visibility.Collapsed)
    );

    public Visibility HeaderTextVisibility
    {
        get => (Visibility)GetValue(HeaderTextVisibilityProperty);
        private set => SetValue(HeaderTextVisibilityProperty, value);
    }

    // DescriptionVisibility property
    public static readonly DependencyProperty DescriptionVisibilityProperty = DependencyProperty.Register(
        nameof(DescriptionVisibility),
        typeof(Visibility),
        typeof(SettingsCard),
        new PropertyMetadata(Visibility.Collapsed)
    );

    public Visibility DescriptionVisibility
    {
        get => (Visibility)GetValue(DescriptionVisibilityProperty);
        private set => SetValue(DescriptionVisibilityProperty, value);
    }

    // HeaderVisibility property (computed from Header and Description)
    public static readonly DependencyProperty HeaderVisibilityProperty = DependencyProperty.Register(
        nameof(HeaderVisibility),
        typeof(Visibility),
        typeof(SettingsCard),
        new PropertyMetadata(Visibility.Collapsed)
    );

    public Visibility HeaderVisibility
    {
        get => (Visibility)GetValue(HeaderVisibilityProperty);
        private set => SetValue(HeaderVisibilityProperty, value);
    }

    private void UpdateHeaderIconVisibility()
    {
        HeaderIconVisibility = HeaderIcon != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateHeaderTextVisibility()
    {
        HeaderTextVisibility = !string.IsNullOrEmpty(Header) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateDescriptionVisibility()
    {
        DescriptionVisibility = !string.IsNullOrEmpty(Description) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateHeaderVisibility()
    {
        HeaderVisibility =
            (!string.IsNullOrEmpty(Header) || !string.IsNullOrEmpty(Description))
                ? Visibility.Visible
                : Visibility.Collapsed;
    }
}
