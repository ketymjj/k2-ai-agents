using K2Agents.Application.Contacts;
using K2Agents.Application.Conversations;

namespace K2Agents.Tests.Conversations;

public class HandoffDetectorTests
{
    [Theory]
    [InlineData("Quero falar com um atendente")]
    [InlineData("posso falar com uma pessoa?")]
    [InlineData("Preciso de atendimento humano, por favor")]
    [InlineData("me transfere para um atendente")]
    [InlineData("Quero ser atendido por uma pessoa de verdade")]
    [InlineData("Can I talk to a human?")]
    [InlineData("I want to speak with a real person")]
    public void DetectsExplicitRequests(string message) => Assert.True(HandoffDetector.CustomerAsksForHuman(message));

    [Theory]
    [InlineData("Quero um bolo para 20 pessoas")]
    [InlineData("O atendente de ontem foi ótimo")]
    [InlineData("Vocês atendem aos sábados?")]
    [InlineData("Qual o horário de atendimento?")]
    [InlineData("This person told me about you")]
    public void IgnoresMentionsThatAreNotRequests(string message) => Assert.False(HandoffDetector.CustomerAsksForHuman(message));

    [Fact]
    public void ExtractsAndRemovesMarker()
    {
        var (content, requested) = HandoffDetector.ExtractMarker("Vou chamar um atendente para você.\n[[TRANSFERIR]]");
        Assert.True(requested);
        Assert.Equal("Vou chamar um atendente para você.", content);

        Assert.False(HandoffDetector.ExtractMarker("Resposta normal").Requested);
    }
}

public class ContactExtractorTests
{
    [Fact]
    public void ExtractsNameEmailAndPhone()
    {
        var data = ContactExtractor.Extract("Oi! Meu nome é Ana Paula Souza, e-mail ana.paula@email.com e telefone (11) 98888-1234.");
        Assert.Equal("Ana Paula Souza", data.Name);
        Assert.Equal("ana.paula@email.com", data.Email);
        Assert.Equal("11988881234", data.Phone);
    }

    [Theory]
    [InlineData("+55 11 98888-1234", "5511988881234")]
    [InlineData("11988881234", "11988881234")]
    [InlineData("(21) 3333-4444", "2133334444")]
    public void NormalizesBrazilianPhones(string text, string expected) => Assert.Equal(expected, ContactExtractor.Extract(text).Phone);

    [Theory]
    [InlineData("sou a cliente que ligou ontem")]  // sem nome próprio
    [InlineData("quero 2 bolos de 1234 gramas")]  // números que não são telefone
    [InlineData("Qual o preço?")]
    public void DoesNotInventData(string text) => Assert.True(ContactExtractor.Extract(text).IsEmpty);

    [Fact]
    public void NameRequiresCapitalizedWords()
    {
        Assert.Null(ContactExtractor.Extract("meu nome é segredo").Name);
        Assert.Equal("Bruno", ContactExtractor.Extract("me chamo Bruno").Name);
    }
}
