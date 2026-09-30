using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using PboSpy.Themes;
using System.IO;
using System.Xml;

namespace PboSpy.Modules.Preview;

/// <summary>
/// Syntax colours for Arma files (SQF scripts and config / rvmat / sqm class syntax) in a dark and a
/// light variant, so previews stay readable whatever the theme.
/// </summary>
public static class ArmaHighlighting
{
    private static readonly string[] SqfKeywords =
    {
        "if", "then", "else", "exitWith", "while", "do", "for", "from", "to", "step", "forEach", "forEachReversed",
        "switch", "case", "default", "try", "catch", "throw", "with", "waitUntil", "private", "params", "call",
        "spawn", "execVM", "exec", "compile", "compileFinal", "preprocessFile", "preprocessFileLineNumbers",
        "return", "breakOut", "breakTo", "continue", "continueWith", "scopeName", "not", "and", "or", "true", "false",
        "nil", "isNil", "isNull", "count", "select", "apply", "findIf", "in", "set", "pushBack", "pushBackUnique",
        "append", "deleteAt", "resize", "reverse", "sort", "find", "format", "str", "parseNumber", "toUpper", "toLower",
        "getVariable", "setVariable", "missionNamespace", "uiNamespace", "profileNamespace", "localNamespace",
        "player", "vehicle", "driver", "gunner", "commander", "crew", "alive", "damage", "setDamage", "getPos",
        "getPosATL", "getPosASL", "setPos", "setPosATL", "setPosASL", "getDir", "setDir", "typeOf", "isKindOf",
        "createVehicle", "deleteVehicle", "attachTo", "detach", "remoteExec", "remoteExecCall", "publicVariable",
        "isServer", "hasInterface", "isDedicated", "local", "addAction", "removeAction", "addEventHandler",
        "removeEventHandler", "addMissionEventHandler", "hint", "hintSilent", "systemChat", "diag_log", "sleep",
        "uiSleep", "time", "diag_tickTime", "serverTime", "animateSource", "animationSourcePhase", "animate",
        "animationPhase", "setObjectTexture", "setObjectTextureGlobal", "setObjectMaterial", "getText", "getNumber",
        "getArray", "configFile", "missionConfigFile", "isClass", "configName", "configProperties", "selectionPosition",
        "modelToWorld", "worldToModel", "playSound", "playSound3D", "say3D", "createSimpleObject", "allUnits",
        "allPlayers", "units", "group", "side", "west", "east", "independent", "civilian", "objNull", "grpNull",
        "displayNull", "controlNull", "findDisplay", "displayCtrl", "ctrlSetText", "ctrlText", "ctrlShow",
        "ctrlEnable", "createDialog", "closeDialog", "cutRsc", "titleText", "lbAdd", "lbSetData", "lbData",
        "setVelocity", "velocity", "vectorAdd", "vectorDiff", "vectorMultiply", "vectorNormalized", "vectorMagnitude",
        "distance", "distance2D", "nearestObjects", "nearEntities", "lineIntersectsSurfaces", "setFuel", "fuel",
        "setVehicleAmmo", "addMagazine", "addWeapon", "removeWeapon", "currentWeapon", "weapons", "magazines",
        "turretUnit", "assignedVehicleRole", "moveInDriver", "moveInGunner", "moveInCargo", "moveOut", "action",
        "inputAction", "drawIcon3D", "drawLine3D", "BIS_fnc_MP", "floor", "ceil", "round", "abs", "sqrt", "random",
        "selectRandom", "min", "max", "sin", "cos", "tan", "atan2", "linearConversion", "toFixed", "createHashMap",
        "createHashMapFromArray", "keys", "values", "getOrDefault", "insert", "onEachFrame", "addMissionEventHandler"
    };

    private static readonly string[] SqfMagic = { "_this", "_x", "_y", "_forEachIndex", "_thisEventHandler", "_exception", "this" };

    private static readonly string[] ConfigKeywords = { "class", "delete", "import", "__EVAL", "__EXEC", "true", "false" };

    private static readonly Dictionary<string, IHighlightingDefinition> Cache = new();

