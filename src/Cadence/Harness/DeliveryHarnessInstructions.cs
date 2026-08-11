using System.Reflection;
using System.Text;

namespace Cadence;

internal static class CadenceHarnessInstructions
{
    private const string ResourceName = "Cadence.DELIVERY_HARNESS.md";
    private static readonly Lazy<string> _value = new(Load);

    internal static string Value => _value.Value;

    private static string Load()
    {
        using var stream =
            Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded Cadence Harness instructions '{ResourceName}' were not found."
            );
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var value = reader.ReadToEnd();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                "Embedded Cadence Harness instructions must not be empty."
            )
            : value;
    }
}
