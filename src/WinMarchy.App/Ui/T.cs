using System.Windows.Markup;
using WinMarchy.Core;

namespace WinMarchy.App.Ui;

/// <summary>{ui:T nav.start} – a text from the locale files. The language is fixed at start.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class T : MarkupExtension
{
    public T() { }

    public T(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
}