    /// <summary>
    /// Colours an editor from the theme (AvalonEdit stays white otherwise) and keeps it in step
    /// with theme changes until it is unloaded.
    /// </summary>
    public static void Attach(ICSharpCode.AvalonEdit.TextEditor editor, string extension, Func<IHighlightingDefinition> fallback)
    {
        void Apply()
        {
            editor.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "PboSpy.Deep");
            editor.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "PboSpy.Text");
            editor.SetResourceReference(ICSharpCode.AvalonEdit.TextEditor.LineNumbersForegroundProperty, "PboSpy.TextDim");
            editor.TextArea.SetResourceReference(ICSharpCode.AvalonEdit.Editing.TextArea.SelectionBrushProperty, "PboSpy.Selection");
            editor.TextArea.SelectionBorder = null;
            editor.TextArea.SelectionForeground = null;
            editor.TextArea.Caret.CaretBrush = System.Windows.Application.Current.TryFindResource("PboSpy.Text") as System.Windows.Media.Brush;
            editor.SyntaxHighlighting = For(extension) ?? fallback?.Invoke();
        }

        EventHandler onTheme = (_, _) => editor.Dispatcher.BeginInvoke(Apply);
        ThemeService.Changed += onTheme;
        editor.Unloaded += (_, _) => ThemeService.Changed -= onTheme;
        editor.Loaded += (_, _) =>
        {
            ThemeService.Changed -= onTheme;
            ThemeService.Changed += onTheme;
            Apply();
        };
        Apply();
    }

    public static IHighlightingDefinition For(string extension)
    {
        var language = extension switch
        {
            ".sqf" or ".sqs" or ".fsm" => "SQF",
            ".cpp" or ".hpp" or ".h" or ".bin" or ".rvmat" or ".sqm" or ".ext" or ".inc" or ".bisurf" or ".cfg" or ".bikb" => "Config",
            _ => null
        };
        if (language == null)
        {
            return null;
        }
        var dark = ThemeService.IsDark;
        var key = language + (dark ? ":dark" : ":light");
        if (!Cache.TryGetValue(key, out var definition))
        {
            try
            {
                definition = Build(language, dark);
            }
            catch (Exception)
            {
                definition = null;
            }
            Cache[key] = definition;
        }
        return definition;
    }

    private static IHighlightingDefinition Build(string language, bool dark)
    {
        string c(string darkColor, string lightColor) => dark ? darkColor : lightColor;
        var comment = c("#6A9955", "#008000");
        var str = c("#CE9178", "#A31515");
        var number = c("#B5CEA8", "#098658");
        var keyword = c("#569CD6", "#0000FF");
        var command = c("#DCDCAA", "#795E26");
        var magic = c("#9CDCFE", "#001080");
        var local = c("#9CDCFE", "#1F377F");
        var preproc = c("#C586C0", "#AF00DB");
        var className = c("#4EC9B0", "#267F99");

        var keywords = language == "SQF" ? SqfKeywords.Distinct().ToArray() : ConfigKeywords;
        var xml = $@"<?xml version=""1.0""?>
<SyntaxDefinition name=""Arma{language}"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
  <Color name=""Comment"" foreground=""{comment}""/>
  <Color name=""String"" foreground=""{str}""/>
  <Color name=""Number"" foreground=""{number}""/>
  <Color name=""Keyword"" foreground=""{keyword}"" fontWeight=""bold""/>
  <Color name=""Command"" foreground=""{command}""/>
  <Color name=""Magic"" foreground=""{magic}"" fontStyle=""italic""/>
  <Color name=""Local"" foreground=""{local}""/>
  <Color name=""Preprocessor"" foreground=""{preproc}""/>
  <Color name=""ClassName"" foreground=""{className}""/>
  <RuleSet ignoreCase=""{(language == "SQF" ? "true" : "false")}"">
    <Span color=""Comment"" begin=""//""/>
    <Span color=""Comment"" multiline=""true"" begin=""/\*"" end=""\*/""/>
    <Span color=""Preprocessor"" begin=""^\s*\#""/>
    <Span color=""String""><Begin>""</Begin><End>""</End></Span>
    <Span color=""String""><Begin>'</Begin><End>'</End></Span>
    <Keywords color=""Keyword"">{string.Join("", (language == "SQF" ? keywords.Take(40) : keywords).Select(k => $"<Word>{k}</Word>"))}</Keywords>
    {(language == "SQF" ? $@"<Keywords color=""Command"">{string.Join("", keywords.Skip(40).Select(k => $"<Word>{k}</Word>"))}</Keywords>
    <Keywords color=""Magic"">{string.Join("", SqfMagic.Select(k => $"<Word>{k}</Word>"))}</Keywords>
    <Rule color=""Local"">\b_[A-Za-z0-9_]+</Rule>" : @"<Rule color=""ClassName"">(?&lt;=\bclass\s+)[A-Za-z0-9_]+</Rule>")}
    <Rule color=""Number"">\b0[xX][0-9a-fA-F]+|(\b\d+(\.\d+)?|\.\d+)([eE][+-]?\d+)?</Rule>
  </RuleSet>
</SyntaxDefinition>";
        using var reader = XmlReader.Create(new StringReader(xml));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
