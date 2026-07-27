using AaronOS.Modules.Schedule;
using AaronOS.Modules.Schedule.Views;
using Wpf.Ui.Controls;

namespace AaronOS.Modules.Schedule.Tests;

public class ScheduleModuleTests
{
    [Fact]
    public void Exposes_StableContractValues()
    {
        var module = new ScheduleModule();

        Assert.Equal("schedule", module.Id);
        Assert.Equal("Schedule", module.DisplayName);
        Assert.Equal(typeof(ScheduleShellPage), module.HomePageType);
        // The shell does Enum.Parse<SymbolRegular>(IconGlyph) at startup; a bad name is a
        // crash on launch, not a compile error, so pin it here by actually parsing it.
        Assert.True(Enum.TryParse<SymbolRegular>(module.IconGlyph, out var glyph));
        Assert.Equal(SymbolRegular.CalendarLtr24, glyph);
    }
}
