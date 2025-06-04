using System.Text.RegularExpressions;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

/// <summary>
/// Masking regular expressions.
/// </summary>
public static partial class MaskingRules
{
    /// <summary>
    /// IP address masking rule.
    /// </summary>
    /// <remarks>
    /// Masks the last segment of the IPv4.
    /// </remarks>
    public static readonly Regex Ip = IpRegex();

    /// <summary>
    /// Email address masking rule.
    /// </summary>
    /// <remarks>
    /// Leaves the first symbol and the first-level domain.
    /// </remarks>
    public static readonly Regex Email = EmailRegex();

    /// <summary>
    /// Name masking rule.
    /// </summary>
    /// <remarks>
    /// Leaves the first symbol of the name.
    /// </remarks>
    public static readonly Regex Name = NameRegex();

    /// <summary>
    /// Phone masking rule.
    /// </summary>
    /// <remarks>
    /// Leaves two last symbols.
    /// </remarks>
    public static readonly Regex Phone = PhoneRegex();

    /// <summary>
    /// Full value masking rule.
    /// </summary>
    /// <remarks>
    /// Masks the whole value.
    /// </remarks>
    public static readonly Regex Full = FullValueRegex();

    /// <summary>
    /// Full name masking rule.
    /// </summary>
    /// <remarks>
    /// Leaves the first symbol of each word.
    /// </remarks>
    public static readonly Regex FullName = FullNameRegex();

    /// <summary>
    /// Card number masking rule.
    /// </summary>
    /// <remarks>
    /// Leaves the first six, and the last four symbols.
    /// </remarks>
    public static readonly Regex CardNumber = CardNumberRegex();

    /// <summary>
    /// Account number masking rule.
    /// </summary>
    /// <remarks>
    /// Leaves the last four symbols only.
    /// </remarks>
    public static readonly Regex AccountNumber = AccountNumberRegex();

    /// <summary>
    /// Document masking rule.
    /// </summary>
    /// <remarks>
    /// Leaves two symbols of the beginning and of the ending.
    /// </remarks>
    public static readonly Regex DocumentNumber = DocumentNumberRegex();

    /// <summary>
    /// Order description masking rule.
    /// </summary>
    /// <remarks>
    /// Leaves two symbols of the beginning and of the ending.
    /// </remarks>
    public static readonly Regex OrderDescription = OrderDescriptionRegex();

    /// <summary>
    /// Customer ID masking rule.
    /// </summary>
    /// <remarks>
    /// Leaves six symbols of the beginning and of the ending.
    /// </remarks>
    public static readonly Regex CustomerId = CustomerIdRegex();

    /// <summary>
    /// Document masking rule.
    /// </summary>
    /// <remarks>
    /// Leaves two symbols in the beginning and one in the ending.
    /// </remarks>
    public static readonly Regex BirthDate = BirthDateRegex();

    /// <summary>
    /// Recurring template ID masking rule.
    /// </summary>
    /// <remarks>
    /// Leaves two symbols in the beginning and one in the ending.
    /// </remarks>
    public static readonly Regex RecurringTemplateId = RecurringTemplateIdRegex();

    [GeneratedRegex("(?:\\d+){1}$")]
    private static partial Regex IpRegex();

    [GeneratedRegex(@"(?<=.)[^@\n](?=[^@\n]*?@)|(?:(?<=@)|(?!^)\G(?=[^@\n]*$)).(?=.*[^@\n]\.)")]
    private static partial Regex EmailRegex();

    [GeneratedRegex("(?<=.{1}).(?=.{0})")]
    private static partial Regex NameRegex();

    [GeneratedRegex(".(?=.{2})")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(".")]
    private static partial Regex FullValueRegex();

    [GeneratedRegex(@"\B\S")]
    private static partial Regex FullNameRegex();

    [GeneratedRegex("(?<=.{6}).(?=.{4})")]
    private static partial Regex CardNumberRegex();

    [GeneratedRegex(".(?=.{4})")]
    private static partial Regex AccountNumberRegex();

    [GeneratedRegex("(?<=.{2}).(?=.{2})")]
    private static partial Regex DocumentNumberRegex();

    [GeneratedRegex("(?<=.{2}).(?=.{2})")]
    private static partial Regex OrderDescriptionRegex();

    [GeneratedRegex("(?<=.{6}).(?=.{6})")]
    private static partial Regex CustomerIdRegex();

    [GeneratedRegex("(?<=.{2}).(?=.{1})")]
    private static partial Regex BirthDateRegex();

    [GeneratedRegex("(?<=.{14}).(?=.{6})")]
    private static partial Regex RecurringTemplateIdRegex();
}
