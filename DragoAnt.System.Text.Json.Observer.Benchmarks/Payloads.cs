using System.Text;

namespace DragoAnt.System.Text.Json.Observer.Benchmarks;

public enum PayloadShape
{
    Flat,
    Nested,
    Array,
}

public static class Payloads
{
    public static readonly string[] SensitiveNames =
    [
        "password",
        "cardNumber",
        "cvv",
        "email",
        "phone",
        "accessToken",
        "secret",
        "iban",
    ];

    public const string SensitiveMarker = "SENSITIVE";

    public static string Build(PayloadShape shape, int targetBytes, int seed = 0) =>
        shape switch
        {
            PayloadShape.Flat => BuildFlat(targetBytes, seed),
            PayloadShape.Nested => BuildNested(targetBytes, seed),
            PayloadShape.Array => BuildArray(targetBytes, seed),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null),
        };

    private static string BuildFlat(int targetBytes, int seed)
    {
        var sb = new StringBuilder(targetBytes + 256);
        sb.Append('{');
        AppendHeader(sb, seed);
        var i = 0;
        while (sb.Length < targetBytes - 2)
        {
            sb.Append(',');
            AppendField(sb, i++, seed);
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static string BuildNested(int targetBytes, int seed)
    {
        var sb = new StringBuilder(targetBytes + 256);
        sb.Append('{');
        AppendHeader(sb, seed);
        var section = 0;
        while (sb.Length < targetBytes - 2)
        {
            sb.Append(",\"section").Append(section).Append("\":{");
            AppendCustomer(sb, section, seed);
            sb.Append(",\"details\":{\"level\":2,\"meta\":{\"level\":3");
            for (var i = 0; i < 6; i++)
            {
                sb.Append(',');
                AppendField(sb, section * 10 + i, seed);
            }

            sb.Append("}}}");
            section++;
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static string BuildArray(int targetBytes, int seed)
    {
        var item = new StringBuilder();
        AppendArrayItem(item, 0, seed);
        var count = Math.Max(1, targetBytes / (item.Length + 1));

        var sb = new StringBuilder(targetBytes + 256);
        sb.Append('[');
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            AppendArrayItem(sb, i, seed);
        }

        sb.Append(']');
        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, int seed)
    {
        sb.Append("\"id\":\"ord-").Append(100000 + seed).Append('"');
        sb.Append(",\"type\":\"order.created\"");
        sb.Append(",\"createdAt\":\"2026-01-02T03:04:05.678Z\"");
        sb.Append(",\"amount\":1234.56,\"currency\":\"EUR\",\"paid\":true,\"note\":null");
        sb.Append(",\"secret\":\"").Append(SensitiveMarker).Append("-api-secret\"");
        sb.Append(",\"iban\":\"").Append(SensitiveMarker).Append("-DE00123456780000\"");
        sb.Append(",\"customer\":{");
        AppendCustomer(sb, 0, seed);
        sb.Append('}');
    }

    private static void AppendCustomer(StringBuilder sb, int index, int seed)
    {
        sb.Append("\"customerId\":").Append(5000 + index);
        sb.Append(",\"name\":\"Robin Poe ").Append(index).Append('"');
        sb.Append(",\"email\":\"").Append(SensitiveMarker).Append("-mail-").Append(seed).Append("@example.com\"");
        sb.Append(",\"phone\":\"").Append(SensitiveMarker).Append("-555-0100\"");
        sb.Append(",\"password\":\"").Append(SensitiveMarker).Append("-pw-").Append(index).Append('"');
        sb.Append(",\"cardNumber\":\"").Append(SensitiveMarker).Append("-4111111111111111\"");
    }

    private static void AppendArrayItem(StringBuilder sb, int index, int seed)
    {
        sb.Append("{\"id\":").Append(index);
        sb.Append(",\"type\":\"line\"");
        sb.Append(",\"sku\":\"SKU-").Append(10000 + index).Append('"');
        sb.Append(",\"title\":\"Plain product title for line ").Append(index).Append('"');
        sb.Append(",\"quantity\":").Append(index % 7 + 1);
        sb.Append(",\"price\":").Append((19.99m + index).ToString(global::System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(",\"active\":true,\"tags\":[\"a\",\"b\",\"c\"]");
        sb.Append(",\"owner\":{");
        AppendCustomer(sb, index, seed);
        sb.Append(",\"accessToken\":\"").Append(SensitiveMarker).Append("-tok-").Append(index).Append('"');
        sb.Append("}}");
    }

    private static void AppendField(StringBuilder sb, int index, int seed)
    {
        switch (index % 5)
        {
            case 0:
                sb.Append("\"description").Append(index).Append("\":\"Some ordinary text value number ").Append(index + seed).Append('"');
                break;
            case 1:
                sb.Append("\"counter").Append(index).Append("\":").Append(index * 31 + seed);
                break;
            case 2:
                sb.Append("\"ratio").Append(index).Append("\":").Append((index * 0.37).ToString("0.###", global::System.Globalization.CultureInfo.InvariantCulture));
                break;
            case 3:
                sb.Append("\"flag").Append(index).Append("\":").Append(index % 2 == 0 ? "true" : "false");
                break;
            default:
                sb.Append("\"code").Append(index).Append("\":\"C-").Append(index).Append('"');
                break;
        }
    }
}
