using System.Text.Json;
using UK11.Core;

namespace UK11.App;
public sealed record ToggleItem(Tweak? Tweak, string Name, bool IsOn, bool Enabled, string Hint, string Key = "", string Kind = "tweak");
public sealed record AppItem(string Name, string FullName, bool Microsoft, bool Automatic);
public sealed record AdapterItem(int Index, string Name, string Detail) { public override string ToString() => Name; }
public sealed record DriverItem(string Id, string Title, string Manufacturer, string License);
public static class JsonValue
{
    public static string Text(this JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) ? item.ToString() : "";
    public static bool Flag(this JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.True;
    public static JsonElement[] Items(this JsonElement value) => value.ValueKind switch { JsonValueKind.Array => value.EnumerateArray().ToArray(), JsonValueKind.Object => [value], _ => [] };
}
