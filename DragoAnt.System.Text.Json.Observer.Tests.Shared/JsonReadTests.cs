using System.Globalization;
using System.Text;
using Bogus;
using DragoAnt.System.Text.Json.Observer.Strategies;
using static DragoAnt.System.Text.Json.Observer.JsonObserverValuePolicies<DragoAnt.System.Text.Json.Observer.Tests.Shared.JsonReadTests.ReadContext>;

namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

public abstract class JsonReadTests(ITestOutputHelper outputHelper)
{
    private static readonly Faker F = new();

    public static void BenchmarkRead(ReadContext context)
    {
        MaskDeclaration.Read(TestJsonUtf8Bytes, context);
    }

    public sealed class ReadContext
    {
        public string? Method { get; set; }
        public int? ContractId { get; set; }

        public string? ContractId2 { get; set; }

        public string? SavedCardValue { get; set; }
        public string? Ip { get; set; }
        public string? User { get; set; }
    }

    private static readonly JsonObserver<ReadContext> MaskDeclaration = JsonObserver.Obj(
        //Direct paths
        builder => builder
            .Match("routing").Obj(routingBuilder => routingBuilder
                .Match("contractId").ReadInt((v, c) => c.ContractId = v)
                .Match("contractId2").ReadRaw((v, c) => c.ContractId2 = v)
                .Match("method").ReadStr((v, c) => c.Method = v))
            .Match("session", "user", "entered").ReadStr((v, c) => c.User = v),
        Relative(builder => builder
            .Match(PropMatches.EndsWith("card"), "saved", "id").ReadStr((v, c) => c.SavedCardValue = v)
            .Match(PropMatches.Contains("ipAddress")).ReadStr((v, c) => c.Ip = v)));

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
            "routing": {
              "contractId": 2,
              "contractId2": 2,
              "method": "test"
            },
            "session": {
              "id": "1",
              "method": "test",
              "accountNumber": "{{SensitiveValues["accountNumber"]}}",
              "merchant": {
                "id": "4",
                "terminal": {
                  "contractId": 2,
                  "businessActivityType": null
                }
              },
              "recurringTemplate": {
                  "id": "{{SensitiveValues["templateId"]}}"
              },
              "user": {
                  "entered": "{{SensitiveValues["userEntered"]}}"
              },
              "MY_card": {
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
                "id": "222",
                "currency": "RSD",
                "amount": 10000,
                "description": "{{SensitiveValues["orderDescription"]}}"
              }
            }
          }
          """;

    private static readonly byte[] TestJsonUtf8Bytes = Encoding.UTF8.GetBytes(TestJson);

    private ReadContext Read(string testValue, JsonObserver<ReadContext> mask)
    {
        outputHelper.WriteLine($"Before: {Environment.NewLine}{testValue}{Environment.NewLine}");
        var context = new ReadContext();
        mask.Read(testValue, context);
        outputHelper.WriteLine(
            $"Read context: {Environment.NewLine}{JsonSerializer.Serialize(context, new JsonSerializerOptions { WriteIndented = true })}");
        return context;
    }

    [Fact]
    public void Read_ForAllRules()
    {
        // Act
        var readContext = Read(TestJson, MaskDeclaration);

        // Assert
        readContext.Should().NotBeNull();
        readContext.ContractId.Should().Be(2);
        readContext.ContractId2.Should().Be("2");
        readContext.SavedCardValue.Should().Be(SensitiveValues["cardId"]);
        readContext.Ip.Should().Be(SensitiveValues["ip"]);
        readContext.User.Should().Be(SensitiveValues["userEntered"]);

        //Act 2
        var readContext2 = Read(TestJson, MaskDeclaration);

        // Assert 2
        readContext2.Should().NotBeNull();
        readContext2.ContractId.Should().Be(2);
        readContext2.ContractId2.Should().Be("2");
        readContext2.SavedCardValue.Should().Be(SensitiveValues["cardId"]);
        readContext2.Ip.Should().Be(SensitiveValues["ip"]);
        readContext2.User.Should().Be(SensitiveValues["userEntered"]);
    }


    [Fact]
    public void Read_ForAllRules_Debug()
    {
        var requestMasking = JsonObserver.Obj(b => b
                .Match("routing").Obj(routingB => routingB
                    .Match("contractId").ReadInt((v, c) => c.ContractId = v))
                .Match("session", "user", "entered").ReadStr((v, c) => c.User = v),
            Relative(b => b
                .Match(PropMatches.EndsWith("card"), "saved", "id").ReadStr((v, c) => c.SavedCardValue = v)));

        // Act
        var readContext = Read(TestJson, requestMasking);

        // Assert
        readContext.Should().NotBeNull();
        readContext.ContractId.Should().Be(2);

        var readContext2 = Read(TestJson, requestMasking);

        // Assert 2
        readContext2.Should().NotBeNull();
        readContext2.ContractId.Should().Be(2);
    }
}