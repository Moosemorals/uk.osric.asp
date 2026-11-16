// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

namespace uk.osric.asp.Runtime.Tests;

using uk.osric.asp.Core.Compilation;
using uk.osric.asp.Core.Pages;

/// <summary>The compiler's half of the fourth kind: a declaration-only source that declares a worker.</summary>
[TestFixture]
public sealed class PageWorkerTests {

    private const string Echo = """
        <%!
        using System.Threading.Tasks;

        namespace Site.Work {
            public sealed class Echo : IWorker {
                public Task RunAsync(IWorkerContext context) => Task.CompletedTask;
            }
        }
        %>
        """;

    private const string Document = """
        <%!
        namespace Site.Document {
            public static class Chrome {
                public static string Title() => "Site";
            }
        }
        %>
        """;

    private static readonly PageCompiler Compiler = new();

    [Test]
    public void ADeclarationOnlySourceWithNoEntryPointStaysALibrary() {
        CompileResult result = CompileWorker(Document);

        Assert.Multiple(() => {
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Kind, Is.EqualTo(PageKind.Library));
            Assert.That(result.Image, Is.Null, "a library is checked, never emitted");
        });
    }

    [Test]
    public void ASourceWithOneEntryPointIsAWorkerWithAnImage() {
        CompileResult result = CompileWorker(Echo);

        Assert.Multiple(() => {
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.Kind, Is.EqualTo(PageKind.Worker));
            Assert.That(result.Image, Is.Not.Null.And.Not.Empty);
            Assert.That(result.Symbols, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void TwoEntryPointsAreADiagnosticAtTheSecond() {
        CompileResult result = CompileWorker("""
            <%!
            using System.Threading.Tasks;

            namespace Site.Work {
                public sealed class One : IWorker {
                    public Task RunAsync(IWorkerContext context) => Task.CompletedTask;
                }

                public sealed class Two : IWorker {
                    public Task RunAsync(IWorkerContext context) => Task.CompletedTask;
                }
            }
            %>
            """);

        Assert.Multiple(() => {
            Assert.That(result.Kind, Is.EqualTo(PageKind.Worker));
            Assert.That(result.Image, Is.Null);
            Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
                diagnostic => diagnostic is not null
                    && diagnostic.Message.StartsWith("PAGE006:", StringComparison.Ordinal)
                    && diagnostic.Message.Contains("Two", StringComparison.Ordinal)
                    && diagnostic.Position is { Line: 9 }));
        });
    }

    [Test]
    public void AnEntryPointThatIsNotPublicIsADiagnostic() {
        CompileResult result = CompileWorker("""
            <%!
            using System.Threading.Tasks;

            namespace Site.Work {
                internal sealed class Hidden : IWorker {
                    public Task RunAsync(IWorkerContext context) => Task.CompletedTask;
                }
            }
            %>
            """);

        Assert.Multiple(() => {
            Assert.That(result.Image, Is.Null);
            Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
                diagnostic => diagnostic is not null
                    && diagnostic.Message.StartsWith("PAGE006:", StringComparison.Ordinal)
                    && diagnostic.Message.Contains("public", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void AnEntryPointWithoutAParameterlessConstructorIsADiagnostic() {
        CompileResult result = CompileWorker("""
            <%!
            using System.Threading.Tasks;

            namespace Site.Work {
                public sealed class NeedsAnArgument : IWorker {
                    public NeedsAnArgument(int size) => Size = size;

                    public int Size { get; }

                    public Task RunAsync(IWorkerContext context) => Task.CompletedTask;
                }
            }
            %>
            """);

        Assert.Multiple(() => {
            Assert.That(result.Image, Is.Null);
            Assert.That(result.Diagnostics, Has.Some.Matches<PageDiagnostic>(
                diagnostic => diagnostic is not null
                    && diagnostic.Message.StartsWith("PAGE006:", StringComparison.Ordinal)
                    && diagnostic.Message.Contains("parameterless", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void AWorkerIsCompiledWithTheLibrariesItNames() {
        CompileResult result = CompileWorker("""
            <%!
            using System.Threading.Tasks;
            using Site.Document;

            namespace Site.Work {
                public sealed class Echo : IWorker {
                    public string Title { get; private set; } = string.Empty;

                    public Task RunAsync(IWorkerContext context) {
                        Title = Chrome.Title();

                        return Task.CompletedTask;
                    }
                }
            }
            %>
            """, ("/_lib/document", Document));

        Assert.Multiple(() => {
            Assert.That(result.IsSuccess, Is.True, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.That(result.Kind, Is.EqualTo(PageKind.Worker));
            Assert.That(result.Libraries, Does.Contain("/_lib/document"));
        });
    }

    [Test]
    public void AWorkerImageLoadsItsEntryPoint() {
        CompileResult result = CompileWorker(Echo);

        using LoadedWorker worker = Compiler.LoadWorker(result.Image!, result.Symbols);

        Assert.That(worker.Create(), Is.InstanceOf<uk.osric.asp.Sdk.IWorker>());
    }

    [Test]
    public void AnImageWithNoEntryPointCannotBeLoadedAsAWorker() {
        TemplateGenerationResult page = TemplateSourceGenerator.Generate("<% ctx.Response.Write(\"hi\"); %>");
        CompileResult result = Compiler.Compile(page.Source!, []);

        Assert.Throws<InvalidOperationException>(() => Compiler.LoadWorker(result.Image!, result.Symbols));
    }

    private static CompileResult CompileWorker(string template, params (string Key, string Template)[] libraries) {
        List<LibrarySource> sources = [];
        foreach ((string key, string source) in libraries) {
            sources.Add(new LibrarySource(key, TemplateSourceGenerator.GenerateLibrary(source).Source!));
        }

        TemplateGenerationResult generated = TemplateSourceGenerator.GenerateLibrary(template);

        return Compiler.CompileWorker(generated.Source!, sources);
    }
}
