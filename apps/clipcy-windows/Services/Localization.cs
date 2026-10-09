using System.Runtime.CompilerServices;
using Clipcy.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Clipcy.Services;

public static class Localization
{
    private sealed class Registration { }
    private static readonly object Sync = new();
    private static readonly ConditionalWeakTable<DependencyObject, Registration> Registrations = new();
    private static readonly List<WeakReference<DependencyObject>> Targets = new();

    public static readonly DependencyProperty TextProperty = Register("Text");
    public static readonly DependencyProperty ContentProperty = Register("Content");
    public static readonly DependencyProperty HeaderProperty = Register("Header");
    public static readonly DependencyProperty DescriptionProperty = Register("Description");
    public static readonly DependencyProperty PlaceholderTextProperty = Register("PlaceholderText");
    public static readonly DependencyProperty OnContentProperty = Register("OnContent");
    public static readonly DependencyProperty OffContentProperty = Register("OffContent");
    public static readonly DependencyProperty ToolTipProperty = Register("ToolTip");
    private static readonly DependencyProperty[] Properties =
    {
        TextProperty, ContentProperty, HeaderProperty, DescriptionProperty,
        PlaceholderTextProperty, OnContentProperty, OffContentProperty, ToolTipProperty,
    };

    static Localization()
    {
        LocalizationService.LanguageChanged += OnLanguageChanged;
    }

    public static string GetText(DependencyObject target) => (string)target.GetValue(TextProperty);
    public static void SetText(DependencyObject target, string value) => target.SetValue(TextProperty, value);
    public static string GetContent(DependencyObject target) => (string)target.GetValue(ContentProperty);
    public static void SetContent(DependencyObject target, string value) => target.SetValue(ContentProperty, value);
    public static string GetHeader(DependencyObject target) => (string)target.GetValue(HeaderProperty);
    public static void SetHeader(DependencyObject target, string value) => target.SetValue(HeaderProperty, value);
    public static string GetDescription(DependencyObject target) => (string)target.GetValue(DescriptionProperty);
    public static void SetDescription(DependencyObject target, string value) => target.SetValue(DescriptionProperty, value);
    public static string GetPlaceholderText(DependencyObject target) => (string)target.GetValue(PlaceholderTextProperty);
    public static void SetPlaceholderText(DependencyObject target, string value) => target.SetValue(PlaceholderTextProperty, value);
    public static string GetOnContent(DependencyObject target) => (string)target.GetValue(OnContentProperty);
    public static void SetOnContent(DependencyObject target, string value) => target.SetValue(OnContentProperty, value);
    public static string GetOffContent(DependencyObject target) => (string)target.GetValue(OffContentProperty);
    public static void SetOffContent(DependencyObject target, string value) => target.SetValue(OffContentProperty, value);
    public static string GetToolTip(DependencyObject target) => (string)target.GetValue(ToolTipProperty);
    public static void SetToolTip(DependencyObject target, string value) => target.SetValue(ToolTipProperty, value);

    private static DependencyProperty Register(string name) => DependencyProperty.RegisterAttached(
        name, typeof(string), typeof(Localization), new PropertyMetadata(null, OnKeyChanged)
    );

    private static void OnKeyChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        lock (Sync)
        {
            if (!Registrations.TryGetValue(target, out _))
            {
                Registrations.Add(target, new Registration());
                // Compact on registration too, so repeatedly opened windows cannot grow the registry indefinitely.
                Targets.RemoveAll(reference => !reference.TryGetTarget(out _));
                Targets.Add(new WeakReference<DependencyObject>(target));
            }
        }
        Apply(target, args.Property, args.NewValue as string ?? string.Empty);
    }

    private static void OnLanguageChanged(object? sender, EventArgs args)
    {
        WeakReference<DependencyObject>[] snapshot;
        lock (Sync)
        {
            Targets.RemoveAll(reference => !reference.TryGetTarget(out _));
            snapshot = Targets.ToArray();
        }
        foreach (var reference in snapshot)
        {
            if (!reference.TryGetTarget(out var target))
                continue;
            var dispatcher = target.DispatcherQueue;
            if (dispatcher.HasThreadAccess)
                Refresh(target);
            else if (!dispatcher.TryEnqueue(() => Refresh(target)))
                System.Diagnostics.Debug.WriteLine("Unable to refresh localization on a stopped UI dispatcher.");
        }
    }

    private static void Refresh(DependencyObject target)
    {
        foreach (var property in Properties)
        {
            // Null is the unset default; empty is an explicitly localized empty property.
            if (target.GetValue(property) is string key)
                Apply(target, property, key);
        }
    }

    private static void Apply(DependencyObject target, DependencyProperty property, string key)
    {
        var value = LocalizationService.Get(key);
        if (property == TextProperty)
        {
            switch (target)
            {
                case TextBlock text: text.Text = value; break;
                case TextBox text: text.Text = value; break;
                case AutoSuggestBox text: text.Text = value; break;
                case MenuFlyoutItem item: item.Text = value; break;
                case MenuFlyoutSubItem item: item.Text = value; break;
                case ToolTip tooltip: tooltip.Content = value; break;
                default: throw Unsupported(target, "Text");
            }
        }
        else if (property == ContentProperty)
        {
            switch (target)
            {
                case ContentControl control: control.Content = value; break;
                case ContentPresenter presenter: presenter.Content = value; break;
                default: throw Unsupported(target, "Content");
            }
        }
        else if (property == HeaderProperty)
        {
            switch (target)
            {
                case SettingsCard card: card.Header = value; break;
                case ContentDialog dialog: dialog.Title = value; break;
                case Expander expander: expander.Header = value; break;
                case ComboBox combo: combo.Header = value; break;
                case TextBox text: text.Header = value; break;
                case AutoSuggestBox text: text.Header = value; break;
                case NumberBox number: number.Header = value; break;
                case ToggleSwitch toggle: toggle.Header = value; break;
                case PasswordBox password: password.Header = value; break;
                case PivotItem pivot: pivot.Header = value; break;
                default: throw Unsupported(target, "Header");
            }
        }
        else if (property == DescriptionProperty)
        {
            if (target is SettingsCard card)
                card.Description = value;
            else
                throw Unsupported(target, "Description");
        }
        else if (property == PlaceholderTextProperty)
        {
            switch (target)
            {
                case TextBox text: text.PlaceholderText = value; break;
                case AutoSuggestBox text: text.PlaceholderText = value; break;
                case ComboBox combo: combo.PlaceholderText = value; break;
                case NumberBox number: number.PlaceholderText = value; break;
                case PasswordBox password: password.PlaceholderText = value; break;
                default: throw Unsupported(target, "PlaceholderText");
            }
        }
        else if (property == OnContentProperty && target is ToggleSwitch onToggle)
            onToggle.OnContent = value;
        else if (property == OffContentProperty && target is ToggleSwitch offToggle)
            offToggle.OffContent = value;
        else if (property == ToolTipProperty)
            ToolTipService.SetToolTip(target, value);
        else
            throw Unsupported(target, property == OnContentProperty ? "OnContent" : "OffContent");
    }

    private static InvalidOperationException Unsupported(DependencyObject target, string property) =>
        new($"Localization.{property} is not supported on {target.GetType().Name}.");
}
