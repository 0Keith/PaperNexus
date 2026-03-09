# PaperNexus UI Style Guide

Avalonia AXAML patterns used throughout `MainWindow.axaml`. Follow these when adding new UI sections.

---

## Colors

| Role | Value |
|---|---|
| Window background | `#2B2B2B` |
| Panel / group background | `#1E1E1E` |
| Gallery card background | `#222222` |
| Thumbnail placeholder | `#2A2A2A` |
| Separator line | `#333` |
| Border / outline | `#444` / `#555` |
| Favorite (active) | `#E06C75` |
| Ban (active) | `#F97316` |
| Destructive (delete) | `#E06C75` |
| Muted label | `Opacity="0.6"` |
| Description text | `Opacity="0.5"` |
| Group heading | `Opacity="0.85"` |

---

## Settings Group

Wrap each logical group in a `Border` with inner `StackPanel`:

```xml
<Border Background="#1E1E1E" BorderBrush="#444" BorderThickness="1" CornerRadius="6" Padding="12">
    <StackPanel Spacing="12">
        <TextBlock Text="Group Name" FontWeight="SemiBold" FontSize="13" Opacity="0.85"/>
        <!-- separators and controls -->
    </StackPanel>
</Border>
```

### Section Separator

```xml
<Border Height="1" Background="#333"/>
```

---

## Labeled Control

Order: **label → control → description**. Description always goes *beneath* the control.

```xml
<StackPanel Spacing="4">
    <TextBlock Text="Label" FontSize="12" Opacity="0.6"/>
    <!-- control (TextBox, NumericUpDown, etc.) -->
    <TextBlock Text="Description of what this does."
               FontSize="11" Opacity="0.5" TextWrapping="Wrap"/>
</StackPanel>
```

### ComboBox with dynamic description

When options have distinct meanings, bind the description to the selected item's `Description` property so it updates with the selection. Add a `Description` property to the option record (keep `ToString()` returning just the label):

```csharp
public record MyOption(string Label, string Description, MyEnum Value)
{
    public override string ToString() => Label;
}
```

```xml
<StackPanel Spacing="4">
    <TextBlock Text="Label" FontSize="12" Opacity="0.6"/>
    <v:NonScrollableComboBox ItemsSource="{x:Static vm:ViewModel.MyOptions}"
                             SelectedItem="{Binding SelectedOption}"
                             HorizontalAlignment="Stretch"/>
    <TextBlock Text="{Binding SelectedOption.Description}"
               FontSize="11" Opacity="0.5" TextWrapping="Wrap"/>
</StackPanel>
```

### ComboBox with computed description

When the description depends on more than just the selected item (e.g. a typed value, external state, or validation), bind the description TextBlock to a ViewModel property instead of the option's `Description` field. The ViewModel property reads the current selection and any dependent state to produce the string:

```csharp
public string MyDescription
{
    get
    {
        if (SelectedOption.Value == MyEnum.Special)
            return ComputeDescriptionFrom(SomeOtherProperty);
        return SelectedOption.Description;
    }
}
```

Notify it from any property that affects the output:

```csharp
set
{
    if (SetProperty(ref _selectedOption, value))
        OnPropertyChanged(nameof(MyDescription));
}
partial void OnSomeOtherPropertyChanged(string value) => OnPropertyChanged(nameof(MyDescription));
```

```xml
<TextBlock Text="{Binding MyDescription}" FontSize="11" Opacity="0.5" TextWrapping="Wrap"/>
```

### Label + Toggle (right-aligned checkbox)

For settings where a checkbox enables/disables the section:

```xml
<Grid ColumnDefinitions="*,Auto">
    <TextBlock Grid.Column="0" Text="Feature Name" FontSize="12" Opacity="0.6" VerticalAlignment="Center"/>
    <CheckBox Grid.Column="1" IsChecked="{Binding FeatureEnabled}"
              ToolTip.Tip="Describe what it does"/>
</Grid>
```

For a group-level enabled toggle in the heading row:

```xml
<Grid ColumnDefinitions="*,Auto">
    <TextBlock Grid.Column="0" Text="Group Name" FontWeight="SemiBold" FontSize="13" Opacity="0.85" VerticalAlignment="Center"/>
    <CheckBox Grid.Column="1" Content="Enabled" IsChecked="{Binding GroupEnabled}" VerticalAlignment="Center"/>
</Grid>
```

### Collapsing sub-panels when disabled

When a toggle controls whether a group of sub-settings is relevant, wrap the sub-settings in a `StackPanel` with `IsVisible` bound to the toggle property. This collapses them entirely rather than greying them out:

```xml
<!-- Group-level toggle in heading -->
<Grid ColumnDefinitions="*,Auto">
    <TextBlock Grid.Column="0" Text="Feature" FontWeight="SemiBold" FontSize="13" Opacity="0.85" VerticalAlignment="Center"/>
    <CheckBox Grid.Column="1" Content="Enabled" IsChecked="{Binding FeatureEnabled}" VerticalAlignment="Center"/>
</Grid>
<StackPanel Spacing="12" IsVisible="{Binding FeatureEnabled}">
    <!-- sub-settings here -->
</StackPanel>
```

For a checkbox that reveals additional options beneath it:

```xml
<StackPanel Spacing="4">
    <CheckBox Content="Feature Name" IsChecked="{Binding FeatureEnabled}"/>
    <StackPanel Spacing="4" IsVisible="{Binding FeatureEnabled}">
        <!-- dependent controls here -->
    </StackPanel>
</StackPanel>
```

Use `IsEnabled` only when the control should remain visible but inactive (e.g. a text field that shows its current value while disabled).

---

## Buttons

### Toolbar buttons (tab toolbars)

```xml
<Button Command="{Binding ...}" ToolTip.Tip="..." Padding="8,4">
    <PathIcon Data="..." Width="12" Height="12"/>
</Button>
```

### Row action buttons (inside list item DataTemplates)

```xml
<Button Click="Handler" ToolTip.Tip="..." Padding="4,2" VerticalAlignment="Center">
    <PathIcon Data="..." Width="10" Height="10"/>
</Button>
```

### Gallery card buttons

```xml
<Button Command="{Binding ...}" Padding="2" ToolTip.Tip="..." HorizontalContentAlignment="Center">
    <PathIcon Data="..." Width="10" Height="10"/>
</Button>
```

### Footer buttons

```xml
<Button Command="{Binding ...}" ToolTip.Tip="..." Padding="5,2" Opacity="0.6">
    <PathIcon Data="..." Width="14" Height="14"/>
</Button>
```

**Every `Button` must have `ToolTip.Tip`.** Tray `NativeMenuItem`s are exempt.

---

## Tab Headers

```xml
<TabItem.Header>
    <StackPanel Orientation="Horizontal" Spacing="5">
        <PathIcon Data="..." Width="14" Height="14"/>
        <TextBlock Text="Tab Name" FontSize="11"/>
    </StackPanel>
</TabItem.Header>
```

---

## Lists

```xml
<ListBox ItemsSource="{Binding Items}"
         SelectedItem="{Binding SelectedItem}"
         Background="#1E1E1E"
         BorderBrush="#555"
         BorderThickness="1">
```

Row DataTemplates: use `Margin="2,4"` on the root element inside each item.

---

## Indented Sub-settings

Indent child settings under a parent toggle with `Margin="24,0,0,0"` and hide with `IsVisible`:

```xml
<StackPanel Spacing="8" Margin="24,0,0,0" IsVisible="{Binding ParentEnabled}">
    <!-- child settings -->
</StackPanel>
```
