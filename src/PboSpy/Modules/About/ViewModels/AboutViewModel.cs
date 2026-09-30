using PboSpy.Localization;
namespace PboSpy.Modules.About.ViewModels;

[Export(typeof(IAboutInformation))]
[PartCreationPolicy(CreationPolicy.Shared)]
public class AboutViewModel : Document, IAboutInformation
{
    public AboutViewModel()
    {
        DisplayName = Loc.T("Cmd.About");
        Loc.Instance.LanguageChanged += (_, _) => DisplayName = Loc.T("Cmd.About");
    }

    public override bool ShouldReopenOnStart => false;

}
