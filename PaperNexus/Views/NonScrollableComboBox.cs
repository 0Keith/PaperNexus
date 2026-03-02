using Avalonia.Controls;
using Avalonia.Input;

namespace PaperNexus.Views;

public class NonScrollableComboBox : ComboBox
{
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (IsDropDownOpen)
            base.OnPointerWheelChanged(e);
    }
}
