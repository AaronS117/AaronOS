using CommunityToolkit.Mvvm.ComponentModel;

namespace AaronOS.Core;

/// <summary>Shared base for every module's ViewModels, so cross-cutting state (e.g. IsBusy) lives in one place.</summary>
public abstract partial class ViewModelBase : ObservableObject
{
    // ponytail: field-backed [ObservableProperty] instead of partial-property syntax — the
    // partial-property generator doesn't run correctly in this environment regardless of UI
    // framework (confirmed: fails identically in a plain net8.0 library, so it isn't a WinUI/
    // CsWinRT-specific issue as first assumed). Not AOT-published, so MVVMTK0045 doesn't apply.
    [ObservableProperty]
    private bool _isBusy;
}
