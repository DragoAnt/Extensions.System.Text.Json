using System.Globalization;
using Bogus;
using DragoAnt.Observer;
using static DragoAnt.Observer.ValuePolicy;
using static DragoAnt.System.Text.Json.Observer.JsonValuePolicy;


namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public class JsonMaskingTests
{
    private readonly ITestOutputHelper _outputHelper;
    private readonly VerifySettings _verifySettings;

    public JsonMaskingTests(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
        _verifySettings = new VerifySettings();
        _verifySettings.UseDirectory("verify");
    }

    private static readonly Faker F = new();

    private readonly JsonObserver _requestMasking = GetRequestMasking(BlockList);


    internal static JsonObserver GetRequestMasking(JsonValuePolicy<NoContext> defaultValuePolicy)
    {
        return JsonObserver.Obj(AnyDepth(policyBuilder => policyBuilder
                .Path(Names.EndsWith("card"), "saved", "id").Mask(MaskingRules.CustomerId, MaskNulls.Mask)
                .Path("card", "number").Mask(MaskingRules.CardNumber, MaskNulls.Mask)
                .Path("user", "entered").Mask((_, _) => string.Empty, MaskNulls.Mask)
                .Path("recurringTemplate", "id").Mask(MaskingRules.RecurringTemplateId, MaskNulls.Mask)
                .Match(Names.Contains("cardHolder")).Mask(MaskingRules.FullName, MaskNulls.Mask)
                .Path(Names.StartsWith("order"), "description").Mask(MaskingRules.OrderDescription, MaskNulls.Mask)
                .Path(Names.StartsWith("customer"), "id").Mask(MaskingRules.CustomerId, MaskNulls.Mask)
                .Path(Names.StartsWith("customer"), "birthDate").Mask(MaskingRules.BirthDate, MaskNulls.Mask)
                .Match(Names.Contains("ipAddress")).Mask(MaskingRules.Ip, MaskNulls.Mask)
                .Match(Names.Contains("email")).Mask(MaskingRules.Email, MaskNulls.Mask)
                .Match(Names.Contains("phone")).Mask(MaskingRules.Phone, MaskNulls.Mask)
                .Match(Names.Contains("documentNumber")).Mask(MaskingRules.DocumentNumber, MaskNulls.Mask)
                .Match(Names.Contains("firstName")).Mask(MaskingRules.Name, MaskNulls.Mask)
                .Match(Names.Contains("lastName")).Mask(MaskingRules.Name, MaskNulls.Mask)
                .Match(Names.Contains("address")).Mask(MaskingRules.Full, MaskNulls.Mask)
                .Match(Names.Contains("accountNumber")).Mask(MaskingRules.AccountNumber, MaskNulls.Mask)
            ,
            defaultValuePolicy));
    }

    private readonly JsonObserver _ignoreNullsRequestMasking = GetRequestUnmasking(NullList);

    internal static JsonObserver GetRequestUnmasking(JsonValuePolicy<NoContext> defaultValuePolicy)
    {
        return JsonObserver.Obj(b => b
                .Match("routing").Obj(sb => sb.Match("method").Unmasked()),
            AnyDepth(policyBuilder => policyBuilder
                    .Path(Names.EndsWith("card"), "saved", "id").Unmasked()
                    .Path("card", "number").Unmasked()
                    .Match(Names.Contains("cardHolder")).Unmasked()
                    .Path(Names.StartsWith("customer"), "id").Unmasked()
                    .Path(Names.StartsWith("customer"), "birthDate").Unmasked()
                    .Match(Names.Contains("ipAddress")).Unmasked()
                    .Match(Names.Contains("email")).Unmasked(),
                defaultValuePolicy));
    }

    internal static readonly Dictionary<string, string> SensitiveValues = new()
    {
        { "cardId", "0c7ed9e5-1c7f-42bd-9efd-e267edd17e57" },
        { "userEntered", "Excepturi quia voluptatem." },
        { "templateId", "21a60d6c-1043-41d2-b39b-1b9f350c9375" },
        { "accountNumber", "81357746" },
        { "cardNumber", "4902130214042281" },
        { "cardHolder", "JAMARCUS WIZA" },
        { "orderDescription", "Minus et repellat rem autem." },
        { "customerId", "56e48b58-79a3-4442-9a1a-b19120f0b120" },
        { "customerBirth", "11/10/2015 13:58:18" },
        { "ip", "132.152.216.232" },
        { "email", "Jarrell.Ankunding@example.net" },
        { "phone", "310-508-9236" },
        { "document", "5709240376" },
        { "firstName", "Roman" },
        { "lastName", "Shields" },
        { "address", "Wardfurt" },
    };

    //language=json
    internal static readonly string TestJson =
        $$"""
          {
            // Comment
            "routing": {
              "method": "test",
              "contractId": 2
            },
            "session": {
              "id": "1",
              "method": "test",
              "accountNumber": "{{SensitiveValues["accountNumber"]}}",
              "merchant": {
                "id": "213",
                "terminal": {
                  "contractId": 285,
                  "businessActivityType": null
                }
              },
              "recurringTemplate": {
                  "id": "{{SensitiveValues["templateId"]}}"
              },
              "user": {
                  "entered": "{{SensitiveValues["userEntered"]}}"
              },
              "card": {
                  "saved":
                  {
                      "id": "{{SensitiveValues["cardId"]}}"
                  },
                  "number": "{{SensitiveValues["cardNumber"]}}",
                  "cardHolder": "{{SensitiveValues["cardHolder"]}}"
              },
              "browser": {
                "ipAddress": "{{SensitiveValues["ip"]}}",
                "userAgent": null
              },
              "customer": {
                "id": "{{SensitiveValues["customerId"]}}",
                "email": "{{SensitiveValues["email"]}}",
                "phone": "{{SensitiveValues["phone"]}}",
                "firstName": "{{SensitiveValues["firstName"]}}",
                "lastName": "{{SensitiveValues["lastName"]}}",
                "address": "{{SensitiveValues["address"]}}",
                "documentNumber": "{{SensitiveValues["document"]}}",
                "birthDate": "{{SensitiveValues["customerBirth"]}}"
              },
              "order": {
                "id": "333",
                "currency": "RSD",
                "amount": 10000,
                "description": "{{SensitiveValues["orderDescription"]}}"
              }
            }
          }
          """;

    private string? Mask(string testValue, JsonObserver mask, bool ignoreNulls = false)
    {
        _outputHelper.WriteLine($"Before: {Environment.NewLine}{testValue}{Environment.NewLine}");
        var maskedJson = mask.Mask(testValue, new JsonObserverOptions { IgnoreNulls = ignoreNulls, Indented = true });
        _outputHelper.WriteLine($"After: {Environment.NewLine}{maskedJson}");
        return maskedJson;
    }

    [Fact]
    public void Mask_ForAllRules_MaskingData()
    {
        // Act
        var maskedRequest = Mask(TestJson, _requestMasking);

        // Assert
        maskedRequest.Should().NotBeNullOrEmpty();
        foreach (var sensitiveValue in SensitiveValues.Values)
        {
            maskedRequest.Should().NotContain(sensitiveValue);
        }
    }

    [Fact]
    public async Task Mask_ForAllRules_MaskingData_IgnoreNulls_And_Comments()
    {
        // Act
        var maskedRequest = Mask(TestJson, _ignoreNullsRequestMasking, ignoreNulls: true);

        // Assert
        await Verify(maskedRequest, _verifySettings);
    }
}