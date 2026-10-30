namespace uk.osric.asp.Web.Controllers;

using Microsoft.AspNetCore.Mvc;
using uk.osric.asp.Core;

public class HomeController : Controller {
    public IActionResult Index(string? name) {
        Greeting greeting = Greeter.For(name);
        return View(greeting);
    }
}
