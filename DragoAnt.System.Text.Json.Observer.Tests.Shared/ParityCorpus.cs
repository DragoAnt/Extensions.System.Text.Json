namespace DragoAnt.System.Text.Json.Observer.Tests.Shared;

internal static class ParityCorpus
{
    public static IReadOnlyList<string> Documents { get; } =
    [
        """{"user":"alice","password":"s3cret","card":{"number":"4111111111111111","exp":"12/30"},"email":"a@example.com","token":"tok-1234567890"}""",
        """{"lines":[{"qty":1,"sku":"a"},{"qty":2,"sku":"b"}],"qty":3,"meta":{"version":"1.2","id":7}}""",
        """[{"password":"p1","card":{"number":"4000000000000002"}},{"email":"b@example.com"}]""",
        """{"PASSWORD":"x","Card":{"Number":"4111111111111111"},"EMAIL":"c@example.com"}""",
        "{ // head\n\"password\": \"hunter2\", // rotated\n/* t */ \"timeout\": 30 // TODO\n}",
        """{"password":null,"email":"","card":null,"user":{"ssn":null},"x":[]}""",
        """{"password":"a\"b","email":"été@x.org"}""",
        """{"id":12345678901234567890,"password":1.5e3,"token":-0,"qty":true}""",
        """{"a":{"b":{"c":{"password":"deep","card":{"number":"5555555555554444"}}}}}""",
        """{"user":{"ssn":"123-45-6789"},"other":{"user":{"ssn":"987-65-4321"}}}""",
        """{"password":"x"} {"b":1}""",
        "\"password\"",
    ];
}
