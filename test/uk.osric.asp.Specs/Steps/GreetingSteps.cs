namespace uk.osric.asp.Specs.Steps;

using NUnit.Framework;
using Reqnroll;
using uk.osric.asp.Core;

[Binding]
public class GreetingSteps {
    private string? _name;

    [When("I ask for a greeting as {string}")]
    public void WhenIAskForAGreetingAs(string name) {
        _name = name;
    }

    [When("I ask for a greeting without a name")]
    public void WhenIAskForAGreetingWithoutAName() {
        _name = null;
    }

    [Then("I am greeted with {string}")]
    public void ThenIAmGreetedWith(string expected) {
        Greeting greeting = Greeter.For(_name);

        Assert.That(greeting.Text, Is.EqualTo(expected));
    }
}
