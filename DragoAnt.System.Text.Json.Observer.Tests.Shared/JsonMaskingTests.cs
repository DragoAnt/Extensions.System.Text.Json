using System.Globalization;
using Bogus;
using DragoAnt.System.Text.Json.Observer.Strategies;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies;


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


    private static JsonObserver GetRequestMasking(JsonObserverValueDelegate<JsonObserveringEmptyContext> defaultValuePolicy)
    {
        return JsonObserver.Obj(Relative(policyBuilder => policyBuilder
                .Match(PropMatches.EndsWith("card"), "saved", "id").MaskStr(MaskingRules.CustomerId)
                .Match("card", "number").MaskStr(MaskingRules.CardNumber)
                .Match("user", "entered").MaskStr((_, _) => string.Empty)
                .Match("recurringTemplate", "id").MaskStr(MaskingRules.RecurringTemplateId)
                .Match(PropMatches.Contains("cardHolder")).MaskStr(MaskingRules.FullName)
                .Match(PropMatches.StartsWith("order"), "description").MaskStr(MaskingRules.OrderDescription)
                .Match(PropMatches.StartsWith("customer"), "id").MaskStr(MaskingRules.CustomerId)
                .Match(PropMatches.StartsWith("customer"), "birthDate").MaskStr(MaskingRules.BirthDate)
                .Match(PropMatches.Contains("ipAddress")).MaskStr(MaskingRules.Ip)
                .Match(PropMatches.Contains("email")).MaskStr(MaskingRules.Email)
                .Match(PropMatches.Contains("phone")).MaskStr(MaskingRules.Phone)
                .Match(PropMatches.Contains("documentNumber")).MaskStr(MaskingRules.DocumentNumber)
                .Match(PropMatches.Contains("firstName")).MaskStr(MaskingRules.Name)
                .Match(PropMatches.Contains("lastName")).MaskStr(MaskingRules.Name)
                .Match(PropMatches.Contains("address")).MaskStr(MaskingRules.Full)
                .Match(PropMatches.Contains("accountNumber")).MaskStr(MaskingRules.AccountNumber)
            ,
            defaultValuePolicy));
    }

    private readonly JsonObserver _ignoreNullsRequestMasking = GetRequestUnmasking(NullList);

    private static JsonObserver GetRequestUnmasking(JsonObserverValueDelegate<JsonObserveringEmptyContext> defaultValuePolicy)
    {
        return JsonObserver.Obj(b => b
                .Match("routing").Obj(sb => sb.Match("method").Unmasked()),
            Relative(policyBuilder => policyBuilder
                    .Match(PropMatches.EndsWith("card"), "saved", "id").Unmasked()
                    .Match("card", "number").Unmasked()
                    .Match(PropMatches.Contains("cardHolder")).Unmasked()
                    .Match(PropMatches.StartsWith("customer"), "id").Unmasked()
                    .Match(PropMatches.StartsWith("customer"), "birthDate").Unmasked()
                    .Match(PropMatches.Contains("ipAddress")).Unmasked()
                    .Match(PropMatches.Contains("email")).Unmasked(),
                defaultValuePolicy));
    }

    private static readonly Dictionary<string, string> SensitiveValues = new()
    {
        { "cardId", Guid.NewGuid().ToString() },
        { "userEntered", F.Lorem.Sentence(3) },
        { "templateId", Guid.NewGuid().ToString() },
        { "accountNumber", F.Finance.Account() },
        { "cardNumber", F.Finance.CreditCardNumber().Replace("-", string.Empty) },
        { "cardHolder", F.Name.FullName().ToUpper() },
        { "orderDescription", F.Lorem.Sentence(5) },
        { "customerId", Guid.NewGuid().ToString() },
        { "customerBirth", F.Date.Past(20).ToString(CultureInfo.InvariantCulture) },
        { "ip", F.Internet.Ip() },
        { "email", F.Internet.ExampleEmail() },
        { "phone", F.Phone.PhoneNumber() },
        { "document", F.Random.Long(1000000000, 9999999999).ToString() },
        { "firstName", F.Person.FirstName },
        { "lastName", F.Person.LastName },
        { "address", F.Person.Address.City },
    };

    //language=json
    private static readonly string TestJson =
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

    private string? Mask(string testValue, JsonObserver mask, bool ignoreNulls = false, bool ignoreComments = false)
    {
        _outputHelper.WriteLine($"Before: {Environment.NewLine}{testValue}{Environment.NewLine}");
        var maskedJson = mask.Mask(testValue, new JsonReaderOptions
            {
                CommentHandling = JsonCommentHandling.Allow,
            },
            new JsonWriterOptions
            {
                Indented = true,
            },
            ignoreNulls: ignoreNulls, ignoreComments: ignoreComments);
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
    public void Mask_ForAllRules_MaskingData_IgnoreNulls_And_Comments()
    {
        // Act
        var maskedRequest = Mask(TestJson, _ignoreNullsRequestMasking, true, true);

        // Assert
        Verify(maskedRequest, _verifySettings);
    }
}