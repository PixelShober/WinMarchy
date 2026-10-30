namespace WinMarchy.Core;

/// <summary>
/// An error with a translation key ("errors.&lt;code&gt;"). The message is already localized for the log;
/// the bridge also sends code and parameters so the UI can react to specific errors.
/// </summary>
public sealed class WinMarchyException : Exception
{
    public WinMarchyException(string code, params (string Name, object? Value)[] args)
        : base(L.T("errors." + code, args))
    {
        Code = code;
        Params = args.ToDictionary(a => a.Name, a => a.Value);
    }

    public WinMarchyException(Exception inner, string code, params (string Name, object? Value)[] args)
        : base(L.T("errors." + code, args), inner)
    {
        Code = code;
        Params = args.ToDictionary(a => a.Name, a => a.Value);
    }

    public string Code { get; }
    public IReadOnlyDictionary<string, object?> Params { get; }
}
