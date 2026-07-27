using CommunityToolkit.Mvvm.ComponentModel;

namespace AaronOS.Core;

/// <summary>Shared base for every module's ViewModels, so cross-cutting state (e.g. IsBusy) lives in one place.</summary>
public abstract partial class ViewModelBase : ObservableObject
{
    // ponytail: field-backed [ObservableProperty] instead of partial-property syntax —
    // the partial-property generator doesn't run in this WinUI/CsWinRT project even with
    // EnforceExtendedAnalyzerRules set. Not AOT-published, so the MVVMTK0045 warning doesn't apply.
    [ObservableProperty]
    private bool _isBusy;
}
